using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Maps active fire positions to the set of zones that are burning.
    ///
    /// Separate from FireSystem for the same reason BiomeDrift is separate from
    /// BiomeStateSystem: the system is a bridge to another mod and cannot run off-game, while
    /// this — the part that decides WHICH zones scorch — is pure and lives where the harness
    /// compiles and tests it.
    ///
    /// DELIBERATELY BINARY. A zone with one burning fence post scorches at the same rate as a
    /// zone with forty burning pieces: the fires themselves already scale with severity
    /// (FireFront spreads, so a big fire covers more zones), and scaling scorch by fire count
    /// as well would double-count the same severity. If that ever changes, it changes here,
    /// under a test.
    /// </summary>
    public static class FireScorch
    {
        /// <summary>
        /// Append each distinct zone containing at least one fire position to
        /// <paramref name="into"/>. Order follows first appearance; duplicates are dropped.
        ///
        /// Linear-scan dedupe, not a HashSet, for the same reason as BiomeStateSystem's contact
        /// list: fires cluster, so this list is a handful of zones, and staying allocation-free
        /// on a path that runs every tick matters more than asymptotics.
        /// </summary>
        public static void CollectBurningZones(List<Vector3> firePositions, List<ZoneKey> into)
        {
            if (firePositions == null || into == null) return;

            for (int i = 0; i < firePositions.Count; i++)
            {
                ZoneKey zone = ZoneKey.FromWorldPos(firePositions[i]);
                if (!into.Contains(zone)) into.Add(zone);
            }
        }

        /// <summary>
        /// One tick's scorch increment for a burning zone. Rate is per minute because scorch is
        /// slow by design — a value per second invites configs that fully char a zone in one
        /// short fire.
        /// </summary>
        public static float ScorchDelta(float ratePerMinute, float deltaSeconds)
        {
            if (ratePerMinute <= 0f || deltaSeconds <= 0f) return 0f;
            if (float.IsNaN(ratePerMinute) || float.IsNaN(deltaSeconds)) return 0f;

            return ratePerMinute * (deltaSeconds / 60f);
        }
    }
}
