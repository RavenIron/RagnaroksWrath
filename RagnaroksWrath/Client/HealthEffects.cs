using System;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Feedback;
using RavenIron.RagnaroksWrath.Net;

namespace RavenIron.RagnaroksWrath.Client
{
    /// <summary>
    /// Task 11's client half: synced exposure becomes something the player can FEEL. Runs on
    /// any machine with a local player (pure client or listen host; the dedicated server
    /// never adds this component).
    ///
    /// Two afflictions, both code-built SE_Stats instances added through SEMan's INSTANCE
    /// overload — the decompile-verified path that clones what it is handed and never touches
    /// ObjectDB, so this mod still ships no assets, no bundles, no prefabs:
    ///
    ///  - PLAGUESICK: driven by synced exposure. Stamina regen fails first (from tier 1),
    ///    health regen second (from tier 2), both ramping smoothly to their configured floors
    ///    at exposure 1. Multipliers are mutated on the LIVE clone each pass — SEMan reads
    ///    them fresh on every ModifyStaminaRegen/ModifyHealthRegen call, so there is no
    ///    remove/re-add churn and no message spam.
    ///
    ///  - CHILL: zone frost past the threshold makes cold bite where vanilla's would not.
    ///    Deliberately OUR OWN effect rather than vanilla's Cold: vanilla's env-status pass
    ///    REMOVES Cold every update it disagrees with, and fighting that remove/re-add cycle
    ///    is message spam by construction (the hazard task 11's spec flagged; resolved here
    ///    by not fighting at all — no Harmony patch anywhere in this file). The chill applies
    ///    only when vanilla's own gates say exposed — no campfire, no shelter, no frost
    ///    resistance, no warm-cozy area, and no vanilla Cold/Freezing already running — so
    ///    frost-resist mead and a campfire counter it exactly like the real thing.
    ///
    /// NON-LETHAL BY CONSTRUCTION: multipliers only, no damage fields, and never Freezing.
    /// Tier crossings speak through MessageFeed; the status bar icon is vanilla's own UI.
    /// </summary>
    public class HealthEffects : MonoBehaviour
    {
        private const float UpdateInterval = 1f;
        private const float RemedyReportInterval = 5f;
        private const float ErrorLogCooldown = 60f;
        private const float ChillMessageCooldown = 120f;

        private float _nextUpdate;
        private float _nextRemedyReport;
        private float _nextErrorLog;
        private float _chillMessageAt = -999f;

        private SE_Stats _sickTemplate;
        private SE_Stats _chillTemplate;
        private int _sickHash;
        private int _chillHash;

        private int _lastTier = -1;   // sentinel: first pass sets state without exit messages

        private void Update()
        {
            if (Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + UpdateInterval;

            try
            {
                HealthSync.EnsureRegistered();

                Player player = Player.m_localPlayer;
                if (player == null) { _lastTier = -1; return; }

                if (!ModConfig.EnableHealth.Value)
                {
                    RemoveIfPresent(player, _sickHash);
                    RemoveIfPresent(player, _chillHash);
                    _lastTier = -1;
                    return;
                }

                if (Time.time >= _nextRemedyReport)
                {
                    _nextRemedyReport = Time.time + RemedyReportInterval;
                    HealthSync.ReportRemedies(HealthSync.ComputeLocalRemedyBits(player));
                }

                float exposure = HealthSync.ExposureFor(player.GetPlayerID());
                int tier = ExposureMath.TierFor(exposure,
                    ModConfig.ExposureTier1.Value, ModConfig.ExposureTier2.Value,
                    ModConfig.ExposureTier3.Value);

                ApplySickness(player, exposure, tier);
                AnnounceTier(tier);
                ApplyChill(player);
            }
            catch (Exception ex)
            {
                // Gameplay-adjacent, so no permanent latch (unlike a pure visual): log
                // throttled and try again — a transient hiccup must not disarm the sickness
                // for the whole session.
                if (Time.time >= _nextErrorLog)
                {
                    _nextErrorLog = Time.time + ErrorLogCooldown;
                    RagnaroksWrath.Log.LogWarning($"HealthEffects: pass failed: {ex.Message}");
                }
            }
        }

        private void ApplySickness(Player player, float exposure, int tier)
        {
            SEMan seman = player.GetSEMan();
            if (seman == null) return;

            if (tier <= 0)
            {
                RemoveIfPresent(player, _sickHash);
                return;
            }

            if (_sickTemplate == null)
            {
                _sickTemplate = BuildEffect("RW_Plaguesick", "Plaguesick",
                    SEMan.s_statusEffectPoison,
                    "The blight sickens you. Stamina fails first, then the flesh forgets how " +
                    "to heal. It fades away from plagued ground — faster rested, slower to " +
                    "take hold with poison resistance.");
                _sickHash = _sickTemplate.NameHash();
            }

            if (!seman.HaveStatusEffect(_sickHash))
                seman.AddStatusEffect(_sickTemplate);

            // Mutate the LIVE clone: the multipliers are read fresh every regen pass.
            if (seman.GetStatusEffect(_sickHash) is SE_Stats live)
            {
                live.m_staminaRegenMultiplier = ExposureMath.StaminaRegenMultiplier(exposure,
                    ModConfig.ExposureTier1.Value, ModConfig.SicknessStaminaRegenAtMax.Value);
                live.m_healthRegenMultiplier = ExposureMath.HealthRegenMultiplier(exposure,
                    ModConfig.ExposureTier2.Value, ModConfig.SicknessHealthRegenAtMax.Value);
            }
        }

        private void AnnounceTier(int tier)
        {
            if (tier == _lastTier) return;

            // First sight of state this session: announce only if we ARRIVE sick — logging in
            // clean must not say "the sickness has left you".
            bool firstSight = _lastTier < 0;
            int previous = _lastTier;
            _lastTier = tier;
            if (firstSight && tier == 0) return;

            if (tier > previous || firstSight)
            {
                switch (tier)
                {
                    case 1: MessageFeed.ToLocalPlayer("A sickness takes root in you."); break;
                    case 2: MessageFeed.ToLocalPlayer("The sickness deepens."); break;
                    case 3: MessageFeed.ToLocalPlayer("The blight ravages your body.",
                                MessageFeed.Placement.Centre); break;
                }
            }
            else
            {
                switch (tier)
                {
                    case 2: MessageFeed.ToLocalPlayer("The worst of the sickness passes."); break;
                    case 1: MessageFeed.ToLocalPlayer("The sickness loosens its grip."); break;
                    case 0: MessageFeed.ToLocalPlayer("The sickness has left you."); break;
                }
            }
        }

        private void ApplyChill(Player player)
        {
            SEMan seman = player.GetSEMan();
            if (seman == null) return;

            if (!ModConfig.FrostChillEnabled.Value)
            {
                RemoveIfPresent(player, _chillHash);
                return;
            }

            Vector3 pos = player.transform.position;
            float frost = ZoneSync.StateAt(ZoneKey.FromWorldPos(pos)).Frost;

            bool wanted = frost >= ModConfig.FrostChillThreshold.Value
                && !seman.HaveStatusEffect(SEMan.s_statusEffectCold)
                && !seman.HaveStatusEffect(SEMan.s_statusEffectFreezing)
                && !seman.HaveStatusEffect(SEMan.s_statusEffectCampFire)
                && !player.InShelter()
                && !IsFrostResistant(player)
                && EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f) == null;

            if (!wanted)
            {
                RemoveIfPresent(player, _chillHash);
                return;
            }

            if (_chillTemplate == null)
            {
                _chillTemplate = BuildEffect("RW_Chill", "Chilled",
                    SEMan.s_statusEffectCold,
                    "The land's frost seeps into your bones. A fire, shelter, or frost " +
                    "resistance keeps it out.");
                _chillTemplate.m_staminaRegenMultiplier = ModConfig.ChillStaminaRegenMultiplier.Value;
                _chillTemplate.m_healthRegenMultiplier = ModConfig.ChillHealthRegenMultiplier.Value;
                _chillHash = _chillTemplate.NameHash();
            }

            if (!seman.HaveStatusEffect(_chillHash))
            {
                seman.AddStatusEffect(_chillTemplate);

                if (Time.time - _chillMessageAt >= ChillMessageCooldown)
                {
                    _chillMessageAt = Time.time;
                    MessageFeed.ToLocalPlayer("The land's cold seeps into your bones.");
                }
            }
        }

        private static bool IsFrostResistant(Player player)
        {
            HitData.DamageModifier mod =
                player.GetDamageModifiers().GetModifier(HitData.DamageType.Frost);
            return mod == HitData.DamageModifier.Resistant
                || mod == HitData.DamageModifier.VeryResistant
                || mod == HitData.DamageModifier.SlightlyResistant
                || mod == HitData.DamageModifier.Immune;
        }

        private static void RemoveIfPresent(Player player, int hash)
        {
            if (hash == 0) return;
            SEMan seman = player.GetSEMan();
            if (seman != null && seman.HaveStatusEffect(hash))
                seman.RemoveStatusEffect(hash, quiet: true);
        }

        /// <summary>
        /// A code-built SE_Stats: no asset, no bundle, no ObjectDB row. NameHash comes from
        /// the ScriptableObject's Unity NAME (decompile-verified — not m_name), so it is set
        /// explicitly. The icon is borrowed from a vanilla effect so the status bar speaks
        /// the game's own visual language; a missing icon degrades to a blank slot, logged.
        /// </summary>
        private static SE_Stats BuildEffect(string objectName, string displayName,
                                            int iconFromHash, string tooltip)
        {
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = objectName;
            se.m_name = displayName;
            se.m_tooltip = tooltip;
            se.m_ttl = 0f;   // permanent until we remove it; nothing else may expire it

            try
            {
                StatusEffect donor = ObjectDB.instance != null
                    ? ObjectDB.instance.GetStatusEffect(iconFromHash) : null;
                if (donor != null) se.m_icon = donor.m_icon;
                else RagnaroksWrath.Log.LogWarning($"HealthEffects: no icon donor for {objectName}.");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"HealthEffects: icon borrow failed for {objectName}: {ex.Message}");
            }

            return se;
        }
    }
}
