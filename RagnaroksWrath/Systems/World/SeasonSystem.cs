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
    /// THREE MODES
    /// -----------
    /// - Seasonality installed: read its season from vanilla global keys. Deferring is correct
    ///   even though our own clock would be simpler — two disagreeing season clocks on one server
    ///   is a worse experience than either mod alone, and theirs is the one players can see.
    /// - Seasons (shudnal) installed: read its season by reflection. Its global keys are
    ///   default-OFF and the key names are server-configurable strings, so keys are not a
    ///   reliable surface; `Seasons.seasonState.GetCurrentSeason()` is (public static field,
    ///   public method, enum values verified identical to ours — Spring=0..Winter=3).
    ///   The two season mods declare BepInIncompatibility with each other, so at most one
    ///   is ever loaded.
    /// - Neither present: run our own lightweight clock off world age. No visual output.
    /// </summary>
    public class SeasonSystem : IWorldSystem
    {
        public const string SeasonalityGuid = "RustyMods.Seasonality";

        /// <summary>
        /// Seasons by shudnal. GUID verified 2026-08-27 against their published source
        /// (Seasons.cs: `public const string pluginID = "shudnal.Seasons";`).
        /// </summary>
        public const string ShudnalSeasonsGuid = "shudnal.Seasons";

        /// <summary>Where the current season comes from. Fixed at Initialise.</summary>
        public enum SeasonSource
        {
            OwnClock = 0,
            Seasonality = 1,
            ShudnalSeasons = 2
        }

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

        /// <summary>Which season clock is authoritative. OwnClock unless a season mod is loaded.</summary>
        public static SeasonSource Source { get; private set; } = SeasonSource.OwnClock;

        private Season _lastAnnounced = Season.Spring;
        private bool _firstResolveDone;

        // Reflection handles into shudnal's Seasons. Rule 5 shape: resolved once, retried
        // until they succeed, never latched as failed. The FIELD can resolve while its VALUE
        // is still null (their mod not initialised yet) — that is normal early on, not a failure.
        private static FieldInfo _shudnalSeasonStateField;
        private static MethodInfo _shudnalGetCurrentSeason;

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
            if (Chainloader.PluginInfos.ContainsKey(SeasonalityGuid))
            {
                Source = SeasonSource.Seasonality;
                RagnaroksWrath.Log.LogInfo(
                    "SeasonSystem: Seasonality detected — deferring season tracking to it. " +
                    "We will read its global keys and drive gameplay only; no visual output from us.");
            }
            else if (Chainloader.PluginInfos.ContainsKey(ShudnalSeasonsGuid))
            {
                Source = SeasonSource.ShudnalSeasons;
                RagnaroksWrath.Log.LogInfo(
                    "SeasonSystem: Seasons (shudnal) detected — deferring season tracking to it. " +
                    "We will read its season by reflection and drive gameplay only; no visual output from us.");
            }
            else
            {
                Source = SeasonSource.OwnClock;
                RagnaroksWrath.Log.LogInfo(
                    "SeasonSystem: no season mod present — running our own gameplay-only season clock.");
            }
        }

        public void Tick(float deltaSeconds)
        {
            Season resolved;
            switch (Source)
            {
                case SeasonSource.Seasonality:    resolved = ReadFromSeasonality();     break;
                case SeasonSource.ShudnalSeasons: resolved = ReadFromShudnalSeasons();  break;
                default:                          resolved = ComputeFromWorldAge();     break;
            }

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
        /// Read shudnal's Seasons by reflection: `Seasons.seasonState.GetCurrentSeason()`.
        ///
        /// Surfaces verified 2026-08-27 against their published source AND the shipping DLL
        /// (1.8.2, decompiled): `seasonState` is a public static field on the plugin class,
        /// `SeasonState.GetCurrentSeason()` is a public instance method, and their Season
        /// enum is Spring=0, Summer=1, Fall=2, Winter=3 — numerically identical to ours, so
        /// the cast is a mapping, not a guess. Their enum is nested in the plugin class;
        /// irrelevant here, since the boxed value is cast to int and the type never named.
        /// Their global keys were rejected as a surface: default-off AND the key names are
        /// server-configurable strings.
        ///
        /// Failures are warnings, not latches, same as the FireFront bridge: if their surface
        /// moves, every tick names what could not be found. A null seasonState is the one
        /// quiet path — their mod simply has not initialised yet, normal at world load.
        /// </summary>
        private Season ReadFromShudnalSeasons()
        {
            try
            {
                if (_shudnalGetCurrentSeason == null)
                {
                    FieldInfo field = Chainloader.PluginInfos[ShudnalSeasonsGuid].Instance.GetType()
                        .GetField("seasonState", BindingFlags.Public | BindingFlags.Static);
                    if (field == null)
                    {
                        RagnaroksWrath.Log.LogWarning(
                            "SeasonSystem: Seasons.seasonState field not found — Seasons (shudnal) " +
                            "API moved. Season tracking will stay on its last known value.");
                        return Current;
                    }

                    MethodInfo method = field.FieldType.GetMethod("GetCurrentSeason",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (method == null)
                    {
                        RagnaroksWrath.Log.LogWarning(
                            "SeasonSystem: SeasonState.GetCurrentSeason not found — Seasons (shudnal) " +
                            "API moved. Season tracking will stay on its last known value.");
                        return Current;
                    }

                    // Resolve as a pair; never half-resolve.
                    _shudnalSeasonStateField = field;
                    _shudnalGetCurrentSeason = method;
                    RagnaroksWrath.Log.LogInfo(
                        "SeasonSystem: resolved Seasons (shudnal) season accessor.");
                }

                object state = _shudnalSeasonStateField.GetValue(null);
                if (state == null) return Current;   // their mod not initialised yet; normal early on

                int raw = Convert.ToInt32(_shudnalGetCurrentSeason.Invoke(state, null));
                if (raw < 0 || raw > 3)
                {
                    RagnaroksWrath.Log.LogWarning(
                        $"SeasonSystem: Seasons (shudnal) returned unknown season {raw} — their enum " +
                        "grew and our mapping is stale. Season tracking will stay on its last known value.");
                    return Current;
                }

                return (Season)raw;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning(
                    $"SeasonSystem: could not read Seasons (shudnal) season: {ex.Message}");
                return Current;
            }
        }

        /// <summary>
        /// Our own clock, used only when no season mod is present. Driven off world age in days so it
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

        /// <summary>The world day, or 0 when the game cannot say — the relic ledger's
        /// timestamp. Zero is honest ("day unknown"), never a guess.</summary>
        public static int CurrentDayOrZero()
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return 0;
            try { return TryGetCurrentDay(env, out int day) ? day : 0; }
            catch { return 0; }
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

            // Suppressed when any season mod is present: it already tells the player, visibly
            // and more prettily. Two announcements for one event is exactly the kind of
            // duplication that makes players uninstall one of the mods.
            if (Source != SeasonSource.OwnClock) return;

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
