using System;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Renders a player's earned title under their nameplate.
    ///
    /// AN APPEND-ONLY POSTFIX, under rule 1 as amended 2026-08-25: EnemyHud rewrites the
    /// nameplate from `Player.GetHoverName()` every frame, so the only seam that can ADD a
    /// line without replacing vanilla's method (and skipping every other mod with it) is
    /// decorating the result. Default priority, cedes every fight: a mod that replaces the
    /// name outright wins, and we decorate whatever survives.
    ///
    /// Runs on whichever machine renders the plate — that is, clients — reading TitleSync's
    /// cache. It also doubles as the client-side registration heartbeat: pure clients tick no
    /// WorldTick systems, so the render path is where they arm their RPC handler.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetHoverName))]
    public static class Patch_Nameplate_GetHoverName
    {
        private static void Postfix(Player __instance, ref string __result)
        {
            try
            {
                TitleSync.EnsureRegistered();

                long playerId = __instance.GetPlayerID();
                if (playerId == 0) return;

                string title = TitleSync.TitleFor(playerId);
                if (title == null) return;

                __result += TitleFormat.Suffix(title);
            }
            catch (Exception)
            {
                // Per-frame render path: a title that fails to draw must never take the
                // nameplate down with it. The name vanilla built is already in __result.
            }
        }
    }
}
