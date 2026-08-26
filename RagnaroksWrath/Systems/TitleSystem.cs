using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Feedback;
using RavenIron.RagnaroksWrath.Net;
using RavenIron.RagnaroksWrath.Systems.World;

namespace RavenIron.RagnaroksWrath.Systems
{
    /// <summary>
    /// Earned titles, from world-sim events — POINTEDLY not from tracked stats. The locked
    /// decision: data mining is The Raven's Call's job, and per the reference sheets a server
    /// cannot read player stats anyway. Everything here is earned by being SOMEWHERE when the
    /// world DID something, which is exactly the data this mod already owns.
    ///
    /// The v1 titles, one per running system, so each is provable end to end today:
    ///
    ///  - Stormrider: inside a Devastating Storm's area while it rages.
    ///  - Plaguewalker: stood in a zone with plague at or past the spread threshold.
    ///  - Winterborn: a configurable stretch of time online through Winter.
    ///
    /// One title per player, LATEST EARNED WINS — a title is where you have been lately, not a
    /// trophy case. The Winterborn clock is in-memory and resets on restart, accepted for v1:
    /// under-awarding a title is a shrug, double-awarding announcements is spam.
    /// </summary>
    public class TitleSystem : IWorldSystem
    {
        public string Name => "TitleSystem";
        public bool Enabled => ModConfig.EnableTitle.Value;
        public float IntervalSeconds => ModConfig.TitleIntervalSeconds.Value;

        public const string Stormrider = "Stormrider";
        public const string Plaguewalker = "Plaguewalker";
        public const string Winterborn = "Winterborn";

        private readonly Dictionary<long, float> _winterSeconds = new Dictionary<long, float>(8);
        private readonly HashSet<long> _knownOnline = new HashSet<long>();
        private readonly HashSet<long> _seenThisTick = new HashSet<long>();
        private bool _storeLoaded;

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] titles: {Stormrider} (in a storm's area), {Plaguewalker} " +
                $"(plague >= {ModConfig.PlagueSpreadThreshold.Value:F2} underfoot), {Winterborn} " +
                $"({ModConfig.WinterbornSeconds.Value:F0}s online in Winter). Latest earned wins.");
        }

        public void Tick(float deltaSeconds)
        {
            // Registration is instance-keyed and cheap; doing it here keeps the server side
            // armed. Clients arm through the nameplate patch, which runs where they render.
            TitleSync.EnsureRegistered();

            if (!Persistence.IsLoaded) return;

            if (!_storeLoaded)
            {
                TitleStore.Load();
                _storeLoaded = TitleStore.IsLoaded;
            }
            if (!_storeLoaded) return;

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            bool winter = SeasonSystem.Current == Season.Winter;
            float plagueThreshold = ModConfig.PlagueSpreadThreshold.Value;

            _seenThisTick.Clear();

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;

                // The identity long, not the session long — see PLAYER-IDENTITY-FACTS. A zero
                // means the ZDO predates SetPlayerID this session; skip rather than record it.
                long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                if (playerId == 0) continue;

                _seenThisTick.Add(playerId);
                Vector3 pos = zdo.GetPosition();

                // A newly appeared player gets the full title table replayed at them — their
                // cache started empty, and a nameplate that shows titles only for people who
                // earned one since you joined reads as broken.
                if (_knownOnline.Add(playerId))
                    foreach (KeyValuePair<long, string> kv in TitleStore.All())
                        TitleSync.Broadcast(kv.Key, kv.Value);

                if (WeatherSystem.IsStormAt(pos))
                    Award(playerId, zdo, Stormrider);

                if (Persistence.Get(ZoneKey.FromWorldPos(pos)).Plague >= plagueThreshold)
                    Award(playerId, zdo, Plaguewalker);

                if (winter)
                {
                    _winterSeconds.TryGetValue(playerId, out float s);
                    s += deltaSeconds;
                    _winterSeconds[playerId] = s;
                    if (s >= ModConfig.WinterbornSeconds.Value)
                        Award(playerId, zdo, Winterborn);
                }
            }

            _knownOnline.RemoveWhere(id => !_seenThisTick.Contains(id));
        }

        private void Award(long playerId, ZDO zdo, string title)
        {
            if (TitleStore.Get(playerId) == title) return;   // latest-earned already this

            TitleStore.Set(playerId, title);
            TitleSync.Broadcast(playerId, title);

            string name = zdo.GetString(ZDOVars.s_playerName, "");
            RagnaroksWrath.Log.LogInfo($"[{Name}] {name} ({playerId}) earned {title}.");

            // Reject the placeholder names the identity sheet warns about; an announcement
            // naming "Stranger" is worse than none.
            if (ModConfig.AnnounceTitles.Value
                && !string.IsNullOrEmpty(name) && name != "Stranger" && name != "...")
                MessageFeed.ToEveryone($"{name} is now {title}.");
        }
    }
}
