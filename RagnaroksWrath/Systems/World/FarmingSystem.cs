using System;
using System.Collections.Generic;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Soil depletion from crop density — Fertility's first writer, and the ZDO sweep the
    /// backlog deferred since task 2 finally landing.
    ///
    /// WHAT THIS IS, AND POINTEDLY IS NOT. Crops planted in a zone tire its soil over time:
    /// the more saplings standing in a zone, the faster `Fertility` (a DEPLETION, 0 = rested)
    /// climbs; rest heals it through BiomeDrift like everything else. What this system does
    /// NOT do yet is change growth speed or yield — a plant's lifecycle runs on its ZDO's
    /// OWNER, which on a dedicated server is a nearby client, and clients have no zone store
    /// to read. Consuming depletion needs the client plugin's state sync (task 9). Writing it
    /// is server-side and honest today, so the soil is already tired by the time the effect
    /// arrives.
    ///
    /// THE SWEEP. `ZDOMan.GetAllZDOsWithPrefabIterative` is vanilla's own self-chunking walk
    /// (verified: appends matches, returns true when the index has covered every sector). One
    /// WHOLE prefab is drained per tick — see the comment at the call for why resuming chunks
    /// across ticks was a bug — and the interval staggers us against AwayFromHome's 60s
    /// full-index rescan, which is the whole reason it defaults to 45.
    ///
    /// Crop prefab names are CONFIG, not code: they are data about the game's content, they
    /// drift with game patches, and a wrong name costs a silent zero matches — which the
    /// per-sweep verbose line exists to make visible.
    /// </summary>
    public class FarmingSystem : IWorldSystem
    {
        public string Name => "FarmingSystem";
        public bool Enabled => ModConfig.EnableFarming.Value;
        public float IntervalSeconds => ModConfig.FarmingIntervalSeconds.Value;

        private string[] _cropPrefabs = Array.Empty<string>();
        private int _prefabCursor;

        // GetAllZDOsWithPrefabIterative's resume state for the prefab currently mid-walk.
        private readonly List<ZDO> _found = new List<ZDO>(128);
        private int _sweepIndex;

        // Zone -> crop count, accumulated across the whole prefab rotation, applied when the
        // rotation completes. Applying per-prefab would deplete carrot zones every pass but
        // barley zones only when their turn came up.
        private readonly Dictionary<ZoneKey, int> _cropCounts = new Dictionary<ZoneKey, int>(32);
        private float _rotationSeconds;

        public void Initialise()
        {
            _cropPrefabs = (ModConfig.FarmingCropPrefabs.Value ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < _cropPrefabs.Length; i++) _cropPrefabs[i] = _cropPrefabs[i].Trim();

            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] sweeping {_cropPrefabs.Length} crop prefab(s) every {IntervalSeconds:F0}s; " +
                $"depletion {ModConfig.FarmingDepletionPerCropHour.Value:F4}/crop-hour. " +
                $"Depletion is WRITTEN here; the growth consumer bites client-side " +
                $"(x{ModConfig.FarmingGrowthSlowdownAtFull.Value:0.##} grow time at full depletion).");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;
            if (_cropPrefabs.Length == 0) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            _rotationSeconds += deltaSeconds;

            // One WHOLE prefab per tick. The iterative call yields every ~400 populated
            // sectors, and a world's locations populate thousands — resuming one chunk per
            // tick (the first version of this) stretched a single prefab across minutes and a
            // rotation across the better part of an hour. Vanilla's own callers drain it in a
            // loop within one frame; each chunk is a few thousand integer compares, so a full
            // walk sits comfortably inside WorldTick's budget. Termination is structural:
            // index advances every call until it passes the sector array.
            string prefab = _cropPrefabs[_prefabCursor];
            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] sweep failed on '{prefab}': {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                return;
            }

            // One prefab fully swept: bank its zone counts, move to the next.
            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;

                ZoneKey zone = ZoneKey.FromWorldPos(zdo.GetPosition());
                _cropCounts.TryGetValue(zone, out int n);
                _cropCounts[zone] = n + 1;
            }

            if (ModConfig.VerboseLogging.Value && _found.Count > 0)
                RagnaroksWrath.Log.LogInfo($"[{Name}] '{prefab}': {_found.Count} standing.");

            _found.Clear();
            _sweepIndex = 0;
            _prefabCursor++;

            if (_prefabCursor < _cropPrefabs.Length) return;

            // Rotation complete: apply depletion for the real time the rotation took, so the
            // rate stays per-crop-hour no matter how many prefabs are configured or how long
            // the sweeps ran.
            _prefabCursor = 0;
            float hours = _rotationSeconds / 3600f;
            _rotationSeconds = 0f;

            float perCropHour = ModConfig.FarmingDepletionPerCropHour.Value;
            if (perCropHour > 0f && hours > 0f)
            {
                foreach (KeyValuePair<ZoneKey, int> kv in _cropCounts)
                {
                    ZoneState state = Persistence.Get(kv.Key);
                    state.Fertility += perCropHour * kv.Value * hours;
                    Persistence.Set(kv.Key, state);   // clamps; sparse boundary as ever
                }
            }

            if (ModConfig.VerboseLogging.Value && _cropCounts.Count > 0)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] rotation complete: crops in {_cropCounts.Count} zone(s).");

            _cropCounts.Clear();
        }
    }
}
