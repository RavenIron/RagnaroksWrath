using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class RagnaroksWrath : BaseUnityPlugin
    {
        public const string PluginId      = "com.raveniron.ragnarokswrath";
        public const string PluginName    = "Ragnarok's Wrath";
        public const string PluginVersion = "0.15.2";

        public static RagnaroksWrath Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        /// <summary>
        /// True once ZNet exists and this process is the authority for world simulation.
        /// Null before ZNet.Start â€” callers must handle "not known yet", which is why this
        /// is a method rather than a cached bool set at Awake.
        /// </summary>
        public static bool IsSimulationAuthority()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return false;
            return znet.IsServer();
        }

        /// <summary>Headless dedicated server: no local player, no presence layer, no rendering.</summary>
        public static bool IsDedicated()
        {
            ZNet znet = ZNet.instance;
            return znet != null && znet.IsDedicated();
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Bind(base.Config);

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll();

            // WorldTick is a plain MonoBehaviour driven from Update â€” deliberately NOT a
            // coroutine. See House Style rule 2: long-lived coroutines in this codebase have
            // repeatedly grown a `continue`-past-`yield` hard-lock.
            gameObject.AddComponent<WorldTick>();

            RegisterSystems();

            // Visuals exist only where something renders. GraphicsDeviceType.Null is the
            // headless tell that survives compiling against client reference DLLs (where
            // ZNet.IsDedicated() is a hardcoded false).
            if (UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                gameObject.AddComponent<Visuals.PlagueFog>();
                gameObject.AddComponent<Visuals.FrostBreath>();
                gameObject.AddComponent<Visuals.ScorchAsh>();
                gameObject.AddComponent<Client.HealthEffects>();
                gameObject.AddComponent<Client.ConsequenceEffects>();
            }


            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        /// <summary>
        /// Every system registers here, in one place. Systems are ticked in registration order
        /// by WorldTick's round-robin cursor, so ordering here is a mild scheduling hint only â€”
        /// no system may depend on another having ticked first within the same frame.
        /// </summary>
        private static void RegisterSystems()
        {
            WorldTick.Register(new Systems.World.SeasonSystem());
            WorldTick.Register(new Systems.World.WeatherSystem());
            WorldTick.Register(new Systems.World.WindSystem());
            WorldTick.Register(new Systems.World.BiomeStateSystem());
            WorldTick.Register(new Systems.World.FireSystem());
            WorldTick.Register(new Systems.World.PlagueSystem());
            WorldTick.Register(new Systems.World.EcologySystem());
            WorldTick.Register(new Systems.World.FarmingSystem());
            WorldTick.Register(new Systems.World.WorldStateSystem());
            WorldTick.Register(new Systems.World.HealthSystem());
            WorldTick.Register(new Systems.World.ConsequenceSystem());
            WorldTick.Register(new Systems.World.RivalrySystem());
            WorldTick.Register(new Systems.TitleSystem());
            WorldTick.Register(new Systems.ZoneSyncSystem());
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }
    }
}
