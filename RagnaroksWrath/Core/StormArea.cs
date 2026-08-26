using UnityEngine;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Whether a world position lies inside a storm's area of effect.
    ///
    /// The maths here is copied EXACTLY from `RandEventSystem.IsInsideRandomEventArea`, and that
    /// is the whole point of the file. Vanilla uses that test to decide whether a player sees the
    /// storm's banner and its spawns; we use ours to decide whether the storm's gameplay
    /// multipliers apply. Those are two enforcers of one boundary, and if they disagree the
    /// symptom is a player standing under a storm banner taking no extra fire risk — which is
    /// nearly unreadable from a log, because both halves are behaving exactly as written.
    ///
    /// Three details are load-bearing, and all three are vanilla's:
    ///
    /// 1. Distance is XZ ONLY. A storm reaches up the mountain above it, not merely across flat
    ///    ground. Using Vector3.Distance would shrink the area for anyone climbing.
    /// 2. The comparison is strictly less-than, so the perimeter itself is outside.
    /// 3. Anything above y = 3000 is outside regardless — vanilla's guard for players who are
    ///    mid-teleport or otherwise off the map.
    /// </summary>
    public static class StormArea
    {
        /// <summary>Above this height a position is outside every storm. Vanilla's constant.</summary>
        public const float SkyCeiling = 3000f;

        public static bool Contains(Vector3 centre, float range, Vector3 position)
        {
            if (position.y > SkyCeiling) return false;

            return DistanceXZ(position, centre) < range;
        }

        /// <summary>
        /// Mirrors `Utils.DistanceXZ`. Reimplemented rather than called so this file stays free of
        /// game types and the harness can compile and test it — the formula is two subtractions
        /// and a square root, and it is pinned by a test against a known distance.
        /// </summary>
        public static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;

            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
