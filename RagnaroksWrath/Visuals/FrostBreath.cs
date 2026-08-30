using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Visuals
{
    /// <summary>
    /// The cold made visible: on land whose frost has drifted high, the local player's
    /// breath fogs. The second emitter on the ZoneSync substrate, built to PlagueFog's
    /// template after the chill's first live test came back "chill landed — no vfx": the
    /// land was biting invisibly.
    ///
    /// Same division of labour as fog-vs-sickness: the BREATH is the warning and starts at
    /// a floor BELOW the chill threshold, so a player sees the land's cold before it bites
    /// — discovery first, consequence second. The chill's own gates are deliberately NOT
    /// all mirrored here: breath still fogs beside a campfire (real breath does), but a
    /// roof suppresses it like it suppresses fog, and vanilla-cold weather adds nothing —
    /// this is the LAND's cold, read from the synced zone ring.
    ///
    /// PROCEDURAL, NO ASSETS, RULE 3 THROUGHOUT: every Unity touch inside try/catch, one
    /// throw latches the visual off rather than throwing per second, and nothing here can
    /// reach the gameplay path. Puffs are emitted in world simulation space on a breathing
    /// rhythm rather than a continuous rate — breath comes in breaths.
    /// </summary>
    public class FrostBreath : MonoBehaviour
    {
        private const float CheckInterval = 0.5f;

        private ParticleSystem _system;
        private bool _buildFailed;
        private float _nextCheck;
        private float _nextBreath;

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            try
            {
                ZoneSync.EnsureRegistered();   // the render path arms pure clients, as ever
                SeasonSync.EnsureRegistered(); // twice over: a client with one visual off still needs the season

                if (!ModConfig.FrostBreathEnabled.Value) return;

                Player player = Player.m_localPlayer;
                if (player == null) return;
                if (player.InShelter()) return;   // a roof keeps the land's cold off your breath

                Vector3 pos = player.transform.position;
                float frost = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos)).Frost;
                if (float.IsNaN(frost) || frost < ModConfig.FrostBreathFloor.Value) return;

                if (Time.time < _nextBreath) return;

                if (_system == null && !_buildFailed) Build();
                if (_system == null) return;

                // A breath every few seconds, slightly irregular so a crowd of players
                // would not puff in lockstep; deeper cold means denser breath.
                _system.transform.position =
                    pos + Vector3.up * 1.55f + player.transform.forward * 0.25f;
                _system.transform.rotation = player.transform.rotation;   // exhale where facing
                _system.Emit(3 + Mathf.RoundToInt(Mathf.Clamp01(frost) * 4f));
                _nextBreath = Time.time + UnityEngine.Random.Range(3.2f, 4.4f);
            }
            catch (Exception ex)
            {
                _buildFailed = true;   // a visual that throws once does not get to throw per second
                RagnaroksWrath.Log.LogWarning($"FrostBreath: disabled after error: {ex.Message}");
            }
        }

        /// <summary>Build the emitter lazily, first time a breath is actually owed.</summary>
        private void Build()
        {
            try
            {
                var go = new GameObject("ragnarokswrath_frost_breath");
                go.transform.SetParent(transform, worldPositionStays: false);
                _system = go.AddComponent<ParticleSystem>();

                ParticleSystem.MainModule main = _system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;   // puffs hang where exhaled
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
                main.startColor = new Color(0.92f, 0.96f, 1f, 0.3f);
                main.maxParticles = 64;

                ParticleSystem.ShapeModule shape = _system.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 14f;
                shape.radius = 0.04f;

                ParticleSystem.EmissionModule emission = _system.emission;
                emission.rateOverTime = 0f;   // breaths come from Emit(), never a rate

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.material = ParticleKit.BuildMaterial("FrostBreath");
                renderer.sortMode = ParticleSystemSortMode.Distance;

                RagnaroksWrath.Log.LogInfo("FrostBreath: emitter built.");
            }
            catch (Exception ex)
            {
                _buildFailed = true;
                if (_system != null) { Destroy(_system.gameObject); _system = null; }
                RagnaroksWrath.Log.LogWarning($"FrostBreath: build failed, breath disabled: {ex.Message}");
            }
        }
    }
}
