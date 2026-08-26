using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Corruption's first writer: land held under plague or scorch corrupts, and corruption
    /// outlives the disaster that caused it (recovery is BiomeDrift's, slower than this).
    /// An EVENT system on live tick time — the maths is EcologyPressure, pure and tested.
    ///
    /// Acts on STORED state regardless of player presence, like plague spread and unlike
    /// drift: the pressure exists because the plague exists, not because somebody is watching
    /// it. The AwayFromHome rule this codebase guards is about zone LOAD state — which this
    /// never reads.
    /// </summary>
    public class EcologySystem : IWorldSystem
    {
        public string Name => "EcologySystem";
        public bool Enabled => ModConfig.EnableEcology.Value;
        public float IntervalSeconds => ModConfig.EcologyIntervalSeconds.Value;

        // Snapshot buffer: EcologyPressure writes through Persistence.Set, which may remove
        // entries, and mutating the store while enumerating it is the enumerator's veto.
        private readonly List<KeyValuePair<ZoneKey, ZoneState>> _snapshot =
            new List<KeyValuePair<ZoneKey, ZoneState>>(64);

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] blight corrupts at {ModConfig.EcologyCorruptionPerHour.Value:F3}/h " +
                $"where plague>={ModConfig.EcologyPlagueThreshold.Value:F2} or " +
                $"scorch>={ModConfig.EcologyScorchThreshold.Value:F2}.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;

            float rate = ModConfig.EcologyCorruptionPerHour.Value;
            float plagueT = ModConfig.EcologyPlagueThreshold.Value;
            float scorchT = ModConfig.EcologyScorchThreshold.Value;

            _snapshot.Clear();
            foreach (KeyValuePair<ZoneKey, ZoneState> kv in Persistence.All())
            {
                if (kv.Value.Plague >= plagueT || kv.Value.Scorch >= scorchT)
                    _snapshot.Add(kv);
            }

            for (int i = 0; i < _snapshot.Count; i++)
            {
                ZoneState after = EcologyPressure.Apply(
                    _snapshot[i].Value, deltaSeconds, rate, plagueT, scorchT);

                Persistence.Set(_snapshot[i].Key, after);
            }
        }
    }
}
