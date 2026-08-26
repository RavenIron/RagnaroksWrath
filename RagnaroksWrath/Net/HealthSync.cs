using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.RagnaroksWrath.Core;

namespace RavenIron.RagnaroksWrath.Net
{
    /// <summary>
    /// HealthSystem's two wires, both tiny (TitleSync's shape, ZoneSync's authority rule):
    ///
    /// DOWN — exposure: the server pushes (playerID, exposure) to the peer that owns that
    /// player whenever it crosses a quantize step, plus a slow keepalive so a dropped packet
    /// heals. A listen host's own player never needs a packet: ExposureFor reads the ledger
    /// directly when this process is the authority.
    ///
    /// UP — remedies: the server cannot see a player's status effects (stats, skills and SEs
    /// are all local-only per the reference sheets), so the owning client reports two bits —
    /// poison-resist active, rested active — every few seconds. Trusting a client about its
    /// own relief is accepted: this is co-op drift, not anti-cheat. Reports go stale after
    /// <see cref="RemedyMaxAgeSeconds"/> so a vanished client stops being medicated.
    /// </summary>
    public static class HealthSync
    {
        public const string ExposureRpc = "com.raveniron.ragnarokswrath.health_exposure";
        public const string RemedyRpc = "com.raveniron.ragnarokswrath.health_remedy";

        public const int RemedyPoisonResist = 1;
        public const int RemedyRested = 2;
        public const float RemedyMaxAgeSeconds = 15f;

        // Client-side: exposure for players, filled by the down wire.
        private static readonly Dictionary<long, float> _cache = new Dictionary<long, float>(8);

        // Server-side: last remedy report per sender peer, stamped with receive time.
        private struct RemedyReport { public int Bits; public float Stamp; }
        private static readonly Dictionary<long, RemedyReport> _remedies = new Dictionary<long, RemedyReport>(8);

        private static ZRoutedRpc _registeredOn;

        /// <summary>
        /// Exposure as this machine knows it: the authority reads its own ledger, everyone
        /// else reads the synced cache (ZoneSync.StateAt's rule, applied to a scalar).
        /// </summary>
        public static float ExposureFor(long playerId)
        {
            if (HealthStore.IsLoaded) return HealthStore.Get(playerId);
            return _cache.TryGetValue(playerId, out float e) ? e : 0f;
        }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<long, float>(ExposureRpc, RPC_Exposure);
                rpc.Register<int>(RemedyRpc, RPC_Remedy);
                _registeredOn = rpc;
                _cache.Clear();
                _remedies.Clear();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"HealthSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>Server-side: push one player's exposure to the peer that owns them.</summary>
        public static void SendExposure(long peerUid, long playerId, float exposure)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || playerId == 0) return;

            try
            {
                rpc.InvokeRoutedRPC(peerUid, ExposureRpc, playerId, exposure);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"HealthSync: exposure send failed: {ex.Message}");
            }
        }

        /// <summary>Client-side: report this machine's remedy bits. The no-target overload
        /// routes to the server; a listen host's call dispatches locally.</summary>
        public static void ReportRemedies(int bits)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                rpc.InvokeRoutedRPC(RemedyRpc, bits);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"HealthSync: remedy report failed: {ex.Message}");
            }
        }

        /// <summary>Server-side: the freshest remedy bits for a peer, or 0 when the report is
        /// stale or was never made (a silent client gets no benefit of the doubt).</summary>
        public static int RemedyBitsFor(long peerUid, float now)
        {
            if (!_remedies.TryGetValue(peerUid, out RemedyReport r)) return 0;
            return (now - r.Stamp) <= RemedyMaxAgeSeconds ? r.Bits : 0;
        }

        /// <summary>
        /// Remedy bits read straight off a local player — the client's half of the up wire,
        /// and the authority's shortcut for a listen host's own player (who is not a peer and
        /// sends itself no packets). Poison resistance comes from damage modifiers rather
        /// than a named SE, so mead, gear and food all count — the same aggregation vanilla's
        /// own cold gate uses for frost.
        /// </summary>
        public static int ComputeLocalRemedyBits(Player player)
        {
            if (player == null) return 0;

            int bits = 0;
            try
            {
                SEMan seman = player.GetSEMan();
                if (seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectRested))
                    bits |= RemedyRested;

                HitData.DamageModifier mod =
                    player.GetDamageModifiers().GetModifier(HitData.DamageType.Poison);
                if (mod == HitData.DamageModifier.Resistant
                    || mod == HitData.DamageModifier.VeryResistant
                    || mod == HitData.DamageModifier.SlightlyResistant
                    || mod == HitData.DamageModifier.Immune)
                    bits |= RemedyPoisonResist;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"HealthSync: remedy read failed: {ex.Message}");
            }
            return bits;
        }

        private static void RPC_Exposure(long sender, long playerId, float exposure)
        {
            if (playerId == 0) return;

            if (float.IsNaN(exposure) || exposure <= 0f) _cache.Remove(playerId);
            else _cache[playerId] = exposure > 1f ? 1f : exposure;   // clamp on read: the wire is a boundary
        }

        private static void RPC_Remedy(long sender, int bits)
        {
            _remedies[sender] = new RemedyReport
            {
                Bits = bits & (RemedyPoisonResist | RemedyRested),
                Stamp = Time.realtimeSinceStartup,
            };
        }
    }
}
