using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Task 14, the capstone: consecrated places. Sites where the world's story peaked
    /// become lasting landmarks with real auras, marked by a physical stone. Four
    /// completions consecrate: a great fire fully healed (blessed), a plague driven
    /// through zero (blessed), a spawn war resolved (wild blessed / blight cursed), and
    /// the rarest — the world recovering from Stricken consecrates the one zone whose
    /// healing defined the era.
    ///
    /// Consecration is contact-driven by construction (cures and wars need players), so
    /// someone is present when a stone rises; placement is delegated to that client
    /// (RelicSync), which has real terrain. With nobody covering the zone the moment
    /// queues — nothing spawns blind on headless.
    /// </summary>
    public sealed class RelicSystem : IWorldSystem
    {
        public string Name => "RelicSystem";
        public bool Enabled => ModConfig.EnableRelic.Value;
        public float IntervalSeconds => ModConfig.RelicIntervalSeconds.Value;

        private const float SaveCadenceSeconds = 60f;
        private const float RebroadcastSeconds = 60f;

        private bool _ledgerLoaded;
        private float _sinceSave;
        private float _sinceBroadcast;
        private WorldCondition _lastCondition = WorldCondition.Stable;
        private bool _conditionSeen;

        // One story line per zone per session — the stone speaks to arrivals, not per tick.
        private readonly HashSet<ZoneKey> _storyTold = new HashSet<ZoneKey>();

        // Presence rebuilt each tick: zones covered by a real character's contact ring,
        // with one covering peer per zone for placement routing.
        private readonly Dictionary<ZoneKey, long> _presentPeer = new Dictionary<ZoneKey, long>(64);
        private readonly HashSet<ZoneKey> _occupied = new HashSet<ZoneKey>();

        // War resolutions handed over by RivalrySystem within a tick; drained here.
        private static readonly List<KeyValuePair<ZoneKey, bool>> _warResults =
            new List<KeyValuePair<ZoneKey, bool>>(4);

        private readonly List<ZoneKey> _scratch = new List<ZoneKey>(16);

        /// <summary>RivalrySystem's hand-off at the contested->uncontested edge.</summary>
        public static void NotifyWarResolved(ZoneKey zone, bool wildWon)
        {
            if (!ModConfig.EnableRelic.Value) return;
            _warResults.Add(new KeyValuePair<ZoneKey, bool>(zone, wildWon));
        }

        // ---- aura providers (authority-side; clients go through RelicSync) -------------

        public static float RecoveryMultiplierAt(ZoneKey zone)
        {
            if (!ModConfig.EnableRelic.Value || !RelicLedger.IsLoaded) return 1f;
            RelicLedger.Relic r = RelicLedger.RelicAt(zone);
            return RelicMath.RecoveryMultiplier(r.Type, r.Cursed,
                ModConfig.RelicBlessedRecoveryMult.Value, ModConfig.RelicCursedRecoveryMult.Value);
        }

        public static float ExposureDecayMultAt(ZoneKey zone)
        {
            if (!ModConfig.EnableRelic.Value || !RelicLedger.IsLoaded) return 1f;
            RelicLedger.Relic r = RelicLedger.RelicAt(zone);
            return RelicMath.ExposureDecayMultiplier(r.Type, r.Cursed,
                ModConfig.RelicBlessedExposureDrainMult.Value);
        }

        public static float ExposureMinutesMultAt(ZoneKey zone)
        {
            if (!ModConfig.EnableRelic.Value || !RelicLedger.IsLoaded) return 1f;
            RelicLedger.Relic r = RelicLedger.RelicAt(zone);
            return RelicMath.ExposureMinutesMultiplier(r.Type, r.Cursed,
                ModConfig.RelicCursedExposureAccrualMult.Value);
        }

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] consecrated places: fire healed / plague cured -> blessed, war " +
                $"resolved -> by the victor, Stricken recovered -> the era stone. Auras: " +
                $"recovery x{ModConfig.RelicBlessedRecoveryMult.Value:0.##} blessed / " +
                $"x{ModConfig.RelicCursedRecoveryMult.Value:0.##} cursed; exposure drain " +
                $"x{ModConfig.RelicBlessedExposureDrainMult.Value:0.##} blessed, accrual " +
                $"x{ModConfig.RelicCursedExposureAccrualMult.Value:0.##} cursed.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) { _warResults.Clear(); return; }

            if (!_ledgerLoaded)
            {
                RelicLedger.Load();
                _ledgerLoaded = RelicLedger.IsLoaded;
            }
            if (!_ledgerLoaded) { _warResults.Clear(); return; }

            RelicSync.EnsureRegistered();
            CollectPresence();

            UpdatePeaks();
            ConsecrateCompletions();
            DrainWarResults();
            WatchEra();
            RetryPending();
            TellStories();

            _sinceBroadcast += deltaSeconds;
            if (_sinceBroadcast >= RebroadcastSeconds)
            {
                _sinceBroadcast = 0f;
                RelicSync.BroadcastAll();   // slow full-table push; joiners converge inside a minute
            }

            _sinceSave += deltaSeconds;
            if (_sinceSave >= SaveCadenceSeconds)
            {
                _sinceSave = 0f;
                RelicLedger.SaveIfDirty();
            }
        }

        /// <summary>Desecration, reported by the stone's owner client. Lifts the aura,
        /// books the vandal into the rivalry ledger, and re-arms the zone for a fresh
        /// story cycle (peaks were already cleared when the stone rose).</summary>
        public static void OnRelicBroken(ZoneKey zone, long vandalPlayerId)
        {
            if (!RelicLedger.IsLoaded) return;

            RelicLedger.Relic r = RelicLedger.RelicAt(zone);
            if (!r.Standing) return;   // already lifted, or a stray report

            RelicLedger.RemoveRelic(zone);
            RelicSync.BroadcastRemoval(zone);

            if (vandalPlayerId != 0 && ModConfig.EnableRivalry.Value && RivalryLedger.IsLoaded)
                RivalryLedger.AddHarm(zone, vandalPlayerId, ModConfig.RelicVandalHarm.Value);

            Feedback.MessageFeed.ToPlayersNear(zone.ToWorldPos(), 64f,
                "The stone lies broken. The land forgets.",
                Feedback.MessageFeed.Placement.Centre);

            RagnaroksWrath.Log.LogInfo(
                $"[RelicSystem] relic in {zone} desecrated" +
                (vandalPlayerId != 0 ? $" by {vandalPlayerId} (harm booked)." : " (no attacker identified)."));
        }

        // ---- the passes -----------------------------------------------------------------

        private void CollectPresence()
        {
            _presentPeer.Clear();
            _occupied.Clear();

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            int radius = Math.Max(0, ModConfig.BiomeContactRadiusZones.Value);

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;
                if (zdo.GetLong(ZDOVars.s_playerID, 0L) == 0) continue;

                ZoneKey centre = ZoneKey.FromWorldPos(zdo.GetPosition());
                _occupied.Add(centre);

                long owner = zdo.GetOwner();
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var zone = new ZoneKey(centre.X + dx, centre.Y + dy);
                    if (owner != 0 && !_presentPeer.ContainsKey(zone))
                        _presentPeer[zone] = owner;
                }
            }
        }

        /// <summary>Watermark scorch and plague peaks for zones with no standing stone —
        /// only stored zones can carry values, so the sparse store is the whole sweep.</summary>
        private void UpdatePeaks()
        {
            float fireThr = ModConfig.FireRelicPeakThreshold.Value;
            float plagueThr = ModConfig.PlagueRelicPeakThreshold.Value;

            foreach (KeyValuePair<ZoneKey, ZoneState> kv in Persistence.All())
            {
                if (RelicLedger.RelicAt(kv.Key).Standing) continue;

                RelicLedger.Peaks peaks = RelicLedger.PeaksFor(kv.Key);
                float scorch = RelicMath.TrackPeak(kv.Value.Scorch, fireThr, peaks.Scorch);
                float plague = RelicMath.TrackPeak(kv.Value.Plague, plagueThr, peaks.Plague);

                if (scorch != peaks.Scorch || plague != peaks.Plague)
                    RelicLedger.SetPeaks(kv.Key, new RelicLedger.Peaks { Scorch = scorch, Plague = plague });
            }
        }

        /// <summary>A recorded peak whose value now reads zero is a story completed. This
        /// walks the PEAK rows, not the store: healed zones leave the sparse store, and a
        /// peak row for a zone the store no longer holds is exactly a through-zero
        /// candidate. Snapshot first — consecration mutates the peak table.</summary>
        private void ConsecrateCompletions()
        {
            _peaksSnapshot.Clear();
            foreach (KeyValuePair<ZoneKey, RelicLedger.Peaks> kv in RelicLedger.AllPeaks())
                _peaksSnapshot.Add(kv);

            for (int i = 0; i < _peaksSnapshot.Count; i++)
            {
                ZoneKey zone = _peaksSnapshot[i].Key;
                RelicLedger.Peaks peaks = _peaksSnapshot[i].Value;
                ZoneState state = Persistence.Get(zone);

                if (RelicMath.ShouldConsecrate(peaks.Scorch, state.Scorch))
                {
                    TryConsecrate(zone, RelicMath.Fire, cursed: false);
                    continue;
                }
                if (RelicMath.ShouldConsecrate(peaks.Plague, state.Plague))
                    TryConsecrate(zone, RelicMath.Plague, cursed: false);
            }
        }

        private readonly List<KeyValuePair<ZoneKey, RelicLedger.Peaks>> _peaksSnapshot =
            new List<KeyValuePair<ZoneKey, RelicLedger.Peaks>>(16);

        private void DrainWarResults()
        {
            for (int i = 0; i < _warResults.Count; i++)
                TryConsecrate(_warResults[i].Key, RelicMath.Contest, cursed: !_warResults[i].Value);
            _warResults.Clear();
        }

        /// <summary>The era stone: entering Stricken snapshots every stored zone's damage;
        /// recovering to Stable or better consecrates the one zone that healed the most.</summary>
        private void WatchEra()
        {
            WorldCondition now = WorldStateSystem.Condition;
            if (!_conditionSeen) { _conditionSeen = true; _lastCondition = now; return; }
            if (now == _lastCondition) return;

            if (now == WorldCondition.Stricken && !RelicLedger.EraArmed)
            {
                int rows = 0;
                foreach (KeyValuePair<ZoneKey, ZoneState> kv in Persistence.All())
                {
                    float damage = RivalryMath.ZoneDamage(kv.Value);
                    if (damage > 0f) { RelicLedger.SetEraSnapshot(kv.Key, damage); rows++; }
                }
                RagnaroksWrath.Log.LogInfo($"[{Name}] the land is stricken — era armed over {rows} zone(s).");
            }
            else if (RelicLedger.EraArmed
                     && (now == WorldCondition.Stable || now == WorldCondition.Flourishing))
            {
                ZoneKey best = default;
                float bestDelta = 0f;
                foreach (KeyValuePair<ZoneKey, float> kv in RelicLedger.EraSnapshot())
                {
                    if (RelicLedger.RelicAt(kv.Key).Standing) continue;
                    float delta = kv.Value - RivalryMath.ZoneDamage(Persistence.Get(kv.Key));
                    if (delta > bestDelta) { bestDelta = delta; best = kv.Key; }
                }

                RelicLedger.ClearEra();
                if (bestDelta > 0f)
                {
                    RagnaroksWrath.Log.LogInfo(
                        $"[{Name}] the era turns — {best} healed {bestDelta:0.###}, the most of any ground.");
                    TryConsecrate(best, RelicMath.Era, cursed: false);
                }
            }

            _lastCondition = now;
        }

        private void RetryPending()
        {
            if (RelicLedger.PendingCount == 0) return;

            _scratch.Clear();
            foreach (KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllPending())
                if (_presentPeer.ContainsKey(kv.Key))
                    _scratch.Add(kv.Key);

            for (int i = 0; i < _scratch.Count; i++)
            {
                RelicLedger.Relic relic = default;
                foreach (KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllPending())
                    if (kv.Key == _scratch[i]) { relic = kv.Value; break; }

                RelicLedger.RemovePending(_scratch[i]);
                Commit(_scratch[i], relic);
            }
        }

        private void TellStories()
        {
            foreach (KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllRelics())
            {
                if (_storyTold.Contains(kv.Key)) continue;
                if (!_occupied.Contains(kv.Key)) continue;   // the stone speaks to who STANDS there

                _storyTold.Add(kv.Key);
                Feedback.MessageFeed.ToPlayersNear(kv.Key.ToWorldPos(), 64f,
                    RelicMath.Story(kv.Value.Type, kv.Value.Cursed));
            }
        }

        private void TryConsecrate(ZoneKey zone, int type, bool cursed)
        {
            if (RelicLedger.RelicAt(zone).Standing) return;   // one relic per zone; the story is told

            var relic = new RelicLedger.Relic
            {
                Type = type,
                Cursed = cursed,
                Day = SeasonSystem.CurrentDayOrZero(),
            };

            if (!_presentPeer.ContainsKey(zone))
            {
                RelicLedger.AddPending(zone, relic);
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] consecration of {zone} queued — nobody present to raise the stone.");
                return;
            }

            Commit(zone, relic);
        }

        private void Commit(ZoneKey zone, RelicLedger.Relic relic)
        {
            if (!relic.Standing) return;
            RelicLedger.SetRelic(zone, relic);
            RelicSync.BroadcastRelic(zone, relic);

            if (_presentPeer.TryGetValue(zone, out long peer))
                RelicSync.RequestPlacement(peer, zone, relic);

            _storyTold.Remove(zone);   // a fresh stone earns a fresh telling

            Feedback.MessageFeed.ToPlayersNear(zone.ToWorldPos(), 64f,
                relic.Cursed
                    ? "The gods curse this ground. Its stone remembers."
                    : "The gods bless this ground. Its stone remembers.",
                Feedback.MessageFeed.Placement.Centre);

            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] consecrated {zone}: type {relic.Type}, " +
                $"{(relic.Cursed ? "cursed" : "blessed")}, day {relic.Day}.");
        }
    }
}
