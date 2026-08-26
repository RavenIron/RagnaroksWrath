using System;
using System.Collections.Generic;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Task 13 phase C's pure half: who holds a zone's memory. Per zone, the dominant CARER
    /// and dominant HARMER are computed from the ledger's columns, floor-gated (nobody wins
    /// ground they barely touched) and hysteresis-held (an incumbent keeps a zone until a
    /// challenger beats their CURRENT value by the band — the WorldState anti-flap lesson,
    /// applied to people).
    ///
    /// A FLIP — the announceable event — happens only when a held zone changes hands while
    /// BOTH rivals stand above the floor: the contest voice speaks only about ground both
    /// of them genuinely shaped. A vacancy filled, or an incumbent fading below the floor,
    /// changes the holder silently.
    /// </summary>
    public static class RivalryContest
    {
        public struct Holder
        {
            public long Player;
            public float Value;
        }

        /// <summary>One zone's change of hands, worth announcing.</summary>
        public struct Flip
        {
            public ZoneKey Zone;
            public long From;
            public long To;
        }

        /// <summary>
        /// Recompute one column's dominance for every zone present in <paramref name="values"/>,
        /// updating <paramref name="holders"/> in place and appending announceable flips.
        ///
        /// <paramref name="values"/> is zone -> (player -> column value); only entries at or
        /// above <paramref name="floor"/> compete. An incumbent is dethroned when a challenger
        /// exceeds their current value x (1 + <paramref name="hysteresis"/>); an incumbent
        /// whose value fell below the floor vacates silently. Zones absent from
        /// <paramref name="values"/> (fully decayed) vacate silently too.
        /// </summary>
        public static void Update(
            Dictionary<ZoneKey, Dictionary<long, float>> values,
            Dictionary<ZoneKey, Holder> holders,
            float floor, float hysteresis,
            List<Flip> flips)
        {
            if (values == null || holders == null) return;

            // Vacate zones whose rows fully decayed away.
            List<ZoneKey> dead = null;
            foreach (KeyValuePair<ZoneKey, Holder> kv in holders)
                if (!values.ContainsKey(kv.Key))
                    (dead = dead ?? new List<ZoneKey>(4)).Add(kv.Key);
            if (dead != null)
                for (int i = 0; i < dead.Count; i++) holders.Remove(dead[i]);

            foreach (KeyValuePair<ZoneKey, Dictionary<long, float>> zone in values)
            {
                // The strongest contender at or above the floor.
                long best = 0;
                float bestValue = 0f;
                foreach (KeyValuePair<long, float> p in zone.Value)
                {
                    if (float.IsNaN(p.Value) || p.Value < floor) continue;
                    if (p.Value > bestValue) { best = p.Key; bestValue = p.Value; }
                }

                bool held = holders.TryGetValue(zone.Key, out Holder incumbent);
                float incumbentValue = 0f;
                if (held)
                {
                    zone.Value.TryGetValue(incumbent.Player, out incumbentValue);
                    if (float.IsNaN(incumbentValue)) incumbentValue = 0f;
                }

                if (best == 0)
                {
                    // Nobody above the floor: the ground is unclaimed again.
                    if (held) holders.Remove(zone.Key);
                    continue;
                }

                if (!held || incumbentValue < floor)
                {
                    // Vacancy (or a faded incumbent): crowned silently — a walkover is
                    // not a contest, and announcing it would narrate one-player worlds.
                    holders[zone.Key] = new Holder { Player = best, Value = bestValue };
                    continue;
                }

                if (best == incumbent.Player)
                {
                    holders[zone.Key] = new Holder { Player = best, Value = bestValue };
                    continue;
                }

                // A live challenge: the incumbent stands above the floor and someone else
                // is stronger. Hysteresis decides; both-above-floor makes it announceable.
                if (bestValue >= incumbentValue * (1f + Math.Max(0f, hysteresis)))
                {
                    holders[zone.Key] = new Holder { Player = best, Value = bestValue };
                    flips?.Add(new Flip { Zone = zone.Key, From = incumbent.Player, To = best });
                }
                else
                {
                    holders[zone.Key] = new Holder { Player = incumbent.Player, Value = incumbentValue };
                }
            }
        }

        /// <summary>How many zones a player currently holds in a holder map — the title
        /// layer's question (Warden counts care holdings, Despoiler harm holdings).</summary>
        public static int ZonesHeld(Dictionary<ZoneKey, Holder> holders, long playerId)
        {
            if (holders == null || playerId == 0) return 0;
            int n = 0;
            foreach (KeyValuePair<ZoneKey, Holder> kv in holders)
                if (kv.Value.Player == playerId) n++;
            return n;
        }
    }
}
