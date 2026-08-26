using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Feedback;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// The land's overall condition — a DERIVED aggregate, computed bottom-up from the zone
    /// store and the weather every pass, never stored top-down. If this system dies, nothing is
    /// lost: the next pass recomputes the same answer from the same substrate, which is the
    /// whole argument for deriving instead of persisting.
    ///
    /// Announcements happen only on TRANSITIONS, and transitions are hysteresis-guarded in
    /// WorldConditionRules — the "rare by policy" contract MessageFeed.ToEveryone demands is
    /// enforced by construction, not restraint.
    /// </summary>
    public class WorldStateSystem : IWorldSystem
    {
        public string Name => "WorldStateSystem";
        public bool Enabled => ModConfig.EnableWorldState.Value;
        public float IntervalSeconds => ModConfig.WorldStateIntervalSeconds.Value;

        /// <summary>Latest aggregate, for any system that wants the numbers.</summary>
        public static BiomeMetrics Metrics { get; private set; }

        /// <summary>Latest derived condition. Stable until the first pass says otherwise.</summary>
        public static WorldCondition Condition { get; private set; } = WorldCondition.Stable;

        private bool _hasDerivedOnce;

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] deriving world condition every {IntervalSeconds:F0}s " +
                $"(flourishing<={ModConfig.WorldFlourishingBurden.Value:F1}, " +
                $"ailing>={ModConfig.WorldAilingBurden.Value:F1}, " +
                $"stricken>={ModConfig.WorldStrickenBurden.Value:F1}, storm +{ModConfig.WorldStormBurden.Value:F1}).");
        }

        public void Tick(float deltaSeconds)
        {
            if (!Persistence.IsLoaded) return;

            BiomeMetrics metrics = BiomeMetrics.Compute(Persistence.All());
            Metrics = metrics;

            // Weather is part of the land's condition: an active storm weighs on the world for
            // as long as it runs. This is the "x WeatherSystem" half of the derivation.
            float burden = metrics.Burden()
                + (WeatherSystem.StormActive ? ModConfig.WorldStormBurden.Value : 0f);

            WorldCondition next = WorldConditionRules.Derive(
                burden,
                Condition,
                ModConfig.WorldFlourishingBurden.Value,
                ModConfig.WorldAilingBurden.Value,
                ModConfig.WorldStrickenBurden.Value);

            // The first derivation after load SETS the condition without announcing it: a server
            // rebooting into a stricken world has not just become stricken, and greeting every
            // reboot with doom is how announcements stop meaning anything.
            if (!_hasDerivedOnce)
            {
                _hasDerivedOnce = true;
                Condition = next;
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] initial condition {Condition} (burden {burden:F2}, " +
                    $"{metrics.TrackedZones} tracked, {metrics.InfectedZones} infected).");
                return;
            }

            if (next == Condition) return;

            bool worsened = next > Condition;
            Condition = next;

            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] condition {(worsened ? "worsened" : "improved")} to {next} " +
                $"(burden {burden:F2}).");

            MessageFeed.ToEveryone(AnnouncementFor(next, worsened));
        }

        private static string AnnouncementFor(WorldCondition condition, bool worsened)
        {
            switch (condition)
            {
                case WorldCondition.Flourishing: return "The land flourishes.";
                case WorldCondition.Ailing: return "The land sickens.";
                case WorldCondition.Stricken: return "The land is stricken. The gods watch.";
                default:
                    return worsened ? "The land's bloom fades." : "The land recovers.";
            }
        }
    }
}
