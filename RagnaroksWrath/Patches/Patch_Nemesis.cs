using System;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// Task 13 phase E, the death hook: when the LOCAL player dies to a creature, mark
    /// that creature in its own ZDO and star it up. Runs on the victim's client — the
    /// machine that holds m_lastHit and, being nearest the fight, almost always owns the
    /// killer's ZDO at that moment. When it does not, the mark is skipped and logged:
    /// a non-owner ZDO write is local graffiti that never replicates, and phase E's
    /// design treats an unmarked escape as flavor, not failure.
    ///
    /// An OBSERVING postfix: vanilla's death has fully happened; nothing here changes it.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnDeath")]
    public static class Patch_Nemesis_OnDeath
    {
        // rule 5: m_lastHit is protected — the publicized reference resolves at compile
        // time only. Cached FieldRef, resolved lazily, retried until it succeeds.
        private static AccessTools.FieldRef<Character, HitData> _lastHit;

        internal static readonly int VictimHash = NemesisMark.KeyVictim.GetStableHashCode();
        internal static readonly int KillsHash  = NemesisMark.KeyKills.GetStableHashCode();
        internal static readonly int NameHash   = NemesisMark.KeyName.GetStableHashCode();

        private static void Postfix(Player __instance)
        {
            try
            {
                if (!ModConfig.EnableRivalry.Value || !ModConfig.EnableNemesis.Value) return;
                if (Player.m_localPlayer == null || __instance != Player.m_localPlayer) return;

                if (_lastHit == null)
                {
                    try { _lastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit"); }
                    catch (Exception ex)
                    {
                        RagnaroksWrath.Log.LogError(
                            $"Nemesis: Character.m_lastHit is gone — Valheim's API moved: {ex.Message}");
                        return;
                    }
                }

                HitData hit = _lastHit(__instance);
                Character killer = hit?.GetAttacker();
                if (killer == null || killer.IsPlayer()) return;

                ZNetView nview = killer.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;

                if (!nview.IsOwner())
                {
                    RagnaroksWrath.Log.LogInfo(
                        $"Nemesis: {killer.name} is owned elsewhere — this death goes unmarked.");
                    return;
                }

                ZDO zdo = nview.GetZDO();
                int kills = zdo.GetInt(KillsHash, 0) + 1;
                zdo.Set(VictimHash, __instance.GetPlayerID());
                zdo.Set(KillsHash, kills);
                zdo.Set(NameHash, __instance.GetPlayerName());

                int level = NemesisMark.NextLevel(killer.GetLevel(), ModConfig.NemesisMaxLevel.Value);
                if (level != killer.GetLevel()) killer.SetLevel(level);

                RagnaroksWrath.Log.LogInfo(
                    $"Nemesis: {killer.name} marked — slayer of {__instance.GetPlayerName()} " +
                    $"(kills {kills}, level {level}).");
            }
            catch (Exception ex)
            {
                // The player is already dead; the mark must never make it worse.
                RagnaroksWrath.Log.LogWarning($"Nemesis death hook failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The nemesis remembered: a marked creature's plate carries its story. Decorating
    /// postfix under rule 1 as amended — appends to whatever survives other mods, cedes
    /// every fight. Player plates are untouched by construction: Player overrides
    /// GetHoverName, and Harmony patches the one body it is aimed at.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    public static class Patch_Nemesis_HoverName
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            try
            {
                if (!ModConfig.EnableRivalry.Value || !ModConfig.EnableNemesis.Value) return;
                if (__instance.IsPlayer()) return;

                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;

                ZDO zdo = nview.GetZDO();
                int kills = zdo.GetInt(Patch_Nemesis_OnDeath.KillsHash, 0);
                if (kills <= 0) return;

                __result += NemesisMark.Suffix(
                    zdo.GetString(Patch_Nemesis_OnDeath.NameHash, ""), kills);
            }
            catch (Exception)
            {
                // Per-frame render path: the name vanilla built is already in __result.
            }
        }
    }
}
