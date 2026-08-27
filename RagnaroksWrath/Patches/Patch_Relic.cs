using System;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Task 14, desecration: a relic stone's destruction lifts its aura and books the
    /// vandal. Destructible.RPC_Damage runs on the stone's OWNER and hands Destroy the
    /// killing HitData, so this OBSERVING prefix on Destroy is where both facts — that a
    /// relic died, and who swung — exist on one machine at the same moment. Void prefix:
    /// vanilla's destruction is not altered, only witnessed before the ZDO goes away.
    /// The report rides a no-target routed RPC to the server (the HealthSync shape);
    /// natural deaths (DestroyNow, no HitData, non-player attacker) report vandal 0 —
    /// the aura still lifts, nobody is framed.
    /// </summary>
    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
    public static class Patch_Relic_Destroy
    {
        private static void Prefix(Destructible __instance, HitData hit)
        {
            try
            {
                if (!ModConfig.EnableRelic.Value) return;

                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;

                ZDO zdo = nview.GetZDO();
                if (zdo.GetInt(RelicSync.RelicFlagHash, 0) == 0) return;

                long vandal = 0;
                Character attacker = hit?.GetAttacker();
                if (attacker is Player p) vandal = p.GetPlayerID();

                RelicSync.ReportBroken(
                    zdo.GetInt(RelicSync.RelicZxHash, 0),
                    zdo.GetInt(RelicSync.RelicZyHash, 0),
                    vandal);
            }
            catch (Exception ex)
            {
                // The stone is dying either way; the report must never block the rubble.
                RagnaroksWrath.Log.LogWarning($"Relic destroy hook failed: {ex.Message}");
            }
        }
    }
}
