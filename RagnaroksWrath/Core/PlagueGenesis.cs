using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Where plagues BEGIN. Until 0.22.0 nothing invented outbreaks — patient zero was an
    /// admin's hand — which meant a public world could run forever without the blight arc
    /// ever starting. Genesis rolls a rare chance each pass to seed sickness in ground
    /// players actually touch: sickness follows settlement, blighted ground breeds it
    /// faster, and storms carry it. The seed is tiny and invisible (below the fog's
    /// emission floor) — a fresh outbreak telegraphs nothing until it has grown under
    /// the feet of the people who let it.
    ///
    /// Containment is inherited, not re-argued: a seed grows only through player contact
    /// (BiomeDrift), spreads only past the threshold, and winter or neglect still cures
    /// it through zero. Genesis adds a beginning, never a new rule.
    /// </summary>
    public static class PlagueGenesis
    {
        /// <summary>Per-tick base chance for one genesis roll, from the configured mean
        /// time between outbreaks on clean ground. Garbage disables rather than floods.</summary>
        public static float ChancePerTick(float intervalSeconds, float meanHours)
        {
            if (float.IsNaN(intervalSeconds) || intervalSeconds <= 0f) return 0f;
            if (float.IsNaN(meanHours) || meanHours <= 0f) return 0f;

            float chance = intervalSeconds / (meanHours * 3600f);
            return chance > 1f ? 1f : chance;
        }

        /// <summary>Ground weighting: corrupted and burnt land breeds sickness — up to
        /// five times likelier at full blight, closing the fire -> scar -> corruption ->
        /// plague loop. Clean ground is weight 1; NaN is clean, never punitive.</summary>
        public static float Weight(float corruption, float scorch)
        {
            float c = float.IsNaN(corruption) ? 0f : Math.Min(Math.Max(corruption, 0f), 1f);
            float s = float.IsNaN(scorch) ? 0f : Math.Min(Math.Max(scorch, 0f), 1f);
            return 1f + 2f * c + 2f * s;
        }
    }
}
