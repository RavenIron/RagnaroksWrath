namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The land's overall condition, derived from burden. Order matters: comparisons rely on
    /// worse conditions being greater.
    /// </summary>
    public enum WorldCondition
    {
        Flourishing = 0,
        Stable = 1,
        Ailing = 2,
        Stricken = 3,
    }

    /// <summary>
    /// Burden → condition, with hysteresis. Pure, so the flap-prevention — the part of this
    /// that will page somebody if it is wrong — is pinned by the harness.
    ///
    /// WHY HYSTERESIS IS NOT OPTIONAL HERE: condition transitions are announced to every player
    /// on the server, and burden drifts continuously. A world sitting exactly at a threshold
    /// would otherwise announce "The land sickens" / "The land recovers" on alternating passes
    /// forever — the one guaranteed way to make players turn the feature off. A condition
    /// therefore only changes when burden moves clearly past a boundary: worse at the threshold
    /// itself, better only after falling below it by the hysteresis fraction.
    /// </summary>
    public static class WorldConditionRules
    {
        /// <summary>
        /// Fraction below a threshold the burden must fall before the condition improves across
        /// it. 0.15 means a world that turned Ailing at 4.0 stays Ailing until burden is under
        /// 3.4.
        /// </summary>
        public const float Hysteresis = 0.15f;

        /// <param name="flourishing">At or below this, the land flourishes. Well under ailing.</param>
        /// <param name="ailing">At or above this, the land ails.</param>
        /// <param name="stricken">At or above this, the land is stricken. Above ailing.</param>
        public static WorldCondition Derive(
            float burden,
            WorldCondition previous,
            float flourishing,
            float ailing,
            float stricken)
        {
            // Worsening happens AT the threshold — bad news is prompt.
            WorldCondition byThreshold =
                burden >= stricken ? WorldCondition.Stricken :
                burden >= ailing ? WorldCondition.Ailing :
                burden <= flourishing ? WorldCondition.Flourishing :
                WorldCondition.Stable;

            if (byThreshold >= previous) return byThreshold;

            // Improving requires clearing the band below the boundary being crossed. Walk down
            // one step at a time: a burden that collapsed from Stricken straight past Stable's
            // band improves all the way in one pass, but only through boundaries it has cleared.
            WorldCondition current = previous;
            while (current > byThreshold)
            {
                float boundary = BoundaryBelow(current, flourishing, ailing, stricken);
                if (burden > boundary * (1f - Hysteresis)) break;
                current--;
            }

            return current;
        }

        /// <summary>The burden boundary a condition must fall below to improve one step.</summary>
        private static float BoundaryBelow(
            WorldCondition condition, float flourishing, float ailing, float stricken)
        {
            switch (condition)
            {
                case WorldCondition.Stricken: return stricken;
                case WorldCondition.Ailing: return ailing;
                // Stable -> Flourishing: burden must be at/under the flourishing ceiling with
                // the same margin, so the calmest transition cannot flap either.
                default: return flourishing;
            }
        }
    }
}
