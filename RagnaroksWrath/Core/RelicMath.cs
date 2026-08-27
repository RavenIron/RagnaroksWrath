namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Task 14, the capstone: pure rules for consecrated places. Peaks are watermarks; a
    /// stone rises when a story COMPLETES (the peak driven through zero, a war resolved,
    /// an era recovered), never on the peak itself. Pure so the harness pins the state
    /// machine and the aura arithmetic — every multiplier here lands on a verified rate
    /// path (drift recovery, exposure, star odds) where a wrong number is measurable.
    /// </summary>
    public static class RelicMath
    {
        // Relic types, as plain ints because they travel through routed RPCs.
        public const int None    = -1;
        public const int Fire    = 0;   // a great fire, survived — blessed
        public const int Plague  = 1;   // a plague, cured — blessed
        public const int Contest = 2;   // a war resolved — wild blessed, blight cursed
        public const int Era     = 3;   // the land's long healing — blessed, the rarest

        /// <summary>Watermark update: a value at or past the trigger threshold raises the
        /// recorded peak, anything else leaves it. NaN never writes.</summary>
        public static float TrackPeak(float current, float threshold, float knownPeak)
        {
            if (float.IsNaN(current) || float.IsNaN(threshold)) return knownPeak;
            if (current < threshold) return knownPeak;
            return current > knownPeak ? current : knownPeak;
        }

        /// <summary>The story completes when a recorded peak's value has been driven all
        /// the way through zero — not merely reduced. Through-zero permanence is what makes
        /// this a moment rather than a threshold-flicker.</summary>
        public static bool ShouldConsecrate(float knownPeak, float current)
            => knownPeak > 0f && !float.IsNaN(current) && current <= 0f;

        /// <summary>Zone recovery drift multiplier on consecrated ground.</summary>
        public static float RecoveryMultiplier(int type, bool cursed,
                                               float blessedMult, float cursedMult)
        {
            if (type == None) return 1f;
            float m = cursed ? cursedMult : blessedMult;
            return float.IsNaN(m) || m <= 0f ? 1f : m;
        }

        /// <summary>Exposure DECAY multiplier while standing on consecrated ground: blessed
        /// ground sheds sickness faster; cursed ground does not slow healing (its teeth are
        /// in the accrual). Multiplies the mercy chain.</summary>
        public static float ExposureDecayMultiplier(int type, bool cursed, float blessedMult)
        {
            if (type == None || cursed) return 1f;
            return float.IsNaN(blessedMult) || blessedMult <= 0f ? 1f : blessedMult;
        }

        /// <summary>Multiplier applied to minutes-to-max on consecrated ground: cursed
        /// ground accrues exposure faster, so the minutes SHRINK by the accrual factor.
        /// Blessed ground does not shield from fresh plague (favour heals, never armors).</summary>
        public static float ExposureMinutesMultiplier(int type, bool cursed, float cursedAccrualMult)
        {
            if (type == None || !cursed) return 1f;
            if (float.IsNaN(cursedAccrualMult) || cursedAccrualMult <= 1f) return 1f;
            return 1f / cursedAccrualMult;
        }

        /// <summary>Star-odds multiplier: cursed ground breeds slightly meaner things — the
        /// task 12 surface at a gentler dial than the war's.</summary>
        public static float StarMultiplier(int type, bool cursed, float cursedStarBonus)
        {
            if (type == None || !cursed) return 1f;
            if (float.IsNaN(cursedStarBonus) || cursedStarBonus <= 0f) return 1f;
            return 1f + cursedStarBonus;
        }

        /// <summary>The stone's voice on approach — one line, the story it holds.</summary>
        public static string Story(int type, bool cursed)
        {
            switch (type)
            {
                case Fire:    return "This stone remembers the fire this ground survived.";
                case Plague:  return "This stone remembers the plague driven from this ground.";
                case Contest: return cursed
                    ? "This stone remembers the blight claiming this ground."
                    : "This stone remembers the wild taking this ground back.";
                case Era:     return "This stone remembers the land's long healing.";
                default:      return "";
            }
        }
    }
}
