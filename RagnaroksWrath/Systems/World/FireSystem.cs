using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// The world's memory of fire — a BRIDGE, not a fire simulation.
    ///
    /// FireFront (com.raveniron.firefront) already owns fire: ignition from vanilla fire
    /// damage, spread, ground cells, wind bias, extinguishing, VFX, client sync — shipped and
    /// verified on a dedicated server. Building a second spread simulation here would put two
    /// Raven Iron mods igniting and destroying the same pieces: the same
    /// two-mods-forcing-one-thing conflict rule 4 exists to prevent, in-house. Decided
    /// 2026-08-25 (see the locked-decisions table).
    ///
    /// So this system asks FireFront where fires burn and raises `ZoneState.Scorch` there.
    /// Scorch then suppresses fertility through BiomeDrift and will feed future fire risk.
    /// Fire acts; the land remembers. Without FireFront installed the system is DORMANT by
    /// design — Scorch stays a substrate other events can raise.
    ///
    /// NO ZONE CLOCK. Per docs/zone-clock-ownership.md, this system uses live tick time only:
    /// scorch accrues while a fire actually burns, which is also what the unattended-bases rule
    /// requires — FireFront's fires are the only input, and they exist only where its own
    /// simulation is running.
    ///
    /// FireFront is reached by REFLECTION, resolved once and cached, so it stays a soft
    /// dependency: neither mod fails to load without the other, and this csproj gains no
    /// reference that fetch-libs cannot supply. The contract is
    /// `FireManager.CollectActiveFirePositions(List&lt;Vector3&gt;)`, public in FireFront since
    /// 0.18.0 and documented there as load-bearing for this mod.
    /// </summary>
    public class FireSystem : IWorldSystem
    {
        public const string FireFrontGuid = "com.raveniron.firefront";

        public string Name => "FireSystem";
        public bool Enabled => ModConfig.EnableFire.Value;
        public float IntervalSeconds => ModConfig.FireScorchIntervalSeconds.Value;

        private bool _fireFrontPresent;

        // Rule 5 shape: resolved once, retried until they succeed, never latched as failed.
        private PropertyInfo _instanceProperty;
        private MethodInfo _collectMethod;

        // Reused per tick so a steady state allocates nothing.
        private readonly List<Vector3> _firePositions = new List<Vector3>(64);
        private readonly List<ZoneKey> _burningZones = new List<ZoneKey>(16);
        private readonly object[] _collectArgs = new object[1];

        public void Initialise()
        {
            _fireFrontPresent = Chainloader.PluginInfos.ContainsKey(FireFrontGuid);

            if (!_fireFrontPresent)
            {
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] FireFront not present — dormant. Scorch will not accrue from fire; " +
                    "install FireFront (com.raveniron.firefront) 0.18.0+ to light the world's memory.");
                return;
            }

            string version = Chainloader.PluginInfos[FireFrontGuid].Metadata.Version.ToString();
            RagnaroksWrath.Log.LogInfo(
                $"[{Name}] FireFront {version} detected — bridging. Burning zones gain " +
                $"{ModConfig.FireScorchPerMinute.Value:F3} scorch/min.");
        }

        public void Tick(float deltaSeconds)
        {
            if (!_fireFrontPresent) return;
            if (!Persistence.IsLoaded) return;   // not the authority, or world not up yet

            if (!TryCollectFirePositions()) return;
            if (_firePositions.Count == 0) return;

            _burningZones.Clear();
            FireScorch.CollectBurningZones(_firePositions, _burningZones);

            float delta = FireScorch.ScorchDelta(ModConfig.FireScorchPerMinute.Value, deltaSeconds);
            if (delta <= 0f) return;

            for (int i = 0; i < _burningZones.Count; i++)
            {
                ZoneKey zone = _burningZones[i];
                ZoneState state = Persistence.Get(zone);
                state.Scorch += delta;

                // Set clamps to 0..1 and enforces sparseness; scorch recovery is BiomeDrift's
                // job, on the zone clock. This system only ever adds.
                Persistence.Set(zone, state);
            }

            if (ModConfig.VerboseLogging.Value)
                RagnaroksWrath.Log.LogInfo(
                    $"[{Name}] {_firePositions.Count} fire(s) scorching {_burningZones.Count} zone(s).");
        }

        /// <summary>
        /// Fill _firePositions from FireFront, resolving the reflection handles on first use.
        ///
        /// Failures are warnings, not latches: if FireFront's surface moved, every tick names
        /// what could not be found, because a bridge that goes silently dormant after an update
        /// is this codebase's least favourite failure mode.
        /// </summary>
        private bool TryCollectFirePositions()
        {
            try
            {
                if (_collectMethod == null)
                {
                    Type manager = Chainloader.PluginInfos[FireFrontGuid].Instance.GetType()
                        .Assembly.GetType("FireFront.Fire.FireManager");
                    if (manager == null)
                    {
                        RagnaroksWrath.Log.LogWarning(
                            $"[{Name}] FireFront.Fire.FireManager not found — FireFront's API moved.");
                        return false;
                    }

                    _instanceProperty = manager.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static);
                    _collectMethod = manager.GetMethod("CollectActiveFirePositions",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_instanceProperty == null || _collectMethod == null)
                    {
                        _collectMethod = null;   // keep retrying; do not half-resolve
                        RagnaroksWrath.Log.LogWarning(
                            $"[{Name}] FireManager.Instance or CollectActiveFirePositions not found — " +
                            "FireFront is present but older than 0.18.0. Scorch will not accrue.");
                        return false;
                    }
                }

                object instance = _instanceProperty.GetValue(null);
                if (instance == null) return false;   // FireManager not awake yet; normal early on

                _firePositions.Clear();
                _collectArgs[0] = _firePositions;
                _collectMethod.Invoke(instance, _collectArgs);
                return true;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] could not read FireFront fires: {ex.Message}");
                return false;
            }
        }
    }
}
