using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;

namespace RavenIron.RagnaroksWrath.Feedback
{
    /// <summary>
    /// The only way this mod speaks to players.
    ///
    /// There is no HUD and no dashboard, so vanilla's MessageHud is the entire output surface.
    /// Everything funnels through here rather than calling MessageHud directly, for three reasons:
    /// a single place to rate-limit (a world-sim mod can generate a *lot* of events), a single
    /// place to enforce that a headless server never tries to render, and a single try/catch so a
    /// message failure can never abort the system that raised it.
    ///
    /// HOUSE STYLE RULE 3: this is a cosmetic path. Nothing in here may throw into a caller.
    /// </summary>
    public static class MessageFeed
    {
        /// <summary>Matches vanilla's MessageHud.MessageType. Kept local so callers don't need the game type.</summary>
        public enum Placement
        {
            /// <summary>Small text above the hotbar. For ambient, frequent, low-stakes events.</summary>
            TopLeft = 1,
            /// <summary>Large centre-screen text. For rare, significant events only.</summary>
            Centre = 2
        }

        private static float _lastMessageTime;

        /// <summary>
        /// Tell one player something. No-op on a headless server (no local player to tell) and
        /// no-op if <paramref name="player"/> is null.
        /// </summary>
        public static void ToPlayer(Player player, string text, Placement where = Placement.TopLeft)
        {
            if (player == null || string.IsNullOrEmpty(text)) return;

            try
            {
                player.Message((MessageHud.MessageType)where, text);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"MessageFeed.ToPlayer failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Tell the local player something. Safe to call from anywhere: returns immediately on a
        /// dedicated server, where Player.m_localPlayer is always null.
        /// </summary>
        public static void ToLocalPlayer(string text, Placement where = Placement.TopLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (RagnaroksWrath.IsDedicated()) return;

            Player local = Player.m_localPlayer;
            if (local == null) return;

            if (!PassesRateLimit()) return;

            ToPlayer(local, text, where);
        }

        /// <summary>
        /// Tell every player within <paramref name="radius"/> of a world position.
        ///
        /// This is the right call for a zone-local event — a fire starting, a plague taking hold —
        /// because it reaches exactly the people who can see it happen, and it works from the
        /// server without needing our own RPC.
        /// </summary>
        public static void ToPlayersNear(Vector3 pos, float radius, string text,
                                         Placement where = Placement.TopLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!PassesRateLimit())
            {
                // Zone-local lines are rare by policy; one eaten by the limiter deserves
                // a trace, or a missing announcement is indistinguishable from a broken pipe.
                RagnaroksWrath.Log.LogInfo($"MessageFeed: rate-limited '{text}'.");
                return;
            }

            try
            {
                // Deliver by hand instead of Player.MessageAllInRange, for the COUNT:
                // vanilla's static walks this machine's instantiated Player list, and on
                // a headless server that list's contents depend on which zones the server
                // happens to instantiate. Zero recipients for a line someone should have
                // seen is the exact fact worth a log line (2026-08-27: the flip
                // announcement vanished without one).
                int recipients = 0;
                var players = Player.GetAllPlayers();
                for (int i = 0; i < players.Count; i++)
                {
                    Player p = players[i];
                    if (p == null) continue;
                    if (Vector3.Distance(p.transform.position, pos) >= radius) continue;
                    p.Message((MessageHud.MessageType)where, text);
                    recipients++;
                }

                if (recipients == 0)
                    RagnaroksWrath.Log.LogInfo(
                        $"MessageFeed: ZERO recipients for '{text}' at {pos} " +
                        $"({players.Count} player instance(s) exist on this machine).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"MessageFeed.ToPlayersNear failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Tell everyone on the server. Reserve this for genuinely world-scale events — a season
        /// turning, a Devastating Storm arriving. A world-sim mod that broadcasts routine zone
        /// drift becomes noise within an hour and players turn it off.
        ///
        /// Goes through ZRoutedRpc rather than MessageHud. MessageHud.MessageAll is an *instance*
        /// method, and MessageHud.instance is null on a headless server — there is no HUD to be an
        /// instance of. Calling it there would silently do nothing on exactly the configuration
        /// this mod is built for. The routed RPC targets MessageHud's own registered
        /// "ShowMessage" handler on each client, so it works identically from a dedicated server,
        /// a listen server, or singleplayer.
        /// </summary>
        public static void ToEveryone(string text, Placement where = Placement.Centre)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                if (rpc != null)
                {
                    rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)where, text);
                    return;
                }

                // Singleplayer before networking is up: fall back to the local HUD.
                MessageHud hud = MessageHud.instance;
                if (hud != null)
                {
                    hud.ShowMessage((MessageHud.MessageType)where, text);
                }
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"MessageFeed.ToEveryone failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Minimum gap between messages, so a cascade of simultaneous world events can't spam a
        /// player off their own screen. Deliberately not applied to ToEveryone: those are rare by
        /// policy, and silently dropping a season change would be worse than the noise.
        /// </summary>
        private static bool PassesRateLimit()
        {
            float now = Time.realtimeSinceStartup;
            float gap = Mathf.Clamp(ModConfig.MessageMinIntervalSeconds.Value, 0f, 300f);

            if (now - _lastMessageTime < gap) return false;

            _lastMessageTime = now;
            return true;
        }

        /// <summary>Diagnostic line, only emitted when VerboseLogging is on.</summary>
        public static void Verbose(string text)
        {
            if (!ModConfig.VerboseLogging.Value) return;
            RagnaroksWrath.Log.LogInfo(text);
        }
    }
}
