using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Zone-to-zone plague spread. An EVENT system, per docs/zone-clock-ownership.md: it writes
    /// state (seeds into neighbouring zones) on its own live tick, while plague's time-based
    /// GROWTH and CURE both live in BiomeDrift.Apply on the zone clock. This file never touches
    /// elapsed time.
    ///
    /// The division of labour that makes plague self-limiting:
    ///
    /// - This system seeds the pristine ring around hotspots (threshold-gated, see PlagueSpread).
    /// - BiomeDrift grows seeds toward the threshold — but only on player contact, so the
    ///   disease front advances exactly as far as people actually go.
    /// - BiomeDrift's recovery is the cure: winter (or any season where growth drops below
    ///   recovery) drives plague through zero, the epsilon snap kills it, and the zone leaves
    ///   the store. Nothing here resurrects it.
    ///
    /// HOW A PLAGUE STARTS (0.22.0): GENESIS — a rare roll each pass seeds sickness into a
    /// zone inside some online player's contact ring, weighted toward corrupted and burnt
    /// ground and multiplied under storms. Sickness follows settlement; nothing seeds where
    /// nobody goes (an uncontacted outbreak would be invisible bookkeeping anyway — drift
    /// only grows plague under someone's feet). Admins retain the older instruments: the
    /// wrath console and the hand-editable store.
    /// </summary>
    public class PlagueSystem : IWorldSystem
    {
        public string Name => "PlagueSystem";
        public bool Enabled => ModConfig.EnablePlague.Value;
        public float IntervalSeconds => ModConfig.PlagueSpreadIntervalSeconds.Value;

        private readonly System.Random _rng = new System.Random();

        // Rebuilt each tick, retained to stay allocation-free at steady state.
        private readonly List<KeyValuePair<ZoneKey, float>> _plagued = new List<KeyValuePair<ZoneKey, float>>(32);
        private readonly HashSet<ZoneKey> _infected = new HashSet<ZoneKey>();
        private readonly List<ZoneKey> _targets = new List<ZoneKey>(16);

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] spread every {IntervalSeconds:F0}s at {ModConfig.PlagueSpreadChance.Value:P0} " +
                $"per frontier zone (threshold {ModConfig.PlagueSpreadThreshold.Value:F2}, " +
                $"seed {ModConfig.PlagueSeedAmount.Value:F2}). Growth and cure live in BiomeDrift. " +
                (ModConfig.PlagueGenesisEnabled.Value
                    ? $"Genesis: outbreaks take root organically, ~{ModConfig.PlagueGenesisMeanHours.Value:0.#}h " +
                      "mean on clean ground, faster on blighted, storm-carried."
                    : "Genesis OFF: outbreaks start only by admin hand."));
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;

            // Snapshot the store's plagued zones. Persistence.All() is the live dictionary, and
            // seeding while iterating it would mutate under the enumerator.
            _plagued.Clear();
            _infected.Clear();
            foreach (KeyValuePair<ZoneKey, ZoneState> kv in Persistence.All())
            {
                if (kv.Value.Plague <= 0f) continue;
                _plagued.Add(new KeyValuePair<ZoneKey, float>(kv.Key, kv.Value.Plague));
                _infected.Add(kv.Key);
            }

            // Genesis runs BEFORE the no-plague early-out — starting from nothing is its
            // entire purpose.
            TryGenesis();

            if (_plagued.Count == 0) return;

            _targets.Clear();
            PlagueSpread.CollectSpreadTargets(
                _plagued, ModConfig.PlagueSpreadThreshold.Value, _infected, _targets);

            if (_targets.Count == 0) return;

            float baseChance = ModConfig.PlagueSpreadChance.Value;
            float seed = ModConfig.PlagueSeedAmount.Value;
            int cap = ModConfig.PlagueMaxSpreadsPerTick.Value;
            int seeded = 0;

            for (int i = 0; i < _targets.Count && seeded < cap; i++)
            {
                ZoneKey zone = _targets[i];

                // Storms carry sickness: the multiplier is positional, read at the target zone's
                // centre, so a storm on the far side of the map changes nothing here.
                float chance = baseChance * WeatherSystem.PlagueSpreadMultiplierAt(zone.ToWorldPos());
                if (_rng.NextDouble() > chance) continue;

                ZoneState state = Persistence.Get(zone);
                state.Plague = seed;
                Persistence.Set(zone, state);
                seeded++;
            }

            if (seeded > 0 && ModConfig.VerboseLogging.Value)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] seeded {seeded} zone(s) from {_plagued.Count} infected " +
                    $"({_targets.Count} frontier candidate(s)).");
        }

        /// <summary>
        /// One genesis roll per pass: pick a random zone inside a random online player's
        /// contact ring; if it is clean, sickness takes root there with a chance built
        /// from the configured mean, the ground's blight, and any storm overhead. The
        /// seed sits below the fog's emission floor — outbreaks are DISCOVERED, not
        /// announced; the server log alone carries the birth certificate.
        /// </summary>
        private void TryGenesis()
        {
            if (!ModConfig.PlagueGenesisEnabled.Value) return;

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null || characters.Count == 0) return;

            ZDO pick = characters[_rng.Next(characters.Count)];
            if (pick == null || !pick.IsValid()) return;
            if (pick.GetLong(ZDOVars.s_playerID, 0L) == 0) return;

            int radius = Math.Max(0, ModConfig.BiomeContactRadiusZones.Value);
            ZoneKey centre = ZoneKey.FromWorldPos(pick.GetPosition());
            var zone = new ZoneKey(
                centre.X + _rng.Next(-radius, radius + 1),
                centre.Y + _rng.Next(-radius, radius + 1));

            ZoneState state = Persistence.Get(zone);
            if (state.Plague > 0f) return;   // already sick; no reroll — the rate stays honest

            float weight = PlagueGenesis.Weight(state.Corruption, state.Scorch);
            float storm = WeatherSystem.PlagueSpreadMultiplierAt(zone.ToWorldPos());
            float chance = PlagueGenesis.ChancePerTick(
                IntervalSeconds, ModConfig.PlagueGenesisMeanHours.Value) * weight * storm;

            if (_rng.NextDouble() > chance) return;

            state.Plague = ModConfig.PlagueSeedAmount.Value;
            Persistence.Set(zone, state);
            ZoneClock.MarkContact(zone);   // fresh stamp: genesis must not inherit a backlog

            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] sickness takes root in {zone} " +
                $"(ground weight {weight:0.##}, storm x{storm:0.##}).");
        }
    }
}
