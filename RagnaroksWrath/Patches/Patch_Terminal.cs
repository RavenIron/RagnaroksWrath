using System;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;
using RavenIron.RagnaroksWrath.Net;
using RavenIron.RagnaroksWrath.Systems.World;

namespace RavenIron.RagnaroksWrath.Patches
{
    /// <summary>
    /// The `wrath` console — the locked-decision prefix, finally cashed. Registered from
    /// an InitTerminal postfix (the ConsoleCommand constructor overwrites same-name
    /// entries, so the repeat call per terminal is harmless).
    ///
    /// AUTHORITY RULE, self-gated: reads answer everywhere (synced caches on a pure
    /// client, the live stores on the authority); MUTATIONS run only where the stores
    /// live — the dedicated server's own console, or a listen host. A pure client asking
    /// to mutate is refused with directions, not trusted. This deletes the
    /// stop-edit-copy-restart dance that today needed five performances.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Wrath
    {
        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("wrath",
                    "Ragnarok's Wrath admin: wrath status | zone [x y] | zone set x y field v | " +
                    "care/harm set x y playerId v | relics | save",
                    Run);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"wrath console: register failed: {ex.Message}");
            }
        }

        private static bool Authority => Persistence.IsLoaded;

        /// <summary>
        /// Mutations typed on a pure client forward to the server through VANILLA's own
        /// remote-command pipe (`ZNet.RemoteCommand` — decompile-verified 2026-08-26):
        /// the server checks the sender against adminlist.txt, refuses non-admins with
        /// "You are not admin", logs the admin and the exact line, and replays the
        /// command through its own console where Authority holds. Output lands on the
        /// SERVER console — the client confirms through its own reads a ring-push later.
        /// </summary>
        private static bool TryForward(Terminal.ConsoleEventArgs args)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer()) return false;

            string line = string.Join(" ", args.Args);
            znet.RemoteCommand(line);
            // Diagnostic breadcrumb (2026-08-26): the first live forward produced a
            // phantom usage line client-side and no audit line server-side — this names
            // the terminal and the exact line so the next repro is a fact, not a theory.
            RagnaroksWrath.Log.LogInfo(
                $"wrath: forwarded '{line}' via {args.Context?.GetType().Name ?? "?"}.");
            args.Context.AddString(
                "wrath: forwarded to the server — vanilla's admin gate applies " +
                "(non-admins are refused). Confirm with `wrath zone <x> <y>` after the " +
                "next sync (~10s); the server console carries the receipt.");
            return true;
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "help";
                switch (sub)
                {
                    case "status": Status(args); return;
                    case "zone":   Zone(args); return;
                    case "care":   Column(args, care: true); return;
                    case "harm":   Column(args, care: false); return;
                    case "relics": Relics(args); return;
                    case "save":   SaveAll(args); return;
                    default:
                        args.Context.AddString(
                            "wrath status — world overview\n" +
                            "wrath zone [x y] — one zone's full story (underfoot when omitted)\n" +
                            $"wrath zone set <x> <y> <field> <value> — fields: {WrathAdmin.ZoneFields}\n" +
                            "wrath care set <x> <y> <playerId> <value> — set a ledger column\n" +
                            "wrath harm set <x> <y> <playerId> <value>\n" +
                            "wrath relics — peaks, stones, pending, era\n" +
                            "wrath save — flush every store now\n" +
                            "Mutations run on the authority; typed in-game they forward to the " +
                            "server through vanilla's admin gate (adminlist.txt).");
                        return;
                }
            }
            catch (Exception ex)
            {
                args.Context.AddString($"wrath: failed — {ex.Message}");
            }
        }

        private static void Status(Terminal.ConsoleEventArgs args)
        {
            var sb = new StringBuilder(256);
            if (Authority)
            {
                sb.Append($"condition {WorldStateSystem.Condition}, season {SeasonSystem.Current} ({SeasonSystem.Source}), ");
                sb.Append($"day {SeasonSystem.CurrentDayOrZero()}\n");
                sb.Append($"zones tracked {Persistence.TrackedZoneCount}, ");
                sb.Append($"wars {RivalrySystem.ContestedZoneCount}, ");
                sb.Append($"rivalry rows {RivalryLedger.Count}, ");
                sb.Append($"relics {RelicLedger.RelicCount} standing / {RelicLedger.PendingCount} pending, ");
                sb.Append($"era {(RelicLedger.EraArmed ? "ARMED" : "clear")}");
            }
            else
            {
                sb.Append("(pure client — synced view only)\n");
                // Season AND its source, never one without the other. Spring is index 0 and so
                // is "nothing has ever told us", so on a client the source is the only thing
                // that separates a working sync from none at all — and before 0.25.0 there was
                // no sync, so this line would have read Spring in midwinter.
                sb.Append(SeasonSystem.Source == SeasonSystem.SeasonSource.Server
                    ? $"season {SeasonSystem.Current} (synced from the server), "
                    : $"season {SeasonSystem.Current} (NOT SYNCED - local default), ");
                sb.Append($"day {SeasonSystem.CurrentDayOrZero()}\n");
                Player local = Player.m_localPlayer;
                if (local != null)
                {
                    ZoneKey here = ZoneKey.FromWorldPos(local.transform.position);
                    sb.Append(DescribeZone(here));
                }
                else sb.Append("no local player.");
            }
            args.Context.AddString(sb.ToString());
        }

        private static void Zone(Terminal.ConsoleEventArgs args)
        {
            // wrath zone set <x> <y> <field> <value>
            if (args.Args.Length >= 3 && args.Args[2].ToLowerInvariant() == "set")
            {
                if (!Authority)
                {
                    if (!TryForward(args))
                        args.Context.AddString("wrath: mutations run on the authority only (server console or listen host).");
                    return;
                }
                if (args.Args.Length < 7
                    || !WrathAdmin.TryParseInt(args.Args[3], out int sx)
                    || !WrathAdmin.TryParseInt(args.Args[4], out int sy)
                    || !WrathAdmin.TryParseValue(args.Args[6], out float value))
                {
                    args.Context.AddString($"usage: wrath zone set <x> <y> <field> <value>  (fields: {WrathAdmin.ZoneFields})");
                    RagnaroksWrath.Log.LogInfo(
                        $"wrath: zone-set usage rejection (authority={Authority}, " +
                        $"terminal={args.Context?.GetType().Name ?? "?"}, args='{string.Join(" ", args.Args)}').");
                    return;
                }

                var zone = new ZoneKey(sx, sy);
                ZoneState state = Persistence.Get(zone);
                if (!WrathAdmin.TrySetZoneField(state, args.Args[5], value, out ZoneState next))
                {
                    args.Context.AddString($"unknown field '{args.Args[5]}' — fields: {WrathAdmin.ZoneFields}");
                    return;
                }

                Persistence.Set(zone, next);
                // Fresh contact stamp, always: a stale stamp credits the backlog and the
                // staged value drains before anyone measures it — the runbook's own trap,
                // now impossible to forget because the tool does it for you.
                ZoneClock.MarkContact(zone);
                Persistence.Save(force: true);
                args.Context.AddString($"zone {zone}: {args.Args[5]} = {value.ToString("0.####", CultureInfo.InvariantCulture)} (fresh contact stamp, saved).");
                return;
            }

            // wrath zone [x y] — show
            ZoneKey target;
            if (args.Args.Length >= 4
                && WrathAdmin.TryParseInt(args.Args[2], out int zx)
                && WrathAdmin.TryParseInt(args.Args[3], out int zy))
            {
                target = new ZoneKey(zx, zy);
            }
            else
            {
                Player local = Player.m_localPlayer;
                if (local == null)
                {
                    args.Context.AddString("no local player — give coordinates: wrath zone <x> <y>");
                    return;
                }
                target = ZoneKey.FromWorldPos(local.transform.position);
            }

            args.Context.AddString(DescribeZone(target));
        }

        private static string DescribeZone(ZoneKey zone)
        {
            var c = CultureInfo.InvariantCulture;
            ZoneState s = ZoneSync.StateAt(zone);
            var sb = new StringBuilder(192);
            sb.Append($"zone {zone}: fert {s.Fertility.ToString("0.####", c)}, corr {s.Corruption.ToString("0.####", c)}, ");
            sb.Append($"scorch {s.Scorch.ToString("0.####", c)}, frost {s.Frost.ToString("0.####", c)}, plague {s.Plague.ToString("0.####", c)}\n");
            sb.Append($"war {ZoneSync.WarAt(zone).ToString("0.#", c)}");

            RelicLedger.Relic relic = RelicSync.RelicAt(zone);
            if (relic.Standing)
                sb.Append($", relic type {relic.Type} {(relic.Cursed ? "cursed" : "blessed")} day {relic.Day}");

            if (Authority && RivalryLedger.IsLoaded)
            {
                foreach (System.Collections.Generic.KeyValuePair<RivalryLedger.Key, RivalryLedger.Row> kv in RivalryLedger.All())
                    if (kv.Key.Zone == zone)
                        sb.Append($"\n  {kv.Key.Player}: harm {kv.Value.Harm.ToString("0.####", c)}, care {kv.Value.Care.ToString("0.####", c)}");
            }
            return sb.ToString();
        }

        private static void Column(Terminal.ConsoleEventArgs args, bool care)
        {
            if (!Authority || !RivalryLedger.IsLoaded)
            {
                if (!TryForward(args))
                    args.Context.AddString("wrath: mutations run on the authority only (server console or listen host).");
                return;
            }
            if (args.Args.Length < 7
                || args.Args[2].ToLowerInvariant() != "set"
                || !WrathAdmin.TryParseInt(args.Args[3], out int x)
                || !WrathAdmin.TryParseInt(args.Args[4], out int y)
                || !WrathAdmin.TryParseLong(args.Args[5], out long playerId)
                || !WrathAdmin.TryParseValue(args.Args[6], out float value))
            {
                args.Context.AddString($"usage: wrath {(care ? "care" : "harm")} set <x> <y> <playerId> <value>");
                return;
            }

            var zone = new ZoneKey(x, y);
            RivalryLedger.SetColumns(zone, playerId, care ? (float?)null : value, care ? value : (float?)null);
            RivalryLedger.SaveIfDirty();
            args.Context.AddString($"zone {zone}, player {playerId}: {(care ? "care" : "harm")} = {value.ToString("0.####", CultureInfo.InvariantCulture)} (saved).");
        }

        private static void Relics(Terminal.ConsoleEventArgs args)
        {
            if (!Authority || !RelicLedger.IsLoaded)
            {
                args.Context.AddString("(pure client) standing relics come from the synced cache — use `wrath zone <x> <y>`.");
                return;
            }

            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(192);
            foreach (System.Collections.Generic.KeyValuePair<ZoneKey, RelicLedger.Peaks> kv in RelicLedger.AllPeaks())
                sb.Append($"peak {kv.Key}: scorch {kv.Value.Scorch.ToString("0.####", c)}, plague {kv.Value.Plague.ToString("0.####", c)}\n");
            foreach (System.Collections.Generic.KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllRelics())
                sb.Append($"stone {kv.Key}: type {kv.Value.Type} {(kv.Value.Cursed ? "cursed" : "blessed")} day {kv.Value.Day}{(kv.Value.Placed ? "" : " (unplaced)")}\n");
            foreach (System.Collections.Generic.KeyValuePair<ZoneKey, RelicLedger.Relic> kv in RelicLedger.AllPending())
                sb.Append($"pending {kv.Key}: type {kv.Value.Type}\n");
            if (RelicLedger.EraArmed) sb.Append("era: ARMED\n");
            if (sb.Length == 0) sb.Append("no peaks, no stones, no pending, era clear.");
            args.Context.AddString(sb.ToString().TrimEnd('\n'));
        }

        private static void SaveAll(Terminal.ConsoleEventArgs args)
        {
            if (!Authority)
            {
                if (!TryForward(args))
                    args.Context.AddString("wrath: nothing to save on a pure client.");
                return;
            }
            Persistence.Save(force: true);
            HealthStore.SaveIfDirty();
            RivalryLedger.SaveIfDirty();
            RelicLedger.SaveIfDirty();
            args.Context.AddString("all four stores flushed.");
        }
    }
}
