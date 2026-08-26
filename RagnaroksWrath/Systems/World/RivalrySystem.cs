using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// The world keeps score — task 13, PHASE A ONLY: the influence ledger and its
    /// attribution writers. No grudges, no contests, no spawn war yet; those phases read
    /// what this one records, and each lands separately per the spec.
    ///
    /// What books in phase A:
    ///
    ///  - TENDING (care): planted crops carry vanilla's creator id — `Piece.SetCreator`
    ///    writes `GetPlayerID()` into `ZDOVars.s_creator` (decompile-verified), the same
    ///    identity long every ledger keys on. The sweep copies FarmingSystem's proven
    ///    one-whole-prefab-per-tick walk, and the persisted plantTime WATERMARK makes each
    ///    plant book exactly once across restarts. (A plant sown mid-rotation after its own
    ///    prefab's turn can slip under the advancing watermark and never book — rare, and
    ///    under-crediting is a shrug; the Winterborn precedent.)
    ///
    ///  - HEALING PRESENCE (care): drift only cures contacted zones, and contact is
    ///    per-player — so a zone's damage DECREASING between observations books care, split
    ///    among the players whose ring covered it (the same reach BiomeDrift credits).
    ///    Damage increasing books nothing: presence during decay is not culpability.
    ///
    ///  - ARSON (harm): DORMANT, deliberately. The ignition forward that knows the igniter
    ///    lives in FireFront, and its cross-mod surface carries fire POSITIONS only.
    ///    Attributing scorch by mere presence would frame bystanders, so the harm column
    ///    waits for a FireFront igniter-aware surface (0.17.3) rather than lying today.
    ///
    /// Plague books nobody, by design: task 11 chose exposure over carried infection, so no
    /// player "brings" plague anywhere.
    /// </summary>
    public class RivalrySystem : IWorldSystem
    {
        public string Name => "RivalrySystem";
        public bool Enabled => ModConfig.EnableRivalry.Value;
        public float IntervalSeconds => ModConfig.RivalryIntervalSeconds.Value;

        private const float SaveCadenceSeconds = 60f;

        private bool _ledgerLoaded;
        private float _sinceSave;

        // Tending sweep state — FarmingSystem's shape: one whole prefab drained per tick.
        private string[] _cropPrefabs = Array.Empty<string>();
        private int _prefabCursor;
        private readonly List<ZDO> _found = new List<ZDO>(128);
        private int _sweepIndex;
        private long _rotationMaxPlantTime;

        // Healing observation: last observed damage per zone, and this tick's presence map.
        private readonly Dictionary<ZoneKey, float> _lastDamage = new Dictionary<ZoneKey, float>(64);
        private readonly Dictionary<ZoneKey, List<long>> _present = new Dictionary<ZoneKey, List<long>>(32);
        private readonly List<ZoneKey> _stale = new List<ZoneKey>(16);

        public void Initialise()
        {
            _cropPrefabs = (ModConfig.FarmingCropPrefabs.Value ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < _cropPrefabs.Length; i++) _cropPrefabs[i] = _cropPrefabs[i].Trim();

            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] phase A — the influence ledger. Writers armed: tending " +
                $"({ModConfig.TendingCarePerPlant.Value:F3} care/plant, watermarked), healing presence " +
                $"({ModConfig.CarePerHealedPoint.Value:F2} care/healed point, split by ring). " +
                $"Both columns fade, half-life {ModConfig.RivalryHalfLifeHours.Value:F0}h. " +
                "ARSON DORMANT: harm attribution waits for a FireFront igniter surface rather " +
                "than framing bystanders by presence.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;

            if (!_ledgerLoaded)
            {
                RivalryLedger.Load();
                _ledgerLoaded = RivalryLedger.IsLoaded;
            }
            if (!_ledgerLoaded) return;

            RivalryLedger.DecayAll(RivalryMath.DecayFactor(
                ModConfig.RivalryHalfLifeHours.Value, deltaSeconds));

            ObserveHealing();
            SweepTending();

            _sinceSave += deltaSeconds;
            if (_sinceSave >= SaveCadenceSeconds)
            {
                _sinceSave = 0f;
                RivalryLedger.SaveIfDirty();
            }
        }

        /// <summary>
        /// Book care from zones that healed since the last look, split among the players
        /// whose contact ring covered them. First sight of a zone only baselines it; a
        /// baseline is not a heal.
        /// </summary>
        private void ObserveHealing()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            foreach (List<long> list in _present.Values) list.Clear();

            int radius = Math.Max(0, ModConfig.BiomeContactRadiusZones.Value);

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;

                long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                if (playerId == 0) continue;

                ZoneKey centre = ZoneKey.FromWorldPos(zdo.GetPosition());
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var zone = new ZoneKey(centre.X + dx, centre.Y + dy);
                    if (!_present.TryGetValue(zone, out List<long> list))
                        _present[zone] = list = new List<long>(4);
                    list.Add(playerId);
                }
            }

            float perPoint = ModConfig.CarePerHealedPoint.Value;

            foreach (KeyValuePair<ZoneKey, List<long>> kv in _present)
            {
                if (kv.Value.Count == 0) continue;   // cleared list from a previous tick

                float damage = RivalryMath.ZoneDamage(Persistence.Get(kv.Key));

                if (_lastDamage.TryGetValue(kv.Key, out float before))
                {
                    float care = RivalryMath.CareFromHealing(before, damage, perPoint);
                    float share = RivalryMath.SplitAmong(care, kv.Value.Count);
                    if (share > 0f)
                        for (int i = 0; i < kv.Value.Count; i++)
                            RivalryLedger.AddCare(kv.Key, kv.Value[i], share);
                }

                _lastDamage[kv.Key] = damage;
            }

            // Forget baselines for zones nobody's ring covers any more: drift only acts on
            // contacted zones, so nothing heals there while we are not looking — and a stale
            // baseline re-observed later would book the whole gap to whoever walked back.
            _stale.Clear();
            foreach (KeyValuePair<ZoneKey, float> kv in _lastDamage)
                if (!_present.TryGetValue(kv.Key, out List<long> list) || list.Count == 0)
                    _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++)
                _lastDamage.Remove(_stale[i]);
        }

        /// <summary>One whole crop prefab per tick; on rotation completion the watermark
        /// advances to the newest plantTime seen, and only then.</summary>
        private void SweepTending()
        {
            if (_cropPrefabs.Length == 0) return;
            if (ModConfig.TendingCarePerPlant.Value <= 0f) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            string prefab = _cropPrefabs[_prefabCursor];
            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] tending sweep failed on '{prefab}': {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                return;
            }

            long watermark = RivalryLedger.PlantWatermark;
            int booked = 0;

            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;

                long plantTime = zdo.GetLong(ZDOVars.s_plantTime, 0L);
                if (plantTime > _rotationMaxPlantTime) _rotationMaxPlantTime = plantTime;
                if (!RivalryMath.IsNewPlant(plantTime, watermark)) continue;

                long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
                if (creator == 0) continue;   // pre-creator worlds and spawner plants book nobody

                RivalryLedger.AddCare(ZoneKey.FromWorldPos(zdo.GetPosition()), creator,
                    ModConfig.TendingCarePerPlant.Value);
                booked++;
            }

            if (ModConfig.VerboseLogging.Value && booked > 0)
                RagnaroksWrath.Log.LogInfo($"[{Name}] '{prefab}': {booked} new plant(s) booked.");

            _found.Clear();
            _sweepIndex = 0;
            _prefabCursor++;

            if (_prefabCursor < _cropPrefabs.Length) return;

            _prefabCursor = 0;
            RivalryLedger.PlantWatermark = _rotationMaxPlantTime;
            _rotationMaxPlantTime = 0;
        }
    }
}
