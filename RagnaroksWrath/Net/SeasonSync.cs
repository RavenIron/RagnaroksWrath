using System;
using RavenIron.RagnaroksWrath.Systems.World;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// Server → client season. The smallest sync in the mod and the last one that was missing.
    ///
    /// WHY IT EXISTS. `SeasonSystem.Current` is assigned only inside `Tick()`, and `WorldTick`
    /// opens with `if (!IsSimulationAuthority()) return;` — so on a pure client no system of ours
    /// ever ticks and `Current` sits at the enum default, Spring, for the entire session. Every
    /// gameplay consumer of the season runs on the authority, so nothing in THIS mod was visibly
    /// wrong; the cost landed next door. Undertow computes its current field on the peer that
    /// OWNS a hull — a client — and asks us for the season, so its seasonal shift was inert for
    /// everyone not playing on a listen host, and `wrath status` on a client reported spring in
    /// midwinter. Found while reading Undertow's bridge, 2026-08-28; fixed here rather than there,
    /// because a second season clock in another mod is exactly the conflict house rule 4 exists
    /// to prevent.
    ///
    /// ABSOLUTE, UNCONDITIONAL, EVERY TICK. One int to Everybody on SeasonSystem's own 10s
    /// cadence, whether or not anything changed. No "have I told this peer yet" bookkeeping,
    /// which is ZoneSyncSystem's stated reasoning and holds a fortiori here: a joining client is
    /// correct within one tick with nothing to track, a dropped packet heals itself, and the
    /// payload is four bytes. Deltas would be smaller and would drift forever on one loss.
    ///
    /// `Everybody` dispatches locally as well as remotely, so a listen host receives its own
    /// broadcast. That is harmless because the receiver refuses to act on the authority — the
    /// authority's season belongs to `Tick()` and nothing else may assign it.
    /// </summary>
    public static class SeasonSync
    {
        /// <summary>GUID-prefixed: RPC names share one hash namespace with the base game and
        /// every other mod. Rename on any wire change, so a version-skewed pair no-ops cleanly
        /// rather than mis-parsing (the lesson ZoneSync's "3" suffix carries).</summary>
        public const string RpcName = "com.raveniron.ragnarokswrath.season";

        private static ZRoutedRpc _registeredOn;
        private static bool _warnedBadIndex;

        /// <summary>
        /// Registration is keyed on the ZRoutedRpc INSTANCE, not a "done" flag: the instance is
        /// per-world-session, and a stale flag would leave the next world with no handler at all.
        /// Safe and cheap to call from any per-tick path on either side.
        /// </summary>
        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<int>(RpcName, RPC_Season);
                _registeredOn = rpc;
                _warnedBadIndex = false;

                // New world session, new truth. This forgets that a season was ever synced
                // WITHOUT resetting it to spring — the same reasoning as ReadFromSeasonality's
                // fallback, since a reset would make every server join look like a change to
                // spring. No-ops on the authority, whose season is its own.
                SeasonSystem.ForgetServerSeason();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"SeasonSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>Authority-side: tell everyone what season it is. Called from SeasonSystem's
        /// tick, which only runs on the authority.</summary>
        public static void Broadcast(Season season)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, (int)season);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"SeasonSync: broadcast failed: {ex.Message}");
            }
        }

        private static void RPC_Season(long sender, int index)
        {
            // The authority computes its own season and is never told one. This also absorbs a
            // listen host's local copy of its own broadcast.
            if (RagnaroksWrath.IsSimulationAuthority()) return;

            if (index < 0 || index > 3)
            {
                if (!_warnedBadIndex)
                {
                    _warnedBadIndex = true;   // once per session; a bad server must not spam a client
                    RagnaroksWrath.Log.LogWarning(
                        $"SeasonSync: server sent season index {index}, which is outside 0..3. " +
                        "Ignoring it — the season stays on its last known value.");
                }
                return;
            }

            SeasonSystem.ApplyFromServer((Season)index);
        }
    }
}
