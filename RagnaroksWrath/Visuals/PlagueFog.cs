using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Visuals
{
    /// <summary>
    /// The plague made visible: a low, slow, grey-green miasma in zones the sync says are
    /// sick. The first client visual — and the template for the ones that follow.
    ///
    /// PROCEDURAL, NO ASSETS. The particle, its texture and its material are all built in
    /// code (FireFront's proven approach): this mod ships no prefabs and no bundles, so
    /// `ZNetScene.CreateObjectsSorted` has nothing to DestroyZDO over, and the effect exists
    /// only on the machine that renders it — never networked, never saved.
    ///
    /// RULE 3 THROUGHOUT: every touch of Unity is inside its own try/catch, and failure
    /// downgrades to "no fog" rather than anything louder. A visual must never take gameplay
    /// down with it.
    ///
    /// ONE emitter, moved to follow the local player, in world simulation space — moving the
    /// box does not drag its already-spawned particles (they keep their history, per the
    /// presence-layer notes), so walking out of a plagued zone leaves the miasma hanging
    /// behind you instead of following like a personal cloud.
    /// </summary>
    public class PlagueFog : MonoBehaviour
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
                ZoneSync.EnsureRegistered();   // the render path is where a pure client arms

                if (!ModConfig.PlagueFogEnabled.Value) { SetEmission(0f); return; }

                Player player = Player.m_localPlayer;
                if (player == null) { SetEmission(0f); return; }

                // Deliberate: the fog respects shelter, like vanilla's weather particles. A
                // roof that keeps out rain keeps out miasma; whether it should protect from
                // the PLAGUE is HealthSystem's question, not this one's.
                if (player.InShelter()) { SetEmission(0f); return; }

                Vector3 pos = player.transform.position;
                float plague = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos)).Plague;
                float rate = FogMath.EmissionFor(plague, ModConfig.PlagueFogDensity.Value);

                if (rate > 0f && _system == null && !_buildFailed) Build();
                if (_system == null) return;

                _system.transform.position = pos + Vector3.up * 0.5f;
                SetEmission(rate);
            }
            catch (Exception ex)
            {
                _buildFailed = true;   // a visual that throws once does not get to throw per second
                RagnaroksWrath.Log.LogWarning($"PlagueFog: disabled after error: {ex.Message}");
            }
        }

        private void SetEmission(float rate)
        {
            if (_system == null) return;
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.rateOverTime = rate;
        }

        /// <summary>Build the emitter lazily, first time fog is actually owed.</summary>
        private void Build()
        {
            try
            {
                var go = new GameObject("ragnarokswrath_plague_fog");
                go.transform.SetParent(transform, worldPositionStays: false);
                _system = go.AddComponent<ParticleSystem>();

                ParticleSystem.MainModule main = _system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startColor = new Color(0.42f, 0.5f, 0.38f, 0.16f);
                main.maxParticles = 600;

                ParticleSystem.ShapeModule shape = _system.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(42f, 2.5f, 42f);   // roughly a zone's heart, ankle-height

                ParticleSystem.EmissionModule emission = _system.emission;
                emission.rateOverTime = 0f;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.material = BuildMaterial();
                renderer.sortMode = ParticleSystemSortMode.Distance;

                RagnaroksWrath.Log.LogInfo("PlagueFog: emitter built.");
            }
            catch (Exception ex)
            {
                _buildFailed = true;
                if (_system != null) { Destroy(_system.gameObject); _system = null; }
                RagnaroksWrath.Log.LogWarning($"PlagueFog: build failed, fog disabled: {ex.Message}");
            }
        }

        /// <summary>
        /// Shaders Valheim's build might actually contain, in FOG order: alpha-blended first
        /// (soft haze), additive last (it glows, which reads as magic rather than miasma).
        /// The list is FireFront's proven chain — "Particles/Standard Unlit" is CONFIRMED
        /// stripped from Valheim's build, kept first only for future Unity versions. This is
        /// the bug the first release shipped: two candidates, both absent, no fog anywhere.
        /// </summary>
        private static readonly string[] CandidateShaders =
        {
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default",
            "UI/Default",
            "Particles/Standard Surface",
            "Legacy Shaders/Particles/Additive",
        };

        /// <summary>
        /// A soft radial-falloff blob, generated rather than shipped. Shader.Find can return
        /// null on stripped builds (the ShieldDome lesson in every server log), so every
        /// candidate is tried before giving up — and the chosen one is logged, because two
        /// clients disagreeing about fog appearance will otherwise be undiagnosable.
        /// </summary>
        private static Material BuildMaterial()
        {
            Shader shader = null;
            foreach (string name in CandidateShaders)
            {
                shader = Shader.Find(name);
                if (shader != null)
                {
                    RagnaroksWrath.Log.LogInfo($"PlagueFog: using shader '{name}'.");
                    break;
                }
            }
            if (shader == null) throw new InvalidOperationException("no particle shader available");

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            float half = (size - 1) / 2f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));   // squared: soft edge, dense heart
            }
            tex.Apply();

            var mat = new Material(shader) { mainTexture = tex };
            return mat;
        }
    }
}
