using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Visuals
{
    /// <summary>
    /// The land's memory of fire, made visible: gray ash motes drifting over zones whose
    /// Scorch runs high, thinning as BiomeDrift heals them. The third emitter on the
    /// ZoneSync substrate, PlagueFog's template with FrostBreath's shared ParticleKit.
    ///
    /// THE BOUNDARY HOLDS: FireFront owns LIVING fire — flames, spread, its own permanent
    /// dirt-paint on the exact cells it burned. This shows the zone-level AFTERMATH the
    /// drift store remembers, which outlives the flames by ~50 hours and fades with the
    /// land's own healing rather than on any timer of ours.
    ///
    /// Procedural, no assets, rule 3 throughout, one throw latches it off. World simulation
    /// space so walking out of a scar leaves the ash hanging behind you; a roof keeps it
    /// off you like it keeps off the fog.
    /// </summary>
    public class ScorchAsh : MonoBehaviour
    {
        private const float UpdateInterval = 1f;

        private ParticleSystem _system;
        private bool _buildFailed;
        private float _nextUpdate;

        private void Update()
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + UpdateInterval;

            try
            {
                ZoneSync.EnsureRegistered();

                if (!ModConfig.ScorchAshEnabled.Value) { SetEmission(0f); return; }

                Player player = Player.m_localPlayer;
                if (player == null) { SetEmission(0f); return; }
                if (player.InShelter()) { SetEmission(0f); return; }

                Vector3 pos = player.transform.position;
                float scorch = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos)).Scorch;
                float rate = AshMath.EmissionFor(scorch, ModConfig.ScorchAshDensity.Value);

                if (rate > 0f && _system == null && !_buildFailed) Build();
                if (_system == null) return;

                _system.transform.position = pos + Vector3.up * 3.5f;   // ash falls FROM above
                SetEmission(rate);
            }
            catch (Exception ex)
            {
                _buildFailed = true;   // a visual that throws once does not get to throw per second
                RagnaroksWrath.Log.LogWarning($"ScorchAsh: disabled after error: {ex.Message}");
            }
        }

        private void SetEmission(float rate)
        {
            if (_system == null) return;
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.rateOverTime = rate;
        }

        /// <summary>Build the emitter lazily, first time ash is actually owed.</summary>
        private void Build()
        {
            try
            {
                var go = new GameObject("ragnarokswrath_scorch_ash");
                go.transform.SetParent(transform, worldPositionStays: false);
                _system = go.AddComponent<ParticleSystem>();

                // The 0.14.0 numbers made ash invisible (tiny, half-transparent, zone-
                // dispersed). These are budgeted for visibility at scar-level scorch:
                // ~20 particles/s x 7s alive over a 28m box = one clear dark fleck per
                // ~6 square meters, drifting down — ash, not snow, but unmistakably there.
                ParticleSystem.MainModule main = _system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);   // motes, not fog
                main.startColor = new Color(0.16f, 0.15f, 0.14f, 0.75f);
                main.gravityModifier = 0.015f;   // a slow, believable fall
                main.maxParticles = 800;

                ParticleSystem.ShapeModule shape = _system.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(28f, 1.5f, 28f);   // tight around the walker, above head height

                ParticleSystem.EmissionModule emission = _system.emission;
                emission.rateOverTime = 0f;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.material = ParticleKit.BuildMaterial("ScorchAsh");
                renderer.sortMode = ParticleSystemSortMode.Distance;

                RagnaroksWrath.Log.LogInfo("ScorchAsh: emitter built.");
            }
            catch (Exception ex)
            {
                _buildFailed = true;
                if (_system != null) { Destroy(_system.gameObject); _system = null; }
                RagnaroksWrath.Log.LogWarning($"ScorchAsh: build failed, ash disabled: {ex.Message}");
            }
        }
    }
}
