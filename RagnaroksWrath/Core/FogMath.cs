using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// How much miasma a plague level earns. Pure, because this number feeds a particle
    /// emitter every second for the whole session and a NaN or a negative here is a visual
    /// bug nobody can screenshot usefully.
    /// </summary>
    public static class FogMath
    {
        /// <summary>
        /// Particles per second for a plague level, scaled by the density config.
        ///
        /// Zero below the floor: a freshly seeded zone (0.05) should NOT visibly fog — the
        /// fog is how a player discovers a zone has turned bad, and if seeds glow the whole
        /// frontier telegraphs itself the tick it spreads. Above the floor, emission ramps
        /// linearly to the cap.
        /// </summary>
        public const float VisibleFloor = 0.15f;
        public const float FullRate = 40f;

        public static float EmissionFor(float plague, float density)
        {
            if (float.IsNaN(plague) || float.IsNaN(density)) return 0f;
            if (plague < VisibleFloor || density <= 0f) return 0f;

            float t = (Math.Min(plague, 1f) - VisibleFloor) / (1f - VisibleFloor);
            return FullRate * t * Math.Min(density, 4f);
        }
    }
}
