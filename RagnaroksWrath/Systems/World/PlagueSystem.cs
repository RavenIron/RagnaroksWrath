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
    /// HOW A PLAGUE STARTS: nothing in this system invents outbreaks. Patient zero comes from
    /// elsewhere — a future event system, a wrath console command, or (the store being plain
    /// tab-separated text by design) an admin's editor.
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
                $"seed {ModConfig.PlagueSeedAmount.Value:F2}). Growth and cure live in BiomeDrift.");
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
    }
}
