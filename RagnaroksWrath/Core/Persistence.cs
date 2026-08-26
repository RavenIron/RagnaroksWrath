using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The sparse per-zone drift store.
    ///
    /// THREE PROPERTIES, each chosen against a specific failure:
    ///
    /// 1. WORLD-SCOPED. One file per world, keyed by the world's uid. A single shared file
    ///    would leak drift between worlds, so a test world would poison a real one and a
    ///    fresh world would inherit someone else's burnt forests.
    ///
    /// 2. ATOMIC WRITES. Write to .tmp, then replace. A crash mid-write otherwise corrupts
    ///    the only copy that exists. One previous good file is kept as .bak.
    ///
    /// 3. FAIL-SAFE. A missing or corrupt file means "no drift anywhere", logged loudly —
    ///    never an exception. A world that loads pristine is recoverable; a world that
    ///    refuses to load is not. Parsing is per-line for the same reason: one bad line is
    ///    skipped and reported, it does not discard the other nine thousand.
    ///
    /// AwayFromHome deliberately avoided a registry file ("nothing to corrupt, no list to fall
    /// out of step with reality") because it could derive its state live from ZDOMan. We cannot:
    /// zone drift is invented state with no world object backing it, so it must be stored. These
    /// three properties are the price of that.
    /// </summary>
    public static class Persistence
    {
        private const int FormatVersion = 1;
        private const string FileStem = "ragnarokswrath_zones";

        /// <summary>
        /// UTF-8 with NO byte-order mark.
        ///
        /// Encoding.UTF8 emits one, and it lands in front of the version header. Load() survives
        /// it — File.ReadAllLines detects and consumes a BOM — so this is not a correctness fix
        /// for our own reader, and the test proves it: the legacy-BOM store still loads.
        ///
        /// It is a FORMAT fix. This file is deliberately plain tab-separated text so it can be
        /// read, diffed and hand-repaired, and three invisible bytes in front of the header defeat
        /// exactly that: `grep '^version'` misses, a diff shows a phantom change, and any reader
        /// less forgiving than ReadAllLines gets a header it cannot match. The store should
        /// contain what it says it contains.
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Dictionary<ZoneKey, ZoneState> _zones =
            new Dictionary<ZoneKey, ZoneState>(256);

        private static bool _dirty;
        private static bool _loaded;
        private static bool _warnedNoSavePath;

        /// <summary>Test seam. When set, used instead of the world save directory.</summary>
        internal static string OverrideDirectory;

        /// <summary>Test seam. When set, used instead of the live world uid.</summary>
        internal static ulong? OverrideWorldUid;

        // ---- public state access --------------------------------------------------------

        public static int TrackedZoneCount => _zones.Count;

        public static bool IsLoaded => _loaded;

        /// <summary>
        /// Read a zone's state. Returns the pristine default for zones with no history, which is
        /// what makes "most of the world" cost nothing to represent.
        /// </summary>
        public static ZoneState Get(ZoneKey zone)
            => _zones.TryGetValue(zone, out ZoneState s) ? s : default;

        /// <summary>
        /// Write a zone's state. Clamps on the way in, and REMOVES the entry entirely if the
        /// state has returned to default — a zone that heals must stop costing disk space, or
        /// the file only ever grows.
        /// </summary>
        public static void Set(ZoneKey zone, ZoneState state)
        {
            state.Clamp();

            if (state.IsDefault)
            {
                if (_zones.Remove(zone)) _dirty = true;
                return;
            }

            _zones[zone] = state;
            _dirty = true;
        }

        public static IEnumerable<KeyValuePair<ZoneKey, ZoneState>> All() => _zones;

        public static void Clear()
        {
            _zones.Clear();
            _dirty = true;
        }

        // ---- paths ----------------------------------------------------------------------

        /// <summary>
        /// The host's live World, or null. Only the host persists: clients receive state, they
        /// do not own it.
        /// </summary>
        private static World ResolveHostWorld()
        {
            try
            {
                return ZNet.GetWorldIfIsHost();
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"Persistence: could not resolve host world: {ex.Message}");
                return null;
            }
        }

        private static ulong ResolveWorldUid(World world)
        {
            try
            {
                // Rule 5: reach game internals through cached reflection, never directly.
                // Publicized assemblies are compile-time only.
                //
                // m_uid is declared `long` on World, and FieldRefAccess is TYPE-EXACT: asking for
                // ulong throws rather than converting. That threw on every load — one warning, a
                // uid of 0, and a store that then silently never wrote anything.
                return (ulong)AccessTools.FieldRefAccess<World, long>(world, "m_uid");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"Persistence: could not resolve world uid: {ex.Message}");
                return 0UL;
            }
        }

        private static string ResolveDirectory()
        {
            if (!string.IsNullOrEmpty(OverrideDirectory)) return OverrideDirectory;

            try
            {
                // Explicitly LOCAL — never Auto, and never the world's own FileSource.
                //
                // Utils.GetSaveDataPath returns "" for Auto and Cloud whenever Steam Cloud is
                // enabled, because a cloud save is addressed by a RELATIVE path through Steam's
                // cloud API rather than by a filesystem path. Concatenated, that produced
                // "\worlds\..." — a perfectly correct cloud path, and the root of the current
                // drive to File.WriteAllText. It looked like a path, which is why it survived
                // two rounds of "fixing" the wrong end.
                //
                // Deliberate consequence: for a cloud-saved world the drift store stays on this
                // machine and does not travel with the save. Writing through the cloud API is a
                // far larger change, and a store that writes nowhere is worse than a local one.
                return World.GetWorldSavePath(FileHelpers.FileSource.Local);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogWarning($"Persistence: could not resolve save path: {ex.Message}");
                return null;
            }
        }

        private static string ResolvePath() => ResolvePath(out _);

        /// <summary>
        /// Resolve the store path, reporting WHICH part failed.
        ///
        /// The failures are indistinguishable from the outside and mean opposite things: no host
        /// world is a client that legitimately does not persist, while a uid of 0 is the host
        /// failing to read `m_uid` through reflection — after which every save is a silent no-op
        /// forever. Collapsing them into one message is exactly the confident, well-formed, wrong
        /// measurement the debugging discipline warns about.
        /// </summary>
        private static string ResolvePath(out string detail)
        {
            // The two test seams are independent: a test may want a real directory with a
            // synthetic uid. Only the uid needs the live world; the directory is world-agnostic.
            string dir = ResolveDirectory();
            ulong uid = OverrideWorldUid ?? 0UL;

            if (!OverrideWorldUid.HasValue)
            {
                World world = ResolveHostWorld();
                if (world == null)
                {
                    detail = "no host world (client, or world not loaded yet)";
                    return null;
                }

                uid = ResolveWorldUid(world);
            }

            if (string.IsNullOrEmpty(dir))
            {
                detail = "world save directory came back empty";
                return null;
            }

            if (uid == 0UL)
            {
                detail = "world uid resolved as 0 — the m_uid field access failed";
                return null;
            }

            detail = $"uid {uid}, dir {dir}";
            return Path.Combine(dir, $"{FileStem}_{uid}.dat");
        }

        // ---- load -----------------------------------------------------------------------

        /// <summary>
        /// Load state for the current world. Safe to call repeatedly; only the first call for a
        /// given world does work.
        ///
        /// Never throws. Every failure path ends with an empty, usable store.
        /// </summary>
        public static void Load()
        {
            _zones.Clear();
            _dirty = false;
            _loaded = false;
            _warnedNoSavePath = false;

            string path = ResolvePath(out string detail);
            if (path == null)
            {
                RagnaroksWrath.Log.LogInfo(
                    $"Persistence: no world path available ({detail}) — starting with no stored drift.");
                return;
            }

            // Proof-of-life for the one value that fails silently and permanently. A uid of 0
            // makes every later save a no-op with no error, so name it once per load rather than
            // inferring it from a file that may never appear.
            RagnaroksWrath.Log.LogInfo($"Persistence: resolved store — {detail}.");

            if (!File.Exists(path))
            {
                RagnaroksWrath.Log.LogInfo($"Persistence: no existing store at {Path.GetFileName(path)} — fresh world.");
                _loaded = true;
                return;
            }

            int good = 0, bad = 0;

            try
            {
                string[] lines = File.ReadAllLines(path);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                    if (line.StartsWith("version", StringComparison.Ordinal))
                    {
                        // Version line. Unknown future versions are not fatal: read what parses.
                        // Recognised by content rather than by position: skipping line 1 whatever
                        // it holds would swallow the first line of a wholly corrupt file, and on a
                        // short binary file that is the only line there is to notice.
                        continue;
                    }

                    if (TryParseLine(line, out ZoneKey key, out ZoneState state, out long contactTicks))
                    {
                        _zones[key] = state;
                        if (contactTicks > 0) ZoneClock.Restore(key, contactTicks);
                        good++;
                    }
                    else
                    {
                        bad++;
                        // Per-line isolation: report the first few, do not discard the file.
                        if (bad <= 3)
                            RagnaroksWrath.Log.LogWarning($"Persistence: unreadable line {i + 1}, skipped.");
                    }
                }

                _loaded = true;

                // File.ReadAllLines does not throw on binary garbage — it hands back junk strings
                // that simply fail to parse — so a wholly destroyed file otherwise reaches here
                // looking exactly like a fresh world, and the next autosave overwrites it. bad > 0
                // is what separates the two: a file of only a version header and comments parses
                // nothing because there is nothing to parse, and is legitimately empty.
                if (good == 0 && bad > 0)
                {
                    RagnaroksWrath.Log.LogError(
                        $"Persistence: {Path.GetFileName(path)} is unreadable — all {bad} content " +
                        "line(s) failed to parse. Stored drift has been reset to none. The file " +
                        "has been kept for inspection.");

                    TryQuarantine(path);
                    _zones.Clear();
                    return;
                }

                if (bad > 0)
                    RagnaroksWrath.Log.LogWarning(
                        $"Persistence: loaded {good} zone(s), skipped {bad} unreadable line(s) from " +
                        $"{Path.GetFileName(path)}. Those zones have reset to default.");
                else
                    RagnaroksWrath.Log.LogInfo($"Persistence: loaded {good} zone(s).");
            }
            catch (Exception ex)
            {
                // Corrupt or unreadable file. Keep the world playable and preserve the evidence.
                RagnaroksWrath.Log.LogError(
                    $"Persistence: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Continuing with no stored drift. The file has been kept for inspection.");

                TryQuarantine(path);
                _zones.Clear();
                _loaded = true;
            }
        }

        private static void TryQuarantine(string path)
        {
            try
            {
                string dead = path + ".corrupt";
                if (File.Exists(dead)) File.Delete(dead);
                File.Move(path, dead);
            }
            catch
            {
                // Nothing useful to do; the load already degraded safely.
            }
        }

        // ---- save -----------------------------------------------------------------------

        /// <summary>
        /// Write state to disk if anything changed. Never throws.
        /// </summary>
        /// <param name="force">Write even if nothing is marked dirty.</param>
        public static void Save(bool force = false)
        {
            if (!_loaded) return;
            if (!_dirty && !force) return;

            string path = ResolvePath(out string detail);
            if (path == null)
            {
                // Load already resolved a path or we would not be _loaded, so losing it here is
                // an anomaly, not the ordinary client case. Warn once: repeating it every
                // autosave would bury the world it happened in.
                if (!_warnedNoSavePath)
                {
                    _warnedNoSavePath = true;
                    RagnaroksWrath.Log.LogWarning(
                        $"Persistence: cannot save, no world path ({detail}). State kept in memory.");
                }
                return;
            }

            string tmp = path + ".tmp";
            string bak = path + ".bak";

            try
            {
                var sb = new StringBuilder(_zones.Count * 48 + 128);
                sb.Append("version\t").Append(FormatVersion).Append('\n');
                sb.Append("# zoneX\tzoneY\tcontactTicks\tfert\tcorr\tscorch\tfrost\tplague\n");

                foreach (KeyValuePair<ZoneKey, ZoneState> kv in _zones)
                {
                    if (kv.Value.IsDefault) continue;   // sparseness, enforced at the boundary

                    long ticks = 0;
                    foreach (KeyValuePair<ZoneKey, long> c in ZoneClock.Snapshot())
                    {
                        if (c.Key == kv.Key) { ticks = c.Value; break; }
                    }

                    AppendLine(sb, kv.Key, kv.Value, ticks);
                }

                // Vanilla does the same before writing a world. worlds_local need not exist yet on
                // a machine that has only ever used cloud saves.
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);

                // Atomic-ish replace: the old file survives until the new one is fully written.
                if (File.Exists(path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                File.Move(tmp, path);

                _dirty = false;

                if (Config.ModConfig.VerboseLogging.Value)
                    RagnaroksWrath.Log.LogInfo($"Persistence: saved {_zones.Count} zone(s).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError($"Persistence: save failed ({ex.Message}). State kept in memory.");
                // _dirty stays true so the next autosave retries.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // ---- line format ----------------------------------------------------------------
        // Tab-separated text, one zone per line. Deliberately not binary: it can be read,
        // diffed and hand-repaired, and a damaged line is visibly one zone rather than an
        // unrecoverable stream.

        private static void AppendLine(StringBuilder sb, ZoneKey key, ZoneState s, long contactTicks)
        {
            var c = CultureInfo.InvariantCulture;   // never let a comma-decimal locale write the file
            sb.Append(key.X.ToString(c)).Append('\t')
              .Append(key.Y.ToString(c)).Append('\t')
              .Append(contactTicks.ToString(c)).Append('\t')
              .Append(s.Fertility.ToString("R", c)).Append('\t')
              .Append(s.Corruption.ToString("R", c)).Append('\t')
              .Append(s.Scorch.ToString("R", c)).Append('\t')
              .Append(s.Frost.ToString("R", c)).Append('\t')
              .Append(s.Plague.ToString("R", c)).Append('\n');
        }

        internal static bool TryParseLine(string line, out ZoneKey key, out ZoneState state,
                                          out long contactTicks)
        {
            key = default;
            state = default;
            contactTicks = 0;

            string[] p = line.Split('\t');
            if (p.Length < 8) return false;

            var c = CultureInfo.InvariantCulture;

            if (!int.TryParse(p[0], NumberStyles.Integer, c, out int x)) return false;
            if (!int.TryParse(p[1], NumberStyles.Integer, c, out int y)) return false;
            if (!long.TryParse(p[2], NumberStyles.Integer, c, out contactTicks)) contactTicks = 0;

            if (!float.TryParse(p[3], NumberStyles.Float, c, out float fert))   return false;
            if (!float.TryParse(p[4], NumberStyles.Float, c, out float corr))   return false;
            if (!float.TryParse(p[5], NumberStyles.Float, c, out float scorch)) return false;
            if (!float.TryParse(p[6], NumberStyles.Float, c, out float frost))  return false;
            if (!float.TryParse(p[7], NumberStyles.Float, c, out float plague)) return false;

            key = new ZoneKey(x, y);
            state = new ZoneState
            {
                Fertility = fert,
                Corruption = corr,
                Scorch = scorch,
                Frost = frost,
                Plague = plague
            };
            state.Clamp();   // clamp on READ, not just on write

            return true;
        }
    }
}
