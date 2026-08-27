using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using BepInEx.Configuration;
using RavenIron.RagnaroksWrath.Config;
using RavenIron.RagnaroksWrath.Core;

namespace RagnaroksWrath.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program that returns a nonzero exit code is enough, and adds no dependency to keep
    /// current.
    ///
    /// What this is actually for: ZoneClock's credit math fails SILENTLY. A wrong cap or a
    /// mishandled negative delta does not throw, it just makes the world drift oddly weeks
    /// later on someone else's server. That is precisely the class of bug worth catching
    /// without launching the game.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.WriteLine("Ragnarok's Wrath — core tests\n");

            // Bind config first: ZoneClock reads MaxCreditSeconds on every call.
            ModConfig.Bind(new ConfigFile());

            ZoneKeyTests();
            ZoneClockTests();
            ZoneStateTests();
            PersistenceTests();
            BiomeStateTests();
            StormAreaTests();
            WindStateTests();
            FireScorchTests();
            PlagueTests();
            WorldStateTests();
            EcologyTests();
            TitleTests();
            FogTests();
            ExposureTests();
            HealthStoreTests();
            ConsequenceTests();
            RivalryTests();
            ContestTests();
            NemesisTests();
            RelicTests();
            WrathAdminTests();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- ZoneKey ----------------------------------------------------------------

        private static void ZoneKeyTests()
        {
            Console.WriteLine("ZoneKey");

            Check("equality is by value",
                new ZoneKey(3, -7) == new ZoneKey(3, -7));

            Check("inequality distinguishes swapped coords",
                new ZoneKey(3, -7) != new ZoneKey(-7, 3));

            Check("equal keys hash equally",
                new ZoneKey(12, 34).GetHashCode() == new ZoneKey(12, 34).GetHashCode());

            // Not a correctness requirement, but a collision here would quietly degrade every
            // per-zone dictionary in the mod, so it is worth knowing if it ever changes.
            Check("swapped coords do not collide",
                new ZoneKey(12, 34).GetHashCode() != new ZoneKey(34, 12).GetHashCode());

            Check("negative coords round-trip through TryParse",
                ZoneKey.TryParse(new ZoneKey(-15, -200).ToString(), out ZoneKey parsed)
                && parsed == new ZoneKey(-15, -200));

            Check("zero round-trips",
                ZoneKey.TryParse(new ZoneKey(0, 0).ToString(), out ZoneKey zero)
                && zero == new ZoneKey(0, 0));

            Check("garbage is rejected rather than silently parsed",
                !ZoneKey.TryParse("not-a-key", out _)
                && !ZoneKey.TryParse("", out _)
                && !ZoneKey.TryParse(null, out _));
        }

        // ---- ZoneClock --------------------------------------------------------------

        private static void ZoneClockTests()
        {
            Console.WriteLine("\nZoneClock");

            ZoneClock.Clear();

            // A zone with no history has no backlog. If this ever returns elapsed time, a
            // brand-new world instantly drifts by however long the save file has existed.
            var fresh = new ZoneKey(1, 1);
            Check("first contact credits nothing",
                Math.Abs(ZoneClock.CreditOnContact(fresh)) < 0.001);

            Check("first contact establishes history",
                ZoneClock.HasHistory(fresh));

            // Restore() is the seam that makes elapsed time testable without waiting for it.
            var twoHours = new ZoneKey(2, 2);
            ZoneClock.Restore(twoHours, DateTime.UtcNow.AddHours(-2).Ticks);
            double credited = ZoneClock.CreditOnContact(twoHours);
            Check($"two hours away credits ~7200s (got {credited:F1})",
                credited > 7195 && credited < 7205);

            Check("crediting consumes the backlog",
                ZoneClock.CreditOnContact(twoHours) < 1.0);

            // The cap is what keeps a long-idle world playable.
            var longGone = new ZoneKey(3, 3);
            ZoneClock.Restore(longGone, DateTime.UtcNow.AddDays(-30).Ticks);
            double capped = ZoneClock.CreditOnContact(longGone);
            Check($"30 days is capped at MaxCreditSeconds (got {capped:F0}s, cap {ModConfig.MaxCreditSeconds.Value:F0}s)",
                Math.Abs(capped - ModConfig.MaxCreditSeconds.Value) < 1.0);

            // NTP correction, host reboot, or a save copied between machines.
            var future = new ZoneKey(4, 4);
            ZoneClock.Restore(future, DateTime.UtcNow.AddHours(1).Ticks);
            Check("a backwards clock credits zero, not a negative",
                Math.Abs(ZoneClock.CreditOnContact(future)) < 0.001);

            // Peek must not consume, or diagnostics would silently eat real drift.
            var peeked = new ZoneKey(5, 5);
            ZoneClock.Restore(peeked, DateTime.UtcNow.AddHours(-1).Ticks);
            double peek1 = ZoneClock.PeekElapsed(peeked);
            double peek2 = ZoneClock.PeekElapsed(peeked);
            Check($"PeekElapsed does not consume the backlog ({peek1:F0}s then {peek2:F0}s)",
                peek1 > 3595 && Math.Abs(peek1 - peek2) < 1.0);

            Check("PeekElapsed is also capped",
                PeekIsCapped());

            Check("PeekElapsed on an unknown zone is zero",
                Math.Abs(ZoneClock.PeekElapsed(new ZoneKey(999, 999))) < 0.001);

            var forgotten = new ZoneKey(6, 6);
            ZoneClock.Restore(forgotten, DateTime.UtcNow.AddHours(-5).Ticks);
            ZoneClock.Forget(forgotten);
            Check("Forget clears history so the next contact credits zero",
                !ZoneClock.HasHistory(forgotten)
                && Math.Abs(ZoneClock.CreditOnContact(forgotten)) < 0.001);

            // Sparse by construction — only contacted zones exist. A registry that grows to
            // every zone in the world is the thing this design exists to avoid.
            ZoneClock.Clear();
            ZoneClock.MarkContact(new ZoneKey(10, 10));
            ZoneClock.MarkContact(new ZoneKey(11, 11));
            Check($"only contacted zones are tracked (got {ZoneClock.TrackedZoneCount})",
                ZoneClock.TrackedZoneCount == 2);

            Check("MarkContact establishes history without crediting",
                Math.Abs(ZoneClock.CreditOnContact(new ZoneKey(10, 10))) < 1.0);

            // Snapshot is what persistence will serialise.
            int snapshotCount = 0;
            foreach (var _ in ZoneClock.Snapshot()) snapshotCount++;
            Check($"Snapshot exposes every tracked zone (got {snapshotCount})",
                snapshotCount == ZoneClock.TrackedZoneCount);
        }

        private static bool PeekIsCapped()
        {
            var z = new ZoneKey(7, 7);
            ZoneClock.Restore(z, DateTime.UtcNow.AddDays(-30).Ticks);
            return Math.Abs(ZoneClock.PeekElapsed(z) - ModConfig.MaxCreditSeconds.Value) < 1.0;
        }

        // ---- ZoneState --------------------------------------------------------------

        private static void ZoneStateTests()
        {
            Console.WriteLine("\nZoneState");

            Check("a fresh state is default (and so is never written to disk)",
                new ZoneState().IsDefault);

            var touched = new ZoneState { Scorch = 0.1f };
            Check("any non-zero field makes it non-default",
                !touched.IsDefault);

            var over = new ZoneState { Fertility = 5f, Corruption = -3f };
            over.Clamp();
            Check("Clamp bounds values into 0..1",
                Math.Abs(over.Fertility - 1f) < 0.001f && Math.Abs(over.Corruption) < 0.001f);

            // NaN is the dangerous one: it survives every later multiply and silently poisons
            // whatever gameplay value it feeds.
            var nan = new ZoneState { Plague = float.NaN };
            nan.Clamp();
            Check("Clamp converts NaN to zero rather than propagating it",
                !float.IsNaN(nan.Plague) && Math.Abs(nan.Plague) < 0.001f);
        }

        // ---- Persistence ------------------------------------------------------------

        private static void PersistenceTests()
        {
            Console.WriteLine("\nPersistence");

            string dir = Path.Combine(Path.GetTempPath(), "rw_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                Persistence.OverrideDirectory = dir;
                Persistence.OverrideWorldUid = 123456789UL;

                Persistence.Load();
                Check("loading a world with no store yields an empty, usable state",
                    Persistence.IsLoaded && Persistence.TrackedZoneCount == 0);

                // Sparseness is the core guarantee: a default state must not create an entry.
                Persistence.Set(new ZoneKey(1, 1), new ZoneState());
                Check("storing a default state does not create an entry",
                    Persistence.TrackedZoneCount == 0);

                var burnt = new ZoneState { Scorch = 0.75f, Fertility = 0.25f };
                Persistence.Set(new ZoneKey(10, -20), burnt);
                Persistence.Set(new ZoneKey(-5, 7), new ZoneState { Plague = 0.5f });
                Check($"non-default states are stored (got {Persistence.TrackedZoneCount})",
                    Persistence.TrackedZoneCount == 2);

                // A zone that heals must stop costing disk space, or the file only ever grows.
                Persistence.Set(new ZoneKey(-5, 7), new ZoneState());
                Check("a zone returning to default is removed entirely",
                    Persistence.TrackedZoneCount == 1);

                Persistence.Save(force: true);

                // The round-trip is the thing that fails silently in production.
                Persistence.Clear();
                Persistence.Load();

                ZoneState back = Persistence.Get(new ZoneKey(10, -20));
                Check($"values survive a save/load round-trip ({back})",
                    Math.Abs(back.Scorch - 0.75f) < 0.0001f &&
                    Math.Abs(back.Fertility - 0.25f) < 0.0001f);

                Check("negative zone coordinates round-trip",
                    Persistence.TrackedZoneCount == 1);

                Check("an untouched zone reads back as pristine default",
                    Persistence.Get(new ZoneKey(999, 999)).IsDefault);

                // Per-line isolation: one bad line must not discard the rest of the file.
                string path = Directory.GetFiles(dir, "*.dat")[0];
                var lines = new List<string>(File.ReadAllLines(path));
                lines.Insert(2, "this\tis\tnot\ta\tvalid\tline");
                lines.Add("42\t42\t0\t0.5\t0.5\t0.5\t0.5\t0.5");
                File.WriteAllLines(path, lines);

                Persistence.Clear();
                Persistence.Load();
                Check($"a corrupt line is skipped, others survive (got {Persistence.TrackedZoneCount})",
                    Persistence.TrackedZoneCount == 2);

                // Values out of range in the file must be clamped on READ, not trusted.
                File.WriteAllLines(path, new[]
                {
                    "version\t1",
                    "7\t7\t0\t9.0\t-9.0\t0.5\t0.5\t0.5"
                });
                Persistence.Clear();
                Persistence.Load();
                ZoneState clamped = Persistence.Get(new ZoneKey(7, 7));
                Check($"out-of-range values in the file are clamped on read ({clamped})",
                    Math.Abs(clamped.Fertility - 1f) < 0.001f &&
                    Math.Abs(clamped.Corruption) < 0.001f);

                // A wholly unreadable file must leave the world playable.
                File.WriteAllBytes(path, new byte[] { 0x00, 0xFF, 0x00, 0xFF });
                Persistence.Clear();
                Persistence.Load();
                Check("a binary-garbage file degrades to empty rather than throwing",
                    Persistence.IsLoaded);

                // The damage has to survive as evidence. Left in place it reads as a fresh world
                // and the next autosave writes over it.
                Check("a wholly unreadable file is quarantined instead of left to be overwritten",
                    Directory.GetFiles(dir, "*.corrupt").Length == 1 && !File.Exists(path));

                // A header and nothing else is a world that has simply never drifted.
                foreach (string dead in Directory.GetFiles(dir, "*.corrupt")) File.Delete(dead);
                File.WriteAllLines(path, new[]
                {
                    "version\t1",
                    "# zoneX\tzoneY\tcontactTicks\tfert\tcorr\tscorch\tfrost\tplague"
                });
                Persistence.Clear();
                Persistence.Load();
                Check("a file holding only a header is an empty world, not a damaged one",
                    Persistence.IsLoaded && Directory.GetFiles(dir, "*.corrupt").Length == 0);

                // Partial damage stays on the per-line path: one bad line costs one zone.
                File.WriteAllLines(path, new[]
                {
                    "version\t1",
                    "3\t4\t0\t0.5\t0.5\t0.5\t0.5\t0.5",
                    "not a zone",
                    "still\tnot\ta\tzone"
                });
                Persistence.Clear();
                Persistence.Load();
                Check($"a file with any readable zone is kept, not quarantined (got {Persistence.TrackedZoneCount})",
                    Persistence.TrackedZoneCount == 1
                    && Directory.GetFiles(dir, "*.corrupt").Length == 0);

                // ---- what the mod writes, the mod must be able to read ----------------------
                // Every fixture above was written by the harness with File.WriteAllLines, which
                // emits no BOM. The shipped writer used Encoding.UTF8, which does — so the tests
                // agreed with each other and disagreed with the file on disk.

                foreach (string dead in Directory.GetFiles(dir, "*.corrupt")) File.Delete(dead);

                Persistence.Clear();
                Persistence.Set(new ZoneKey(5, 6), new ZoneState { Frost = 0.4f });
                Persistence.Save(force: true);

                byte[] raw = File.ReadAllBytes(path);
                Check("the store is written without a byte-order mark",
                    raw.Length >= 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF));

                Persistence.Clear();
                Persistence.Load();
                ZoneState frosted = Persistence.Get(new ZoneKey(5, 6));
                Check($"a store written by Save round-trips through Load ({frosted})",
                    Persistence.TrackedZoneCount == 1
                    && Math.Abs(frosted.Frost - 0.4f) < 0.0001f
                    && Directory.GetFiles(dir, "*.corrupt").Length == 0);

                // The thinnest real store: headers and nothing else. Guards the corruption rule
                // against its worst false positive, on a file the MOD wrote rather than one the
                // harness hand-built.
                Persistence.Clear();
                Persistence.Save(force: true);
                Persistence.Load();
                Check("a store the mod wrote with no zones loads as empty, not corrupt",
                    Persistence.IsLoaded
                    && Persistence.TrackedZoneCount == 0
                    && Directory.GetFiles(dir, "*.corrupt").Length == 0);

                // Stores written by v0.1.5 and earlier carry a BOM. File.ReadAllLines consumes it,
                // so they load fine — this pins that down, and fails if the read path is ever
                // swapped for something less forgiving.
                File.WriteAllText(path,
                    "version\t1\n8\t9\t0\t0.5\t0\t0\t0\t0\n",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Persistence.Clear();
                Persistence.Load();
                Check("a store carrying a legacy BOM still loads its zones",
                    Persistence.TrackedZoneCount == 1
                    && Directory.GetFiles(dir, "*.corrupt").Length == 0);

                // Different worlds must not share drift.
                Persistence.OverrideWorldUid = 987654321UL;
                Persistence.Load();
                Check("a different world uid sees none of the first world's drift",
                    Persistence.TrackedZoneCount == 0);
            }
            finally
            {
                Persistence.OverrideDirectory = null;
                Persistence.OverrideWorldUid = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- BiomeState -------------------------------------------------------------

        private static void BiomeStateTests()
        {
            Console.WriteLine("\nBiomeState");

            const float Hour = 3600f;

            // Recovery is linear in elapsed time. Exponential decay would look more natural and
            // never reach zero, which is what would keep every visited zone in the store forever.
            var damaged = new ZoneState { Scorch = 0.5f, Corruption = 0.5f };
            ZoneState afterHour = BiomeDrift.Apply(damaged, Hour, 0.1f, 0f, 0f, 1f);
            Check($"an hour of recovery removes one hour's worth ({afterHour})",
                Math.Abs(afterHour.Corruption - 0.4f) < 0.0001f);

            ZoneState afterTwo = BiomeDrift.Apply(damaged, 2 * Hour, 0.1f, 0f, 0f, 1f);
            Check($"twice the time removes twice as much ({afterTwo})",
                Math.Abs(afterTwo.Corruption - 0.3f) < 0.0001f);

            // The whole point of the snap: a zone must be able to become default again, or it
            // never leaves the store and the file grows without bound.
            var nearlyHealed = new ZoneState { Corruption = 0.0001f };
            ZoneState healed = BiomeDrift.Apply(nearlyHealed, Hour, 0.02f, 0f, 0f, 1f);
            Check("decay terminates at exactly zero rather than approaching it",
                healed.IsDefault);

            // ...and that healed zone must actually leave the store, which is the acceptance
            // criterion rather than an implementation detail.
            string dir = Path.Combine(Path.GetTempPath(), "rw_biome_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Persistence.OverrideDirectory = dir;
                Persistence.OverrideWorldUid = 555UL;
                Persistence.Load();

                var zone = new ZoneKey(4, 4);
                Persistence.Set(zone, new ZoneState { Corruption = 0.0001f });
                bool wasStored = Persistence.TrackedZoneCount == 1;

                Persistence.Set(zone, BiomeDrift.Apply(
                    Persistence.Get(zone), Hour, 0.02f, 0f, 0f, 1f));

                Check("a zone that drifts back to pristine is removed from the store",
                    wasStored && Persistence.TrackedZoneCount == 0);
            }
            finally
            {
                Persistence.OverrideDirectory = null;
                Persistence.OverrideWorldUid = null;
                try { Directory.Delete(dir, true); } catch { }
            }

            Check("recovery never drives a value negative",
                BiomeDrift.Apply(new ZoneState { Frost = 0.1f }, 100 * Hour, 0.5f, 0f, 0f, 1f)
                    .IsDefault);

            Check("zero elapsed time changes nothing",
                BiomeDrift.Apply(damaged, 0.0, 0.1f, 0.1f, 1.6f, 1f).Scorch == damaged.Scorch);

            // Frost is the one field that builds with no event behind it.
            ZoneState winter = BiomeDrift.Apply(default, Hour, 0.02f, 0.05f, 1.6f, 0.4f);
            Check($"winter accumulates frost on a pristine zone ({winter})",
                winter.Frost > 0f && winter.IsDefault == false);

            ZoneState summer = BiomeDrift.Apply(default, Hour, 0.02f, 0.05f, 0f, 1.6f);
            Check("summer accumulates no frost, and invents nothing else either",
                summer.IsDefault);

            // Net effect, not order of operations: recovery and pressure are applied to the same
            // field, so winter must still be a net gain and the thaw a net loss.
            var frosted = new ZoneState { Frost = 0.5f };
            Check("winter is a net gain against recovery",
                BiomeDrift.Apply(frosted, Hour, 0.02f, 0.05f, 1.6f, 1f).Frost > 0.5f);
            Check("the thaw is a net loss",
                BiomeDrift.Apply(frosted, Hour, 0.02f, 0.05f, 0.3f, 1f).Frost < 0.5f);

            // Dry season slows healing rather than causing new damage.
            var burnt = new ZoneState { Scorch = 0.5f };
            float summerScorch = BiomeDrift.Apply(burnt, Hour, 0.1f, 0f, 0f, 1.6f).Scorch;
            float springScorch = BiomeDrift.Apply(burnt, Hour, 0.1f, 0f, 0f, 0.8f).Scorch;
            Check($"scorch recovers more slowly at higher fire risk ({summerScorch:F3} vs {springScorch:F3})",
                summerScorch > springScorch);

            // Clamp on the way out, same as everywhere else that touches ZoneState.
            ZoneState piled = BiomeDrift.Apply(
                new ZoneState { Frost = 0.9f }, 100 * Hour, 0f, 0.5f, 1.6f, 1f);
            Check($"accumulated frost is clamped to 1 ({piled.Frost:F3})",
                Math.Abs(piled.Frost - 1f) < 0.0001f);

            // REGRESSION. A tick's gain is far smaller than any value a player would notice, so
            // a rounding floor anywhere near it does not slow accumulation, it stops it dead: the
            // next pass's decay zeroes what the last one added, forever, and the store fills with
            // dust while looking healthy. These two run at the real default rates and interval.
            ZoneState acc = default;
            for (int i = 0; i < 40; i++)
                acc = BiomeDrift.Apply(acc, 30f, 0f, 0.015f, 0.3f, 1f);
            Check($"small per-tick gains accumulate instead of being rounded away each pass ({acc.Frost:E2})",
                acc.Frost > 0.001f);

            ZoneState winterAcc = default;
            for (int i = 0; i < 200; i++)
                winterAcc = BiomeDrift.Apply(winterAcc, 30f, 0.02f, 0.015f, 1.6f, 1f);
            Check($"winter accumulates against recovery at default rates ({winterAcc.Frost:E2})",
                winterAcc.Frost > 0.001f);

            Check("a NaN that reaches the drift math is neutralised, not propagated",
                !float.IsNaN(BiomeDrift.Apply(
                    new ZoneState { Plague = float.NaN }, Hour, 0.02f, 0f, 0f, 1f).Plague));
        }

        // ---- StormArea --------------------------------------------------------------

        private static void StormAreaTests()
        {
            Console.WriteLine("\nStormArea");

            var centre = new Vector3(100f, 30f, 100f);
            const float range = 96f;

            Check("a position at the centre is inside",
                StormArea.Contains(centre, range, centre));

            // XZ only. A storm reaches up the mountain above it - using Vector3.Distance here
            // would shrink the area for anyone climbing, and vanilla's banner would disagree.
            Check("height does not shrink the area",
                StormArea.Contains(centre, range, new Vector3(100f, 900f, 100f)));

            Check("a position beyond the range is outside",
                !StormArea.Contains(centre, range, new Vector3(100f + range + 1f, 30f, 100f)));

            // Strictly less-than, so the perimeter itself is out. Pinned because "<" vs "<=" is
            // the kind of boundary that differs silently between two implementations.
            Check("the perimeter is outside, not inside",
                !StormArea.Contains(centre, range, new Vector3(100f + range, 30f, 100f)));

            Check("just inside the perimeter is inside",
                StormArea.Contains(centre, range, new Vector3(100f + range - 0.5f, 30f, 100f)));

            // Vanilla's guard for anyone mid-teleport or otherwise off the map.
            Check("above the sky ceiling nothing is inside",
                !StormArea.Contains(centre, range, new Vector3(100f, StormArea.SkyCeiling + 1f, 100f)));

            Check("exactly at the sky ceiling is still inside",
                StormArea.Contains(centre, range, new Vector3(100f, StormArea.SkyCeiling, 100f)));

            // 3-4-5: proves the formula rather than merely its comparisons.
            Check($"DistanceXZ matches the flat distance ({StormArea.DistanceXZ(new Vector3(0f, 0f, 0f), new Vector3(3f, 999f, 4f)):F2})",
                Math.Abs(StormArea.DistanceXZ(new Vector3(0f, 0f, 0f), new Vector3(3f, 999f, 4f)) - 5f) < 0.0001f);

            Check("a zero-range storm contains nothing, not even its centre",
                !StormArea.Contains(centre, 0f, centre));
        }

        // ---- WindState --------------------------------------------------------------

        private static void WindStateTests()
        {
            Console.WriteLine("\nWindState");

            Check("wind passes through unchanged with no storm",
                Math.Abs(WindState.Combine(0.4f, 1f) - 0.4f) < 0.0001f);

            Check("a storm amplifies wind",
                Math.Abs(WindState.Combine(0.3f, 2f) - 0.6f) < 0.0001f);

            // Everything downstream multiplies by this, so it must stay in range.
            Check("amplified wind is clamped to 1",
                Math.Abs(WindState.Combine(0.8f, 5f) - 1f) < 0.0001f);

            Check("calm stays calm however strong the storm",
                Math.Abs(WindState.Combine(0f, 10f)) < 0.0001f);

            Check("a negative multiplier cannot drive wind below zero",
                Math.Abs(WindState.Combine(0.5f, -3f)) < 0.0001f);

            // A NaN survives every later multiply and silently poisons fire spread. Same reason
            // ZoneState.Clamp neutralises it rather than passing it on.
            Check("a NaN reading is neutralised, not propagated",
                !float.IsNaN(WindState.Combine(float.NaN, 2f))
                && !float.IsNaN(WindState.Combine(0.5f, float.NaN)));
        }

        // ---- FireScorch -------------------------------------------------------------

        private static void FireScorchTests()
        {
            Console.WriteLine("\nFireScorch");

            // Zone size is 64; positions 10m apart share a zone, 100m apart do not.
            var fires = new List<Vector3>
            {
                new Vector3(10f, 30f, 10f),
                new Vector3(20f, 30f, 20f),     // same zone as the first
                new Vector3(100f, 30f, 100f),   // a different zone
            };
            var zones = new List<ZoneKey>();
            FireScorch.CollectBurningZones(fires, zones);
            Check($"fires in one zone count once, fires apart count separately (got {zones.Count})",
                zones.Count == 2);

            // Binary per zone is the contract: severity already shows up as more zones burning,
            // and scaling by count as well would double-count it.
            zones.Clear();
            FireScorch.CollectBurningZones(new List<Vector3>
            {
                new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 2f), new Vector3(3f, 0f, 3f),
            }, zones);
            Check("forty fires in one zone are still one burning zone",
                zones.Count == 1);

            Check("no fires means no zones",
                (new Func<bool>(() => { zones.Clear();
                    FireScorch.CollectBurningZones(new List<Vector3>(), zones);
                    return zones.Count == 0; }))());

            // Rate is per minute; a 10s tick delivers a sixth of it.
            Check($"a 10s tick delivers a sixth of the per-minute rate ({FireScorch.ScorchDelta(0.06f, 10f):F4})",
                Math.Abs(FireScorch.ScorchDelta(0.06f, 10f) - 0.01f) < 0.0001f);

            Check("zero rate scorches nothing",
                FireScorch.ScorchDelta(0f, 10f) == 0f);

            Check("negative or NaN inputs scorch nothing rather than poisoning the store",
                FireScorch.ScorchDelta(-1f, 10f) == 0f
                && FireScorch.ScorchDelta(0.02f, -5f) == 0f
                && FireScorch.ScorchDelta(float.NaN, 10f) == 0f
                && FireScorch.ScorchDelta(0.02f, float.NaN) == 0f);
        }

        // ---- Plague -----------------------------------------------------------------

        private static void PlagueTests()
        {
            Console.WriteLine("\nPlague");

            const float Hour = 3600f;
            // recovery 0.02/h; growth passed in already season-multiplied, boost as stated.

            // Growth needs a seed. A pristine zone must stay pristine through any weather, or
            // the store stops being sparse and plague appears from nowhere.
            ZoneState pristine = BiomeDrift.Apply(default, Hour, 0.02f, 0f, 0f, 1f, 0.042f, 1f);
            Check("an unseeded zone grows no plague", pristine.IsDefault);

            // Spring at defaults: growth 0.042/h beats recovery 0.02/h.
            var seeded = new ZoneState { Plague = 0.05f };
            ZoneState spring = BiomeDrift.Apply(seeded, Hour, 0.02f, 0f, 0f, 1f, 0.042f, 0f);
            Check($"a seeded zone grows in spring ({spring.Plague:F4})",
                Math.Abs(spring.Plague - (0.05f - 0.02f + 0.042f)) < 0.0005f);

            // Winter at defaults: growth 0.015/h loses to recovery 0.02/h — the seasonal cure.
            ZoneState winter = BiomeDrift.Apply(seeded, Hour, 0.02f, 0f, 0f, 1f, 0.015f, 0f);
            Check($"winter is a net cure ({winter.Plague:F4})",
                winter.Plague < seeded.Plague);

            // ...and driving it through zero KILLS it: the gate is the post-decay value, so a
            // cured zone cannot be resurrected by the next warm season without re-infection.
            ZoneState cured = BiomeDrift.Apply(new ZoneState { Plague = 0.01f }, 2 * Hour,
                0.02f, 0f, 0f, 1f, 0.015f, 0f);
            Check("a cure that reaches zero is permanent, not a low ebb", cured.IsDefault);
            ZoneState afterCure = BiomeDrift.Apply(cured, Hour, 0.02f, 0f, 0f, 1f, 0.1f, 1f);
            Check("warm weather does not resurrect a cured zone", afterCure.IsDefault);

            // Corruption feeds plague: boost 1 with corruption 0.5 is x1.5 growth.
            var corrupt = new ZoneState { Plague = 0.05f, Corruption = 0.5f };
            var clean   = new ZoneState { Plague = 0.05f };
            float corruptGrown = BiomeDrift.Apply(corrupt, Hour, 0f, 0f, 0f, 1f, 0.04f, 1f).Plague;
            float cleanGrown   = BiomeDrift.Apply(clean,   Hour, 0f, 0f, 0f, 1f, 0.04f, 1f).Plague;
            Check($"corruption accelerates plague ({corruptGrown:F4} vs {cleanGrown:F4})",
                corruptGrown > cleanGrown
                && Math.Abs((corruptGrown - 0.05f) - 1.5f * (cleanGrown - 0.05f)) < 0.0005f);

            // Spread targeting: only zones at threshold seed, only pristine neighbours, once.
            var hot = new List<KeyValuePair<ZoneKey, float>>
            {
                new KeyValuePair<ZoneKey, float>(new ZoneKey(0, 0), 0.6f),   // source
                new KeyValuePair<ZoneKey, float>(new ZoneKey(1, 0), 0.6f),   // adjacent source
                new KeyValuePair<ZoneKey, float>(new ZoneKey(5, 5), 0.1f),   // below threshold
            };
            var infected = new HashSet<ZoneKey> { new ZoneKey(0, 0), new ZoneKey(1, 0), new ZoneKey(5, 5) };
            var targets = new List<ZoneKey>();
            PlagueSpread.CollectSpreadTargets(hot, 0.5f, infected, targets);

            // Two sources in a row: 8 orthogonal neighbours minus each other, minus the shared
            // duplicates — (−1,0),(0,1),(0,−1),(2,0),(1,1),(1,−1) = 6. The weak zone adds none.
            Check($"frontier is uninfected orthogonal neighbours of hot zones only (got {targets.Count})",
                targets.Count == 6
                && !targets.Contains(new ZoneKey(0, 0))
                && !targets.Contains(new ZoneKey(1, 0))
                && !targets.Contains(new ZoneKey(4, 5)));

            Check("a zone between two hot sources is listed once",
                (new Func<bool>(() => {
                    var two = new List<KeyValuePair<ZoneKey, float>>
                    {
                        new KeyValuePair<ZoneKey, float>(new ZoneKey(0, 0), 0.9f),
                        new KeyValuePair<ZoneKey, float>(new ZoneKey(2, 0), 0.9f),
                    };
                    var inf = new HashSet<ZoneKey> { new ZoneKey(0, 0), new ZoneKey(2, 0) };
                    var t = new List<ZoneKey>();
                    PlagueSpread.CollectSpreadTargets(two, 0.5f, inf, t);
                    int middle = 0;
                    foreach (ZoneKey z in t) if (z == new ZoneKey(1, 0)) middle++;
                    return middle == 1;
                }))());

            Check("no hot zones means no frontier",
                (new Func<bool>(() => {
                    var t = new List<ZoneKey>();
                    PlagueSpread.CollectSpreadTargets(
                        new List<KeyValuePair<ZoneKey, float>>
                        { new KeyValuePair<ZoneKey, float>(new ZoneKey(3, 3), 0.49f) },
                        0.5f, new HashSet<ZoneKey> { new ZoneKey(3, 3) }, t);
                    return t.Count == 0;
                }))());
        }

        // ---- WorldState -------------------------------------------------------------

        private static void WorldStateTests()
        {
            Console.WriteLine("\nWorldState");

            var zones = new List<KeyValuePair<ZoneKey, ZoneState>>
            {
                new KeyValuePair<ZoneKey, ZoneState>(new ZoneKey(0, 0),
                    new ZoneState { Plague = 0.6f, Corruption = 0.5f }),
                new KeyValuePair<ZoneKey, ZoneState>(new ZoneKey(1, 0),
                    new ZoneState { Scorch = 0.2f }),
                new KeyValuePair<ZoneKey, ZoneState>(new ZoneKey(2, 0),
                    new ZoneState { Frost = 0.4f }),
            };
            BiomeMetrics m = BiomeMetrics.Compute(zones);

            Check($"metrics count what they should ({m.TrackedZones} tracked, {m.InfectedZones} infected)",
                m.TrackedZones == 3 && m.InfectedZones == 1);

            // Burden is the weighted sum: 0.6*1.5 + 0.5*1 + 0.2*1 + 0.4*0.5 = 1.8.
            Check($"burden weights each field as documented ({m.Burden():F3})",
                Math.Abs(m.Burden() - 1.8f) < 0.0005f);

            Check("an empty store carries no burden",
                BiomeMetrics.Compute(new List<KeyValuePair<ZoneKey, ZoneState>>()).Burden() == 0f);

            // Worsening is prompt: at the threshold, the condition turns.
            Check("ailing begins at its threshold",
                WorldConditionRules.Derive(4f, WorldCondition.Stable, 0.25f, 4f, 12f)
                    == WorldCondition.Ailing);

            Check("stricken begins at its threshold",
                WorldConditionRules.Derive(12f, WorldCondition.Ailing, 0.25f, 4f, 12f)
                    == WorldCondition.Stricken);

            // Improvement needs the hysteresis band. This is the no-flap guarantee: a burden
            // hovering exactly at a boundary announces once, not every pass forever.
            Check("just under the threshold does NOT improve (hysteresis)",
                WorldConditionRules.Derive(3.9f, WorldCondition.Ailing, 0.25f, 4f, 12f)
                    == WorldCondition.Ailing);

            Check("clearing the hysteresis band improves",
                WorldConditionRules.Derive(3.3f, WorldCondition.Ailing, 0.25f, 4f, 12f)
                    == WorldCondition.Stable);

            // A collapse can improve several steps in one pass, but only through boundaries it
            // has genuinely cleared.
            Check("a full collapse improves straight to flourishing",
                WorldConditionRules.Derive(0.1f, WorldCondition.Stricken, 0.25f, 4f, 12f)
                    == WorldCondition.Flourishing);

            Check("a partial collapse stops at the band it has not cleared",
                WorldConditionRules.Derive(3.8f, WorldCondition.Stricken, 0.25f, 4f, 12f)
                    == WorldCondition.Ailing);

            // The calm end has the same protection: flourishing is not re-entered at its
            // ceiling, only below the band under it.
            Check("flourishing needs its own band cleared",
                WorldConditionRules.Derive(0.24f, WorldCondition.Stable, 0.25f, 4f, 12f)
                    == WorldCondition.Stable
                && WorldConditionRules.Derive(0.2f, WorldCondition.Stable, 0.25f, 4f, 12f)
                    == WorldCondition.Flourishing);
        }

        // ---- Ecology ----------------------------------------------------------------

        private static void EcologyTests()
        {
            Console.WriteLine("\nEcology");

            const float Hour = 3600f;

            Check("clean land does not corrupt",
                EcologyPressure.Apply(new ZoneState { Plague = 0.29f, Scorch = 0.29f },
                    Hour, 0.01f, 0.3f, 0.3f).Corruption == 0f);

            // At the threshold exactly, pressure is a trickle (quarter rate), not full-on: the
            // effect ramps in rather than switching at one epsilon past the line.
            ZoneState atLine = EcologyPressure.Apply(new ZoneState { Plague = 0.3f },
                Hour, 0.01f, 0.3f, 0.3f);
            Check($"pressure starts as a trickle at the threshold ({atLine.Corruption:E2})",
                Math.Abs(atLine.Corruption - 0.0025f) < 0.0001f);

            // Plague at double the threshold: excess 1, so rate x1.25.
            ZoneState hot = EcologyPressure.Apply(new ZoneState { Plague = 0.6f },
                Hour, 0.01f, 0.3f, 0.3f);
            Check($"pressure scales with how far past the line ({hot.Corruption:E2})",
                Math.Abs(hot.Corruption - 0.0125f) < 0.0001f);

            Check("scorch pressure corrupts too",
                EcologyPressure.Apply(new ZoneState { Scorch = 0.5f },
                    Hour, 0.01f, 0.3f, 0.3f).Corruption > 0f);

            Check("zero rate or zero time changes nothing",
                EcologyPressure.Apply(new ZoneState { Plague = 0.9f }, 0f, 0.01f, 0.3f, 0.3f).Corruption == 0f
                && EcologyPressure.Apply(new ZoneState { Plague = 0.9f }, Hour, 0f, 0.3f, 0.3f).Corruption == 0f);

            Check("NaN inputs corrupt nothing rather than poisoning the store",
                EcologyPressure.Apply(new ZoneState { Plague = 0.9f }, float.NaN, 0.01f, 0.3f, 0.3f).Corruption == 0f
                && !float.IsNaN(EcologyPressure.Apply(new ZoneState { Plague = 0.9f }, Hour, float.NaN, 0.3f, 0.3f).Corruption));
        }

        // ---- Titles -----------------------------------------------------------------

        private static void TitleTests()
        {
            Console.WriteLine("\nTitles");

            string dir = Path.Combine(Path.GetTempPath(), "rw_titles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "titles.dat");

            try
            {
                TitleStore.OverridePath = path;

                TitleStore.Load();
                Check("a fresh world has no titles and a usable store",
                    TitleStore.IsLoaded && TitleStore.Count == 0);

                TitleStore.Set(123456789L, "Stormrider");
                TitleStore.Set(987654321L, "Winterborn");
                TitleStore.Set(123456789L, "Plaguewalker");   // latest earned wins
                TitleStore.Load();
                Check($"titles survive a save/load round-trip, latest wins (got '{TitleStore.Get(123456789L)}')",
                    TitleStore.Count == 2 && TitleStore.Get(123456789L) == "Plaguewalker");

                // The placeholder id a dedicated server's own profile would produce.
                TitleStore.Set(0L, "Stormrider");
                Check("player id 0 is never recorded", TitleStore.Get(0L) == null);

                TitleStore.Set(987654321L, null);
                TitleStore.Load();
                Check("clearing a title removes the row entirely", TitleStore.Count == 1);

                byte[] raw = File.ReadAllBytes(path);
                Check("the title store is written without a BOM",
                    raw.Length >= 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF));

                File.WriteAllBytes(path, new byte[] { 0x00, 0xFF, 0x00, 0xFF });
                TitleStore.Load();
                Check("a corrupt title store degrades to empty and is quarantined",
                    TitleStore.IsLoaded && TitleStore.Count == 0
                    && File.Exists(path + ".corrupt") && !File.Exists(path));

                Check("a titled suffix renders on its own smaller line",
                    TitleFormat.Suffix("Stormrider").StartsWith("\n")
                    && TitleFormat.Suffix("Stormrider").Contains("Stormrider"));

                Check("no title means no suffix at all",
                    TitleFormat.Suffix(null) == "" && TitleFormat.Suffix("  ") == "");
            }
            finally
            {
                TitleStore.OverridePath = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Fog --------------------------------------------------------------------

        private static void FogTests()
        {
            Console.WriteLine("\nFog");

            // The floor is the discovery mechanic: fresh seeds (0.05) must not telegraph the
            // frontier the tick it spreads.
            Check("a fresh seed shows no fog",
                FogMath.EmissionFor(0.05f, 1f) == 0f);

            Check("below the visible floor shows no fog",
                FogMath.EmissionFor(0.1499f, 1f) == 0f);

            Check($"full plague fogs at the full rate ({FogMath.EmissionFor(1f, 1f):F1})",
                Math.Abs(FogMath.EmissionFor(1f, 1f) - FogMath.FullRate) < 0.01f);

            float half = FogMath.EmissionFor(0.575f, 1f);
            Check($"halfway up the ramp is half the rate ({half:F1})",
                Math.Abs(half - FogMath.FullRate / 2f) < 0.5f);

            Check("density scales and is capped",
                FogMath.EmissionFor(1f, 2f) > FogMath.EmissionFor(1f, 1f)
                && Math.Abs(FogMath.EmissionFor(1f, 99f) - FogMath.FullRate * 4f) < 0.01f);

            Check("zero density, NaN plague and NaN density all mean no fog",
                FogMath.EmissionFor(1f, 0f) == 0f
                && FogMath.EmissionFor(float.NaN, 1f) == 0f
                && FogMath.EmissionFor(1f, float.NaN) == 0f);

            // Ash: FogMath's shape at the burn scars — but with a non-zero BASE at the
            // floor, because a ramp-from-zero rendered real scars invisible (0.14.0).
            Check("trace scorch dusts nothing, real burns visibly show, NaN shows nothing",
                AshMath.EmissionFor(0.09f, 1f) == 0f
                && Math.Abs(AshMath.EmissionFor(0.1f, 1f) - AshMath.BaseRate) < 0.01f
                && Math.Abs(AshMath.EmissionFor(1f, 1f) - AshMath.FullRate) < 0.01f
                && AshMath.EmissionFor(float.NaN, 1f) == 0f
                && AshMath.EmissionFor(1f, 0f) == 0f);

            float halfAsh = AshMath.EmissionFor(0.55f, 1f);
            Check($"ash ramps linearly from base to full ({halfAsh:F1})",
                Math.Abs(halfAsh - (AshMath.BaseRate + (AshMath.FullRate - AshMath.BaseRate) * 0.5f)) < 0.5f);
        }

        // ---- Exposure (HealthSystem's pure half) --------------------------------------

        private static void ExposureTests()
        {
            Console.WriteLine("\nExposure");

            // Predict before reading: 60s on plague 0.95 at 30min-to-max is
            // 0.95 / (30*60) * 60 = 0.031667 exposure.
            float oneMinute = ExposureMath.Accrue(0f, 0.95f, 30f, false, 0.5f, 60f);
            Check($"a minute in the outbreak matches prediction ({oneMinute:F5})",
                Math.Abs(oneMinute - 0.95f / 30f) < 1e-5f);

            Check("below the fog floor nothing accrues — the sickness keeps the fog's secret",
                ExposureMath.Accrue(0.2f, 0.1499f, 30f, false, 0.5f, 600f) == 0.2f);

            float resisted = ExposureMath.Accrue(0f, 0.95f, 30f, true, 0.5f, 60f);
            Check($"poison resistance halves the taking hold ({resisted:F5})",
                Math.Abs(resisted - 0.95f / 60f) < 1e-5f);

            float e = 0f;
            for (int i = 0; i < 30 * 12; i++)   // 30 minutes of 5s ticks at full plague
                e = ExposureMath.Accrue(e, 1f, 30f, false, 0.5f, 5f);
            Check($"full plague maxes out in the configured minutes ({e:F3})",
                Math.Abs(e - 1f) < 1e-3f);

            Check("exposure is capped at 1",
                ExposureMath.Accrue(0.999f, 1f, 5f, false, 0.5f, 3600f) == 1f);

            float drained = ExposureMath.Decay(1f, 20f, false, 2f, 60f);
            Check($"a minute of recovery matches prediction ({drained:F3})",
                Math.Abs(drained - 0.95f) < 1e-5f);

            float rested = ExposureMath.Decay(1f, 20f, true, 2f, 60f);
            Check($"rested doubles the drain ({rested:F3})",
                Math.Abs(rested - 0.90f) < 1e-5f);

            Check("recovery terminates at exactly zero, not almost-zero",
                ExposureMath.Decay(0.001f, 20f, false, 2f, 60f) == 0f);

            Check("NaN inputs are neutralised, not propagated",
                !float.IsNaN(ExposureMath.Accrue(float.NaN, float.NaN, 30f, false, float.NaN, 60f))
                && ExposureMath.Decay(float.NaN, 20f, false, 2f, 60f) == 0f
                && ExposureMath.TierFor(float.NaN, 0.25f, 0.5f, 0.8f) == 0
                && ExposureMath.StaminaRegenMultiplier(float.NaN, 0.25f, 0.85f, 0.3f) == 1f);

            Check("tiers begin at their thresholds, inclusive",
                ExposureMath.TierFor(0.2f, 0.25f, 0.5f, 0.8f) == 0
                && ExposureMath.TierFor(0.25f, 0.25f, 0.5f, 0.8f) == 1
                && ExposureMath.TierFor(0.5f, 0.25f, 0.5f, 0.8f) == 2
                && ExposureMath.TierFor(0.8f, 0.25f, 0.5f, 0.8f) == 3);

            // The owner's palette call: stamina fails FIRST. At exposure 0.4 (past tier 1,
            // short of tier 2) stamina already sags while health regen is untouched.
            Check("stamina fails before health regen",
                ExposureMath.StaminaRegenMultiplier(0.4f, 0.25f, 0.85f, 0.3f) < 1f
                && ExposureMath.HealthRegenMultiplier(0.4f, 0.5f, 0.8f, 0.38f) == 1f);

            // THE 0.8.0 REGRESSION, pinned. A ramp starting at 1.0 on the threshold gave
            // x0.98 here — announced as "a sickness takes root in you" and imperceptible in
            // the hands. Crossing a tier must be FELT on the pass it is announced.
            float atTier1 = ExposureMath.StaminaRegenMultiplier(0.25f, 0.25f, 0.85f, 0.3f);
            Check($"crossing tier 1 is felt immediately, not approached ({atTier1:F2})",
                Math.Abs(atTier1 - 0.85f) < 1e-4f);

            float atTier2 = ExposureMath.HealthRegenMultiplier(0.5f, 0.5f, 0.8f, 0.38f);
            Check($"crossing tier 2 lands the wound half at once ({atTier2:F2})",
                Math.Abs(atTier2 - 0.8f) < 1e-4f);

            // The three points the owner agreed to, reproduced by one ramp off the step.
            float s50 = ExposureMath.StaminaRegenMultiplier(0.5f, 0.25f, 0.85f, 0.3f);
            float s80 = ExposureMath.StaminaRegenMultiplier(0.8f, 0.25f, 0.85f, 0.3f);
            Check($"the agreed stamina table holds at every tier (0.85 / {s50:F2} / {s80:F2})",
                Math.Abs(s50 - 0.667f) < 0.02f && Math.Abs(s80 - 0.45f) < 0.02f);

            float h80 = ExposureMath.HealthRegenMultiplier(0.8f, 0.5f, 0.8f, 0.38f);
            Check($"the agreed health table holds too (0.80 / {h80:F2})",
                Math.Abs(h80 - 0.55f) < 0.02f);

            Check("the ramp still reaches its floor at full exposure",
                Math.Abs(ExposureMath.StaminaRegenMultiplier(1f, 0.25f, 0.85f, 0.3f) - 0.3f) < 1e-4f
                && Math.Abs(ExposureMath.HealthRegenMultiplier(1f, 0.5f, 0.8f, 0.38f) - 0.38f) < 1e-4f);

            Check("below its start tier every multiplier is exactly 1",
                ExposureMath.StaminaRegenMultiplier(0.2499f, 0.25f, 0.85f, 0.3f) == 1f
                && ExposureMath.HealthRegenMultiplier(0.45f, 0.5f, 0.8f, 0.38f) == 1f);

            Check("quantized sync fires on a full step or a zero transition, not on dust",
                !ExposureMath.QuantizedDiffer(0.5f, 0.505f)
                && ExposureMath.QuantizedDiffer(0.5f, 0.511f)
                && ExposureMath.QuantizedDiffer(0.005f, 0f)
                && !ExposureMath.QuantizedDiffer(0f, 0f));
        }

        // ---- HealthStore ---------------------------------------------------------------

        private static void HealthStoreTests()
        {
            Console.WriteLine("\nHealthStore");

            string dir = Path.Combine(Path.GetTempPath(), "rw_health_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "health.dat");

            try
            {
                HealthStore.OverridePath = path;

                HealthStore.Load();
                Check("a fresh world has no exposure and a usable store",
                    HealthStore.IsLoaded && HealthStore.Count == 0);

                HealthStore.SaveIfDirty();
                Check("a clean store writes nothing", !File.Exists(path));

                HealthStore.Set(123456789L, 0.4321f);
                HealthStore.Set(987654321L, 0.05f);
                HealthStore.SaveIfDirty();
                HealthStore.Load();
                Check($"exposure survives a round-trip through the shipping writer (got {HealthStore.Get(123456789L):F4})",
                    HealthStore.Count == 2 && Math.Abs(HealthStore.Get(123456789L) - 0.4321f) < 1e-4f);

                HealthStore.Set(0L, 0.9f);
                Check("player id 0 is never recorded", HealthStore.Get(0L) == 0f);

                HealthStore.Set(987654321L, 0f);
                HealthStore.SaveIfDirty();
                HealthStore.Load();
                Check("a recovered player's row is removed entirely", HealthStore.Count == 1);

                byte[] raw = File.ReadAllBytes(path);
                Check("the health store is written without a BOM",
                    raw.Length >= 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF));

                File.WriteAllLines(path, new[] { "version\t1", "555\t1.7" });
                HealthStore.Load();
                Check($"an out-of-range value in the file is clamped on read ({HealthStore.Get(555L):F1})",
                    HealthStore.Get(555L) == 1f);

                File.WriteAllBytes(path, new byte[] { 0x00, 0xFF, 0x00, 0xFF });
                HealthStore.Load();
                Check("a corrupt health store degrades to empty and is quarantined",
                    HealthStore.IsLoaded && HealthStore.Count == 0
                    && File.Exists(path + ".corrupt") && !File.Exists(path));
            }
            finally
            {
                HealthStore.OverridePath = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Consequence ---------------------------------------------------------------

        private static void ConsequenceTests()
        {
            Console.WriteLine("\nConsequence");

            Check("plagued OR scorched ground is barren, inclusive at each threshold",
                ConsequenceMath.Barren(0.4f, 0f, 0.4f, 0.5f)
                && ConsequenceMath.Barren(0f, 0.5f, 0.4f, 0.5f)
                && !ConsequenceMath.Barren(0.39f, 0.49f, 0.4f, 0.5f));

            Check("blight for crops is the WORSE of plague and corruption",
                ConsequenceMath.WithersCrops(0.7f, 0f, 0.6f)
                && ConsequenceMath.WithersCrops(0f, 0.65f, 0.6f)
                && !ConsequenceMath.WithersCrops(0.3f, 0.3f, 0.6f));

            Check("wildlife sickens at its threshold and not below",
                ConsequenceMath.SickensWildlife(0.4f, 0.4f)
                && !ConsequenceMath.SickensWildlife(0.399f, 0.4f));

            float atFull = ConsequenceMath.EmpowerLevelUpMultiplier(1f, 0.5f, 6f);
            float mid = ConsequenceMath.EmpowerLevelUpMultiplier(0.75f, 0.5f, 6f);
            Check($"empower odds ramp from 1 at threshold to the dial at full ({mid:F1} / {atFull:F1})",
                ConsequenceMath.EmpowerLevelUpMultiplier(0.49f, 0.5f, 6f) == 1f
                && Math.Abs(mid - 3.5f) < 1e-4f
                && Math.Abs(atFull - 6f) < 1e-4f);

            Check("NaN inputs empower nothing, sicken nothing, wither nothing",
                ConsequenceMath.EmpowerLevelUpMultiplier(float.NaN, 0.5f, 6f) == 1f
                && !ConsequenceMath.SickensWildlife(float.NaN, 0.4f)
                && !ConsequenceMath.Barren(float.NaN, float.NaN, 0.4f, 0.5f)
                && !ConsequenceMath.WithersCrops(float.NaN, float.NaN, 0.6f));

            var hot = new ZoneState { Plague = 0.7f, Corruption = 0.6f, Scorch = 0f };
            ConsequenceFlags flags = ConsequenceMath.FlagsFor(hot, 0.4f, 0.5f, 0.4f, 0.5f, 0.6f);
            Check($"a hot zone earns every applicable flag ({flags})",
                flags == (ConsequenceFlags.Barren | ConsequenceFlags.Empowered
                        | ConsequenceFlags.Sickening | ConsequenceFlags.Withering));

            Check("a clean zone earns none",
                ConsequenceMath.FlagsFor(default, 0.4f, 0.5f, 0.4f, 0.5f, 0.6f)
                    == ConsequenceFlags.None);

            // Instantiated objects are named "Deer(Clone)"; the match must be exact after
            // truncation — "Boar" quietly sickening a "BoarPiggy" breeding pen is the bug
            // this test exists to forbid.
            Check("passive list matches clones exactly, case-insensitively, never by prefix",
                ConsequenceMath.IsPassivePrefab("Deer(Clone)", "Deer,Boar,Hare")
                && ConsequenceMath.IsPassivePrefab("deer", " Deer , Boar ")
                && !ConsequenceMath.IsPassivePrefab("BoarPiggy(Clone)", "Deer,Boar,Hare")
                && !ConsequenceMath.IsPassivePrefab("Deer(Clone)", "")
                && !ConsequenceMath.IsPassivePrefab("", "Deer"));
        }

        // ---- Rivalry (phase A: the influence ledger) -----------------------------------

        private static void RivalryTests()
        {
            Console.WriteLine("\nRivalry");

            // Decay: exactly half after one half-life, compounding correctly, disabled at 0.
            float half = RivalryMath.DecayFactor(48f, 48f * 3600f);
            Check($"one half-life fades a row to exactly half ({half:F4})",
                Math.Abs(half - 0.5f) < 1e-4f);

            float quarter = RivalryMath.DecayFactor(48f, 96f * 3600f);
            Check($"two half-lives fade to a quarter ({quarter:F4})",
                Math.Abs(quarter - 0.25f) < 1e-4f);

            Check("zero elapsed, zero half-life and NaN all mean no decay",
                RivalryMath.DecayFactor(48f, 0f) == 1f
                && RivalryMath.DecayFactor(0f, 3600f) == 1f
                && RivalryMath.DecayFactor(float.NaN, 3600f) == 1f);

            // Healing care: only decreases book, and the split is even.
            Check("healing books care, worsening books nothing",
                RivalryMath.CareFromHealing(1.0f, 0.7f, 1f) > 0f
                && RivalryMath.CareFromHealing(0.7f, 1.0f, 1f) == 0f
                && Math.Abs(RivalryMath.CareFromHealing(1.0f, 0.7f, 2f) - 0.6f) < 1e-4f);

            Check("care splits evenly and nobody splits with zero people",
                Math.Abs(RivalryMath.SplitAmong(0.6f, 3) - 0.2f) < 1e-5f
                && RivalryMath.SplitAmong(0.6f, 0) == 0f
                && RivalryMath.SplitAmong(float.NaN, 2) == 0f);

            Check("zone damage sums every field and neutralises NaN",
                Math.Abs(RivalryMath.ZoneDamage(new ZoneState
                    { Fertility = 0.1f, Corruption = 0.2f, Scorch = 0.3f, Frost = 0.1f, Plague = 0.3f }) - 1.0f) < 1e-5f
                && RivalryMath.ZoneDamage(new ZoneState { Plague = float.NaN, Frost = 0.5f }) == 0.5f);

            Check("the watermark admits only genuinely newer plants",
                RivalryMath.IsNewPlant(100, 50)
                && !RivalryMath.IsNewPlant(50, 50)
                && !RivalryMath.IsNewPlant(0, 0));

            // Phase B: the grudge and its teeth.
            Check("grudge is net harm, clamped, and care genuinely mollifies",
                Math.Abs(RivalryMath.GrudgeFor(0.5f, 0.2f, 1f) - 0.3f) < 1e-5f
                && RivalryMath.GrudgeFor(0.2f, 0.5f, 1f) == 0f
                && RivalryMath.GrudgeFor(5f, 0f, 1f) == 1f
                && RivalryMath.GrudgeFor(0.25f, 0f, 2f) == 0.5f
                && RivalryMath.GrudgeFor(float.NaN, 0f, 1f) == 0f);

            Check("a full grudge halves recovery and doubles pressure, never more",
                Math.Abs(RivalryMath.GrudgedRecovery(0.02f, 1f) - 0.01f) < 1e-6f
                && Math.Abs(RivalryMath.GrudgedPressure(0.03f, 1f) - 0.06f) < 1e-6f
                && RivalryMath.GrudgedRecovery(0.02f, 0f) == 0.02f
                && RivalryMath.GrudgedPressure(0.03f, 0f) == 0.03f
                && Math.Abs(RivalryMath.GrudgedRecovery(0.02f, 9f) - 0.01f) < 1e-6f);

            Check("a half grudge sits exactly between",
                Math.Abs(RivalryMath.GrudgedRecovery(0.02f, 0.5f) - 0.015f) < 1e-6f
                && Math.Abs(RivalryMath.GrudgedPressure(0.02f, 0.5f) - 0.03f) < 1e-6f);

            // The ledger itself, through the shipping writer.
            string dir = Path.Combine(Path.GetTempPath(), "rw_rivalry_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "rivalry.dat");

            try
            {
                RivalryLedger.OverridePath = path;

                RivalryLedger.Load();
                Check("a fresh world has an empty, usable ledger with watermark 0",
                    RivalryLedger.IsLoaded && RivalryLedger.Count == 0
                    && RivalryLedger.PlantWatermark == 0);

                var zoneA = new ZoneKey(0, -1);
                RivalryLedger.AddHarm(zoneA, 111L, 0.5f);
                RivalryLedger.AddCare(zoneA, 111L, 0.25f);
                RivalryLedger.AddCare(zoneA, 222L, 1.0f);
                RivalryLedger.AddCare(new ZoneKey(3, 3), 111L, 0.1f);
                RivalryLedger.PlantWatermark = 987654321L;
                RivalryLedger.SaveIfDirty();
                RivalryLedger.Load();
                var row = RivalryLedger.Get(zoneA, 111L);
                Check($"rows and watermark survive the shipping writer (harm={row.Harm:F2} care={row.Care:F2})",
                    RivalryLedger.Count == 3
                    && Math.Abs(row.Harm - 0.5f) < 1e-4f && Math.Abs(row.Care - 0.25f) < 1e-4f
                    && RivalryLedger.PlantWatermark == 987654321L);

                RivalryLedger.PlantWatermark = 5L;   // an attempt to LOWER it
                Check("the watermark only rises", RivalryLedger.PlantWatermark == 987654321L);

                RivalryLedger.AddHarm(zoneA, 0L, 9f);
                Check("player id 0 is never recorded", RivalryLedger.Get(zoneA, 0L).Harm == 0f);

                float worst = RivalryLedger.MaxGrudgeFor(111L, 1f);
                Check($"the worst grudge finds the right zone and nets out care ({worst:F2})",
                    Math.Abs(worst - 0.25f) < 1e-4f     // zone A: 0.5 harm - 0.25 care
                    && RivalryLedger.MaxGrudgeFor(222L, 1f) == 0f    // pure carer, no grudge
                    && RivalryLedger.MaxGrudgeFor(999L, 1f) == 0f);  // stranger, no rows

                RivalryLedger.DecayAll(0.5f);
                Check($"decay halves every column ({RivalryLedger.Get(zoneA, 111L).Harm:F3})",
                    Math.Abs(RivalryLedger.Get(zoneA, 111L).Harm - 0.25f) < 1e-4f
                    && Math.Abs(RivalryLedger.Get(zoneA, 222L).Care - 0.5f) < 1e-4f);

                RivalryLedger.DecayAll(1e-6f);
                RivalryLedger.SaveIfDirty();
                RivalryLedger.Load();
                Check("rows that fade to nothing are pruned, the file stays sparse",
                    RivalryLedger.Count == 0);

                byte[] raw = File.ReadAllBytes(path);
                Check("the ledger is written without a BOM",
                    raw.Length >= 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF));

                File.WriteAllLines(path, new[] { "version\t1", "0\t0\t555\t-3\t-9" });
                RivalryLedger.Load();
                Check("hand-edited negatives are floored on read, not trusted",
                    RivalryLedger.Get(new ZoneKey(0, 0), 555L).Harm == 0f);

                File.WriteAllBytes(path, new byte[] { 0x00, 0xFF, 0x00, 0xFF });
                RivalryLedger.Load();
                Check("a corrupt ledger degrades to empty and is quarantined",
                    RivalryLedger.IsLoaded && RivalryLedger.Count == 0
                    && File.Exists(path + ".corrupt") && !File.Exists(path));
            }
            finally
            {
                RivalryLedger.OverridePath = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---- Contest (phase C) ---------------------------------------------------------

        private static void ContestTests()
        {
            Console.WriteLine("\nContest");

            var zone = new ZoneKey(1, -1);
            var other = new ZoneKey(2, 2);

            Dictionary<ZoneKey, Dictionary<long, float>> Values(params (ZoneKey z, long p, float v)[] rows)
            {
                var d = new Dictionary<ZoneKey, Dictionary<long, float>>();
                foreach ((ZoneKey z, long p, float v) r in rows)
                {
                    if (!d.TryGetValue(r.z, out Dictionary<long, float> inner))
                        d[r.z] = inner = new Dictionary<long, float>();
                    inner[r.p] = r.v;
                }
                return d;
            }

            var holders = new Dictionary<ZoneKey, RivalryContest.Holder>();
            var flips = new List<RivalryContest.Flip>();

            // Below the floor: nobody holds anything, and nothing is announced.
            RivalryContest.Update(Values((zone, 111L, 0.1f)), holders, 0.2f, 0.15f, flips);
            Check("nobody wins ground they barely touched",
                holders.Count == 0 && flips.Count == 0);

            // First real claim: crowned SILENTLY (a walkover is not a contest).
            RivalryContest.Update(Values((zone, 111L, 0.5f)), holders, 0.2f, 0.15f, flips);
            Check("an unclaimed zone is crowned silently",
                holders.Count == 1 && holders[zone].Player == 111L && flips.Count == 0);

            // A challenger inside the hysteresis band does NOT take it.
            RivalryContest.Update(Values((zone, 111L, 0.5f), (zone, 222L, 0.55f)), holders, 0.2f, 0.15f, flips);
            Check("a challenger inside the band does not dethrone (0.55 < 0.5 x 1.15)",
                holders[zone].Player == 111L && flips.Count == 0);

            // Clearing the band takes the zone AND announces — both above the floor.
            RivalryContest.Update(Values((zone, 111L, 0.5f), (zone, 222L, 0.6f)), holders, 0.2f, 0.15f, flips);
            Check($"clearing the band flips the zone and announces ({flips.Count} flip)",
                holders[zone].Player == 222L && flips.Count == 1
                && flips[0].From == 111L && flips[0].To == 222L && flips[0].Zone == zone);

            // An incumbent who falls below the floor is replaced SILENTLY: no rival
            // genuinely contested them, they simply faded.
            flips.Clear();
            RivalryContest.Update(Values((zone, 222L, 0.1f), (zone, 333L, 0.9f)), holders, 0.2f, 0.15f, flips);
            Check("a faded incumbent is replaced without an announcement",
                holders[zone].Player == 333L && flips.Count == 0);

            // Everyone decays below the floor: the ground is unclaimed again, silently.
            flips.Clear();
            RivalryContest.Update(Values((zone, 333L, 0.05f)), holders, 0.2f, 0.15f, flips);
            Check("ground nobody shapes any more becomes unclaimed",
                holders.Count == 0 && flips.Count == 0);

            // A zone that vanishes from the ledger entirely vacates too.
            holders[other] = new RivalryContest.Holder { Player = 444L, Value = 1f };
            RivalryContest.Update(Values((zone, 111L, 0.5f)), holders, 0.2f, 0.15f, flips);
            Check("a fully decayed zone vacates its holder",
                !holders.ContainsKey(other));

            Check("ZonesHeld counts only that player's holdings",
                RivalryContest.ZonesHeld(holders, 111L) == 1
                && RivalryContest.ZonesHeld(holders, 999L) == 0
                && RivalryContest.ZonesHeld(holders, 0L) == 0);

            // Phase D: the spawn war's gates and its verdict.
            Check("contested needs BOTH sides strong — sick alone or loved alone is peace",
                RivalryContest.IsContested(0.6f, 0.4f, 0.5f, 0.3f)
                && !RivalryContest.IsContested(0.6f, 0.2f, 0.5f, 0.3f)
                && !RivalryContest.IsContested(0.3f, 0.9f, 0.5f, 0.3f)
                && !RivalryContest.IsContested(float.NaN, 0.9f, 0.5f, 0.3f));

            Check("blight is the worse of plague and corruption",
                Math.Abs(RivalryContest.BlightOf(new ZoneState { Plague = 0.3f, Corruption = 0.7f }) - 0.7f) < 1e-5f
                && RivalryContest.BlightOf(new ZoneState { Plague = float.NaN, Corruption = 0.4f }) == 0.4f);

            Check("storms escalate the war and peace has no intensity",
                RivalryContest.Intensity(contested: true, inStorm: false, 2f) == 1f
                && RivalryContest.Intensity(contested: true, inStorm: true, 2f) == 2f
                && RivalryContest.Intensity(contested: false, inStorm: true, 2f) == 0f
                && RivalryContest.Intensity(contested: true, inStorm: true, float.NaN) == 1f);

            Check("the wild wins when the blight itself broke; the blight wins otherwise",
                RivalryContest.Winner(0.3f, 0.5f) == RivalryContest.WarWinner.Wild
                && RivalryContest.Winner(0.8f, 0.5f) == RivalryContest.WarWinner.Blight
                && RivalryContest.Winner(float.NaN, 0.5f) == RivalryContest.WarWinner.Wild);

            // Mercy is decay-only and never a penalty.
            float plain = ExposureMath.Decay(1f, 20f, false, 2f, 60f);
            float merciful = ExposureMath.Decay(1f, 20f, false, 2f, 60f, 1.5f);
            Check($"mercy quickens recovery only ({plain:F3} -> {merciful:F3})",
                merciful < plain
                && Math.Abs((1f - merciful) - (1f - plain) * 1.5f) < 1e-5f
                && ExposureMath.Decay(1f, 20f, false, 2f, 60f, 0.1f) == plain
                && ExposureMath.Decay(1f, 20f, false, 2f, 60f, float.NaN) == plain);
        }

        private static void NemesisTests()
        {
            Console.WriteLine("\nNemesis");

            Check("a first kill lifts level 1 to 2", NemesisMark.NextLevel(1, 3) == 2);
            Check("the cap holds at the top", NemesisMark.NextLevel(3, 3) == 3);
            Check("a cap below current never demotes", NemesisMark.NextLevel(3, 2) == 3);
            Check("a garbage level is floored to 1 before stepping", NemesisMark.NextLevel(0, 3) == 2);
            Check("a garbage cap is floored to 1, and current still wins", NemesisMark.NextLevel(2, 0) == 2);

            Check("one kill reads as a single slaying",
                NemesisMark.Suffix("Nomad", 1) == "\n<size=70%><color=#b45050>slayer of Nomad</color></size>");
            Check("repeat kills carry the count",
                NemesisMark.Suffix("Nomad", 3) == "\n<size=70%><color=#b45050>slayer of Nomad x3</color></size>");
            Check("a padded victim name is trimmed",
                NemesisMark.Suffix("  Nomad ", 1) == "\n<size=70%><color=#b45050>slayer of Nomad</color></size>");
            Check("no name, no story", NemesisMark.Suffix("", 2) == "");
            Check("whitespace is not a name", NemesisMark.Suffix("   ", 2) == "");
            Check("no kills, no story", NemesisMark.Suffix("Nomad", 0) == "");
            Check("negative kills are no story either", NemesisMark.Suffix("Nomad", -1) == "");
        }

        private static void RelicTests()
        {
            Console.WriteLine("\nRelic");

            // The peak watermark.
            Check("below threshold records nothing", RelicMath.TrackPeak(0.4f, 0.5f, 0f) == 0f);
            Check("at threshold records the value", RelicMath.TrackPeak(0.5f, 0.5f, 0f) == 0.5f);
            Check("peaks only rise", RelicMath.TrackPeak(0.6f, 0.5f, 0.9f) == 0.9f);
            Check("NaN never writes", RelicMath.TrackPeak(float.NaN, 0.5f, 0.3f) == 0.3f);

            // Through-zero, not merely reduced.
            Check("a peak driven to zero consecrates", RelicMath.ShouldConsecrate(0.7f, 0f));
            Check("reduced-but-alive does not", !RelicMath.ShouldConsecrate(0.7f, 0.01f));
            Check("no recorded peak, no story", !RelicMath.ShouldConsecrate(0f, 0f));

            // Aura arithmetic.
            Check("blessed ground heals quicker",
                RelicMath.RecoveryMultiplier(RelicMath.Plague, false, 1.25f, 0.8f) == 1.25f);
            Check("cursed ground sulks",
                RelicMath.RecoveryMultiplier(RelicMath.Contest, true, 1.25f, 0.8f) == 0.8f);
            Check("no stone, no aura",
                RelicMath.RecoveryMultiplier(RelicMath.None, false, 1.25f, 0.8f) == 1f);
            Check("blessed drains exposure faster",
                RelicMath.ExposureDecayMultiplier(RelicMath.Fire, false, 1.5f) == 1.5f);
            Check("cursed does not slow healing",
                RelicMath.ExposureDecayMultiplier(RelicMath.Contest, true, 1.5f) == 1f);
            Check("cursed shrinks minutes-to-max",
                Math.Abs(RelicMath.ExposureMinutesMultiplier(RelicMath.Contest, true, 1.25f) - 0.8f) < 1e-6f);
            Check("blessed never shields from accrual",
                RelicMath.ExposureMinutesMultiplier(RelicMath.Fire, false, 1.25f) == 1f);
            Check("cursed ground breeds meaner things",
                RelicMath.StarMultiplier(RelicMath.Contest, true, 0.25f) == 1.25f);
            Check("blessed ground adds no stars",
                RelicMath.StarMultiplier(RelicMath.Fire, false, 0.25f) == 1f);

            // Every standing type tells a story; a missing stone says nothing.
            Check("the contest stone knows who won",
                RelicMath.Story(RelicMath.Contest, true).Contains("blight")
                && RelicMath.Story(RelicMath.Contest, false).Contains("wild"));
            Check("no stone, no words", RelicMath.Story(RelicMath.None, false) == "");

            // The ledger round-trips all four row kinds through the SHIPPING writer.
            string path = Path.Combine(Path.GetTempPath(), "rw_relic_test.dat");
            try
            {
                RelicLedger.OverridePath = path;
                if (File.Exists(path)) File.Delete(path);

                RelicLedger.Load();
                Check("a fresh ledger loads empty and usable",
                    RelicLedger.IsLoaded && RelicLedger.RelicCount == 0 && !RelicLedger.EraArmed);

                var zone = new ZoneKey(3, -7);
                RelicLedger.SetPeaks(zone, new RelicLedger.Peaks { Scorch = 0.61f, Plague = 0f });
                RelicLedger.SetRelic(new ZoneKey(0, -1),
                    new RelicLedger.Relic { Type = RelicMath.Contest, Cursed = true, Day = 214 });
                RelicLedger.AddPending(new ZoneKey(5, 5),
                    new RelicLedger.Relic { Type = RelicMath.Fire, Cursed = false, Day = 100 });
                RelicLedger.SetEraSnapshot(new ZoneKey(-2, 2), 1.75f);
                RelicLedger.SaveIfDirty();

                byte[] bytes = File.ReadAllBytes(path);
                Check("the relic ledger is written without a byte-order mark",
                    bytes.Length > 0 && bytes[0] != 0xEF);

                RelicLedger.Load();
                RelicLedger.Relic r = RelicLedger.RelicAt(new ZoneKey(0, -1));
                Check("a standing relic round-trips with type, verdict and day",
                    r.Standing && r.Type == RelicMath.Contest && r.Cursed && r.Day == 214);
                Check("an unconfirmed stone stays unplaced through the round-trip", !r.Placed);

                RelicLedger.MarkPlaced(new ZoneKey(0, -1));
                RelicLedger.SaveIfDirty();
                RelicLedger.Load();
                Check("a confirmed stone round-trips placed",
                    RelicLedger.RelicAt(new ZoneKey(0, -1)).Placed);

                Check("peaks round-trip", RelicLedger.PeaksFor(zone).Scorch == 0.61f);
                Check("pending rows round-trip", RelicLedger.PendingCount == 1);
                Check("the era snapshot round-trips armed", RelicLedger.EraArmed);

                Check("a standing stone ends peak tracking for its zone",
                    RelicLedger.PeaksFor(new ZoneKey(0, -1)).Empty);

                RelicLedger.RemoveRelic(new ZoneKey(0, -1));
                Check("desecration removes the stone",
                    !RelicLedger.RelicAt(new ZoneKey(0, -1)).Standing);

                // A 0.17.0-format row (no placed column) must load unplaced — that is what
                // re-arms the retry for a stone that never rose.
                File.WriteAllText(path,
                    "version\t1\nR\t9\t9\t0\t0\t5\n",
                    new System.Text.UTF8Encoding(false));
                RelicLedger.Load();
                RelicLedger.Relic old = RelicLedger.RelicAt(new ZoneKey(9, 9));
                Check("a six-column 0.17.0 row loads standing but unplaced",
                    old.Standing && !old.Placed);

                File.WriteAllBytes(path, new byte[] { 0x00, 0xFF, 0x13, 0x37, 0x00, 0xFF });
                RelicLedger.Load();
                Check("a garbage file is quarantined, world stays playable",
                    RelicLedger.IsLoaded && RelicLedger.RelicCount == 0 && File.Exists(path + ".corrupt"));
            }
            finally
            {
                RelicLedger.OverridePath = null;
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                try { if (File.Exists(path + ".corrupt")) File.Delete(path + ".corrupt"); } catch { }
            }
        }

        private static void WrathAdminTests()
        {
            Console.WriteLine("\nWrathAdmin");

            var s = default(ZoneState);
            Check("plague lands on the plague field",
                WrathAdmin.TrySetZoneField(s, "plague", 0.6f, out var r1) && r1.Plague == 0.6f && r1.Scorch == 0f);
            Check("scorch lands on the scorch field",
                WrathAdmin.TrySetZoneField(s, "scorch", 0.3f, out var r2) && r2.Scorch == 0.3f && r2.Plague == 0f);
            Check("corr and corruption are the same field",
                WrathAdmin.TrySetZoneField(s, "corr", 0.2f, out var r3) && r3.Corruption == 0.2f
                && WrathAdmin.TrySetZoneField(s, "corruption", 0.2f, out var r4) && r4.Corruption == 0.2f);
            Check("fert lands on fertility",
                WrathAdmin.TrySetZoneField(s, "fert", 0.4f, out var r5) && r5.Fertility == 0.4f);
            Check("frost lands on frost",
                WrathAdmin.TrySetZoneField(s, "frost", 0.9f, out var r6) && r6.Frost == 0.9f);
            Check("values clamp to the store's own bounds",
                WrathAdmin.TrySetZoneField(s, "plague", 3f, out var r7) && r7.Plague == 1f
                && WrathAdmin.TrySetZoneField(s, "plague", -1f, out var r8) && r8.Plague == 0f);
            Check("an unknown field refuses rather than guessing",
                !WrathAdmin.TrySetZoneField(s, "spice", 0.5f, out _));
            Check("NaN refuses", !WrathAdmin.TrySetZoneField(s, "plague", float.NaN, out _));

            Check("invariant parse reads what the store writes",
                WrathAdmin.TryParseValue("0.5086", out float v) && Math.Abs(v - 0.5086f) < 1e-6f);
            Check("garbage is not a value", !WrathAdmin.TryParseValue("blight", out _));
            Check("NaN text is not a value", !WrathAdmin.TryParseValue("NaN", out _));
        }

        // ---- harness ----------------------------------------------------------------


        private static void Check(string what, bool ok)
        {
            if (ok) { _passed++; Console.WriteLine($"  PASS  {what}"); }
            else    { _failed++; Console.WriteLine($"  FAIL  {what}"); }
        }
    }
}
