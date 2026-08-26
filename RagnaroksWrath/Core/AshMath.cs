using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// How much ash a scorch level earns — FogMath's shape for the burn scars. Unlike the
    /// plague floor (a DISCOVERY gate: seeds must not telegraph), the ash floor is only
    /// taste and perf: any real burn may show, but trace scorch from a brushed-past fire
    /// should not dust the whole map. Fades with the land's own healing, because the
    /// emission reads the same store BiomeDrift writes.
    /// </summary>
    public static class AshMath
    {
        public const float VisibleFloor = 0.1f;

        /// <summary>Rate AT the floor. Non-zero on purpose, unlike fog: ash is not a
        /// discovery mechanic, and the 0.14.0 lesson was that a ramp-from-zero over a
        /// zone-sized box renders a real burn scar invisible (~1 faint mote per 84 square
        /// meters at scar-level scorch — arithmetic nobody ran until a player stood in
        /// their own scar and saw nothing).</summary>
        public const float BaseRate = 12f;
        public const float FullRate = 80f;

        public static float EmissionFor(float scorch, float density)
        {
            if (float.IsNaN(scorch) || float.IsNaN(density)) return 0f;
            if (scorch < VisibleFloor || density <= 0f) return 0f;

            float t = (Math.Min(scorch, 1f) - VisibleFloor) / (1f - VisibleFloor);
            return (BaseRate + (FullRate - BaseRate) * t) * Math.Min(density, 4f);
        }
    }
}
