using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// Task 14's wire, in TitleSync's shape: GUID-prefixed routed RPCs, per-world-session
    /// registration, a client cache the aura and story surfaces read. Relics are FEW, so
    /// instead of join detection the server re-broadcasts the whole table on a slow
    /// cadence — a joining client converges within a minute and the payload is a handful
    /// of rows.
    ///
    /// Three RPCs: relic_set (server -> everybody, delta or removal), relic_place
    /// (server -> the one present client that owns real terrain — the placement
    /// delegation rule; the server never calls ground-height math, per the
    /// returns-its-input trap), relic_broken (owner client -> server, with the vandal).
    /// </summary>
    public static class RelicSync
    {
        public const string SetRpc    = "com.raveniron.ragnarokswrath.relic_set";
        public const string PlaceRpc  = "com.raveniron.ragnarokswrath.relic_place";
        public const string PlacedRpc = "com.raveniron.ragnarokswrath.relic_placed";
        public const string BrokenRpc = "com.raveniron.ragnarokswrath.relic_broken";

        // Zones this client already raised a stone in this session: the retry loop asks
        // again until the server hears the confirmation, and asking twice must not build
        // twice. Cross-session duplicates are prevented by the persisted Placed flag.
        private static readonly HashSet<ZoneKey> _placedThisSession = new HashSet<ZoneKey>();

        internal static readonly int RelicFlagHash = "rw_relic".GetStableHashCode();
        internal static readonly int RelicZxHash   = "rw_relic_zx".GetStableHashCode();
        internal static readonly int RelicZyHash   = "rw_relic_zy".GetStableHashCode();

        private static readonly Dictionary<ZoneKey, RelicLedger.Relic> _cache =
            new Dictionary<ZoneKey, RelicLedger.Relic>(8);

        private static ZRoutedRpc _registeredOn;

        /// <summary>Standing relic at a zone, same authority rule as ZoneSync.StateAt: the
        /// authority asks the ledger, a pure client reads the synced cache.</summary>
        public static RelicLedger.Relic RelicAt(ZoneKey zone)
        {
            if (RelicLedger.IsLoaded) return RelicLedger.RelicAt(zone);
            return _cache.TryGetValue(zone, out RelicLedger.Relic r)
                ? r : new RelicLedger.Relic { Type = RelicMath.None };
        }

        /// <summary>Star-odds multiplier for the task 12 surface — cursed ground breeds
        /// meaner things at a gentler dial than the war's.</summary>
        public static float StarMultiplierAt(ZoneKey zone)
        {
            if (!ModConfig.EnableRelic.Value) return 1f;
            RelicLedger.Relic r = RelicAt(zone);
            return RelicMath.StarMultiplier(r.Type, r.Cursed, ModConfig.RelicCursedStarBonus.Value);
        }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<int, int, int, bool>(PlaceRpc, RPC_RelicPlace);
                rpc.Register<int, int, int, bool, int>(SetRpc, RPC_RelicSet);
                rpc.Register<int, int>(PlacedRpc, RPC_RelicPlaced);
                rpc.Register<int, int, long>(BrokenRpc, RPC_RelicBroken);
                _registeredOn = rpc;
                _cache.Clear();   // new world session, new truth; the server re-broadcasts
                _placedThisSession.Clear();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        // ---- server side ------------------------------------------------------------

        public static void BroadcastRelic(ZoneKey zone, RelicLedger.Relic relic)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            try
            {
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, SetRpc,
                    zone.X, zone.Y, relic.Type, relic.Cursed, relic.Day);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: broadcast failed: {ex.Message}");
            }
        }

        public static void BroadcastRemoval(ZoneKey zone)
            => BroadcastRelic(zone, new RelicLedger.Relic { Type = RelicMath.None });

        public static void BroadcastAll()
        {
            if (!RelicLedger.IsLoaded) return;
            foreach (KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllRelics())
                BroadcastRelic(kv.Key, kv.Value);
        }

        /// <summary>Ask the one present client to raise the stone — it has real terrain.</summary>
        public static void RequestPlacement(long peerUid, ZoneKey zone, RelicLedger.Relic relic)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || peerUid == 0) return;
            try
            {
                rpc.InvokeRoutedRPC(peerUid, PlaceRpc, zone.X, zone.Y, relic.Type, relic.Cursed);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: placement request failed: {ex.Message}");
            }
        }

        // ---- client side ------------------------------------------------------------

        /// <summary>Owner-client report: a relic stone was destroyed. No-target routed RPC
        /// goes to the server (the HealthSync remedy-report shape).</summary>
        public static void ReportBroken(int zx, int zy, long vandalPlayerId)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            try
            {
                rpc.InvokeRoutedRPC(BrokenRpc, zx, zy, vandalPlayerId);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: broken report failed: {ex.Message}");
            }
        }

        // ---- handlers -----------------------------------------------------------------

        private static void RPC_RelicSet(long sender, int zx, int zy, int type, bool cursed, int day)
        {
            var zone = new ZoneKey(zx, zy);
            if (type == RelicMath.None) { _cache.Remove(zone); return; }
            if (type < RelicMath.Fire || type > RelicMath.Era) return;
            _cache[zone] = new RelicLedger.Relic { Type = type, Cursed = cursed, Day = day };
        }

        private static void RPC_RelicPlace(long sender, int zx, int zy, int type, bool cursed)
        {
            try
            {
                // The server retries until confirmed; a repeat for ground this client
                // already built on is answered with the confirmation alone.
                var requested = new ZoneKey(zx, zy);
                if (_placedThisSession.Contains(requested))
                {
                    ReportPlaced(zx, zy);
                    return;
                }

                ZNetScene scene = ZNetScene.instance;
                if (scene == null) return;

                GameObject prefab = null;
                string chosen = null;
                string[] candidates = (ModConfig.RelicPrefabCandidates.Value ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < candidates.Length; i++)
                {
                    string name = candidates[i].Trim();
                    GameObject p = scene.GetPrefab(name);
                    if (p == null)
                    {
                        RagnaroksWrath.Log.LogWarning($"RelicSync: prefab '{name}' not found — skipped.");
                        continue;
                    }
                    if (p.GetComponent<ZNetView>() == null)
                    {
                        RagnaroksWrath.Log.LogWarning($"RelicSync: prefab '{name}' has no ZNetView — skipped.");
                        continue;
                    }
                    prefab = p;
                    chosen = name;
                    break;
                }

                if (prefab == null)
                {
                    RagnaroksWrath.Log.LogError(
                        "RelicSync: no relic prefab candidate resolved — no stone will stand. " +
                        "Check RelicPrefabCandidates.");
                    return;
                }

                // Zone centre snapped to REAL loaded terrain — this client is here, so the
                // ground data is honest (the server-side returns-its-input trap is why the
                // placement was delegated at all).
                var zone = new ZoneKey(zx, zy);
                Vector3 pos = zone.ToWorldPos();
                ZoneSystem.instance.GetGroundData(ref pos, out _, out _, out _, out _);

                GameObject stone = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                ZNetView nview = stone.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    ZDO zdo = nview.GetZDO();
                    zdo.Set(RelicFlagHash, 1);
                    zdo.Set(RelicZxHash, zx);
                    zdo.Set(RelicZyHash, zy);
                }

                _placedThisSession.Add(requested);
                ReportPlaced(zx, zy);

                RagnaroksWrath.Log.LogInfo(
                    $"RelicSync: stone '{chosen}' raised at {pos} for zone ({zx},{zy}) " +
                    $"(destructible={(stone.GetComponent<Destructible>() != null ? "yes" : "no")}, " +
                    $"wearNTear={(stone.GetComponent<WearNTear>() != null ? "yes" : "no")}).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: placement failed: {ex.Message}");
            }
        }

        private static void RPC_RelicBroken(long sender, int zx, int zy, long vandalPlayerId)
        {
            // Only the authority acts; a client receiving a stray report ignores it.
            if (!RelicLedger.IsLoaded) return;
            Systems.World.RelicSystem.OnRelicBroken(new ZoneKey(zx, zy), vandalPlayerId);
        }

        /// <summary>Client confirmation: the stone stands. No-target routed RPC to the server.</summary>
        private static void ReportPlaced(int zx, int zy)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            try { rpc.InvokeRoutedRPC(PlacedRpc, zx, zy); }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"RelicSync: placed report failed: {ex.Message}");
            }
        }

        private static void RPC_RelicPlaced(long sender, int zx, int zy)
        {
            if (!RelicLedger.IsLoaded) return;   // authority only
            RelicLedger.MarkPlaced(new ZoneKey(zx, zy));
        }
    }
}
