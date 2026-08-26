using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Per-zone drift of the five <see cref="ZoneState"/> values. The first real consumer of
    /// <see cref="ZoneClock"/> and <see cref="Persistence"/>, and the substrate every later
    /// system reads.
    ///
    /// FOUR DECISIONS, each against a specific failure:
    ///
    /// 1. CREDIT ON CONTACT, NEVER TICK ON LOAD STATE. Drift is owed to a zone from the moment a
    ///    player last stood in it, and is paid in one lump when they return. Ticking loaded zones
    ///    instead would make drift depend on whether some *other* mod happened to be holding the
    ///    area open - AwayFromHome rotates a keeper through farm sites for ~180s at a time, so
    ///    load state tracks "did somebody build a stone nearby", not "did anything happen here".
    ///    It also means per-tick cost scales with players present, not with world size.
    ///
    /// 2. THE ARITHMETIC LIVES IN <see cref="BiomeDrift"/>, NOT HERE. This class is a
    ///    driver: it finds contacted zones, reads config, and asks SeasonSystem for
    ///    multipliers, none of which exists off-game. The maths underneath is the part
    ///    that can be wrong in a way nobody notices for months, so it sits where the
    ///    harness can compile the shipping source and test it.
    ///
    /// 3. NOTHING IS INVENTED HERE. Fire, plague and farming raise their own fields; this system
    ///    only recovers them. The one exception is Frost, which is genuinely seasonal rather than
    ///    event-driven - winter accumulates it, the thaw takes it away. Summer does not conjure
    ///    Scorch out of nothing; it makes existing Scorch recover more slowly, which is what "dry
    ///    season" should mean to land that has already burnt.
    /// </summary>
    public class BiomeStateSystem : IWorldSystem
    {
        public string Name => "BiomeStateSystem";
        public bool Enabled => ModConfig.EnableBiomeState.Value;
        public float IntervalSeconds => ModConfig.BiomeStateIntervalSeconds.Value;

        // Rebuilt every tick. Kept as a field so a steady state does not allocate a list per pass.
        private readonly List<ZoneKey> _contacted = new List<ZoneKey>(64);

        // Task 13 phase B: the worst grudge among the players whose contact covers each
        // zone — the land reacts to the most hated person present. Rebuilt beside
        // _contacted; empty whenever rivalry is off or nobody present is resented.
        private readonly Dictionary<ZoneKey, float> _grudgeByZone = new Dictionary<ZoneKey, float>(64);

        // Task 13 phase C: zones whose dominant carer is present — the land's mercy.
        // Grudge and mercy can coexist (a resented player beside a beloved one): the land
        // weighs both, multiplicatively.
        private readonly HashSet<ZoneKey> _mercyZones = new HashSet<ZoneKey>();

        // Round-robin position, in the same shape as WorldTick's: it persists across ticks so a
        // pass cut short by the budget resumes rather than restarting, and the zones at the front
        // of the list cannot starve the ones behind them.
        private int _cursor;

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] recovery {ModConfig.BiomeRecoveryPerHour.Value:F3}/h, " +
                $"frost pressure {ModConfig.BiomeFrostPressurePerHour.Value:F3}/h, " +
                $"contact radius {ModConfig.BiomeContactRadiusZones.Value} zone(s), " +
                $"max {ModConfig.BiomeMaxZonesPerTick.Value} zone(s)/tick.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;   // nothing to write into yet

            _contacted.Clear();
            _grudgeByZone.Clear();
            _mercyZones.Clear();
            CollectContactedZones(_contacted, _grudgeByZone, _mercyZones);

            if (_contacted.Count == 0)
            {
                _cursor = 0;
                return;
            }

            if (_cursor >= _contacted.Count) _cursor = 0;

            float recovery      = ModConfig.BiomeRecoveryPerHour.Value;
            float frostPressure = ModConfig.BiomeFrostPressurePerHour.Value;
            float cold          = SeasonSystem.ColdMultiplier();
            float fireRisk      = SeasonSystem.FireRiskMultiplier();

            // Season is multiplied in HERE, not inside BiomeDrift — one place season becomes a
            // number, same as every other multiplier this pass hands over.
            float plagueGrowth  = ModConfig.PlagueGrowthPerHour.Value * SeasonSystem.PlagueGrowthMultiplier();
            float corruptionBoost = ModConfig.PlagueCorruptionBoost.Value;

            int budget = Math.Min(ModConfig.BiomeMaxZonesPerTick.Value, _contacted.Count);
            int drifted = 0;

            for (int processed = 0; processed < budget; processed++)
            {
                ZoneKey zone = _contacted[_cursor];
                _cursor = (_cursor + 1) % _contacted.Count;

                // Consumes the backlog. First contact establishes history and credits nothing,
                // which is what stops a brand-new world drifting by however long the save file
                // has existed.
                double elapsed = ZoneClock.CreditOnContact(zone);
                if (elapsed <= 0.0) continue;

                // Phase B, tooth one: grudged ground drifts harsher UNDER THE GRUDGED.
                // Recovery slows (never below half), pressure quickens (up to doubled) —
                // scaled per zone here so BiomeDrift's pure math stays untouched and
                // harness-covered. A zone with no resented player present pays nothing.
                float g = _grudgeByZone.TryGetValue(zone, out float zg) ? zg : 0f;

                // Phase C mercy: the dominant carer's presence quickens healing. Applied
                // after the grudge so both can be true at once — the land weighs everyone.
                float zoneRecovery = RivalryMath.GrudgedRecovery(recovery, g);
                if (_mercyZones.Contains(zone))
                    zoneRecovery *= 1f + ModConfig.MercyRecoveryBonus.Value;

                ZoneState before = Persistence.Get(zone);
                ZoneState after  = BiomeDrift.Apply(before, elapsed,
                    zoneRecovery,
                    RivalryMath.GrudgedPressure(frostPressure, g),
                    cold, fireRisk,
                    RivalryMath.GrudgedPressure(plagueGrowth, g),
                    corruptionBoost);

                // Set clamps, and removes the entry outright if the zone has healed back to
                // default. Sparseness is enforced at that boundary, not here.
                Persistence.Set(zone, after);
                drifted++;
            }

            if (drifted > 0 && ModConfig.VerboseLogging.Value)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] drifted {drifted} of {_contacted.Count} contacted zone(s); " +
                    $"{Persistence.TrackedZoneCount} zone(s) tracked.");
        }

        // ---- contact ---------------------------------------------------------------------

        /// <summary>
        /// The zones a player is currently in, plus a configurable ring around each.
        ///
        /// Reads character ZDOs rather than Player instances. A dedicated server's reference
        /// position sits at world origin and never follows anyone, so it instantiates nothing
        /// where players actually are - Player.GetAllPlayers() is not authoritative there, while
        /// position on a character ZDO is replicated state kept fresh by its owner.
        /// </summary>
        private static void CollectContactedZones(List<ZoneKey> into, Dictionary<ZoneKey, float> grudges,
                                                  HashSet<ZoneKey> mercies)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return;

            int radius = ModConfig.BiomeContactRadiusZones.Value;
            bool trackGrudges = ModConfig.EnableRivalry.Value && RivalryLedger.IsLoaded;
            float scale = ModConfig.GrudgeScale.Value;

            List<ZDO> characters;
            try
            {
                characters = znet.GetAllCharacterZDOS();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning(
                    $"[BiomeStateSystem] could not read character ZDOs: {ex.Message}");
                return;
            }

            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;

                long playerId = trackGrudges ? zdo.GetLong(ZDOVars.s_playerID, 0L) : 0L;
                ZoneKey centre = ZoneKey.FromWorldPos(zdo.GetPosition());

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var zone = new ZoneKey(centre.X + dx, centre.Y + dy);

                        // Linear scan rather than a HashSet: this list holds players times the
                        // ring area, which is tens of entries at worst, and staying
                        // allocation-free matters more here than the asymptotics.
                        if (!into.Contains(zone)) into.Add(zone);

                        if (playerId != 0)
                        {
                            RivalryLedger.Row row = RivalryLedger.Get(zone, playerId);
                            float g = RivalryMath.GrudgeFor(row.Harm, row.Care, scale);
                            if (g > 0f && (!grudges.TryGetValue(zone, out float worst) || g > worst))
                                grudges[zone] = g;

                            if (RivalrySystem.IsDominantCarer(zone, playerId))
                                mercies.Add(zone);
                        }
                    }
                }
            }
        }
    }
}
