using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Systems.World
{
    /// <summary>
    /// Gameplay-only wind. No visuals, and no writes of any kind.
    ///
    /// Wind already exists in Valheim and is already animated; reproducing it would be a second
    /// source of truth that disagrees with the trees the player can see. So this system reads
    /// `EnvMan`'s wind and republishes it as a gameplay number that FireSystem and others can
    /// consume without any of them touching `EnvMan` themselves.
    ///
    /// Cached at tick rate rather than read per query. Callers will ask per zone and per piece
    /// once fire spread exists, and `GetWindIntensity` is not free — one read per tick, many
    /// reads per answer.
    /// </summary>
    public class WindSystem : IWorldSystem
    {
        public string Name => "WindSystem";
        public bool Enabled => ModConfig.EnableWind.Value;
        public float IntervalSeconds => ModConfig.WindIntervalSeconds.Value;

        /// <summary>Vanilla's wind intensity, 0..1, as of the last tick. Read, never set.</summary>
        public static float BaseIntensity { get; private set; }

        /// <summary>Vanilla's wind direction as of the last tick.</summary>
        public static Vector3 Direction { get; private set; } = Vector3.forward;

        /// <summary>True once a real reading has been taken, so callers can tell 0 from "unknown".</summary>
        public static bool HasReading { get; private set; }

        public void Initialise()
        {
            RagnaroksWrath.Log.LogInfo($"[{Name}] reading EnvMan wind every {IntervalSeconds:F0}s. No writes.");
        }

        public void Tick(float deltaSeconds)
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return;

            try
            {
                float intensity = env.GetWindIntensity();
                Vector3 direction = env.GetWindDir();

                // Guard before publishing rather than after. Everything downstream multiplies by
                // this, and a NaN would spread rather than merely being wrong.
                BaseIntensity = float.IsNaN(intensity) ? 0f : Mathf.Clamp01(intensity);
                if (!float.IsNaN(direction.x) && !float.IsNaN(direction.z)) Direction = direction;

                HasReading = true;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"[{Name}] could not read wind: {ex.Message}");
            }
        }

        /// <summary>
        /// Gameplay wind at a position: vanilla's wind, amplified inside a storm.
        ///
        /// Positional because storms are. A gale on the other side of the map must not drive fire
        /// spread here, and taking a position is what stops a later caller from reaching for the
        /// global figure by accident.
        /// </summary>
        public static float IntensityAt(Vector3 position)
            => WindState.Combine(BaseIntensity, WeatherSystem.WindMultiplierAt(position));
    }
}
