using System;
using HarmonyLib;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Task 12's physical acts, all four on CLIENT-SIDE surfaces by construction: pickables,
    /// spawns and plants only have live instances on machines near a player (a dedicated
    /// server holds ZDOs, not behaviours), so "consequences fire only where a player stands"
    /// is enforced by WHERE these patches can possibly run, not by a check that could rot.
    /// That is also the AwayFromHome safety: a keeper-held zone has no client, no instances,
    /// and therefore no consequences.
    ///
    /// Rule 1 throughout: behaviour prefixes at Priority.Low honouring __runOriginal,
    /// decorations ceding every fight. Rule 3 throughout: every body is wrapped — a
    /// consequence that throws must never take picking, spawning or planting down with it.
    /// Zone state comes from the ZoneSync ring cache (authority reads its own store).
    /// </summary>
    internal static class ConsequenceGate
    {
        public static bool BarrenAt(Vector3 pos)
        {
            if (!ModConfig.EnableConsequence.Value || !ModConfig.ConsequenceBarren.Value) return false;
            ZoneState s = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos));
            return ConsequenceMath.Barren(s.Plague, s.Scorch,
                ModConfig.BarrenPlagueThreshold.Value, ModConfig.BarrenScorchThreshold.Value);
        }

        /// <summary>Task 13 phase B, the personal refusal: the land here holds a grudge
        /// against THE LOCAL PLAYER specifically. Distinct from Barren, which refuses
        /// everyone — another player picks this same bush without trouble.</summary>
        public static bool ShunnedAt(Vector3 pos)
        {
            if (!ModConfig.EnableRivalry.Value) return false;
            return ZoneSync.GrudgeAt(ZoneKey.FromWorldPos(pos)) >= ModConfig.GrudgePickRefuse.Value;
        }

        public static bool WithersAt(Vector3 pos)
        {
            if (!ModConfig.EnableConsequence.Value || !ModConfig.ConsequenceWither.Value) return false;
            ZoneState s = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos));
            return ConsequenceMath.WithersCrops(s.Plague, s.Corruption,
                ModConfig.CropWitherBlightThreshold.Value);
        }

        public static float EmpowerMultiplierAt(Vector3 pos)
        {
            if (!ModConfig.EnableConsequence.Value || !ModConfig.ConsequenceEmpower.Value) return 1f;
            ZoneKey zone = ZoneKey.FromWorldPos(pos);
            ZoneState s = ZoneSync.StateAt(zone);
            float mult = ConsequenceMath.EmpowerLevelUpMultiplier(s.Corruption,
                ModConfig.EmpowerCorruptionThreshold.Value,
                ModConfig.EmpowerLevelUpMultiplierAtFull.Value);

            // Task 13 phase D: the blight fights harder on contested ground, harder still
            // under a storm (intensity > 1). At peace this is exactly the task 12 number.
            float war = ZoneSync.WarAt(zone);
            if (war > 0f && ModConfig.EnableRivalry.Value)
                mult *= 1f + ModConfig.ContestStarBonus.Value * war;

            // Task 14: cursed ground breeds meaner things — the same surface at a
            // gentler dial than the war's. Blessed ground adds nothing here.
            mult *= RelicSync.StarMultiplierAt(zone);

            return mult;
        }
    }

    /// <summary>Plagued or scorched ground refuses the pick — the land gone quiet.</summary>
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    [HarmonyPriority(Priority.Low)]
    public static class Patch_Pickable_Interact
    {
        private static bool Prefix(Pickable __instance, Humanoid character,
                                   bool __runOriginal, ref bool __result)
        {
            try
            {
                if (!__runOriginal) return true;   // someone earlier already cancelled; no opinion

                bool barren = ConsequenceGate.BarrenAt(__instance.transform.position);
                bool shunned = !barren && ConsequenceGate.ShunnedAt(__instance.transform.position);
                if (!barren && !shunned) return true;

                if (character != null)
                    character.Message(MessageHud.MessageType.TopLeft,
                        barren ? "The land here bears nothing." : "The land refuses your hand.");

                // Mimic vanilla's own refusal shape (the tar case): the interact animation
                // may still play, the pick does not happen, nothing is consumed.
                __result = __instance.m_useInteractAnimation;
                return false;
            }
            catch (Exception)
            {
                return true;   // a broken consequence must never break picking
            }
        }
    }

    /// <summary>The refusal explained before the click, on the hover line.</summary>
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
    public static class Patch_Pickable_Hover
    {
        private static void Postfix(Pickable __instance, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;   // picked/disabled — nothing to explain

                if (ConsequenceGate.BarrenAt(__instance.transform.position))
                    __result += "\n<color=#9a8f6a>withered — this land bears nothing</color>";
                else if (ConsequenceGate.ShunnedAt(__instance.transform.position))
                    __result += "\n<color=#9a8f6a>the land remembers what you did here</color>";
            }
            catch (Exception)
            {
                // hover is cosmetic; vanilla's text is already in __result
            }
        }
    }

    /// <summary>
    /// Corrupted ground breeds meaner things: scale the `levelUpMultiplier` argument of
    /// vanilla's own spawn roll. Vanilla keeps its loop, its per-critter caps
    /// (`m_maxLevel`), its centre-distance rule and its `SetLevel` path — the danger map
    /// follows the drift map without a single mechanism replaced. Passive wildlife is
    /// excluded: a starred deer is a joke, and their fate is Sickening, not Empowerment.
    /// </summary>
    [HarmonyPatch(typeof(SpawnSystem), "Spawn")]
    [HarmonyPriority(Priority.Low)]
    public static class Patch_SpawnSystem_Spawn
    {
        private static void Prefix(SpawnSystem.SpawnData critter, Vector3 spawnPoint,
                                   ref float levelUpMultiplier)
        {
            try
            {
                if (critter?.m_prefab == null) return;
                if (ConsequenceMath.IsPassivePrefab(critter.m_prefab.name,
                        ModConfig.WildlifePrefabs.Value)) return;

                levelUpMultiplier *= ConsequenceGate.EmpowerMultiplierAt(spawnPoint);
            }
            catch (Exception)
            {
                // a broken consequence must never break spawning
            }
        }
    }

    /// <summary>
    /// Fixed spawners (nests, bone piles) get the same odds through the only seam they
    /// offer: the spawned creature comes back as the return value, and an extra star is
    /// rolled at vanilla's own base chance times our multiplier. `SetLevel` at spawn time
    /// IS the vanilla leveling path — the max-health trap does not apply here.
    /// </summary>
    [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
    public static class Patch_CreatureSpawner_Spawn
    {
        private static void Postfix(CreatureSpawner __instance, ZNetView __result)
        {
            try
            {
                if (__result == null || __instance.m_maxLevel < 2) return;

                Character creature = __result.GetComponent<Character>();
                if (creature == null || creature.GetLevel() != 1) return;
                if (ConsequenceMath.IsPassivePrefab(creature.name,
                        ModConfig.WildlifePrefabs.Value)) return;

                float mult = ConsequenceGate.EmpowerMultiplierAt(__instance.transform.position);
                if (mult <= 1f) return;

                // Vanilla's base level-up chance is 10 (percent); we roll once for one star.
                if (UnityEngine.Random.Range(0f, 100f) <= 10f * mult)
                    creature.SetLevel(2);
            }
            catch (Exception)
            {
                // the creature vanilla spawned is already valid; leave it be
            }
        }
    }

    /// <summary>
    /// Blighted soil kills crops, through vanilla's own machinery: an unhealthy status gets
    /// the withered look for free, and `m_destroyIfCantGrow` lets vanilla itself take the
    /// plant at grow time. Reuses `Status.WrongBiome` (no invented enum values for other
    /// mods to trip on); the hover patch below tells the truth about WHY.
    ///
    /// `m_status` is PRIVATE in the real assembly (rule 5: the publicized build compiles a
    /// direct write cleanly and Mono refuses it only in-game), so it is reached through a
    /// cached FieldRefAccess, resolved lazily and retried rather than latched — if it is
    /// ever genuinely gone, Valheim's API moved and the error names it.
    /// </summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
    public static class Patch_Plant_UpdateHealth
    {
        private static AccessTools.FieldRef<Plant, Plant.Status> _statusRef;
        private static bool _resolveLogged;

        internal static bool TryStatusRef(out AccessTools.FieldRef<Plant, Plant.Status> statusRef)
        {
            if (_statusRef == null)
            {
                try { _statusRef = AccessTools.FieldRefAccess<Plant, Plant.Status>("m_status"); }
                catch (Exception ex)
                {
                    if (!_resolveLogged)
                    {
                        _resolveLogged = true;
                        RagnaroksWrath.Log.LogError(
                            $"Patch_Plant: Plant.m_status not resolvable ({ex.Message}) — " +
                            "Valheim's API moved; crop withering disarmed.");
                    }
                }
            }
            statusRef = _statusRef;
            return _statusRef != null;
        }

        private static void Postfix(Plant __instance)
        {
            try
            {
                if (!TryStatusRef(out var statusRef)) return;
                if (statusRef(__instance) != Plant.Status.Healthy) return;   // already dying its own way
                if (!ConsequenceGate.WithersAt(__instance.transform.position)) return;

                statusRef(__instance) = Plant.Status.WrongBiome;
            }
            catch (Exception)
            {
                // a broken consequence must never break planting
            }
        }
    }

    /// <summary>Vanilla's parenthetical would blame the biome; the soil deserves the blame.</summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    public static class Patch_Plant_Hover
    {
        private static void Postfix(Plant __instance, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                if (!ConsequenceGate.WithersAt(__instance.transform.position)) return;

                __result += "\n<color=#9a8f6a>the soil here is blighted</color>";
            }
            catch (Exception)
            {
            }
        }
    }
}
