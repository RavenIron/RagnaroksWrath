using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// How blight begets blight: sustained plague or scorch corrupts the land under it.
    /// Corruption is the slow echo of disaster — it accrues while the loud fields are high,
    /// outlives them (recovery is BiomeDrift's, at its own pace), and feeds back into plague
    /// growth through the corruption boost. This is the loop that makes a neglected outbreak
    /// worse than two clean ones.
    ///
    /// Pure, and rate-based on LIVE tick time (the caller's dt) per docs/zone-clock-ownership.md:
    /// corruption accrues while the pressure exists, event-style like fire scorch — not against
    /// the zone clock, which stays BiomeDrift's alone.
    /// </summary>
    public static class EcologyPressure
    {
        /// <summary>
        /// One tick of corruption pressure for one zone. Zero unless plague or scorch stands at
        /// or above its threshold; above, pressure scales with how far past the worst offender
        /// is — a fully plagued zone corrupts faster than one scraping the line.
        /// </summary>
        public static ZoneState Apply(
            ZoneState state,
            float deltaSeconds,
            float corruptionPerHour,
            float plagueThreshold,
            float scorchThreshold)
        {
            if (deltaSeconds <= 0f || corruptionPerHour <= 0f) return state;
            if (float.IsNaN(deltaSeconds) || float.IsNaN(corruptionPerHour)) return state;

            float excess = 0f;
            if (plagueThreshold > 0f && state.Plague >= plagueThreshold)
                excess = Math.Max(excess, (state.Plague - plagueThreshold) / plagueThreshold);
            if (scorchThreshold > 0f && state.Scorch >= scorchThreshold)
                excess = Math.Max(excess, (state.Scorch - scorchThreshold) / scorchThreshold);

            // At the threshold exactly, excess is 0 — pressure starts as a trickle and ramps,
            // rather than switching on at full rate one epsilon past the line.
            if (state.Plague < plagueThreshold && state.Scorch < scorchThreshold) return state;

            state.Corruption += corruptionPerHour * (deltaSeconds / 3600f) * (0.25f + excess);
            state.Clamp();
            return state;
        }
    }
}
