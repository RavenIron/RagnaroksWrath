using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>Which consequences a zone's drift has earned. A zone announces at most once,
    /// on its FIRST crossing into any flag — the mixed-by-weight voice from the task 12
    /// conversation: per-bush and per-deer effects are silent, the zone speaks once.</summary>
    [Flags]
    public enum ConsequenceFlags
    {
        None = 0,
        /// <summary>Pickables refuse the hand — plagued or scorched ground bears nothing.</summary>
        Barren = 1,
        /// <summary>Hostile spawns come up starred on corrupted ground.</summary>
        Empowered = 2,
        /// <summary>Passive wildlife sickens and slows in plagued zones.</summary>
        Sickening = 4,
        /// <summary>Planted crops wither and die in blighted soil.</summary>
        Withering = 8,
    }

    /// <summary>
    /// The drift store growing hands — task 12's pure half. Maps zone state to the
    /// consequences the owner chose: the land goes QUIET (barren pickables), the teeth live
    /// in creatures (starred hostiles, staggering wildlife), crops die in blight, and player
    /// structures are NEVER touched — that domain was deliberately excluded, and no function
    /// in this file can express it.
    /// </summary>
    public static class ConsequenceMath
    {
        /// <summary>Plagued or scorched ground yields nothing. Ash bears nothing either.</summary>
        public static bool Barren(float plague, float scorch, float plagueThreshold, float scorchThreshold)
        {
            if (!float.IsNaN(plague) && plague >= plagueThreshold) return true;
            if (!float.IsNaN(scorch) && scorch >= scorchThreshold) return true;
            return false;
        }

        /// <summary>Passive wildlife sickens where plague has taken hold.</summary>
        public static bool SickensWildlife(float plague, float threshold)
            => !float.IsNaN(plague) && plague >= threshold;

        /// <summary>Blighted soil kills what is planted in it. Blight is the worse of plague
        /// and corruption — either poisons a field. Growth-RATE effects belong to
        /// FarmingSystem; this is only the kill line.</summary>
        public static bool WithersCrops(float plague, float corruption, float threshold)
        {
            float blight = Math.Max(float.IsNaN(plague) ? 0f : plague,
                                    float.IsNaN(corruption) ? 0f : corruption);
            return blight >= threshold;
        }

        /// <summary>
        /// Multiplier for vanilla's own level-up roll on corrupted ground: 1.0 below the
        /// threshold, ramping linearly to <paramref name="multiplierAtFull"/> at corruption
        /// 1. Fed into `SpawnSystem.Spawn`'s own `levelUpMultiplier` argument, so vanilla's
        /// loop, caps and `SetLevel` path all stay in charge — we decorate the odds, never
        /// the mechanism.
        /// </summary>
        public static float EmpowerLevelUpMultiplier(float corruption, float threshold, float multiplierAtFull)
        {
            if (float.IsNaN(corruption) || float.IsNaN(threshold) || float.IsNaN(multiplierAtFull)) return 1f;
            if (corruption < threshold || threshold >= 1f) return 1f;

            float t = (corruption - threshold) / (1f - threshold);
            if (t > 1f) t = 1f;
            float full = Math.Max(1f, multiplierAtFull);
            return 1f + (full - 1f) * t;
        }

        /// <summary>Every consequence this zone state has earned, for the announcer.</summary>
        public static ConsequenceFlags FlagsFor(ZoneState state,
            float barrenPlague, float barrenScorch, float sickenPlague,
            float empowerCorruption, float witherBlight)
        {
            ConsequenceFlags flags = ConsequenceFlags.None;
            if (Barren(state.Plague, state.Scorch, barrenPlague, barrenScorch)) flags |= ConsequenceFlags.Barren;
            if (!float.IsNaN(state.Corruption) && state.Corruption >= empowerCorruption) flags |= ConsequenceFlags.Empowered;
            if (SickensWildlife(state.Plague, sickenPlague)) flags |= ConsequenceFlags.Sickening;
            if (WithersCrops(state.Plague, state.Corruption, witherBlight)) flags |= ConsequenceFlags.Withering;
            return flags;
        }

        /// <summary>
        /// Whether a spawned object's prefab is on the passive-wildlife list. Instantiated
        /// objects are named "Deer(Clone)", so the name is truncated at the first '(' before
        /// an exact, case-insensitive match — exact, because "Boar" must not match
        /// "BoarPiggy" and quietly sicken someone's breeding pen.
        /// </summary>
        public static bool IsPassivePrefab(string objectName, string csvList)
        {
            if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(csvList)) return false;

            int paren = objectName.IndexOf('(');
            string name = (paren >= 0 ? objectName.Substring(0, paren) : objectName).Trim();
            if (name.Length == 0) return false;

            string[] entries = csvList.Split(',');
            for (int i = 0; i < entries.Length; i++)
                if (string.Equals(entries[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }
    }
}
