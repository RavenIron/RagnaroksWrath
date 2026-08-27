using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Systems
{
    /// <summary>
    /// Pushes each connected player their zone ring on an interval. Ticks only on the
    /// authority (WorldTick gates on that), so a client never sends; a listen host's own
    /// player needs no packet at all, because ZoneSync.StateAt reads the local store first.
    ///
    /// Unconditional pushes on a slow clock, no dirtiness tracking: the payload is under a
    /// kilobyte, the interval is seconds, and "did I already tell this peer" bookkeeping is
    /// exactly the kind of state that silently rots. Absolute and boring, on purpose.
    /// </summary>
    public class ZoneSyncSystem : IWorldSystem
    {
        public string Name => "ZoneSyncSystem";
        public bool Enabled => ModConfig.EnableZoneSync.Value;
        public float IntervalSeconds => ModConfig.ZoneSyncIntervalSeconds.Value;

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] pushing a {ModConfig.ZoneSyncRadiusZones.Value * 2 + 1}x" +
                $"{ModConfig.ZoneSyncRadiusZones.Value * 2 + 1} zone ring to each player every " +
                $"{IntervalSeconds:F0}s (absolute snapshots, defaults included).");
        }

        public void Tick(float deltaSeconds)
        {
            ZoneSync.EnsureRegistered();
            Net.VersionSync.EnsureRegistered();

            if (!Persistence.IsLoaded) return;

            // The loud half of version safety: tell every client what version runs here.
            Net.VersionSync.MaybeBroadcast(deltaSeconds);

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZNetPeer> peers;
            try { peers = znet.GetPeers(); }
            catch { return; }
            if (peers == null) return;

            int radius = Math.Max(1, ModConfig.ZoneSyncRadiusZones.Value);
            ZDOMan zdoMan = ZDOMan.instance;

            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                if (peer == null || !peer.IsReady()) continue;

                // The peer's IDENTITY long, for the per-player grudge column: their
                // character ZDO carries it (the HealthSystem mapping, inlined). 0 while
                // the character is still spawning in — the grudge column sends as 0 then,
                // which is the right answer for a player who is not yet anywhere.
                long playerId = 0;
                ZDO character = zdoMan?.GetZDO(peer.m_characterID);
                if (character != null) playerId = character.GetLong(ZDOVars.s_playerID, 0L);

                // m_refPos over the character ZDO: always populated once the peer is ready,
                // ~2s stale, and never gated by the player's map-visibility toggle.
                ZoneSync.SendRing(peer.m_uid, playerId, ZoneKey.FromWorldPos(peer.m_refPos), radius);
            }
        }
    }
}
