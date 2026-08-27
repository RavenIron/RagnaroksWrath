using System;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Where storm fires BEGIN — the arithmetic half of lightning. Devastating Storms have
    /// multiplied fire RISK since 0.2.2 without ever producing a flame; lightning closes
    /// that gap: a rare bolt during a storm asks FireFront to ignite ground near a player
    /// standing under it. This class is only the dice and the geometry — per-tick chance
    /// from a configured mean, and the ring around the player where the bolt lands. The
    /// gates (a real player present, rain, the homestead standoff) live in FireSystem, and
    /// the fire itself is entirely FireFront's: a strike is one ignite call, never a
    /// second simulation.
    /// </summary>
    public static class LightningStrike
    {
        /// <summary>Per-tick chance of one strike, from the configured mean minutes
        /// between strikes while a storm holds at least one player. Garbage disables
        /// rather than floods — the PlagueGenesis contract, same shape on purpose.</summary>
        public static float ChancePerTick(float intervalSeconds, float meanMinutes)
        {
            if (float.IsNaN(intervalSeconds) || intervalSeconds <= 0f) return 0f;
            if (float.IsNaN(meanMinutes) || meanMinutes <= 0f) return 0f;

            float chance = intervalSeconds / (meanMinutes * 60f);
            return chance > 1f ? 1f : chance;
        }

        /// <summary>
        /// The bolt's landing point: area-uniform over the ring between minRadius and
        /// maxRadius around the anchor (sqrt keeps strikes from bunching at the inner
        /// edge), at the anchor's own height — a strike lands near a LOADED player, so
        /// the anchor's Y is honest, and the server must never ask for ground height
        /// (it returns its input on a miss). u1/u2 are uniform [0,1) rolls, taken as
        /// parameters so the geometry is deterministic under test. NaN or negative radii
        /// clamp to zero; an inverted pair collapses to the surviving honest value.
        /// </summary>
        public static Vector3 StrikePoint(Vector3 anchor, double u1, double u2,
                                          float minRadius, float maxRadius)
        {
            float lo = (float.IsNaN(minRadius) || minRadius < 0f) ? 0f : minRadius;
            float hi = (float.IsNaN(maxRadius) || maxRadius < lo) ? lo : maxRadius;

            double r = Math.Sqrt(lo * lo + (hi * hi - lo * lo) * u1);
            double angle = u2 * 2.0 * Math.PI;

            return new Vector3(
                anchor.x + (float)(r * Math.Cos(angle)),
                anchor.y,
                anchor.z + (float)(r * Math.Sin(angle)));
        }
    }
}
