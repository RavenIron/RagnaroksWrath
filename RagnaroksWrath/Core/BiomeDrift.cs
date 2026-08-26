using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The drift math, on its own, with no clock, no config and no season lookup.
    ///
    /// Separate from BiomeStateSystem deliberately. The system is a driver: it finds contacted
    /// zones through ZNet, reads config, and asks SeasonSystem for multipliers — none of which
    /// exists off-game. The arithmetic underneath is the part that can be wrong in a way nobody
    /// notices for months, so it lives here where the harness compiles the shipping source and
    /// tests it against a fixed elapsed time.
    ///
    /// TWO PROPERTIES THE ARITHMETIC MUST HAVE:
    ///
    /// 1. EVERY FIELD IS A DEVIATION FROM PRISTINE. Zero means nothing has happened here, for
    ///    all five — including Fertility, which holds soil *depletion* rather than soil quality.
    ///    That is what lets an untouched zone cost nothing to represent, and it is the reason
    ///    ZoneState.IsDefault can decide what reaches disk.
    ///
    /// 2. LINEAR DECAY, NOT EXPONENTIAL. Exponential recovery is the natural-looking choice and
    ///    it never reaches zero — every zone a player ever visited would sit in the store forever
    ///    at 1e-9, and the file would grow without bound while looking perfectly healthy. Linear
    ///    decay with a snap to zero below <see cref="Epsilon"/> guarantees a healed zone actually
    ///    leaves the store.
    /// </summary>
    public static class BiomeDrift
    {
        /// <summary>
        /// Float-dust guard, NOT a noise floor. Linear decay reaches zero by crossing it, so this
        /// only mops up the residue either side of the crossing.
        ///
        /// It was 5e-4 and that was a silent, permanent bug. Decay runs on every field every
        /// pass, so the floor applied to accumulated Frost too: a 30s tick in winter nets about
        /// 3e-5, the next pass saw a value under the floor and zeroed it, and frost could never
        /// climb past roughly 2e-4 no matter how long a player stood in the snow. The store
        /// filled with meaningless dust and the system looked like it was working. Anything above
        /// the per-tick delta reintroduces it, so this must stay far below any plausible rate.
        /// </summary>
        public const float Epsilon = 1e-6f;

        private const float SecondsPerHour = 3600f;

        /// <summary>
        /// Apply <paramref name="elapsedSeconds"/> of drift to one zone.
        ///
        /// Pure: every input is a parameter. The season multipliers are passed in rather than
        /// read, both so this stays testable and because there must be exactly one place season
        /// turns into a number — a second season clock that can disagree with the first is the
        /// failure the mod's compatibility rules exist to prevent, and it would be no better
        /// inside our own codebase than across two mods.
        /// </summary>
        /// <param name="coldMultiplier">From SeasonSystem.ColdMultiplier(). Scales frost gain.</param>
        /// <param name="fireRiskMultiplier">From SeasonSystem.FireRiskMultiplier(). Divides scorch recovery.</param>
        /// <param name="plagueGrowthPerHour">
        /// Effective plague growth — config rate x SeasonSystem.PlagueGrowthMultiplier(),
        /// multiplied out by the CALLER so this stays the one place season already turned into a
        /// number. Optional (default 0) so callers that predate plague are untouched.
        /// </param>
        /// <param name="corruptionBoost">
        /// How strongly zone Corruption accelerates plague growth: growth x (1 + boost x Corruption).
        /// </param>
        public static ZoneState Apply(
            ZoneState state,
            double elapsedSeconds,
            float recoveryPerHour,
            float frostPressurePerHour,
            float coldMultiplier,
            float fireRiskMultiplier,
            float plagueGrowthPerHour = 0f,
            float corruptionBoost = 0f)
        {
            if (elapsedSeconds <= 0.0) return state;

            float hours = (float)(elapsedSeconds / SecondsPerHour);
            float recover = Math.Max(0f, recoveryPerHour) * hours;

            // Dry land holds a burn scar longer. This is the only thing season does to Scorch:
            // no fire, no new damage, just slower healing where it is hot. Guarded because a
            // multiplier of zero would divide recovery into infinity.
            float scorchRecover = fireRiskMultiplier > 0.01f ? recover / fireRiskMultiplier : recover;

            state.Fertility  = Decay(state.Fertility,  recover);
            state.Corruption = Decay(state.Corruption, recover);
            state.Plague     = Decay(state.Plague,     recover);
            state.Scorch     = Decay(state.Scorch,     scorchRecover);
            state.Frost      = Decay(state.Frost,      recover);

            // Frost is the one field that accumulates with no event behind it: fire, plague and
            // farming raise their own, but cold is weather. Applied after recovery so the two
            // combine into a net figure, rather than the order of application deciding which of
            // them wins.
            //
            // No floor on this addition, and none on the decay above large enough to reach it.
            // A tick's gain is orders of magnitude smaller than any value a player would notice,
            // so anything that rounds "too small to matter" away here stops frost accumulating at
            // all rather than merely slowing it. Decay terminates at zero so a zone can leave the
            // store; accumulation stays unrounded so a zone can build its way into one.
            float frostGain = Math.Max(0f, frostPressurePerHour) * hours * Math.Max(0f, coldMultiplier);
            if (frostGain > 0f) state.Frost += frostGain;

            // Plague grows only where it already exists. The gate is the POST-decay value, and
            // that is what "curable" means mechanically: once recovery (or a cure) drives plague
            // through zero, the epsilon snap kills it, the gate closes, and no amount of warm
            // weather resurrects it — the zone needs re-infecting, not merely re-visiting. An
            // unseeded zone likewise stays pristine forever, which is what keeps the store
            // sparse. Growth is linear like frost pressure, not proportional to the current
            // level: proportional growth can never outrun linear decay at low values, so a fresh
            // seed would always die before it took hold and spread could never work.
            if (state.Plague > 0f && plagueGrowthPerHour > 0f)
            {
                float boost = 1f + Math.Max(0f, corruptionBoost) * state.Corruption;
                state.Plague += plagueGrowthPerHour * hours * boost;
            }

            state.Clamp();   // bounds to 0..1, and converts any NaN that reached us to zero
            return state;
        }

        /// <summary>
        /// Linear decay, terminating at exactly zero. Subtraction is what reaches zero — see
        /// property 2 on the class; <see cref="Epsilon"/> only removes the float dust around the
        /// crossing, and must never be raised to a level that could swallow a tick's worth of
        /// accumulation.
        /// </summary>
        private static float Decay(float value, float amount)
        {
            if (value <= 0f) return 0f;
            if (amount <= 0f) return value;   // nothing was taken; do not round away what we kept

            value -= amount;
            return value <= Epsilon ? 0f : value;
        }
    }
}
