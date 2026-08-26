using System;
using System.Collections.Generic;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// Carries titles from the server's TitleStore to every client's nameplate patch — the
    /// first real piece of client plumbing in this mod, kept deliberately tiny.
    ///
    /// WHY AN RPC AND NOT A ZDO KEY: a character ZDO is owned by its own client, and only the
    /// OWNER's writes replicate — a foreign key the server sets stays local and is stomped by
    /// the owner's next sync. The ZDO-position trap in CLAUDE.md, generalised. So the server
    /// broadcasts (playerID, title) pairs and every client keeps a cache the nameplate patch
    /// reads. GUID-prefixed name, because RPC names share one hash namespace with the base game
    /// and every mod.
    ///
    /// Registration is keyed on the ZRoutedRpc INSTANCE, not a "done" flag: the instance is
    /// per-world-session, and a stale flag from the previous world would leave the next one
    /// with no handler at all (docs/reference/VALHEIM-DEDICATED-SERVER-FACTS.md, Routed RPCs).
    /// Call EnsureRegistered from any per-tick path on both sides; it is a reference compare
    /// when nothing changed.
    /// </summary>
    public static class TitleSync
    {
        public const string RpcName = "com.raveniron.ragnarokswrath.title_set";

        private static readonly Dictionary<long, string> _cache = new Dictionary<long, string>(16);
        private static ZRoutedRpc _registeredOn;

        /// <summary>The local cache the nameplate patch reads. Never null entries.</summary>
        public static string TitleFor(long playerId)
            => _cache.TryGetValue(playerId, out string t) ? t : null;

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<long, string>(RpcName, RPC_TitleSet);
                _registeredOn = rpc;
                _cache.Clear();   // new world session, new truth; the server re-broadcasts
            }
            catch (Exception ex)
            {
                // Same-name double-register throws; if another of our own instances got here
                // first that is fine, but say so once rather than silently owning no handler.
                RagnaroksWrath.Log.LogWarning($"TitleSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>Server-side: tell everybody (and ourselves — Everybody dispatches locally
        /// too, which is what keeps a listen-server host's own cache warm).</summary>
        public static void Broadcast(long playerId, string title)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || playerId == 0) return;

            try
            {
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, playerId, title ?? "");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"TitleSync: broadcast failed: {ex.Message}");
            }
        }

        private static void RPC_TitleSet(long sender, long playerId, string title)
        {
            if (playerId == 0) return;

            if (string.IsNullOrEmpty(title)) _cache.Remove(playerId);
            else _cache[playerId] = title;
        }
    }
}
