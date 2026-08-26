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
        public const float FullRate = 30f;

        public static float EmissionFor(float scorch, float density)
        {
            if (float.IsNaN(scorch) || float.IsNaN(density)) return 0f;
            if (scorch < VisibleFloor || density <= 0f) return 0f;

            float t = (Math.Min(scorch, 1f) - VisibleFloor) / (1f - VisibleFloor);
            return FullRate * t * Math.Min(density, 4f);
        }
    }
}
