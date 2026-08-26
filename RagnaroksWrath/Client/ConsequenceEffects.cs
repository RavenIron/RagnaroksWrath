using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Client
{
    /// <summary>
    /// Task 12's creature half, wildlife side: in plagued zones, passive animals sicken —
    /// a code-built SE_Stats (task 11's no-asset pattern) that SLOWS them, visibly, the
    /// staggering deer of the design conversation. Silent per-creature by the mixed-voice
    /// rule; the zone announced itself once, server-side.
    ///
    /// Runs on any machine with a local player, acts only on characters whose ZDOs THIS
    /// client owns — the ownership check is what stops two present players double-dosing
    /// the same deer. The effect carries a short TTL and is re-applied while the ground
    /// stays plagued, so a deer that escapes the zone (or a player who leaves) recovers by
    /// simple expiry — no removal bookkeeping to rot.
    ///
    /// The lethal edge from the spec ("may kill a starving deer eventually") is NOT built:
    /// SE_Stats' health-over-time path only heals (decompile-verified — negative values
    /// never arm the ticker), so sickness is slow-only until a deliberate damage mechanism
    /// earns its own pass. Recorded in the backlog rather than faked here.
    /// </summary>
    public class ConsequenceEffects : MonoBehaviour
    {
        private const float UpdateInterval = 2f;
        private const float SickTtlSeconds = 30f;
        private const float ReachMeters = 48f;
        private const float ErrorLogCooldown = 60f;

        private float _nextUpdate;
        private float _nextErrorLog;

        private SE_Stats _sickTemplate;
        private int _sickHash;

        private void Update()
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + UpdateInterval;

            try
            {
                if (!ModConfig.EnableConsequence.Value || !ModConfig.ConsequenceSicken.Value) return;

                Player player = Player.m_localPlayer;
                if (player == null) return;

                Vector3 origin = player.transform.position;
                string passiveList = ModConfig.WildlifePrefabs.Value;
                float threshold = ModConfig.SickenPlagueThreshold.Value;

                List<Character> characters = Character.GetAllCharacters();
                for (int i = 0; i < characters.Count; i++)
                {
                    Character c = characters[i];
                    if (c == null || c.IsDead() || c.IsPlayer()) continue;
                    if (Vector3.Distance(c.transform.position, origin) > ReachMeters) continue;
                    if (!ConsequenceMath.IsPassivePrefab(c.name, passiveList)) continue;

                    // Only the owner doses; and only on ground that is actually plagued
                    // UNDER THE ANIMAL, not under the player — a deer at the zone border
                    // sickens by where it stands.
                    ZNetView nview = c.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid() || !nview.IsOwner()) continue;

                    float plague = ZoneSync.StateAt(ZoneKey.FromWorldPos(c.transform.position)).Plague;
                    if (!ConsequenceMath.SickensWildlife(plague, threshold)) continue;

                    Dose(c);
                }
            }
            catch (Exception ex)
            {
                if (Time.time >= _nextErrorLog)
                {
                    _nextErrorLog = Time.time + ErrorLogCooldown;
                    RagnaroksWrath.Log.LogWarning($"ConsequenceEffects: pass failed: {ex.Message}");
                }
            }
        }

        private void Dose(Character creature)
        {
            SEMan seman = creature.GetSEMan();
            if (seman == null) return;

            if (_sickTemplate == null)
            {
                _sickTemplate = ScriptableObject.CreateInstance<SE_Stats>();
                _sickTemplate.name = "RW_Blightsick";        // NameHash reads the OBJECT name
                _sickTemplate.m_name = "Blightsick";
                _sickTemplate.m_tooltip = "The blight is in this creature.";
                _sickTemplate.m_ttl = SickTtlSeconds;        // expiry IS the cure
                _sickTemplate.m_speedModifier = -Mathf.Clamp01(ModConfig.SickenSpeedPenalty.Value);
                _sickHash = _sickTemplate.NameHash();
            }

            // One call covers both cases (decompile-verified): already sick -> ResetTime
            // refreshes the TTL; not yet sick -> the instance overload clones the template.
            // A still-exposed animal stays continuously sick; expiry cures the escapee.
            seman.AddStatusEffect(_sickTemplate, resetTime: true);
        }
    }
}
