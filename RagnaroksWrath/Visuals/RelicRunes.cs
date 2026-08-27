using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Visuals
{
    /// <summary>
    /// The nordic design on the stones: rune glyphs — fehu, algiz, gebo, thurisaz —
    /// rising slowly around every standing relic, gold where the ground is blessed, a
    /// dull red where it is cursed. Fourth emitter on the ParticleKit template, and the
    /// first anchored to PLACES rather than the walking player: one column per standing
    /// relic in the player's 3x3 neighbourhood, pruned when the stone falls or the
    /// player leaves. Procedural, no assets (the design is CODE — a carved texture would
    /// break both the no-assets rule and rule 4's material ban), rule 3 throughout, one
    /// throw latches it off.
    ///
    /// Visibility budget (the 0.14.1 arithmetic, done BEFORE shipping): 3 glyphs/s x 5s
    /// alive in a 2.4m-wide column = ~15 runes visible at 0.35m each — unmistakable at
    /// the stone, invisible from the next zone. Dignified, not fireworks.
    /// </summary>
    public class RelicRunes : MonoBehaviour
    {
        private const float UpdateInterval = 1f;
        private const float EmissionRate = 3f;

        private static readonly Color Blessed = new Color(0.79f, 0.71f, 0.35f, 0.85f); // the title gold
        private static readonly Color Cursed  = new Color(0.71f, 0.31f, 0.31f, 0.85f); // the nemesis red

        private readonly Dictionary<ZoneKey, ParticleSystem> _columns =
            new Dictionary<ZoneKey, ParticleSystem>(4);
        private readonly List<ZoneKey> _dead = new List<ZoneKey>(4);

        private Material _material;
        private bool _buildFailed;
        private float _nextUpdate;

        private void Update()
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + UpdateInterval;

            try
            {
                RelicSync.EnsureRegistered();

                Player player = Player.m_localPlayer;
                bool show = ModConfig.RelicRunesEnabled.Value && player != null && !_buildFailed;

                // Prune columns for fallen stones and left-behind ground.
                _dead.Clear();
                ZoneKey centre = show
                    ? ZoneKey.FromWorldPos(player.transform.position) : default;
                foreach (KeyValuePair<ZoneKey, ParticleSystem> kv in _columns)
                {
                    bool near = show
                        && Math.Abs(kv.Key.X - centre.X) <= 1
                        && Math.Abs(kv.Key.Y - centre.Y) <= 1;
                    if (!near || !RelicSync.RelicAt(kv.Key).Standing)
                        _dead.Add(kv.Key);
                }
                for (int i = 0; i < _dead.Count; i++)
                {
                    ParticleSystem gone = _columns[_dead[i]];
                    _columns.Remove(_dead[i]);
                    if (gone != null) Destroy(gone.gameObject);
                }

                if (!show) return;

                // Raise columns for standing relics in the 3x3 around the player.
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var zone = new ZoneKey(centre.X + dx, centre.Y + dy);
                    if (_columns.ContainsKey(zone)) continue;

                    RelicLedger.Relic relic = RelicSync.RelicAt(zone);
                    if (!relic.Standing) continue;

                    ParticleSystem column = BuildColumn(zone, relic.Cursed);
                    if (column != null) _columns[zone] = column;
                }
            }
            catch (Exception ex)
            {
                _buildFailed = true;   // a visual that throws once does not get to throw per second
                RagnaroksWrath.Log.LogWarning($"RelicRunes: disabled after error: {ex.Message}");
            }
        }

        /// <summary>One rune column at a stone: the same deterministic spot placement
        /// used — zone centre snapped to this client's real loaded terrain.</summary>
        private ParticleSystem BuildColumn(ZoneKey zone, bool cursed)
        {
            try
            {
                if (_material == null)
                    _material = ParticleKit.BuildRuneMaterial("RelicRunes");

                Vector3 pos = zone.ToWorldPos();
                ZoneSystem.instance.GetGroundData(ref pos, out _, out _, out _, out _);

                var go = new GameObject($"ragnarokswrath_relic_runes_{zone.X}_{zone.Y}");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.position = pos + Vector3.up * 0.4f;
                var system = go.AddComponent<ParticleSystem>();

                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);   // a slow rise
                main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
                main.startColor = cursed ? Cursed : Blessed;
                main.gravityModifier = 0f;
                main.maxParticles = 64;

                ParticleSystem.ShapeModule shape = system.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.2f;

                // One random rune per glyph, held for its whole life — carved, not animated.
                ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 2;
                sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);

                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = EmissionRate;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.material = _material;
                renderer.sortMode = ParticleSystemSortMode.Distance;

                RagnaroksWrath.Log.LogInfo(
                    $"RelicRunes: column raised at {zone} ({(cursed ? "cursed" : "blessed")}).");
                return system;
            }
            catch (Exception ex)
            {
                _buildFailed = true;
                RagnaroksWrath.Log.LogWarning($"RelicRunes: build failed, runes disabled: {ex.Message}");
                return null;
            }
        }
    }
}
