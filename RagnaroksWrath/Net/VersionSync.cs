using System;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// The loud half of version safety. The versioned wire names already make a skewed
    /// client/server pair no-op CLEANLY — correct, and a terrible support story, because
    /// the user's symptom is "the fog doesn't show" with nothing anywhere saying why.
    /// The server broadcasts its mod version on a slow cadence; a client that disagrees
    /// warns ONCE — log and a corner message — and then shuts up. No enforcement, no
    /// kick: mismatch already fails safe, this just makes it fail AUDIBLY.
    /// </summary>
    public static class VersionSync
    {
        public const string RpcName = "com.raveniron.ragnarokswrath.version";
        private const float BroadcastSeconds = 60f;

        private static ZRoutedRpc _registeredOn;
        private static float _sinceBroadcast = BroadcastSeconds;   // first tick broadcasts
        private static bool _warned;

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<string>(RpcName, RPC_Version);
                _registeredOn = rpc;
                _warned = false;   // new world session, fresh ears
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"VersionSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>Server-side, called from a ticking system: broadcast on a slow cadence.</summary>
        public static void MaybeBroadcast(float deltaSeconds)
        {
            _sinceBroadcast += deltaSeconds;
            if (_sinceBroadcast < BroadcastSeconds) return;
            _sinceBroadcast = 0f;

            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, RagnaroksWrath.PluginVersion);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"VersionSync: broadcast failed: {ex.Message}");
            }
        }

        private static void RPC_Version(long sender, string serverVersion)
        {
            if (_warned || string.IsNullOrEmpty(serverVersion)) return;
            if (serverVersion == RagnaroksWrath.PluginVersion) return;

            _warned = true;   // once per session; the message must never become spam
            RagnaroksWrath.Log.LogWarning(
                $"VersionSync: this client runs Ragnarok's Wrath {RagnaroksWrath.PluginVersion} " +
                $"but the server runs {serverVersion}. Mismatched pairs fail SAFE and SILENT — " +
                "some features simply won't show. Update to match.");

            try
            {
                Feedback.MessageFeed.ToLocalPlayer(
                    $"Ragnarok's Wrath version differs from the server ({RagnaroksWrath.PluginVersion} " +
                    $"vs {serverVersion}) — update to match.");
            }
            catch { /* the warning already stands in the log */ }
        }
    }
}
