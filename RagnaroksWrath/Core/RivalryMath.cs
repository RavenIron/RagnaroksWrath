using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The world keeps score — task 13 phase A's pure half. Attribution amounts, decay, and
    /// the once-per-plant watermark, all as arithmetic on config values so the harness pins
    /// the rates before the game ever runs them.
    ///
    /// Decay is exponential by half-life: grudges and gratitude both FADE, which is what
    /// keeps the ledger sparse and the world forgiving on a long enough timeline. A half-
    /// life of zero or less disables decay rather than dividing by it.
    /// </summary>
    public static class RivalryMath
    {
        /// <summary>Rows where harm and care have both faded below this are pruned at write.</summary>
        public const float PruneEpsilon = 1e-4f;

        /// <summary>Multiplier for <paramref name="deltaSeconds"/> of fading at the given
        /// half-life: exactly 0.5 after one half-life, 1.0 when decay is disabled.</summary>
        public static float DecayFactor(float halfLifeHours, float deltaSeconds)
        {
            if (float.IsNaN(halfLifeHours) || halfLifeHours <= 0f) return 1f;
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f) return 1f;

            return (float)Math.Pow(0.5, deltaSeconds / (halfLifeHours * 3600.0));
        }

        /// <summary>A zone's total damage: the sum of every deviation from pristine. The
        /// healing-presence hook books care from DECREASES in this number.</summary>
        public static float ZoneDamage(ZoneState state)
        {
            float sum = 0f;
            if (!float.IsNaN(state.Fertility)) sum += state.Fertility;
            if (!float.IsNaN(state.Corruption)) sum += state.Corruption;
            if (!float.IsNaN(state.Scorch)) sum += state.Scorch;
            if (!float.IsNaN(state.Frost)) sum += state.Frost;
            if (!float.IsNaN(state.Plague)) sum += state.Plague;
            return sum;
        }

        /// <summary>Care earned by a zone healing from <paramref name="damageBefore"/> to
        /// <paramref name="damageAfter"/>. Damage INCREASING books nothing — harm has its
        /// own attributed writers, and presence during decay is not culpability.</summary>
        public static float CareFromHealing(float damageBefore, float damageAfter, float perHealedPoint)
        {
            if (float.IsNaN(damageBefore) || float.IsNaN(damageAfter)
                || float.IsNaN(perHealedPoint) || perHealedPoint <= 0f) return 0f;

            float healed = damageBefore - damageAfter;
            return healed > 0f ? healed * perHealedPoint : 0f;
        }

        /// <summary>An even split of credit among everyone whose contact enabled it.</summary>
        public static float SplitAmong(float total, int presentPlayers)
        {
            if (float.IsNaN(total) || total <= 0f || presentPlayers <= 0) return 0f;
            return total / presentPlayers;
        }

        /// <summary>
        /// Whether a crop is NEW against the tending watermark. The watermark is persisted
        /// with the ledger and advanced to the newest plantTime after each full sweep, so
        /// every plant books its care exactly once — a server restart cannot re-credit a
        /// standing field, which would otherwise make rebooting a farming strategy.
        /// </summary>
        public static bool IsNewPlant(long plantTimeTicks, long watermarkTicks)
            => plantTimeTicks > 0 && plantTimeTicks > watermarkTicks;

        // ---- phase B: the grudge -------------------------------------------------------

        /// <summary>
        /// A zone's grudge against one player: their harm net of their care, scaled and
        /// clamped to 0..1. Care OFFSETS harm — tending a zone you burned genuinely
        /// mollifies it — and a net carer holds no grudge at all, never a negative one
        /// (mercies are phase C's business, not a sign flip here).
        /// </summary>
        public static float GrudgeFor(float harm, float care, float scale)
        {
            if (float.IsNaN(harm) || float.IsNaN(care) || float.IsNaN(scale)) return 0f;

            float g = (harm - care) * Math.Max(0f, scale);
            if (g <= 0f) return 0f;
            return g > 1f ? 1f : g;
        }

        /// <summary>Recovery slows on grudged ground for the grudged: x(1 - g/2), so even a
        /// full grudge only HALVES healing — the land sulks, it does not refuse.</summary>
        public static float GrudgedRecovery(float recoveryPerHour, float grudge)
        {
            if (float.IsNaN(recoveryPerHour) || float.IsNaN(grudge)) return recoveryPerHour;
            float g = grudge < 0f ? 0f : (grudge > 1f ? 1f : grudge);
            return recoveryPerHour * (1f - g * 0.5f);
        }

        /// <summary>Harmful pressure quickens on grudged ground: x(1 + g), up to doubled at a
        /// full grudge. Applied to frost pressure and plague growth alike.</summary>
        public static float GrudgedPressure(float pressurePerHour, float grudge)
        {
            if (float.IsNaN(pressurePerHour) || float.IsNaN(grudge)) return pressurePerHour;
            float g = grudge < 0f ? 0f : (grudge > 1f ? 1f : grudge);
            return pressurePerHour * (1f + g);
        }
    }
}
