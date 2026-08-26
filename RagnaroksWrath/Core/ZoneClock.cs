using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Per-zone elapsed-time accounting, measured against real UTC rather than Valheim's world clock.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// `ZNet.UpdateNetTime` returns early when zero players are connected, so on an empty
    /// dedicated server the world clock stops entirely. Anything measured against it cannot
    /// advance. Vanilla is right not to care — the zone would be unloaded anyway — but a
    /// world-simulation mod is exactly the case where it matters.
    ///
    /// The fix is NOT to patch the global clock. AwayFromHome credits offline production by
    /// correcting each machine's own start time, and unfreezing the world clock would
    /// double-credit every furnace on the server. We stay entirely out of that path and keep
    /// our own real-time ledger instead.
    ///
    /// CREDIT ON CONTACT
    /// -----------------
    /// We never tick drift on zone load state. AwayFromHome's keeper actively loads a site,
    /// holds it, then unloads it, rotating through every Keeper Stone on the server — so
    /// "tick while loaded" would make zones drift based on whether someone happened to build
    /// a stone nearby. Instead every zone records when it was last accounted for, and the
    /// elapsed real time is credited in one lump the next time anything touches it.
    ///
    /// A zone nobody visits for a week accrues a week of drift the moment it is next read,
    /// with no CPU spent in between and no dependence on the world clock at all.
    /// </summary>
    public static class ZoneClock
    {
        private static readonly Dictionary<ZoneKey, long> _lastContactUtcTicks =
            new Dictionary<ZoneKey, long>(256);

        private static long NowTicks => DateTime.UtcNow.Ticks;

        /// <summary>
        /// Real seconds elapsed for this zone since it was last credited, then re-stamps it.
        ///
        /// Returns 0.0 the first time a zone is ever seen — a zone with no history has no
        /// backlog, and inventing one would mean a fresh world instantly drifts by however
        /// long the save file happens to have existed.
        ///
        /// The result is clamped to <see cref="ModConfig.MaxCreditSeconds"/>. That cap is the
        /// difference between "you were away a month, here is a month of plague" and a
        /// playable world.
        /// </summary>
        public static double CreditOnContact(ZoneKey zone)
        {
            long now = NowTicks;

            if (!_lastContactUtcTicks.TryGetValue(zone, out long last))
            {
                _lastContactUtcTicks[zone] = now;
                return 0.0;
            }

            _lastContactUtcTicks[zone] = now;

            double seconds = (now - last) / (double)TimeSpan.TicksPerSecond;

            // Clock moved backwards (NTP correction, host reboot, save copied between machines).
            // Credit nothing rather than a negative or absurd delta.
            if (seconds < 0.0) return 0.0;

            double cap = ModConfig.MaxCreditSeconds.Value;
            return seconds > cap ? cap : seconds;
        }

        /// <summary>
        /// Elapsed seconds without re-stamping — for reads that must not consume the backlog
        /// (diagnostics, console output, a system deciding whether it is worth acting).
        /// </summary>
        public static double PeekElapsed(ZoneKey zone)
        {
            if (!_lastContactUtcTicks.TryGetValue(zone, out long last)) return 0.0;
            double seconds = (NowTicks - last) / (double)TimeSpan.TicksPerSecond;
            if (seconds < 0.0) return 0.0;
            double cap = ModConfig.MaxCreditSeconds.Value;
            return seconds > cap ? cap : seconds;
        }

        /// <summary>Stamp a zone as current without crediting anything. Used on first-generation.</summary>
        public static void MarkContact(ZoneKey zone) => _lastContactUtcTicks[zone] = NowTicks;

        public static bool HasHistory(ZoneKey zone) => _lastContactUtcTicks.ContainsKey(zone);

        public static int TrackedZoneCount => _lastContactUtcTicks.Count;

        /// <summary>Drop a zone's timing history. Its next contact will credit zero.</summary>
        public static void Forget(ZoneKey zone) => _lastContactUtcTicks.Remove(zone);

        public static void Clear() => _lastContactUtcTicks.Clear();

        // ---- persistence hand-off -------------------------------------------------------
        // Sparse by construction: only zones that have actually been contacted appear here.

        public static IEnumerable<KeyValuePair<ZoneKey, long>> Snapshot() => _lastContactUtcTicks;

        public static void Restore(ZoneKey zone, long utcTicks) => _lastContactUtcTicks[zone] = utcTicks;
    }
}
