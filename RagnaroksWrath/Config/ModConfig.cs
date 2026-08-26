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

        // ---- Titles ---------------------------------------------------------------------
        public static ConfigEntry<float> TitleIntervalSeconds;
        public static ConfigEntry<float> WinterbornSeconds;
        public static ConfigEntry<bool>  AnnounceTitles;

        // ---- Zone sync + client visuals ---------------------------------------------------
        public static ConfigEntry<float> ZoneSyncIntervalSeconds;
        public static ConfigEntry<int>   ZoneSyncRadiusZones;
        public static ConfigEntry<bool>  PlagueFogEnabled;
        public static ConfigEntry<float> PlagueFogDensity;
        public static ConfigEntry<bool>  FrostBreathEnabled;
        public static ConfigEntry<float> FrostBreathFloor;

        // ---- Health ---------------------------------------------------------------------
        public static ConfigEntry<float> HealthIntervalSeconds;
        public static ConfigEntry<float> ExposureMinutesToMax;
        public static ConfigEntry<float> ExposureRecoveryMinutes;
        public static ConfigEntry<float> ExposureRestedRecoveryMultiplier;
        public static ConfigEntry<float> ExposurePoisonResistMultiplier;
        public static ConfigEntry<float> ExposureTier1;
        public static ConfigEntry<float> ExposureTier2;
        public static ConfigEntry<float> ExposureTier3;
        public static ConfigEntry<float> SicknessStaminaRegenAtTier1;
        public static ConfigEntry<float> SicknessStaminaRegenAtMax;
        public static ConfigEntry<float> SicknessHealthRegenAtTier2;
        public static ConfigEntry<float> SicknessHealthRegenAtMax;
        public static ConfigEntry<bool>  FrostChillEnabled;
        public static ConfigEntry<float> FrostChillThreshold;
        public static ConfigEntry<float> ChillStaminaRegenMultiplier;
        public static ConfigEntry<float> ChillHealthRegenMultiplier;

        // ---- Consequence ----------------------------------------------------------------
        public static ConfigEntry<float>  ConsequenceIntervalSeconds;
        public static ConfigEntry<bool>   ConsequenceBarren;
        public static ConfigEntry<bool>   ConsequenceEmpower;
        public static ConfigEntry<bool>   ConsequenceSicken;
        public static ConfigEntry<bool>   ConsequenceWither;
        public static ConfigEntry<bool>   AnnounceConsequences;
        public static ConfigEntry<float>  BarrenPlagueThreshold;
        public static ConfigEntry<float>  BarrenScorchThreshold;
        public static ConfigEntry<float>  SickenPlagueThreshold;
        public static ConfigEntry<float>  SickenSpeedPenalty;
        public static ConfigEntry<float>  EmpowerCorruptionThreshold;
        public static ConfigEntry<float>  EmpowerLevelUpMultiplierAtFull;
        public static ConfigEntry<float>  CropWitherBlightThreshold;
        public static ConfigEntry<string> WildlifePrefabs;

        // ---- Rivalry --------------------------------------------------------------------
        public static ConfigEntry<float> RivalryIntervalSeconds;
        public static ConfigEntry<float> RivalryHalfLifeHours;
        public static ConfigEntry<float> CarePerHealedPoint;
        public static ConfigEntry<float> TendingCarePerPlant;
        public static ConfigEntry<float> ArsonHarmPerScorchPoint;

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
        public static ConfigEntry<bool> EnableZoneSync;

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
                    "(com.raveniron.firefront, 0.17.2+) is installed - without it FireSystem is " +
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

            const string titles = "13 - Titles";

            TitleIntervalSeconds = cfg.Bind(titles, "TitleIntervalSeconds", 10f,
                new ConfigDescription(
                    "Seconds between title-earning checks against online players.",
                    new AcceptableValueRange<float>(2f, 120f)));

            WinterbornSeconds = cfg.Bind(titles, "WinterbornSeconds", 1800f,
                new ConfigDescription(
                    "Online seconds through Winter to earn Winterborn. The clock is in-memory " +
                    "and resets on server restart - under-awarding is a shrug, double-announcing " +
                    "is spam.",
                    new AcceptableValueRange<float>(60f, 86400f)));

            AnnounceTitles = cfg.Bind(titles, "AnnounceTitles", true,
                "Announce newly earned titles to everyone. Titles are rare by construction; " +
                "placeholder names (Stranger) are never announced.");

            const string sync = "14 - Client sync and visuals";

            ZoneSyncIntervalSeconds = cfg.Bind(sync, "ZoneSyncIntervalSeconds", 10f,
                new ConfigDescription(
                    "Seconds between zone-state pushes to each connected player. Pushes are " +
                    "absolute snapshots of the ring around them, defaults included, so a " +
                    "dropped packet heals on the next push - no delta bookkeeping to rot.",
                    new AcceptableValueRange<float>(2f, 120f)));

            ZoneSyncRadiusZones = cfg.Bind(sync, "ZoneSyncRadiusZones", 2,
                new ConfigDescription(
                    "Ring radius in zones pushed to each player. 2 means a 5x5 block - under " +
                    "a kilobyte per push.",
                    new AcceptableValueRange<int>(1, 4)));

            PlagueFogEnabled = cfg.Bind(sync, "PlagueFogEnabled", true,
                "Render the plague miasma on this client. Purely visual, built procedurally " +
                "(no assets), local-only - never networked, never saved.");

            PlagueFogDensity = cfg.Bind(sync, "PlagueFogDensity", 1f,
                new ConfigDescription(
                    "Fog density multiplier for this client. Zones below plague 0.15 never " +
                    "fog regardless - the frontier's fresh seeds should not telegraph " +
                    "themselves the tick they spread.",
                    new AcceptableValueRange<float>(0f, 4f)));

            FrostBreathEnabled = cfg.Bind(sync, "FrostBreathEnabled", true,
                "Fog the local player's breath on land whose frost has drifted high. Purely " +
                "visual, procedural, local-only - the warning that precedes the chill.");

            FrostBreathFloor = cfg.Bind(sync, "FrostBreathFloor", 0.3f,
                new ConfigDescription(
                    "Zone frost at which breath starts to fog. Deliberately BELOW the chill " +
                    "threshold (0.5 default): the land shows its cold before it bites, the " +
                    "way plague fogs before it sickens.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            const string health = "15 - Health";

            HealthIntervalSeconds = cfg.Bind(health, "HealthIntervalSeconds", 5f,
                new ConfigDescription(
                    "Seconds between exposure passes over online players. Small enough that " +
                    "walking through the edge of an outbreak registers; the per-pass work is " +
                    "one zone read per player.",
                    new AcceptableValueRange<float>(1f, 60f)));

            ExposureMinutesToMax = cfg.Bind(health, "ExposureMinutesToMax", 30f,
                new ConfigDescription(
                    "Minutes of standing on FULL plague (1.0) to reach maximum exposure; the " +
                    "rate scales linearly with the plague actually underfoot, and nothing " +
                    "accrues below the fog floor (0.15) — the sickness must not telegraph " +
                    "what the fog hides. Sickness is the consequence of settling in blight, " +
                    "not of visiting it.",
                    new AcceptableValueRange<float>(5f, 240f)));

            ExposureRecoveryMinutes = cfg.Bind(health, "ExposureRecoveryMinutes", 20f,
                new ConfigDescription(
                    "Minutes from maximum exposure back to clean, off plagued ground.",
                    new AcceptableValueRange<float>(2f, 240f)));

            ExposureRestedRecoveryMultiplier = cfg.Bind(health, "ExposureRestedRecoveryMultiplier", 2f,
                new ConfigDescription(
                    "Recovery speed multiplier while Rested. Vanilla remedies are the whole " +
                    "counterplay language: rest heals, no new items to learn.",
                    new AcceptableValueRange<float>(1f, 10f)));

            ExposurePoisonResistMultiplier = cfg.Bind(health, "ExposurePoisonResistMultiplier", 0.5f,
                new ConfigDescription(
                    "Accrual multiplier while poison-resistant (mead, gear or food — read " +
                    "from damage modifiers, the same aggregation vanilla's cold gate uses). " +
                    "0.5 means protection halves how fast the sickness takes hold.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ExposureTier1 = cfg.Bind(health, "ExposureTier1", 0.25f,
                new ConfigDescription(
                    "Exposure at which the sickness BEGINS: the icon appears and stamina " +
                    "regen starts to sag (stamina first — sickness in the body before the " +
                    "wound).",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            ExposureTier2 = cfg.Bind(health, "ExposureTier2", 0.5f,
                new ConfigDescription(
                    "Exposure at which health regen starts failing too.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            ExposureTier3 = cfg.Bind(health, "ExposureTier3", 0.8f,
                new ConfigDescription(
                    "Exposure announced as the sickness at its worst. Announcement tier only " +
                    "— the multipliers ramp smoothly, this is where the centre-screen line " +
                    "fires.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            SicknessStaminaRegenAtTier1 = cfg.Bind(health, "SicknessStaminaRegenAtTier1", 0.85f,
                new ConfigDescription(
                    "Stamina regen multiplier the INSTANT Tier1 is crossed — the step that " +
                    "makes 'a sickness takes root in you' true when it is announced. Setting " +
                    "this to 1.0 restores the 0.8.0 behaviour where the first tier was " +
                    "announced but could not be felt.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            SicknessStaminaRegenAtMax = cfg.Bind(health, "SicknessStaminaRegenAtMax", 0.3f,
                new ConfigDescription(
                    "Stamina regen multiplier at exposure 1.0, ramping from the Tier1 step. " +
                    "With the defaults this reproduces the agreed table: x0.85 at 0.25, " +
                    "x0.67 at 0.5, x0.45 at 0.8. NEVER a damage number: the sickness " +
                    "weakens, the world kills.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            SicknessHealthRegenAtTier2 = cfg.Bind(health, "SicknessHealthRegenAtTier2", 0.8f,
                new ConfigDescription(
                    "Health regen multiplier the instant Tier2 is crossed — the wound half " +
                    "of the sickness arriving where 'the sickness deepens' is announced.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            SicknessHealthRegenAtMax = cfg.Bind(health, "SicknessHealthRegenAtMax", 0.38f,
                new ConfigDescription(
                    "Health regen multiplier at exposure 1.0, ramping from the Tier2 step " +
                    "(x0.80 at 0.5, x0.55 at 0.8 with the defaults).",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            FrostChillEnabled = cfg.Bind(health, "FrostChillEnabled", true,
                "High zone frost chills players vanilla's weather would not — through our " +
                "own Cold-like effect, never vanilla's (fighting its env pass is message " +
                "spam by construction). Campfires, shelter and frost resistance all cancel " +
                "it, exactly like the real thing.");

            FrostChillThreshold = cfg.Bind(health, "FrostChillThreshold", 0.5f,
                new ConfigDescription(
                    "Zone frost at which the chill takes hold of exposed players.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            ChillStaminaRegenMultiplier = cfg.Bind(health, "ChillStaminaRegenMultiplier", 0.8f,
                new ConfigDescription(
                    "Stamina regen multiplier while chilled.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            ChillHealthRegenMultiplier = cfg.Bind(health, "ChillHealthRegenMultiplier", 0.7f,
                new ConfigDescription(
                    "Health regen multiplier while chilled.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            const string consequence = "16 - Consequence";

            ConsequenceIntervalSeconds = cfg.Bind(consequence, "ConsequenceIntervalSeconds", 10f,
                new ConfigDescription(
                    "Seconds between the announcer's passes over online players. The physical " +
                    "acts are client-side and continuous; this only paces the one-line-per-zone " +
                    "voice.",
                    new AcceptableValueRange<float>(2f, 120f)));

            ConsequenceBarren = cfg.Bind(consequence, "ConsequenceBarren", true,
                "Pickables (berries, mushrooms, thistle) refuse the hand on plagued or " +
                "scorched ground, with a withered hover line explaining why.");

            ConsequenceEmpower = cfg.Bind(consequence, "ConsequenceEmpower", true,
                "Hostile spawns on corrupted ground get better odds on vanilla's own " +
                "level-up roll — starred enemies where the land is worst. Passive wildlife " +
                "is never starred.");

            ConsequenceSicken = cfg.Bind(consequence, "ConsequenceSicken", true,
                "Passive wildlife in plagued zones sickens and visibly slows. Wears off on " +
                "its own once the animal (or the plague) is gone.");

            ConsequenceWither = cfg.Bind(consequence, "ConsequenceWither", true,
                "Crops planted in badly blighted soil turn unhealthy and die at grow time, " +
                "through vanilla's own cant-grow path. Replanting after curing the land is " +
                "the remedy.");

            AnnounceConsequences = cfg.Bind(consequence, "AnnounceConsequences", true,
                "One line, once per zone per session, the first time a player stands in a " +
                "zone that has crossed into any consequence. Never per-bush, never per-deer.");

            BarrenPlagueThreshold = cfg.Bind(consequence, "BarrenPlagueThreshold", 0.4f,
                new ConfigDescription(
                    "Plague at which pickables stop yielding.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            BarrenScorchThreshold = cfg.Bind(consequence, "BarrenScorchThreshold", 0.5f,
                new ConfigDescription(
                    "Scorch at which pickables stop yielding. Ash bears nothing.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            SickenPlagueThreshold = cfg.Bind(consequence, "SickenPlagueThreshold", 0.4f,
                new ConfigDescription(
                    "Plague at which passive wildlife sickens.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            SickenSpeedPenalty = cfg.Bind(consequence, "SickenSpeedPenalty", 0.35f,
                new ConfigDescription(
                    "Fraction of movement speed a sickened animal loses (0.35 = 35% slower) " +
                    "— the visible stagger. Slow-only: the lethal edge from the design " +
                    "conversation is deliberately unbuilt until it earns its own pass.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            EmpowerCorruptionThreshold = cfg.Bind(consequence, "EmpowerCorruptionThreshold", 0.5f,
                new ConfigDescription(
                    "Corruption at which spawns start coming up meaner.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            EmpowerLevelUpMultiplierAtFull = cfg.Bind(consequence, "EmpowerLevelUpMultiplierAtFull", 6f,
                new ConfigDescription(
                    "Multiplier on vanilla's level-up chance at corruption 1.0, ramping from " +
                    "1.0 at the threshold. Vanilla's base roll is ~10 percent per level, so 6 " +
                    "means roughly 60 percent of eligible spawns star on fully corrupted " +
                    "ground. Vanilla's own per-creature caps still apply.",
                    new AcceptableValueRange<float>(1f, 10f)));

            CropWitherBlightThreshold = cfg.Bind(consequence, "CropWitherBlightThreshold", 0.6f,
                new ConfigDescription(
                    "Blight (the worse of plague and corruption) at which planted crops " +
                    "wither and die. Growth-RATE effects belong to FarmingSystem; this is " +
                    "only the kill line.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            WildlifePrefabs = cfg.Bind(consequence, "WildlifePrefabs", "Deer,Boar,Hare",
                "Comma-separated prefab names counted as passive wildlife: sickened by " +
                "plague, never starred by corruption. An explicit list rather than a " +
                "faction guess - factions lump deer in with greydwarfs. Game content, so " +
                "data rather than code.");

            const string rivalry = "17 - Rivalry";

            RivalryIntervalSeconds = cfg.Bind(rivalry, "RivalryIntervalSeconds", 30f,
                new ConfigDescription(
                    "Seconds between ledger passes (decay, healing observation, one tending " +
                    "sweep step). Deliberately offset from Farming's 45 and AwayFromHome's " +
                    "60 so the ZDO walks do not land on the same frame.",
                    new AcceptableValueRange<float>(5f, 300f)));

            RivalryHalfLifeHours = cfg.Bind(rivalry, "RivalryHalfLifeHours", 48f,
                new ConfigDescription(
                    "Real hours for recorded harm and care to fade by half. Grudges and " +
                    "gratitude both decay — the world forgives on a long enough timeline, " +
                    "and the ledger stays sparse because of it. 0 would disable decay, so " +
                    "the floor is 1.",
                    new AcceptableValueRange<float>(1f, 720f)));

            CarePerHealedPoint = cfg.Bind(rivalry, "CarePerHealedPoint", 1f,
                new ConfigDescription(
                    "Care booked per point of zone damage healed while present, split among " +
                    "everyone whose contact ring covered the zone. A full plague cure earns " +
                    "its attendants one point between them at the default.",
                    new AcceptableValueRange<float>(0f, 10f)));

            TendingCarePerPlant = cfg.Bind(rivalry, "TendingCarePerPlant", 0.05f,
                new ConfigDescription(
                    "Care booked to a crop's planter, once per plant ever (watermarked " +
                    "against replant-farming and restarts). Uses the same crop prefab list " +
                    "as Farming.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ArsonHarmPerScorchPoint = cfg.Bind(rivalry, "ArsonHarmPerScorchPoint", 1f,
                new ConfigDescription(
                    "Harm booked to a fire event's igniter per point of scorch their fire " +
                    "burns into each zone — fully charring one zone books its arsonist one " +
                    "point at the default. Needs FireFront 0.17.3+ (the igniter surface); " +
                    "with an older FireFront, arson attribution is dormant and scorch " +
                    "still accrues. Natural and creature fires book nobody.",
                    new AcceptableValueRange<float>(0f, 10f)));

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
            EnableZoneSync    = cfg.Bind(systems, "EnableZoneSync",    true, "Master switch for ZoneSyncSystem (zone state pushed to clients for visuals).");
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
