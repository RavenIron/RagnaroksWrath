using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Feedback;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Task 12's server half, deliberately THIN: every physical act lives client-side
    /// (Patch_Consequence, ConsequenceEffects) where instances exist. All the authority
    /// does is speak — the mixed-by-weight voice: per-bush and per-deer effects are silent,
    /// and a zone earns ONE MessageFeed line the first time a player stands in it while it
    /// holds any consequence, worded by the worst thing true about it.
    ///
    /// The announced set is in-memory and per-session, like the Winterborn clock:
    /// re-announcing after a restart is a shrug, announcing twice in one session is spam.
    /// </summary>
    public class ConsequenceSystem : IWorldSystem
    {
        public string Name => "ConsequenceSystem";
        public bool Enabled => ModConfig.EnableConsequence.Value;
        public float IntervalSeconds => ModConfig.ConsequenceIntervalSeconds.Value;

        private readonly HashSet<ZoneKey> _announced = new HashSet<ZoneKey>();

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] the drift store grows hands: barren pickables (plague >= " +
                $"{ModConfig.BarrenPlagueThreshold.Value:F2} / scorch >= {ModConfig.BarrenScorchThreshold.Value:F2}), " +
                $"starred spawns (corruption >= {ModConfig.EmpowerCorruptionThreshold.Value:F2}, " +
                $"x{ModConfig.EmpowerLevelUpMultiplierAtFull.Value:F1} odds at full), sickened wildlife " +
                $"(plague >= {ModConfig.SickenPlagueThreshold.Value:F2}), withering crops (blight >= " +
                $"{ModConfig.CropWitherBlightThreshold.Value:F2}). Player structures are NEVER touched.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;
            if (!ModConfig.AnnounceConsequences.Value) return;

            ZNet znet = ZNet.instance;
            if (znet == null) return;

            List<ZDO> characters;
            try { characters = znet.GetAllCharacterZDOS(); }
            catch { return; }
            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                ZDO zdo = characters[i];
                if (zdo == null || !zdo.IsValid()) continue;

                Vector3 pos = zdo.GetPosition();
                ZoneKey zone = ZoneKey.FromWorldPos(pos);
                if (_announced.Contains(zone)) continue;

                ConsequenceFlags flags = ConsequenceMath.FlagsFor(Persistence.Get(zone),
                    ModConfig.BarrenPlagueThreshold.Value, ModConfig.BarrenScorchThreshold.Value,
                    ModConfig.SickenPlagueThreshold.Value, ModConfig.EmpowerCorruptionThreshold.Value,
                    ModConfig.CropWitherBlightThreshold.Value);
                if (flags == ConsequenceFlags.None) continue;

                _announced.Add(zone);
                MessageFeed.ToPlayersNear(pos, 64f, LineFor(flags));

                if (ModConfig.VerboseLogging.Value)
                    RagnaroksWrath.Log.LogInfo($"[{Name}] zone {zone} announced: {flags}.");
            }
        }

        /// <summary>One line, worded by the worst truth. Order matters: empowered ground is
        /// the danger, barren ground the misery, sickness the omen, withering the detail.</summary>
        internal static string LineFor(ConsequenceFlags flags)
        {
            if ((flags & ConsequenceFlags.Empowered) != 0) return "Something festers in this ground.";
            if ((flags & ConsequenceFlags.Barren) != 0) return "The land here has gone barren.";
            if ((flags & ConsequenceFlags.Sickening) != 0) return "The wild things here are sickening.";
            return "Nothing planted here will live.";
        }
    }
}
