using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// The world's state reaching the player's body — task 11's server half. The server
    /// decides PRESSURE (exposure accrued from plague underfoot, decayed elsewhere, remedy-
    /// adjusted, persisted so relogging is not a cure); the owning client applies EFFECT
    /// (Client.HealthEffects turns synced exposure into a status effect, its messages, and
    /// the frost chill). The TitleSync split, promoted to a whole system.
    ///
    /// Sweeps character ZDOs like TitleSystem — never Player lists, which do not exist
    /// headless. Exposure only moves for players who are ONLINE: a character with no ZDO has
    /// no position, so their ledger row simply waits for them, sick exactly as they left.
    /// </summary>
    public class HealthSystem : IWorldSystem
    {
        public string Name => "HealthSystem";
        public bool Enabled => ModConfig.EnableHealth.Value;
        public float IntervalSeconds => ModConfig.HealthIntervalSeconds.Value;

        private const float SaveCadenceSeconds = 60f;
        private const float KeepaliveSeconds = 30f;

        private bool _storeLoaded;
        private float _sinceSave;

        // Reused per tick so a steady state allocates nothing.
        private readonly Dictionary<long, long> _peerByPlayer = new Dictionary<long, long>(8);
        private readonly Dictionary<long, float> _lastSent = new Dictionary<long, float>(8);
        private readonly Dictionary<long, float> _lastSentAt = new Dictionary<long, float>(8);
        private readonly HashSet<long> _seenThisTick = new HashSet<long>();
        private readonly List<long> _departed = new List<long>(4);

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] exposure: clean->max in {ModConfig.ExposureMinutesToMax.Value:F0}min at full plague " +
                $"(floor {FogMath.VisibleFloor:F2}), max->clean in {ModConfig.ExposureRecoveryMinutes.Value:F0}min; " +
                $"poison-resist x{ModConfig.ExposurePoisonResistMultiplier.Value:F2} accrual, " +
                $"rested x{ModConfig.ExposureRestedRecoveryMultiplier.Value:F1} recovery. " +
                "Weakens, never kills — regen multipliers only, applied client-side.");
        }

        public void Tick(float deltaSeconds)
        {
            HealthSync.EnsureRegistered();

            if (!Persistence.IsLoaded) return;

            if (!_storeLoaded)
            {
                HealthStore.Load();
                _storeLoaded = HealthStore.IsLoaded;
            }
            if (!_storeLoaded) return;

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            float now = Time.realtimeSinceStartup;
            MapPeersToPlayers(znet);
            _seenThisTick.Clear();

            float minutesToMax = ModConfig.ExposureMinutesToMax.Value;
            float recoveryMinutes = ModConfig.ExposureRecoveryMinutes.Value;
            float poisonMult = ModConfig.ExposurePoisonResistMultiplier.Value;
            float restedMult = ModConfig.ExposureRestedRecoveryMultiplier.Value;

            int sick = 0;

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;

                long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                if (playerId == 0) continue;

                _seenThisTick.Add(playerId);

                ZoneKey underfoot = ZoneKey.FromWorldPos(zdo.GetPosition());
                float plague = Persistence.Get(underfoot).Plague;
                int remedies = RemedyBitsFor(playerId, now);

                float current = HealthStore.Get(playerId);

                // Phase C mercy: ground whose memory you hold as dominant carer sheds
                // your sickness faster. Only the decay branch — the land's favour helps
                // you heal, it does not shield you from fresh plague.
                float mercy = RivalrySystem.IsDominantCarer(underfoot, playerId)
                    ? 1f + ModConfig.MercySicknessBonus.Value : 1f;

                // Task 14: consecrated ground. Blessed joins the mercy chain (decay only —
                // favour heals, it does not shield); cursed shrinks minutes-to-max so
                // exposure accrues faster on ground the gods marked against you.
                mercy *= RelicSystem.ExposureDecayMultAt(underfoot);
                float relicMinutes = minutesToMax * RelicSystem.ExposureMinutesMultAt(underfoot);

                float next = plague >= FogMath.VisibleFloor
                    ? ExposureMath.Accrue(current, plague, relicMinutes,
                        (remedies & HealthSync.RemedyPoisonResist) != 0, poisonMult, deltaSeconds)
                    : ExposureMath.Decay(current, recoveryMinutes,
                        (remedies & HealthSync.RemedyRested) != 0, restedMult, deltaSeconds, mercy);

                if (next != current) HealthStore.Set(playerId, next);
                if (next > 0f) sick++;

                MaybeSync(playerId, next, now);
            }

            PruneDeparted();

            _sinceSave += deltaSeconds;
            if (_sinceSave >= SaveCadenceSeconds)
            {
                _sinceSave = 0f;
                HealthStore.SaveIfDirty();
            }

            if (ModConfig.VerboseLogging.Value && sick > 0)
                RagnaroksWrath.Log.LogInfo($"[{Name}] {sick} exposed player(s) of {_seenThisTick.Count} online.");
        }

        /// <summary>
        /// Push a player's exposure to their peer when it crosses a quantize step, plus a slow
        /// keepalive so one dropped packet cannot leave a client sick forever. A listen host's
        /// own player has no peer row and needs no packet — ExposureFor reads the ledger.
        /// </summary>
        private void MaybeSync(long playerId, float exposure, float now)
        {
            if (!_peerByPlayer.TryGetValue(playerId, out long peerUid)) return;

            _lastSent.TryGetValue(playerId, out float sent);
            _lastSentAt.TryGetValue(playerId, out float sentAt);

            bool due = ExposureMath.QuantizedDiffer(sent, exposure)
                       || (exposure > 0f && now - sentAt >= KeepaliveSeconds);
            if (!due) return;

            HealthSync.SendExposure(peerUid, playerId, exposure);
            _lastSent[playerId] = exposure;
            _lastSentAt[playerId] = now;
        }

        /// <summary>Peer uid per playerID, rebuilt each tick — a peer's character ZDO carries
        /// the identity long. The host's own player is deliberately absent.</summary>
        private void MapPeersToPlayers(ZNet znet)
        {
            _peerByPlayer.Clear();

            List<ZNetPeer> peers;
            try { peers = znet.GetPeers(); }
            catch { return; }
            if (peers == null) return;

            ZDOMan zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                if (peer == null || !peer.IsReady()) continue;

                ZDO zdo = zdoMan.GetZDO(peer.m_characterID);
                if (zdo == null) continue;

                long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                if (playerId != 0) _peerByPlayer[playerId] = peer.m_uid;
            }
        }

        /// <summary>Remedy bits for a player: remote players report over the wire per peer;
        /// a listen host's own player is read directly off the local Player.</summary>
        private int RemedyBitsFor(long playerId, float now)
        {
            if (_peerByPlayer.TryGetValue(playerId, out long peerUid))
                return HealthSync.RemedyBitsFor(peerUid, now);

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId)
                return HealthSync.ComputeLocalRemedyBits(local);

            return 0;
        }

        /// <summary>Drop sync bookkeeping for players who logged off. Their LEDGER row stays —
        /// that is the whole point — only the per-session send state goes.</summary>
        private void PruneDeparted()
        {
            _departed.Clear();
            foreach (KeyValuePair<long, float> kv in _lastSent)
                if (!_seenThisTick.Contains(kv.Key))
                    _departed.Add(kv.Key);

            for (int i = 0; i < _departed.Count; i++)
            {
                _lastSent.Remove(_departed[i]);
                _lastSentAt.Remove(_departed[i]);
            }
        }
    }
}
