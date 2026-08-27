namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The consumer side of fertility depletion — the half FarmingSystem's docs promised
    /// "with the client plugin" and that waited thirteen versions. Depleted soil grows
    /// crops SLOWER: the grow-time multiplier runs 1.0 on pristine ground up to the
    /// configured slowdown on fully depleted ground, linearly, because the depletion
    /// writer is linear too and a farmer should be able to feel the halves.
    /// </summary>
    public static class FarmingGrowth
    {
        /// <summary>Multiplier applied to a crop's grow time. Depletion is the zone
        /// store's Fertility field (0 pristine .. 1 exhausted). A slowdown at or below
        /// 1 disables the effect; garbage inputs are pristine, never punitive.</summary>
        public static float GrowTimeMultiplier(float depletion, float slowdownAtFull)
        {
            if (float.IsNaN(depletion) || depletion <= 0f) return 1f;
            if (float.IsNaN(slowdownAtFull) || slowdownAtFull <= 1f) return 1f;
            if (depletion > 1f) depletion = 1f;

            return 1f + depletion * (slowdownAtFull - 1f);
        }
    }
}
