using HarmonyLib;
using RavenIron.RagnaroksWrath.Systems.World;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Appends the Devastating Storm to the vanilla event list.
    ///
    /// A PREFIX, not a postfix, because house rule 1 says prefix only and this one can honour
    /// that. `RandEventSystem.Awake` is `m_instance = this;` and nothing else — it never touches
    /// `m_events`, which Unity has already deserialized from the prefab by the time Awake runs.
    /// So there is nothing to wait for, and the rule costs us nothing here. (The backlog
    /// suggested a postfix; that was written without the body in front of it.)
    ///
    /// `Priority.Low` and an unconditional `return true`: we have no opinion about whether Awake
    /// should run, and ceding the final say is what keeps every other mod's ordering intact.
    /// </summary>
    [HarmonyPatch(typeof(RandEventSystem), "Awake")]
    public static class Patch_RandEventSystem_Awake
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(RandEventSystem __instance)
        {
            // RegisterStormEvent is idempotent and swallows its own failures: a world that loads
            // without our storm is a world missing one event, and that must never be a world that
            // fails to load.
            WeatherSystem.RegisterStormEvent(__instance);

            return true;
        }
    }
}
