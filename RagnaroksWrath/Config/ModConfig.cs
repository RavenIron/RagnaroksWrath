using BepInEx.Configuration;

namespace RavenIron.RagnaroksWrath.Config
{
    /// <summary>
    /// Config surface. Expected to grow large — season length, contest thresholds, fire spread
    /// rate and so on all belong here eventually.
    ///
    /// Two conventions worth keeping:
    ///
    /// 1. Every system gets its own on/off toggle from day one. That is what makes incremental
    ///    testing possible (build one system, disable the rest) and lets server owners adopt
    ///    part of the mod without all of it.
    ///
    /// 2. Clamp on READ as well as on write. Config files get hand-edited, and a value validated
    ///    only for a floor and not a ceiling has already taken a production service down for six
    ///    hours in this codebase's history. AcceptableValueRange handles the write side; anything
    ///    consumed in a loop should be re-clamped where it is used.
    /// </summary>
    public static class ModConfig
    {
        // ---- Core ----------------------------------------------------------------------
        public static ConfigEntry<float> TickBudgetMs;
        public static ConfigEntry<float> MaxCreditSeconds;
        public static ConfigEntry<bool>  VerboseLogging;
        public static ConfigEntry<float> AutosaveIntervalSeconds;

        // ---- Feedback -------------------------------------------------------------------
        public static ConfigEntry<float> MessageMinIntervalSeconds;

        // ---- Season ---------------------------------------------------------------------
        public static ConfigEntry<int>  SeasonLengthDays;
        public static ConfigEntry<bool> AnnounceSeasonChange;

        // ---- Biome state ----------------------------------------------------------------
        public static ConfigEntry<float> BiomeStateIntervalSeconds;
        public static ConfigEntry<int>   BiomeContactRadiusZones;
        public static ConfigEntry<int>   BiomeMaxZonesPerTick;
        public static ConfigEntry<float> BiomeRecoveryPerHour;
        public static ConfigEntry<float> BiomeFrostPressurePerHour;

        // ---- Weather and storms ---------------------------------------------------------
        public static ConfigEntry<float>  WeatherIntervalSeconds;
        public static ConfigEntry<float>  StormMinIntervalSeconds;
        public static ConfigEntry<float>  StormMaxIntervalSeconds;
        public static ConfigEntry<float>  StormDurationSeconds;
        public static ConfigEntry<float>  StormRangeMeters;
        public static ConfigEntry<float>  StormFireRiskMultiplier;
        public static ConfigEntry<float>  StormWindMultiplier;
        public static ConfigEntry<float>  StormPlagueSpreadMultiplier;
        public static ConfigEntry<bool>   StormsForceWeather;
        public static ConfigEntry<string> StormForcedEnvironment;

        // ---- Wind -----------------------------------------------------------------------
        public static ConfigEntry<float> WindIntervalSeconds;

        // ---- Fire (bridge to FireFront) -------------------------------------------------
        public static ConfigEntry<float> FireScorchIntervalSeconds;
        public static ConfigEntry<float> FireScorchPerMinute;

        // ---- Plague ---------------------------------------------------------------------
        public static ConfigEntry<float> PlagueSpreadIntervalSeconds;
        public static ConfigEntry<float> PlagueGrowthPerHour;
        public static ConfigEntry<float> PlagueCorruptionBoost;
        public static ConfigEntry<float> PlagueSpreadThreshold;
        public static ConfigEntry<float> PlagueSeedAmount;
        public static ConfigEntry<float> PlagueSpreadChance;
        public static ConfigEntry<int>   PlagueMaxSpreadsPerTick;

        // ---- World state ----------------------------------------------------------------
        public static ConfigEntry<float> WorldStateIntervalSeconds;
        public static ConfigEntry<float> WorldFlourishingBurden;
        public static ConfigEntry<float> WorldAilingBurden;
        public static ConfigEntry<float> WorldStrickenBurden;
        public static ConfigEntry<float> WorldStormBurden;

        // ---- Ecology --------------------------------------------------------------------
        public static ConfigEntry<float> EcologyIntervalSeconds;
        public static ConfigEntry<float> EcologyCorruptionPerHour;
        public static ConfigEntry<float> EcologyPlagueThreshold;
        public static ConfigEntry<float> EcologyScorchThreshold;

        // ---- Farming --------------------------------------------------------------------
        public static ConfigEntry<float>  FarmingIntervalSeconds;
        public static ConfigEntry<float>  FarmingDepletionPerCropHour;
        public static ConfigEntry<string> FarmingCropPrefabs;

        // ---- Per-system master switches -------------------------------------------------

        public static ConfigEntry<bool> EnableSeason;
        public static ConfigEntry<bool> EnableWeather;
        public static ConfigEntry<bool> EnableWind;
        public static ConfigEntry<bool> EnableBiomeState;
        public static ConfigEntry<bool> EnableFire;
        public static ConfigEntry<bool> EnablePlague;
        public static ConfigEntry<bool> EnableEcology;
        public static ConfigEntry<bool> EnableFarming;
        public static ConfigEntry<bool> EnableHealth;
        public static ConfigEntry<bool> EnableConsequence;
        public static ConfigEntry<bool> EnableRivalry;
        public static ConfigEntry<bool> EnableRelic;
        public static ConfigEntry<bool> EnableTitle;
        public static ConfigEntry<bool> EnableWorldState;

        public static void Bind(ConfigFile cfg)
        {
            const string core = "1 - Core";

            TickBudgetMs = cfg.Bind(core, "TickBudgetMs", 2.0f,
                new ConfigDescription(
                    "Milliseconds per frame WorldTick may spend across all systems combined. " +
                    "Work that does not fit resumes next frame. Raise only if systems are " +
                    "visibly falling behind on a server with headroom.",
                    new AcceptableValueRange<float>(0.25f, 16.0f)));

            MaxCreditSeconds = cfg.Bind(core, "MaxCreditSeconds", 86400f,
                new ConfigDescription(
                    "Cap on the real elapsed time a single zone can be credited in one contact. " +
                    "Default is 24 hours: a zone untouched for a month accrues a day of drift, " +
                    "not a month of it. This is the dial that keeps a long-idle world playable.",
                    new AcceptableValueRange<float>(60f, 2592000f)));

            VerboseLogging = cfg.Bind(core, "VerboseLogging", false,
                "Log every system pass rather than summaries. This mod's work is invisible by " +
                "design; this is how you see it running.");

            AutosaveIntervalSeconds = cfg.Bind(core, "AutosaveIntervalSeconds", 120f,
                new ConfigDescription(
                    "Seconds between writes of the zone drift store. Writing is skipped entirely " +
                    "when nothing has changed, and a final write always happens on shutdown, so " +
                    "this only bounds how much drift a hard crash can lose. Set 0 to disable " +
                    "periodic writes (shutdown save still occurs).",
                    new AcceptableValueRange<float>(0f, 3600f)));

            const string feedback = "2 - Feedback";

            MessageMinIntervalSeconds = cfg.Bind(feedback, "MessageMinIntervalSeconds", 8.0f,
                new ConfigDescription(
                    "Minimum seconds between on-screen messages, so a cascade of simultaneous " +
                    "world events cannot spam a player off their own screen. Does not apply to " +
                    "server-wide announcements, which are rare by policy.",
                    new AcceptableValueRange<float>(0f, 300f)));

            const string season = "3 - Season";

            SeasonLengthDays = cfg.Bind(season, "SeasonLengthDays", 7,
                new ConfigDescription(
                    "In-game days per season, when running our own clock. Ignored entirely if " +
                    "Seasonality (RustyMods) is installed — we defer to its season in that case " +
                    "rather than run a second clock that could disagree with it.",
                    new AcceptableValueRange<int>(1, 120)));

            AnnounceSeasonChange = cfg.Bind(season, "AnnounceSeasonChange", true,
                "Announce season changes on screen. Automatically suppressed when Seasonality is " +
                "installed, since it already shows the player the season visibly.");

            const string weather = "6 - Weather";

            WeatherIntervalSeconds = cfg.Bind(weather, "WeatherIntervalSeconds", 5f,
                new ConfigDescription(
                    "Seconds between WeatherSystem passes. Also how quickly a storm's start and " +
                    "end are noticed, so keep it small: this reads state, it does not compute.",
                    new AcceptableValueRange<float>(1f, 60f)));

            StormMinIntervalSeconds = cfg.Bind(weather, "StormMinIntervalSeconds", 3600f,
                new ConfigDescription(
                    "Shortest gap between storms, in real seconds. No storm can fire before this " +
                    "has passed since the last one ended.",
                    new AcceptableValueRange<float>(60f, 86400f)));

            StormMaxIntervalSeconds = cfg.Bind(weather, "StormMaxIntervalSeconds", 10800f,
                new ConfigDescription(
                    "Longest gap between storms. Chance ramps from zero at the minimum to " +
                    "certainty here, so a storm is guaranteed by this point rather than merely " +
                    "likely. A flat per-tick roll has a long tail, and a server that goes a real " +
                    "day without a storm looks broken rather than unlucky.",
                    new AcceptableValueRange<float>(120f, 172800f)));

            StormDurationSeconds = cfg.Bind(weather, "StormDurationSeconds", 300f,
                new ConfigDescription(
                    "How long a storm runs. Vanilla pauses this while no player is in the area, " +
                    "so it is time experienced rather than time elapsed.",
                    new AcceptableValueRange<float>(30f, 3600f)));

            StormRangeMeters = cfg.Bind(weather, "StormRangeMeters", 96f,
                new ConfigDescription(
                    "Radius of a storm's effect, in metres. Vanilla's own event range is 96; the " +
                    "banner and the gameplay multipliers both use this figure, so they cannot " +
                    "disagree about where the storm is.",
                    new AcceptableValueRange<float>(32f, 1024f)));

            StormFireRiskMultiplier = cfg.Bind(weather, "StormFireRiskMultiplier", 1.8f,
                new ConfigDescription(
                    "Fire risk multiplier inside a storm. Applies only within StormRangeMeters.",
                    new AcceptableValueRange<float>(0f, 10f)));

            StormWindMultiplier = cfg.Bind(weather, "StormWindMultiplier", 2.0f,
                new ConfigDescription(
                    "Gameplay wind multiplier inside a storm. Feeds fire spread rate and " +
                    "direction; does not touch the wind the player can see.",
                    new AcceptableValueRange<float>(0f, 10f)));

            StormPlagueSpreadMultiplier = cfg.Bind(weather, "StormPlagueSpreadMultiplier", 1.5f,
                new ConfigDescription(
                    "Plague spread multiplier inside a storm.",
                    new AcceptableValueRange<float>(0f, 10f)));

            StormsForceWeather = cfg.Bind(weather, "StormsForceWeather", false,
                "THE ONLY SETTING IN THIS MOD THAT SELECTS AN ENVIRONMENT. Default off, and it " +
                "should stay off for anyone running Seasonality or any other weather mod: two " +
                "mods forcing environment selection is a straight conflict where whoever patches " +
                "last silently wins. It exists for owners running no weather mod at all, who " +
                "would otherwise get a storm with a clear sky.");

            StormForcedEnvironment = cfg.Bind(weather, "StormForcedEnvironment", "ThunderStorm",
                "Environment name used only when StormsForceWeather is on. Ignored entirely " +
                "otherwise - with that off, this value is never read and the sky is never set.");

            const string wind = "7 - Wind";

            WindIntervalSeconds = cfg.Bind(wind, "WindIntervalSeconds", 5f,
                new ConfigDescription(
                    "Seconds between wind readings. Wind is read from EnvMan and cached; nothing " +
                    "in this mod ever writes it.",
                    new AcceptableValueRange<float>(1f, 60f)));

            const string fire = "8 - Fire";

            FireScorchIntervalSeconds = cfg.Bind(fire, "FireScorchIntervalSeconds", 10f,
                new ConfigDescription(
                    "Seconds between scorch passes. Only matters while FireFront " +
                    "(com.raveniron.firefront, 0.18.0+) is installed - without it FireSystem is " +
                    "dormant: fire simulation belongs to FireFront, this mod only records what " +
                    "fire does to the land.",
                    new AcceptableValueRange<float>(2f, 120f)));

            FireScorchPerMinute = cfg.Bind(fire, "FireScorchPerMinute", 0.02f,
                new ConfigDescription(
                    "Scorch added per minute to any zone containing at least one active fire. " +
                    "Deliberately per-MINUTE and deliberately flat per zone: a fire's severity " +
                    "already shows up as more zones burning, so scaling by fire count too would " +
                    "double-count it. Default chars a zone fully after ~50 minutes of continuous " +
                    "burning; recovery is BiomeStateSystem's job and is slower than this.",
                    new AcceptableValueRange<float>(0f, 1f)));

            const string plague = "9 - Plague";

            PlagueSpreadIntervalSeconds = cfg.Bind(plague, "PlagueSpreadIntervalSeconds", 60f,
                new ConfigDescription(
                    "Seconds between spread passes. Spread is the EVENT half of plague; growth " +
                    "and cure run on the zone clock inside BiomeStateSystem and have their own " +
                    "pace.",
                    new AcceptableValueRange<float>(10f, 600f)));

            PlagueGrowthPerHour = cfg.Bind(plague, "PlagueGrowthPerHour", 0.03f,
                new ConfigDescription(
                    "How much plague grows per hour of elapsed contact time in an already-" +
                    "infected zone, before the season multiplier. Against the recovery rate this " +
                    "decides curability by season: at defaults, spring (x1.4) grows at 0.042/h " +
                    "vs 0.02/h recovery, while winter (x0.5) manages 0.015/h and the zone heals. " +
                    "Set 0 to freeze all growth.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PlagueCorruptionBoost = cfg.Bind(plague, "PlagueCorruptionBoost", 1.0f,
                new ConfigDescription(
                    "How strongly zone Corruption feeds plague: growth x (1 + boost x Corruption). " +
                    "At 1.0 a fully corrupted zone doubles its plague growth.",
                    new AcceptableValueRange<float>(0f, 4f)));

            PlagueSpreadThreshold = cfg.Bind(plague, "PlagueSpreadThreshold", 0.5f,
                new ConfigDescription(
                    "Plague level a zone needs before it can infect its neighbours. Fresh seeds " +
                    "start far below this and only climb through player contact - which is the " +
                    "containment: the front advances exactly as far as people actually go.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            PlagueSeedAmount = cfg.Bind(plague, "PlagueSeedAmount", 0.05f,
                new ConfigDescription(
                    "Plague level a newly infected zone starts at.",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));

            PlagueSpreadChance = cfg.Bind(plague, "PlagueSpreadChance", 0.25f,
                new ConfigDescription(
                    "Chance per spread pass that each frontier zone is seeded, before the storm " +
                    "multiplier at that zone. Not per source: a zone bordered by three hotspots " +
                    "rolls once.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PlagueMaxSpreadsPerTick = cfg.Bind(plague, "PlagueMaxSpreadsPerTick", 16,
                new ConfigDescription(
                    "Upper bound on zones seeded in one pass, as a backstop against a huge " +
                    "frontier all rolling well at once.",
                    new AcceptableValueRange<int>(1, 256)));

            const string worldstate = "10 - World state";

            WorldStateIntervalSeconds = cfg.Bind(worldstate, "WorldStateIntervalSeconds", 30f,
                new ConfigDescription(
                    "Seconds between derivations of the world condition. Derived bottom-up " +
                    "from the zone store and weather every pass, never persisted: if it dies, " +
                    "the next pass recomputes the same answer.",
                    new AcceptableValueRange<float>(10f, 600f)));

            WorldFlourishingBurden = cfg.Bind(worldstate, "WorldFlourishingBurden", 0.25f,
                new ConfigDescription(
                    "Total burden at or below which the land flourishes.",
                    new AcceptableValueRange<float>(0f, 10f)));

            WorldAilingBurden = cfg.Bind(worldstate, "WorldAilingBurden", 4f,
                new ConfigDescription(
                    "Total burden at which the land turns Ailing. Burden is a weighted sum over " +
                    "every tracked zone (plague x1.5, corruption x1, scorch x1, depletion x0.75, " +
                    "frost x0.5), so it scales with real damage, not with how many zones happen " +
                    "to be tracked.",
                    new AcceptableValueRange<float>(0.5f, 100f)));

            WorldStrickenBurden = cfg.Bind(worldstate, "WorldStrickenBurden", 12f,
                new ConfigDescription(
                    "Total burden at which the land is Stricken. Keep well above Ailing; " +
                    "improvements only announce after burden clears a 15 percent hysteresis " +
                    "band, so transitions cannot flap.",
                    new AcceptableValueRange<float>(1f, 500f)));

            WorldStormBurden = cfg.Bind(worldstate, "WorldStormBurden", 1f,
                new ConfigDescription(
                    "Burden added while a Devastating Storm runs - the weather taking its seat " +
                    "when the land is judged.",
                    new AcceptableValueRange<float>(0f, 20f)));

            const string ecology = "11 - Ecology";

            EcologyIntervalSeconds = cfg.Bind(ecology, "EcologyIntervalSeconds", 60f,
                new ConfigDescription(
                    "Seconds between blight-pressure passes.",
                    new AcceptableValueRange<float>(10f, 600f)));

            EcologyCorruptionPerHour = cfg.Bind(ecology, "EcologyCorruptionPerHour", 0.01f,
                new ConfigDescription(
                    "Corruption accrued per hour in a zone held at the pressure threshold; " +
                    "scales up as plague or scorch exceed it. Corruption then feeds plague " +
                    "growth through PlagueCorruptionBoost - the loop that makes a neglected " +
                    "outbreak worse than two clean ones. Set 0 to sever the loop.",
                    new AcceptableValueRange<float>(0f, 1f)));

            EcologyPlagueThreshold = cfg.Bind(ecology, "EcologyPlagueThreshold", 0.3f,
                new ConfigDescription(
                    "Plague level at which the land beneath begins to corrupt.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            EcologyScorchThreshold = cfg.Bind(ecology, "EcologyScorchThreshold", 0.3f,
                new ConfigDescription(
                    "Scorch level at which the land beneath begins to corrupt.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            const string farming = "12 - Farming";

            FarmingIntervalSeconds = cfg.Bind(farming, "FarmingIntervalSeconds", 45f,
                new ConfigDescription(
                    "Seconds between crop-sweep steps. Deliberately not 60: AwayFromHome " +
                    "rescans the full ZDO index every 60s, and two sweeps landing together is " +
                    "a stutter neither mod can diagnose alone.",
                    new AcceptableValueRange<float>(10f, 600f)));

            FarmingDepletionPerCropHour = cfg.Bind(farming, "FarmingDepletionPerCropHour", 0.002f,
                new ConfigDescription(
                    "Fertility depletion per standing crop per hour. At the default, a 25-crop " +
                    "field tires its zone fully in about 20 hours of real uptime; rest heals " +
                    "it through the biome recovery rate. Depletion is only WRITTEN server-side " +
                    "today - growth and yield effects arrive with the client plugin.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            FarmingCropPrefabs = cfg.Bind(farming, "FarmingCropPrefabs",
                "sapling_carrot,sapling_turnip,sapling_onion,sapling_barley,sapling_flax,sapling_seedcarrot,sapling_seedturnip,sapling_seedonion,sapling_jotunpuffs,sapling_magecap",
                "Comma-separated crop prefab names to sweep. Game content, so data rather than " +
                "code: a name the game no longer knows costs a silent zero matches - check " +
                "with VerboseLogging if a crop stops tiring soil after a game patch.");

            const string systems = "4 - Systems";





            EnableSeason      = cfg.Bind(systems, "EnableSeason",      true, "Master switch for SeasonSystem.");
            EnableWeather     = cfg.Bind(systems, "EnableWeather",     true, "Master switch for WeatherSystem, including Devastating Storms.");
            EnableWind        = cfg.Bind(systems, "EnableWind",        true, "Master switch for WindSystem.");
            EnableBiomeState  = cfg.Bind(systems, "EnableBiomeState",  true, "Master switch for BiomeStateSystem (per-zone fertility, corruption, scorch, frost).");
            EnableFire        = cfg.Bind(systems, "EnableFire",        true, "Master switch for FireSystem.");
            EnablePlague      = cfg.Bind(systems, "EnablePlague",      true, "Master switch for PlagueSystem.");
            EnableEcology     = cfg.Bind(systems, "EnableEcology",     true, "Master switch for EcologySystem.");
            EnableFarming     = cfg.Bind(systems, "EnableFarming",     true, "Master switch for FarmingSystem.");
            EnableHealth      = cfg.Bind(systems, "EnableHealth",      true, "Master switch for HealthSystem.");
            EnableConsequence = cfg.Bind(systems, "EnableConsequence", true, "Master switch for ConsequenceSystem.");
            EnableRivalry     = cfg.Bind(systems, "EnableRivalry",     true, "Master switch for RivalrySystem.");
            EnableRelic       = cfg.Bind(systems, "EnableRelic",       true, "Master switch for RelicSystem.");
            EnableWorldState  = cfg.Bind(systems, "EnableWorldState",  true, "Master switch for WorldStateSystem (derived world condition and announcements).");
            EnableTitle       = cfg.Bind(systems, "EnableTitle",       true, "Master switch for TitleSystem (earned title under player nameplates).");

            const string biome = "5 - Biome state";

            BiomeStateIntervalSeconds = cfg.Bind(biome, "BiomeStateIntervalSeconds", 30f,
                new ConfigDescription(
                    "Seconds between BiomeStateSystem passes. Deliberately not 60: AwayFromHome " +
                    "rescans the full ZDO index every 60s by default, and two heavy passes " +
                    "landing on the same frame is a stutter neither mod can diagnose alone.",
                    new AcceptableValueRange<float>(5f, 600f)));

            BiomeContactRadiusZones = cfg.Bind(biome, "BiomeContactRadiusZones", 1,
                new ConfigDescription(
                    "How far a player's presence reaches, in zones. 0 is the zone they stand in " +
                    "only; 1 is the 3x3 around them. Raising this multiplies per-tick work by " +
                    "the ring area, so it is the first dial to lower on a crowded server.",
                    new AcceptableValueRange<int>(0, 3)));

            BiomeMaxZonesPerTick = cfg.Bind(biome, "BiomeMaxZonesPerTick", 64,
                new ConfigDescription(
                    "Upper bound on zones drifted in one pass. Work already scales with players " +
                    "present rather than world size, so this is a backstop for a very crowded " +
                    "server: whatever does not fit resumes next tick from where it stopped, and " +
                    "the elapsed time it is owed keeps accruing meanwhile.",
                    new AcceptableValueRange<int>(1, 1024)));

            BiomeRecoveryPerHour = cfg.Bind(biome, "BiomeRecoveryPerHour", 0.02f,
                new ConfigDescription(
                    "How much of the 0..1 scale a zone recovers per hour of elapsed real time. " +
                    "Every field is a deviation from pristine, so this is the rate at which the " +
                    "land forgets. Default heals fully-damaged land in about 50 hours away.",
                    new AcceptableValueRange<float>(0f, 1f)));

            BiomeFrostPressurePerHour = cfg.Bind(biome, "BiomeFrostPressurePerHour", 0.015f,
                new ConfigDescription(
                    "How much Frost accumulates per hour, before the season's cold multiplier. " +
                    "Frost is the one value that builds with no event behind it: winter is a net " +
                    "gain against recovery, and the thaw is a net loss. Set 0 to make this " +
                    "system purely restorative.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
