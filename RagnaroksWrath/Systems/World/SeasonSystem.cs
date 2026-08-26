using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Feedback;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Fall   = 2,
        Winter = 3
    }

    /// <summary>
    /// Tracks the current season as GAMEPLAY STATE ONLY.
    ///
    /// HOUSE STYLE RULE 4 — read this before editing.
    /// This system never selects an EnvMan environment and never touches a material or texture.
    /// Seasonality (RustyMods) owns that ground: 558K downloads, 608 dependent mods. Two mods
    /// forcing environment selection on the same client is a straight conflict where whichever
    /// patches last silently wins. So the world's *appearance* is not ours; the world's
    /// *behaviour* is. Season here exists to feed fire risk, plague growth, farming yield and
    /// contest escalation — nothing else.
    ///
    /// TWO MODES
    /// ---------
    /// - Seasonality installed: read its season from vanilla global keys. Deferring is correct
    ///   even though our own clock would be simpler — two disagreeing season clocks on one server
    ///   is a worse experience than either mod alone, and theirs is the one players can see.
    /// - Seasonality absent: run our own lightweight clock off world age. No visual output.
    /// </summary>
    public class SeasonSystem : IWorldSystem
    {
        public const string SeasonalityGuid = "RustyMods.Seasonality";

        // Seasonality publishes season as vanilla global keys.
        private static readonly string[] SeasonalityKeys =
        {
            "season_spring",
            "season_summer",
            "season_fall",
            "season_winter"
        };

        public string Name => "SeasonSystem";
        public bool Enabled => ModConfig.EnableSeason.Value;
        public float IntervalSeconds => 10f;

        /// <summary>Current season. Safe to read from any system; never null-state.</summary>
        public static Season Current { get; private set; } = Season.Spring;

        /// <summary>True when we are deferring to Seasonality rather than running our own clock.</summary>
        public static bool DeferringToSeasonality { get; private set; }

        private Season _lastAnnounced = Season.Spring;
        private bool _firstResolveDone;

        /// <summary>
        /// EnvMan.GetCurrentDay is PRIVATE in the shipping game.
        ///
        /// The publicized assemblies make it public at COMPILE time only — at runtime the real
        /// assembly_valheim.dll has its original accessibility, and Mono refuses the call with
        /// "is inaccessible from method". It compiles clean and fails only in-game, which is
        /// exactly why the proof-of-life logging exists.
        ///
        /// A cached delegate is the fix. Resolved once, then called at near-native speed —
        /// not reflection on every tick.
        /// </summary>
        private static Func<EnvMan, int> _getCurrentDay;
        private static bool _dayAccessorResolved;

        public void Initialise()
        {
            DeferringToSeasonality = Chainloader.PluginInfos.ContainsKey(SeasonalityGuid);

            if (DeferringToSeasonality)
            {
                RagnaroksWrath.Log.LogInfo(
                    "SeasonSystem: Seasonality detected — deferring season tracking to it. " +
                    "We will read its global keys and drive gameplay only; no visual output from us.");
            }
            else
            {
                RagnaroksWrath.Log.LogInfo(
                    "SeasonSystem: Seasonality not present — running our own gameplay-only season clock.");
            }
        }

        public void Tick(float deltaSeconds)
        {
            Season resolved = DeferringToSeasonality
                ? ReadFromSeasonality()
                : ComputeFromWorldAge();

            // Resolve-on-first-tick, not on Initialise. ZoneSystem may not be ready at plugin load,
            // and a first resolve that legitimately fails must not latch this system off — that
            // exact shape (an early-out the retry can never get past) has cost a full playtest
            // before. See Prior Art / EnvironmentalControl.
            if (!_firstResolveDone)
            {
                Current = resolved;
                _lastAnnounced = resolved;
                _firstResolveDone = true;
                RagnaroksWrath.Log.LogInfo($"SeasonSystem: initial season resolved as {resolved}.");
                return;
            }

            if (resolved == Current) return;

            Season previous = Current;
            Current = resolved;

            MessageFeed.Verbose($"SeasonSystem: {previous} -> {Current}");
            AnnounceIfChanged();
        }

        /// <summary>
        /// Read Seasonality's season from global keys.
        ///
        /// Falls back to the last known season if no key is set — which happens briefly at world
        /// load before Seasonality has written one. Returning a default here instead would make
        /// every server load look like a season change to Spring.
        /// </summary>
        private Season ReadFromSeasonality()
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return Current;

            try
            {
                for (int i = 0; i < SeasonalityKeys.Length; i++)
                {
                    if (zs.GetGlobalKeyExact(SeasonalityKeys[i]))
                        return (Season)i;
                }
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"SeasonSystem: global key read failed: {ex.Message}");
            }

            return Current;
        }

        /// <summary>
        /// Our own clock, used only when Seasonality is absent. Driven off world age in days so it
        /// is deterministic, survives restarts with no persistence of its own, and cannot drift.
        ///
        /// Note this reads the world clock deliberately: season length is measured in *game* days,
        /// and on an empty server nobody is experiencing seasons anyway. This is not the same
        /// concern as ZoneClock, which must accrue real time precisely because it models what
        /// happened while nobody was looking.
        /// </summary>
        private Season ComputeFromWorldAge()
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return Current;

            int lengthDays = Math.Max(1, ModConfig.SeasonLengthDays.Value);

            try
            {
                if (!TryGetCurrentDay(env, out int day)) return Current;

                int index = (day / lengthDays) % 4;
                return (Season)index;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"SeasonSystem: day read failed: {ex.Message}");
                return Current;
            }
        }

        /// <summary>
        /// Resolve EnvMan's private day accessor once, then reuse it.
        ///
        /// Resolution is attempted on every call until it succeeds, deliberately: a failed
        /// resolve must not latch this system off permanently. That "early-out the retry can
        /// never get past" shape has cost a full playtest before.
        /// </summary>
        private static bool TryGetCurrentDay(EnvMan env, out int day)
        {
            day = 0;

            if (!_dayAccessorResolved)
            {
                MethodInfo mi = AccessTools.Method(typeof(EnvMan), "GetCurrentDay");
                if (mi == null)
                {
                    RagnaroksWrath.Log.LogError(
                        "SeasonSystem: EnvMan.GetCurrentDay not found. Valheim's API may have " +
                        "changed — season tracking will stay on its last known value.");
                    _dayAccessorResolved = true;   // stop hunting for something that isn't there
                    return false;
                }

                _getCurrentDay = AccessTools.MethodDelegate<Func<EnvMan, int>>(mi);
                _dayAccessorResolved = true;

                RagnaroksWrath.Log.LogInfo("SeasonSystem: resolved EnvMan.GetCurrentDay accessor.");
            }

            if (_getCurrentDay == null) return false;

            day = _getCurrentDay(env);
            return true;
        }

        private void AnnounceIfChanged()
        {
            if (_lastAnnounced == Current) return;
            _lastAnnounced = Current;

            if (!ModConfig.AnnounceSeasonChange.Value) return;

            // Suppressed when Seasonality is present: it already tells the player, visibly and
            // more prettily. Two announcements for one event is exactly the kind of duplication
            // that makes players uninstall one of the mods.
            if (DeferringToSeasonality) return;

            MessageFeed.ToEveryone(SeasonText(Current), MessageFeed.Placement.Centre);
        }

        private static string SeasonText(Season s)
        {
            switch (s)
            {
                case Season.Spring: return "The thaw comes. The land stirs.";
                case Season.Summer: return "The long days settle over the land.";
                case Season.Fall:   return "The light shortens. The land turns.";
                case Season.Winter: return "Winter takes hold.";
                default:            return "The season turns.";
            }
        }

        // ---- gameplay multipliers -------------------------------------------------------
        // The point of this whole system. Other systems ask these questions; nothing visual.

        /// <summary>Multiplier on fire ignition and spread rate. Dry seasons burn.</summary>
        public static float FireRiskMultiplier()
        {
            switch (Current)
            {
                case Season.Summer: return 1.6f;
                case Season.Fall:   return 1.2f;
                case Season.Spring: return 0.8f;
                case Season.Winter: return 0.4f;
                default:            return 1f;
            }
        }

        /// <summary>Multiplier on plague growth. Cold slows it; damp spring encourages it.</summary>
        public static float PlagueGrowthMultiplier()
        {
            switch (Current)
            {
                case Season.Spring: return 1.4f;
                case Season.Summer: return 1.1f;
                case Season.Fall:   return 1.0f;
                case Season.Winter: return 0.5f;
                default:            return 1f;
            }
        }

        /// <summary>Multiplier on farming yield and crop growth.</summary>
        /// <summary>
        /// How fast cold accumulates. Consumed by BiomeStateSystem's Frost drift, and the reason
        /// that field can build with no event behind it. Kept here with the other three so there
        /// is exactly one place season turns into a number.
        /// </summary>
        public static float ColdMultiplier()
        {
            switch (Current)
            {
                case Season.Winter: return 1.6f;
                case Season.Fall:   return 0.7f;
                case Season.Spring: return 0.3f;
                case Season.Summer: return 0.0f;
                default:            return 0.5f;
            }
        }

        public static float FarmingYieldMultiplier()

        {
            switch (Current)
            {
                case Season.Spring: return 1.2f;
                case Season.Summer: return 1.3f;
                case Season.Fall:   return 1.0f;
                case Season.Winter: return 0.3f;
                default:            return 1f;
            }
        }
    }
}
