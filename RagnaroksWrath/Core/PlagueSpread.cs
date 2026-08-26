using System.Collections.Generic;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Which zones a plague can spread INTO. Pure — the harness compiles and tests it; the
    /// dice, the storm multiplier and the store writes stay in PlagueSystem.
    ///
    /// THE CONTAINMENT ARGUMENT, because "zone-based spreading sickness" is one bad decision
    /// away from blanketing the map while nobody plays:
    ///
    /// 1. Only zones at or above the spread threshold are sources. A fresh seed sits far below
    ///    it.
    /// 2. A seed only GROWS toward the threshold through BiomeDrift, which runs on the zone
    ///    clock — i.e. on player contact. Where nobody goes, seeds stay seeds.
    /// 3. Already-infected zones are never re-seeded, so the frontier is only ever the ring of
    ///    pristine neighbours around contact-grown hotspots.
    ///
    /// Together: the plague advances one ring past wherever players have actually been, and no
    /// further. Spread is 4-way (orthogonal) rather than 8-way — sickness creeping along shared
    /// borders reads better than corner-hopping, and it halves the frontier for free.
    /// </summary>
    public static class PlagueSpread
    {
        /// <summary>
        /// Append each candidate target — an uninfected orthogonal neighbour of a source at or
        /// above <paramref name="spreadThreshold"/> — to <paramref name="into"/>, deduplicated.
        ///
        /// <paramref name="infected"/> is every zone with any plague at all (sources included),
        /// so a target is seeded at most once however many hot zones border it.
        /// </summary>
        public static void CollectSpreadTargets(
            List<KeyValuePair<ZoneKey, float>> plaguedZones,
            float spreadThreshold,
            HashSet<ZoneKey> infected,
            List<ZoneKey> into)
        {
            if (plaguedZones == null || infected == null || into == null) return;

            for (int i = 0; i < plaguedZones.Count; i++)
            {
                if (plaguedZones[i].Value < spreadThreshold) continue;

                ZoneKey source = plaguedZones[i].Key;

                TryAdd(new ZoneKey(source.X + 1, source.Y), infected, into);
                TryAdd(new ZoneKey(source.X - 1, source.Y), infected, into);
                TryAdd(new ZoneKey(source.X, source.Y + 1), infected, into);
                TryAdd(new ZoneKey(source.X, source.Y - 1), infected, into);
            }
        }

        private static void TryAdd(ZoneKey zone, HashSet<ZoneKey> infected, List<ZoneKey> into)
        {
            if (infected.Contains(zone)) return;
            if (!into.Contains(zone)) into.Add(zone);   // frontier is small; linear scan, as elsewhere
        }
    }
}
