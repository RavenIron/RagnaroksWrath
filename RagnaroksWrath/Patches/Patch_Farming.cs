using System;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Fertility depletion, finally FELT: crops in depleted soil take longer to grow.
    ///
    /// The surface is `Plant.GetGrowTime` (decompile-verified 2026-08-26): a per-instance
    /// seeded lerp consulted on every plant SUpdate, with growth fired on the plant's
    /// OWNER — a client, which is exactly the machine holding the synced zone ring. A
    /// scaling postfix at default priority decorates whatever value survives other mods
    /// (the amended rule 1 family: whoever replaces the method outright wins, and we
    /// scale what remains). All machines compute the same multiplier from the same
    /// synced state, so the half-grown visual switch and the owner's grow decision agree.
    ///
    /// Scoped to the FarmingCropPrefabs list — depletion is FARMLAND's memory; wild
    /// trees and bushes owe it nothing. Boundary per task 12: growth RATE is farming's;
    /// withering past 0.6 blight stays ConsequenceSystem's act.
    /// </summary>
    [HarmonyPatch(typeof(Plant), "GetGrowTime")]
    public static class Patch_Farming_GrowTime
    {
        private static void Postfix(Plant __instance, ref float __result)
        {
            try
            {
                if (!ModConfig.EnableFarming.Value) return;

                float slowdown = ModConfig.FarmingGrowthSlowdownAtFull.Value;
                if (slowdown <= 1f) return;

                if (!ConsequenceMath.IsPassivePrefab(__instance.gameObject.name,
                        ModConfig.FarmingCropPrefabs.Value)) return;

                float depletion = ZoneSync.StateAt(
                    ZoneKey.FromWorldPos(__instance.transform.position)).Fertility;

                __result *= FarmingGrowth.GrowTimeMultiplier(depletion, slowdown);
            }
            catch (Exception)
            {
                // A broken consumer must never break growing; vanilla's time stands.
            }
        }
    }
}
