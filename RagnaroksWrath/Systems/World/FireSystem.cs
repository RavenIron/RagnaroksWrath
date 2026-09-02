using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// The world's memory of fire — a BRIDGE, not a fire simulation.
    ///
    /// FireFront (com.raveniron.firefront) already owns fire: ignition from vanilla fire
    /// damage, spread, ground cells, wind bias, extinguishing, VFX, client sync — shipped and
    /// verified on a dedicated server. Building a second spread simulation here would put two
    /// Raven Iron mods igniting and destroying the same pieces: the same
    /// two-mods-forcing-one-thing conflict rule 4 exists to prevent, in-house. Decided
    /// 2026-08-25 (see the locked-decisions table).
    ///
    /// So this system asks FireFront where fires burn and raises `ZoneState.Scorch` there.
    /// Scorch then suppresses fertility through BiomeDrift and will feed future fire risk.
    /// Fire acts; the land remembers. Without FireFront installed the system is DORMANT by
    /// design — Scorch stays a substrate other events can raise.
    ///
    /// Since 0.23.0 the bridge also carries ONE write: storm lightning. A Devastating Storm
    /// over a player under a dry sky may land a bolt nearby, and the bolt is a single call
    /// into FireFront's own ignition (`IgniteGroundNear`) — RW decides when and where,
    /// FireFront owns every consequence, so the "never a second fire sim" decision holds.
    /// Lightning never fires in rain (FireFront's own suppression rule, honoured up front),
    /// never lands within the configured standoff of anything player-built, and only ever
    /// strikes near an ONLINE player — which is also what keeps the AwayFromHome promise:
    /// an unattended base cannot be reached by a bolt that only exists where players are.
    ///
    /// NO ZONE CLOCK. Per docs/zone-clock-ownership.md, this system uses live tick time only:
    /// scorch accrues while a fire actually burns, which is also what the unattended-bases rule
    /// requires — FireFront's fires are the only input, and they exist only where its own
    /// simulation is running.
    ///
    /// FireFront is reached by REFLECTION, resolved once and cached, so it stays a soft
    /// dependency AT LOAD TIME: neither mod fails to load without the other, and this csproj
    /// gains no reference that fetch-libs cannot supply. The contract is
    /// `FireManager.CollectActiveFirePositions(List&lt;Vector3&gt;)`, public in FireFront since
    /// 0.17.2 and documented there as load-bearing for this mod.
    ///
    /// Since 0.23.0 FireFront is ALSO a Thunderstore manifest dependency (owner's call
    /// 2026-08-27, reversing the earlier soft-by-design packaging): mod managers install the
    /// pair together, so a missing FireFront now most likely means a hand install skipped it —
    /// which is why absence warns instead of whispering. Initialise tattles the exact version
    /// against the two API floors so a stale pairing is diagnosed at boot, not discovered as
    /// silence.
    /// </summary>
    public class FireSystem : IWorldSystem
    {
        public const string FireFrontGuid = "com.raveniron.firefront";

        // The two API floors the bridge cares about: positions (scorch) landed in 0.17.2,
        // the igniter surface (arson attribution) in 0.17.3. Compared against BepInEx's
        // parsed plugin metadata at boot so a stale pairing names itself before the
        // per-tick resolver's warnings become the only clue. Fully qualified because
        // Valheim ships its own global-namespace `Version` class, which shadows
        // System.Version in every file that references game types.
        private static readonly System.Version MinimumFireFrontVersion = new System.Version(0, 17, 2);
        private static readonly System.Version IgniterFireFrontVersion = new System.Version(0, 17, 3);

        public string Name => "FireSystem";
        public bool Enabled => ModConfig.EnableFire.Value;
        public float IntervalSeconds => ModConfig.FireScorchIntervalSeconds.Value;

        private bool _fireFrontPresent;

        // Rule 5 shape: resolved once, retried until they succeed, never latched as failed.
        private PropertyInfo _instanceProperty;
        private MethodInfo _collectMethod;

        // The OPTIONAL igniter surface (FireFront 0.17.3+): unlike the load-bearing
        // position method, an older FireFront is a legitimate configuration, so absence
        // logs once and goes quiet instead of warning per tick. Arson attribution simply
        // stays dormant until the surface exists.
        private PropertyInfo _igniterProperty;
        private bool _igniterAbsenceLogged;

        // Reused per tick so a steady state allocates nothing.
        private readonly List<Vector3> _firePositions = new List<Vector3>(64);
        private readonly List<ZoneKey> _burningZones = new List<ZoneKey>(16);
        private readonly object[] _collectArgs = new object[1];

        // Storm lightning (0.23.0). The ignite surface follows the igniter's
        // optional-surface rules: absence logs once, lightning stays dormant, scorch
        // is untouched. Scratch lists reused because the roll runs every tick even
        // though a strike is rare.
        private MethodInfo _igniteMethod;
        private bool _igniteGroundAbsenceLogged;
        private readonly System.Random _rng = new System.Random();
        private readonly List<ZDO> _stormPlayers = new List<ZDO>(8);
        private readonly List<ZDO> _sectorScratch = new List<ZDO>(256);

        public void Initialise()
        {
            _fireFrontPresent = Chainloader.PluginInfos.ContainsKey(FireFrontGuid);

            if (!_fireFrontPresent)
            {
                // A warning, not info, since 0.23.0: FireFront is a listed Thunderstore
                // dependency, so absence usually means a hand install missed half the pair.
                // Running without it stays safe and supported — the world just never scars.
                RagnaroksWrath.Log.LogWarning(
                    $"[{Name}] FireFront not present — dormant. It ships as a dependency of this " +
                    "mod; a manual install likely skipped it. Scorch will not accrue from fire; " +
                    "install FireFront (com.raveniron.firefront) 0.17.2+ to light the world's memory.");
                return;
            }

            System.Version version = Chainloader.PluginInfos[FireFrontGuid].Metadata.Version;
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] FireFront {version} detected — bridging. Burning zones gain " +
                $"{ModConfig.FireScorchPerMinute.Value:F3} scorch/min.");

            if (version < MinimumFireFrontVersion)
                RagnaroksWrath.Log.LogWarning(
                    $"[{Name}] FireFront {version} predates {MinimumFireFrontVersion} — its read API " +
                    "is missing, so scorch CANNOT accrue. Update FireFront; until then every bridge " +
                    "tick will name the unresolved surface.");
            else if (version < IgniterFireFrontVersion)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] FireFront {version} predates {IgniterFireFrontVersion} — arson " +
                    "attribution stays dormant; scorch is unaffected.");

            if (ModConfig.StormLightningEnabled.Value)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] storm lightning armed — ~1 bolt per " +
                    $"{ModConfig.LightningMeanMinutes.Value:0.#} storm-minutes, landing " +
                    $"{ModConfig.LightningRingMinMeters.Value:0}-{ModConfig.LightningRingMaxMeters.Value:0}m " +
                    $"from a present player, {ModConfig.LightningStandoffMeters.Value:0}m homestead standoff.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!_fireFrontPresent) return;
            if (!Persistence.IsLoaded) return;   // not the authority, or world not up yet

            if (!TryCollectFirePositions()) return;

            // Lightning rides the RESOLVED bridge and must run before the no-fires
            // early-out: starting a fire from nothing is its entire purpose.
            TryLightning();

            if (_firePositions.Count == 0) return;

            _burningZones.Clear();
            FireScorch.CollectBurningZones(_firePositions, _burningZones);

            float delta = FireScorch.ScorchDelta(ModConfig.FireScorchPerMinute.Value, deltaSeconds);
            if (delta <= 0f) return;

            // Task 13's arson writer: the fire event's culprit, from FireFront's optional
            // igniter surface, booked the same scorch this tick burns into each zone. One
            // igniter per event — spread fires inherit their arsonist by FireFront's own
            // capture-once rule. 0 means natural fire, attributed to nobody.
            long igniter = TryReadIgniter();
            float harmPerPoint = ModConfig.ArsonHarmPerScorchPoint.Value;
            bool bookHarm = igniter != 0 && harmPerPoint > 0f
                            && ModConfig.EnableRivalry.Value && RivalryLedger.IsLoaded;

            for (int i = 0; i < _burningZones.Count; i++)
            {
                ZoneKey zone = _burningZones[i];
                ZoneState state = Persistence.Get(zone);
                state.Scorch += delta;

                // Set clamps to 0..1 and enforces sparseness; scorch recovery is BiomeDrift's
                // job, on the zone clock. This system only ever adds.
                Persistence.Set(zone, state);

                if (bookHarm)
                    RivalryLedger.AddHarm(zone, igniter, delta * harmPerPoint);
            }

            if (ModConfig.VerboseLogging.Value)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] {_firePositions.Count} fire(s) scorching {_burningZones.Count} zone(s).");
        }

        /// <summary>
        /// One lightning roll per pass while a Devastating Storm runs. Gate order is
        /// cheapest-first and every gate is a real rule: config, storm, dry sky
        /// (FireFront's own rain suppression honoured up front — announcing a fire that
        /// fizzles in seconds reads as a bug), the dice, a player actually under the
        /// storm, the homestead standoff. A blocked or lost bolt is never rerolled —
        /// the configured rate stays honest. The announcement says lightning STRUCK,
        /// not that fire caught: a bolt into rock or sand igniting nothing is honest
        /// weather, and FireFront's cell checks own that verdict.
        /// </summary>
        private void TryLightning()
        {
            if (!ModConfig.StormLightningEnabled.Value) return;
            if (!WeatherSystem.StormActive) return;
            if (EnvMan.IsWet()) return;   // public static, decompile-verified 2026-08-27

            float chance = LightningStrike.ChancePerTick(
                IntervalSeconds, ModConfig.LightningMeanMinutes.Value);
            if (_rng.NextDouble() > chance) return;

            // FireFront's fire event is global and its igniter is captured ONCE — a bolt
            // joining a player-lit event would bill that arsonist for the sky's scorch.
            // While an attributed fire burns, the sky holds its peace.
            if (TryReadIgniter() != 0) return;

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            _stormPlayers.Clear();
            for (int i = 0; i < characters.Count; i++)
            {
                ZDO c = characters[i];
                if (c == null || !c.IsValid()) continue;
                if (c.GetLong(ZDOVars.s_playerID, 0L) == 0) continue;   // real players, never an AFH keeper
                if (!WeatherSystem.IsStormAt(c.GetPosition())) continue;
                _stormPlayers.Add(c);
            }
            if (_stormPlayers.Count == 0) return;   // a storm with nobody under it strikes nobody

            Vector3 anchor = _stormPlayers[_rng.Next(_stormPlayers.Count)].GetPosition();
            Vector3 strike = LightningStrike.StrikePoint(
                anchor, _rng.NextDouble(), _rng.NextDouble(),
                ModConfig.LightningRingMinMeters.Value, ModConfig.LightningRingMaxMeters.Value);

            if (IsNearPlayerBuilt(strike, ModConfig.LightningStandoffMeters.Value))
            {
                if (ModConfig.VerboseLogging.Value)
                    RagnaroksWrath.Log.LogInfo(
                        $"[{Name}] bolt grounded by the homestead in {ZoneKey.FromWorldPos(strike)} — lost.");
                return;
            }

            if (!TryIgniteGround(strike, ModConfig.LightningIgniteRadiusMeters.Value)) return;

            Feedback.MessageFeed.ToPlayersNear(strike, 64f, "Lightning splits the sky!",
                Feedback.MessageFeed.Placement.Centre);
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] lightning strike at ({strike.x:F0}, {strike.z:F0}) in " +
                $"{ZoneKey.FromWorldPos(strike)} — the fire, if any, is FireFront's now.");
        }

        /// <summary>
        /// The bolt standoff, now shared machinery: Homestead owns the player-built scan
        /// (it grew a second caller in 0.26.0 — the storm scheduler anchors storms on
        /// players in the wild with the same question). Lightning fails CLOSED: when the
        /// world cannot be checked, the bolt is lost, never risked.
        /// </summary>
        private bool IsNearPlayerBuilt(Vector3 strike, float standoff)
            => Homestead.IsNearPlayerBuilt(strike, standoff, _sectorScratch, resultWhenUncheckable: true);

        /// <summary>
        /// FireFront's `IgniteGroundNear(Vector3, float)` — the bridge's one WRITE,
        /// promoted to a documented cross-mod contract beside CollectActiveFirePositions.
        /// Optional-surface rules like the igniter: absence logs once and lightning stays
        /// dormant; scorch and everything else is unaffected. Only called on ticks that
        /// already resolved the manager type.
        /// </summary>
        private bool TryIgniteGround(Vector3 origin, float radius)
        {
            try
            {
                if (_igniteMethod == null)
                {
                    Type manager = _collectMethod?.DeclaringType;
                    if (manager == null) return false;

                    _igniteMethod = manager.GetMethod("IgniteGroundNear",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_igniteMethod == null)
                    {
                        if (!_igniteGroundAbsenceLogged)
                        {
                            _igniteGroundAbsenceLogged = true;
                            RagnaroksWrath.Log.LogInfo(
                                $"[{Name}] FireFront has no IgniteGroundNear surface — " +
                                "storm lightning dormant; scorch unaffected.");
                        }
                        return false;
                    }
                }

                object instance = _instanceProperty?.GetValue(null);
                if (instance == null) return false;

                _igniteMethod.Invoke(instance, new object[] { origin, radius });
                return true;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] lightning ignite failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// The current fire event's igniter player id via FireFront's OPTIONAL
        /// `CurrentFireIgniterPlayerId` property (0.17.3+), or 0 when absent, unreadable,
        /// or genuinely nobody. Only called on ticks that already resolved the instance.
        /// </summary>
        private long TryReadIgniter()
        {
            try
            {
                if (_igniterProperty == null)
                {
                    System.Type manager = _collectMethod?.DeclaringType;
                    if (manager == null) return 0L;

                    _igniterProperty = manager.GetProperty("CurrentFireIgniterPlayerId",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_igniterProperty == null)
                    {
                        if (!_igniterAbsenceLogged)
                        {
                            _igniterAbsenceLogged = true;
                            RagnaroksWrath.Log.LogInfo(
                                $"[{Name}] FireFront predates the igniter surface (0.17.3) — " +
                                "arson attribution dormant; scorch still accrues.");
                        }
                        return 0L;
                    }
                }

                object instance = _instanceProperty?.GetValue(null);
                if (instance == null) return 0L;

                return (long)_igniterProperty.GetValue(instance);
            }
            catch (Exception)
            {
                return 0L;   // attribution is optional; scorch must never depend on it
            }
        }

        /// <summary>
        /// Fill _firePositions from FireFront, resolving the reflection handles on first use.
        ///
        /// Failures are warnings, not latches: if FireFront's surface moved, every tick names
        /// what could not be found, because a bridge that goes silently dormant after an update
        /// is this codebase's least favourite failure mode.
        /// </summary>
        private bool TryCollectFirePositions()
        {
            try
            {
                if (_collectMethod == null)
                {
                    Type manager = Chainloader.PluginInfos[FireFrontGuid].Instance.GetType()
                        .Assembly.GetType("FireFront.Fire.FireManager");
                    if (manager == null)
                    {
                        RagnaroksWrath.Log.LogWarning(
                            $"[{Name}] FireFront.Fire.FireManager not found — FireFront's API moved.");
                        return false;
                    }

                    _instanceProperty = manager.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static);
                    _collectMethod = manager.GetMethod("CollectActiveFirePositions",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_instanceProperty == null || _collectMethod == null)
                    {
                        _collectMethod = null;   // keep retrying; do not half-resolve
                        RagnaroksWrath.Log.LogWarning(
                            $"[{Name}] FireManager.Instance or CollectActiveFirePositions not found — " +
                            "FireFront is present but older than 0.17.2. Scorch will not accrue.");
                        return false;
                    }
                }

                object instance = _instanceProperty.GetValue(null);
                if (instance == null) return false;   // FireManager not awake yet; normal early on

                _firePositions.Clear();
                _collectArgs[0] = _firePositions;
                _collectMethod.Invoke(instance, _collectArgs);
                return true;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] could not read FireFront fires: {ex.Message}");
                return false;
            }
        }
    }
}
