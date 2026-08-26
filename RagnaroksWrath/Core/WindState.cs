namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Gameplay wind, derived from vanilla's wind and whatever a storm adds on top.
    ///
    /// READ-ONLY BY CONSTRUCTION. Nothing in this mod may write wind: `EnvMan` owns it, and
    /// Seasonality and SkyNet Redux both patch that ground. What we produce is a separate
    /// gameplay number that FireSystem and others read; the sky is never told about it.
    ///
    /// Kept apart from WindSystem so the arithmetic can be tested off-game — the system itself
    /// touches EnvMan and cannot run in the harness.
    /// </summary>
    public static class WindState
    {
        /// <summary>
        /// Combine vanilla's 0..1 wind intensity with a storm multiplier, back into 0..1.
        ///
        /// Clamped and NaN-guarded for the same reason ZoneState is: this value feeds fire spread
        /// rate, and a NaN there survives every later multiply and silently poisons whatever it
        /// touches. A wrong-but-bounded number is a bug; a NaN is a bug that spreads.
        /// </summary>
        public static float Combine(float baseIntensity, float stormMultiplier)
        {
            if (float.IsNaN(baseIntensity) || float.IsNaN(stormMultiplier)) return 0f;
            if (baseIntensity <= 0f) return 0f;

            float multiplier = stormMultiplier < 0f ? 0f : stormMultiplier;
            float combined = baseIntensity * multiplier;

            if (combined < 0f) return 0f;
            return combined > 1f ? 1f : combined;
        }
    }
}
