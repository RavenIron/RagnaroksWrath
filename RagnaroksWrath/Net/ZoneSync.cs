using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// Server → client zone state: the sync that VFX, farming's growth effects and a future
    /// HealthSystem all queue behind. TitleSync proved the plumbing; this carries the payload.
    ///
    /// ABSOLUTE SNAPSHOTS, DEFAULTS INCLUDED. Every push contains every zone in the ring
    /// around the receiving player — pristine rows too. The stats sheet's lesson holds here:
    /// deltas drift on one dropped packet forever, and omitting healed zones would leave a
    /// client fogging a zone the server already cured. A full 5x5 ring is ~700 bytes; the
    /// self-healing is worth a thousand times that.
    ///
    /// PER-PEER, NOT BROADCAST. Each player gets the ring around their own position
    /// (`ZNetPeer.m_refPos` — always populated, ~2s stale, never gated by the map toggle).
    /// Broadcasting the whole store would scale with world damage; the ring scales with
    /// players.
    /// </summary>
    public static class ZoneSync
    {
        // "3" suffix (0.15.0): each zone row now also carries its WAR INTENSITY (phase D).
        // Renamed at every wire change so a version-skewed pair no-ops cleanly instead of
        // mis-parsing rows (the FireFront 0.17.3 lesson, applied at 0.12.0 and again here).
        public const string RpcName = "com.raveniron.ragnarokswrath.zone_state3";

        // Client-side cache the visuals read. On a HOST (server + local player in one
        // process) reads bypass this entirely — see StateAt.
        private static readonly Dictionary<ZoneKey, ZoneState> _cache = new Dictionary<ZoneKey, ZoneState>(64);

        // The LOCAL player's grudge per zone, from the same push. Per-player by
        // construction: the server computes it for the peer it is sending to.
        private static readonly Dictionary<ZoneKey, float> _grudgeCache = new Dictionary<ZoneKey, float>(64);

        // War intensity per zone (phase D) — per-zone, identical for every peer.
        private static readonly Dictionary<ZoneKey, float> _warCache = new Dictionary<ZoneKey, float>(16);
        private static ZRoutedRpc _registeredOn;

        /// <summary>
        /// Zone state as this machine knows it: the authority reads its own store (a listen
        /// host must not wait on its own network round-trip), everyone else reads the cache.
        /// </summary>
        public static ZoneState StateAt(ZoneKey zone)
        {
            if (Persistence.IsLoaded) return Persistence.Get(zone);
            return _cache.TryGetValue(zone, out ZoneState s) ? s : default;
        }

        /// <summary>
        /// The LOCAL player's grudge in a zone, same authority rule as StateAt: a listen
        /// host computes from its own ledger, a pure client reads the synced cache. Zero
        /// when rivalry is off, unloaded, or the land simply holds nothing against you.
        /// </summary>
        public static float GrudgeAt(ZoneKey zone)
        {
            if (RivalryLedger.IsLoaded)
            {
                Player local = Player.m_localPlayer;
                if (local == null) return 0f;
                RivalryLedger.Row row = RivalryLedger.Get(zone, local.GetPlayerID());
                return RivalryMath.GrudgeFor(row.Harm, row.Care, Config.ModConfig.GrudgeScale.Value);
            }
            return _grudgeCache.TryGetValue(zone, out float g) ? g : 0f;
        }

        /// <summary>War intensity at a zone, same authority rule: the authority asks
        /// RivalrySystem directly, a pure client reads the synced cache.</summary>
        public static float WarAt(ZoneKey zone)
        {
            if (Persistence.IsLoaded)
                return Systems.World.RivalrySystem.WarIntensityAt(zone);
            return _warCache.TryGetValue(zone, out float w) ? w : 0f;
        }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<ZPackage>(RpcName, RPC_ZoneState);
                _registeredOn = rpc;
                _cache.Clear();
                _grudgeCache.Clear();
                _warCache.Clear();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"ZoneSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>Server-side: push the ring around one peer's position to that peer,
        /// each zone carrying THAT PLAYER's grudge (0 when rivalry is off or unloaded).</summary>
        public static void SendRing(long peerUid, long playerId, ZoneKey centre, int radius)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                float scale = Config.ModConfig.GrudgeScale.Value;
                bool haveLedger = RivalryLedger.IsLoaded && playerId != 0;

                var pkg = new ZPackage();
                int side = radius * 2 + 1;
                pkg.Write(side * side);

                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var zone = new ZoneKey(centre.X + dx, centre.Y + dy);
                    ZoneState s = Persistence.Get(zone);
                    pkg.Write(zone.X);
                    pkg.Write(zone.Y);
                    pkg.Write(s.Fertility);
                    pkg.Write(s.Corruption);
                    pkg.Write(s.Scorch);
                    pkg.Write(s.Frost);
                    pkg.Write(s.Plague);

                    float grudge = 0f;
                    if (haveLedger)
                    {
                        RivalryLedger.Row row = RivalryLedger.Get(zone, playerId);
                        grudge = RivalryMath.GrudgeFor(row.Harm, row.Care, scale);
                    }
                    pkg.Write(grudge);
                    pkg.Write(Systems.World.RivalrySystem.WarIntensityAt(zone));
                }

                rpc.InvokeRoutedRPC(peerUid, RpcName, pkg);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"ZoneSync: send failed: {ex.Message}");
            }
        }

        private static void RPC_ZoneState(long sender, ZPackage pkg)
        {
            try
            {
                int count = pkg.ReadInt();
                if (count < 0 || count > 1024) return;   // malformed; drop rather than loop on it

                for (int i = 0; i < count; i++)
                {
                    var zone = new ZoneKey(pkg.ReadInt(), pkg.ReadInt());
                    var s = new ZoneState
                    {
                        Fertility = pkg.ReadSingle(),
                        Corruption = pkg.ReadSingle(),
                        Scorch = pkg.ReadSingle(),
                        Frost = pkg.ReadSingle(),
                        Plague = pkg.ReadSingle(),
                    };
                    s.Clamp();   // clamp on READ: the wire is a boundary like the disk is

                    if (s.IsDefault) _cache.Remove(zone);
                    else _cache[zone] = s;

                    float grudge = pkg.ReadSingle();
                    if (float.IsNaN(grudge) || grudge <= 0f) _grudgeCache.Remove(zone);
                    else _grudgeCache[zone] = grudge > 1f ? 1f : grudge;

                    float war = pkg.ReadSingle();
                    if (float.IsNaN(war) || war <= 0f) _warCache.Remove(zone);
                    else _warCache[zone] = war > 10f ? 10f : war;
                }
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"ZoneSync: bad packet from {sender}: {ex.Message}");
            }
        }
    }
}
