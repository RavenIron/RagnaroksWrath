using System.Collections.Generic;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Aggregate view of the zone store — the "BiomeMetrics" half of WorldStateSystem's
    /// bottom-up derivation. Recomputed from the store every pass, NEVER persisted: the store
    /// is the one source of truth, and an aggregate that is saved is an aggregate that can
    /// disagree with what it aggregates.
    ///
    /// Everything here is a SUM or a COUNT, deliberately not a mean. The store is sparse —
    /// untracked zones are pristine by definition — so a mean over tracked zones alone would
    /// report a world with one badly burnt zone as "50% scorched" the day a second zone gets
    /// frostbite. Sums measure the world's total burden, which grows with real damage and
    /// shrinks with real healing, regardless of how many zones happen to be tracked.
    /// </summary>
    public struct BiomeMetrics
    {
        public int TrackedZones;

        /// <summary>Zones with any plague at all — the outbreak's full footprint, seeds included.</summary>
        public int InfectedZones;

        public float PlagueSum;
        public float CorruptionSum;
        public float ScorchSum;
        public float FrostSum;
        public float FertilityDepletionSum;

        public static BiomeMetrics Compute(IEnumerable<KeyValuePair<ZoneKey, ZoneState>> zones)
        {
            var m = new BiomeMetrics();
            if (zones == null) return m;

            foreach (KeyValuePair<ZoneKey, ZoneState> kv in zones)
            {
                ZoneState s = kv.Value;
                m.TrackedZones++;

                if (s.Plague > 0f) m.InfectedZones++;

                m.PlagueSum += s.Plague;
                m.CorruptionSum += s.Corruption;
                m.ScorchSum += s.Scorch;
                m.FrostSum += s.Frost;
                m.FertilityDepletionSum += s.Fertility;
            }

            return m;
        }

        /// <summary>
        /// The world's total burden, as one number the condition thresholds can bite on.
        ///
        /// Weights are constants, not config: they encode what each field MEANS (plague is an
        /// active disaster, frost is winter being winter), and a server owner tuning them
        /// per-install would make "Ailing" mean different things on different servers while the
        /// thresholds still share one name. Tune severity with the thresholds instead.
        /// </summary>
        public float Burden()
            => PlagueSum * 1.5f
             + CorruptionSum * 1.0f
             + ScorchSum * 1.0f
             + FertilityDepletionSum * 0.75f
             + FrostSum * 0.5f;
    }
}
