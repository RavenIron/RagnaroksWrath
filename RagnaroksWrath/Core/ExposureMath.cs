using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Plague exposure on a player: the pure half of HealthSystem (task 11). Standing on
    /// plagued ground builds a 0..1 meter; leaving drains it. Everything here is arithmetic
    /// on config values so the harness can pin the rates the same way it pins drift.
    ///
    /// NON-LETHAL BY CONSTRUCTION: this file only ever produces exposure levels and REGEN
    /// multipliers. There is no damage path anywhere in HealthSystem — the sickness weakens,
    /// and the last hit always comes from the world.
    ///
    /// The accrual floor is FogMath.VisibleFloor, deliberately the same constant: the fog is
    /// the discovery mechanic, and the sickness must not telegraph what the fog hides.
    /// </summary>
    public static class ExposureMath
    {
        /// <summary>Sync quantization step — also the smallest change worth persisting.</summary>
        public const float QuantizeStep = 0.01f;

        /// <summary>
        /// Exposure after standing <paramref name="deltaSeconds"/> on ground with this much
        /// plague. Below the shared floor nothing accrues (callers should Decay instead).
        /// <paramref name="minutesToMax"/> is the time from clean to max at plague 1.0; the
        /// rate scales linearly with actual plague underfoot. Poison resistance (mead, gear)
        /// multiplies the rate by <paramref name="poisonResistMultiplier"/>.
        /// </summary>
        public static float Accrue(float current, float plague, float minutesToMax,
                                   bool poisonResist, float poisonResistMultiplier,
                                   float deltaSeconds)
        {
            if (float.IsNaN(current)) current = 0f;
            if (float.IsNaN(plague) || plague < FogMath.VisibleFloor) return Clamp01(current);
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f) return Clamp01(current);

            float ratePerSecond = Math.Min(plague, 1f) / (Math.Max(1f, minutesToMax) * 60f);
            if (poisonResist) ratePerSecond *= Clamp01(poisonResistMultiplier);

            return Clamp01(current + ratePerSecond * deltaSeconds);
        }

        /// <summary>
        /// Exposure after <paramref name="deltaSeconds"/> off plagued ground.
        /// <paramref name="recoveryMinutes"/> is the time from max back to clean; the rested
        /// bonus multiplies the drain. Terminates at exactly zero — a recovered player is
        /// clean, not asymptotically almost-clean (the through-zero lesson from BiomeDrift).
        /// </summary>
        public static float Decay(float current, float recoveryMinutes,
                                  bool rested, float restedMultiplier,
                                  float deltaSeconds)
        {
            if (float.IsNaN(current) || current <= 0f) return 0f;
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f) return Clamp01(current);

            float ratePerSecond = 1f / (Math.Max(1f, recoveryMinutes) * 60f);
            if (rested) ratePerSecond *= Math.Max(1f, restedMultiplier);

            float next = current - ratePerSecond * deltaSeconds;
            return next <= 0f ? 0f : Clamp01(next);
        }

        /// <summary>Severity tier 0..3 for the announcement layer and the status icon.</summary>
        public static int TierFor(float exposure, float tier1, float tier2, float tier3)
        {
            if (float.IsNaN(exposure)) return 0;
            if (exposure >= tier3) return 3;
            if (exposure >= tier2) return 2;
            if (exposure >= tier1) return 1;
            return 0;
        }

        /// <summary>
        /// Stamina regen multiplier: 1.0 below the first tier, then ramping linearly to
        /// <paramref name="atMax"/> at exposure 1. Stamina fails FIRST — sickness in the body
        /// before the wound (the owner's palette call).
        /// </summary>
        public static float StaminaRegenMultiplier(float exposure, float tier1, float atMax)
            => Ramp(exposure, tier1, atMax);

        /// <summary>
        /// Health regen multiplier: 1.0 below the SECOND tier, then ramping linearly to
        /// <paramref name="atMax"/> at exposure 1 — the wound half arrives after the fatigue.
        /// </summary>
        public static float HealthRegenMultiplier(float exposure, float tier2, float atMax)
            => Ramp(exposure, tier2, atMax);

        /// <summary>True when two exposure values differ by a full sync/persist step, or one
        /// is exactly clean and the other is not (zero is a state, not just a number).</summary>
        public static bool QuantizedDiffer(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b)) return true;
            if ((a == 0f) != (b == 0f)) return true;
            return Math.Abs(a - b) >= QuantizeStep;
        }

        private static float Ramp(float exposure, float from, float atMax)
        {
            if (float.IsNaN(exposure) || float.IsNaN(from) || float.IsNaN(atMax)) return 1f;
            if (exposure <= from || from >= 1f) return 1f;

            float t = Clamp01((exposure - from) / (1f - from));
            float floor = Clamp01(atMax);
            return 1f + (floor - 1f) * t;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
