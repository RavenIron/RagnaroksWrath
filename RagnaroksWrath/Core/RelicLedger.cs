using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The relic ledger — task 14's memory, the FOURTH write-behind store. Four row kinds
    /// share one tagged TSV file: peak watermarks (P), standing relics (R), consecrations
    /// queued for the next contact (Q), and the era snapshot taken when the world went
    /// Stricken (E). The zone store stays lean; everything the stones need lives here.
    ///
    /// Same disk contract as its siblings: world-scoped beside the zone store, atomic
    /// writes, fail-safe load, quarantine on corruption, no BOM, invariant culture, plain
    /// TSV an admin can hand-edit. Write-behind (dirty flag, cadence saves, and the
    /// WorldTick.OnDestroy flush the 0.8.2 rule demands).
    /// </summary>
    public static class RelicLedger
    {
        public struct Peaks
        {
            public float Scorch;
            public float Plague;
            public bool Empty => Scorch <= 0f && Plague <= 0f;
        }

        public struct Relic
        {
            public int Type;      // RelicMath.None when no stone stands
            public bool Cursed;
            public int Day;       // world day at consecration; 0 when unknown
            public bool Standing => Type != RelicMath.None;
        }

        private const int FormatVersion = 1;
        private const string FileStem = "ragnarokswrath_relics";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Dictionary<ZoneKey, Peaks> _peaks = new Dictionary<ZoneKey, Peaks>(16);
        private static readonly Dictionary<ZoneKey, Relic> _relics = new Dictionary<ZoneKey, Relic>(8);
        private static readonly Dictionary<ZoneKey, Relic> _pending = new Dictionary<ZoneKey, Relic>(4);
        private static readonly Dictionary<ZoneKey, float> _era = new Dictionary<ZoneKey, float>(16);

        private static bool _loaded;
        private static bool _dirty;

        /// <summary>Test seam, like its siblings': when set, used instead of the world path.</summary>
        internal static string OverridePath;

        public static bool IsLoaded => _loaded;
        public static int RelicCount => _relics.Count;
        public static int PendingCount => _pending.Count;
        public static bool EraArmed => _era.Count > 0;

        public static Peaks PeaksFor(ZoneKey zone)
            => _peaks.TryGetValue(zone, out Peaks p) ? p : default;

        public static IEnumerable<KeyValuePair<ZoneKey, Peaks>> AllPeaks() => _peaks;

        public static void SetPeaks(ZoneKey zone, Peaks peaks)
        {
            if (peaks.Empty) { if (_peaks.Remove(zone)) _dirty = true; return; }
            _peaks[zone] = peaks;
            _dirty = true;
        }

        public static Relic RelicAt(ZoneKey zone)
            => _relics.TryGetValue(zone, out Relic r) ? r : new Relic { Type = RelicMath.None };

        public static void SetRelic(ZoneKey zone, Relic relic)
        {
            if (!relic.Standing) { RemoveRelic(zone); return; }
            _relics[zone] = relic;
            _peaks.Remove(zone);     // a standing stone ends peak tracking; desecration re-arms clean
            _pending.Remove(zone);
            _dirty = true;
        }

        public static void RemoveRelic(ZoneKey zone)
        {
            if (_relics.Remove(zone)) _dirty = true;
        }

        public static IEnumerable<KeyValuePair<ZoneKey, Relic>> AllRelics() => _relics;

        public static void AddPending(ZoneKey zone, Relic relic)
        {
            if (!relic.Standing) return;
            if (_relics.ContainsKey(zone)) return;   // a standing stone already told this story
            _pending[zone] = relic;
            _dirty = true;
        }

        public static void RemovePending(ZoneKey zone)
        {
            if (_pending.Remove(zone)) _dirty = true;
        }

        public static IEnumerable<KeyValuePair<ZoneKey, Relic>> AllPending() => _pending;

        public static void SetEraSnapshot(ZoneKey zone, float damage)
        {
            if (float.IsNaN(damage) || damage <= 0f) return;
            _era[zone] = damage;
            _dirty = true;
        }

        public static void ClearEra()
        {
            if (_era.Count == 0) return;
            _era.Clear();
            _dirty = true;
        }

        public static IEnumerable<KeyValuePair<ZoneKey, float>> EraSnapshot() => _era;

        public static void Load()
        {
            _peaks.Clear(); _relics.Clear(); _pending.Clear(); _era.Clear();
            _loaded = false;
            _dirty = false;

            string path = ResolvePath();
            if (path == null) return;

            if (!File.Exists(path)) { _loaded = true; return; }

            try
            {
                string[] lines = File.ReadAllLines(path);
                int good = 0, bad = 0;
                var c = CultureInfo.InvariantCulture;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
                    if (line.StartsWith("version", StringComparison.Ordinal)) continue;

                    string[] p = line.Split('\t');
                    if (p.Length >= 3
                        && p[0].Length == 1
                        && int.TryParse(p[1], NumberStyles.Integer, c, out int zx)
                        && int.TryParse(p[2], NumberStyles.Integer, c, out int zy))
                    {
                        var zone = new ZoneKey(zx, zy);
                        switch (p[0][0])
                        {
                            case 'P':
                                if (p.Length >= 5
                                    && float.TryParse(p[3], NumberStyles.Float, c, out float ps)
                                    && float.TryParse(p[4], NumberStyles.Float, c, out float pp)
                                    && !float.IsNaN(ps) && !float.IsNaN(pp))
                                {
                                    var peaks = new Peaks
                                    {
                                        Scorch = ps < 0f ? 0f : ps,
                                        Plague = pp < 0f ? 0f : pp,
                                    };
                                    if (!peaks.Empty) _peaks[zone] = peaks;
                                    good++;
                                }
                                else bad++;
                                continue;
                            case 'R':
                            case 'Q':
                                if (p.Length >= 6
                                    && int.TryParse(p[3], NumberStyles.Integer, c, out int type)
                                    && int.TryParse(p[4], NumberStyles.Integer, c, out int cursed)
                                    && int.TryParse(p[5], NumberStyles.Integer, c, out int day)
                                    && type >= RelicMath.Fire && type <= RelicMath.Era)
                                {
                                    var relic = new Relic
                                    {
                                        Type = type,
                                        Cursed = cursed != 0,
                                        Day = day < 0 ? 0 : day,
                                    };
                                    if (p[0][0] == 'R') _relics[zone] = relic;
                                    else _pending[zone] = relic;
                                    good++;
                                }
                                else bad++;
                                continue;
                            case 'E':
                                if (p.Length >= 4
                                    && float.TryParse(p[3], NumberStyles.Float, c, out float dmg)
                                    && !float.IsNaN(dmg) && dmg > 0f)
                                {
                                    _era[zone] = dmg;
                                    good++;
                                }
                                else bad++;
                                continue;
                            default:
                                bad++;
                                continue;
                        }
                    }
                    else bad++;
                }

                _loaded = true;

                if (good == 0 && bad > 0)
                {
                    RagnaroksWrath.Log.LogError(
                        $"RelicLedger: {Path.GetFileName(path)} is unreadable — all {bad} content " +
                        "line(s) failed to parse. Ledger reset; file kept for inspection.");
                    TryQuarantine(path);
                    _peaks.Clear(); _relics.Clear(); _pending.Clear(); _era.Clear();
                    return;
                }

                if (_relics.Count > 0 || _peaks.Count > 0)
                    RagnaroksWrath.Log.LogInfo(
                        $"RelicLedger: loaded {_relics.Count} relic(s), {_peaks.Count} peak row(s), " +
                        $"{_pending.Count} pending, era {(EraArmed ? "armed" : "clear")}.");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError(
                    $"RelicLedger: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Ledger reset; file kept for inspection.");
                TryQuarantine(path);
                _peaks.Clear(); _relics.Clear(); _pending.Clear(); _era.Clear();
                _loaded = true;
            }
        }

        public static void SaveIfDirty()
        {
            if (!_loaded || !_dirty) return;

            string path = ResolvePath();
            if (path == null) return;

            string tmp = path + ".tmp";
            try
            {
                var c = CultureInfo.InvariantCulture;
                var sb = new StringBuilder(256);
                sb.Append("version\t").Append(FormatVersion).Append('\n');
                sb.Append("# P zx zy peakScorch peakPlague | R/Q zx zy type cursed day | E zx zy damage\n");

                foreach (KeyValuePair<ZoneKey, Peaks> kv in _peaks)
                    sb.Append("P\t").Append(kv.Key.X.ToString(c)).Append('\t').Append(kv.Key.Y.ToString(c))
                      .Append('\t').Append(kv.Value.Scorch.ToString("0.####", c))
                      .Append('\t').Append(kv.Value.Plague.ToString("0.####", c)).Append('\n');

                foreach (KeyValuePair<ZoneKey, Relic> kv in _relics)
                    AppendRelic(sb, 'R', kv.Key, kv.Value, c);

                foreach (KeyValuePair<ZoneKey, Relic> kv in _pending)
                    AppendRelic(sb, 'Q', kv.Key, kv.Value, c);

                foreach (KeyValuePair<ZoneKey, float> kv in _era)
                    sb.Append("E\t").Append(kv.Key.X.ToString(c)).Append('\t').Append(kv.Key.Y.ToString(c))
                      .Append('\t').Append(kv.Value.ToString("0.####", c)).Append('\n');

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError($"RelicLedger: save failed ({ex.Message}). Ledger kept in memory.");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static void AppendRelic(StringBuilder sb, char tag, ZoneKey zone, Relic r, CultureInfo c)
        {
            sb.Append(tag).Append('\t').Append(zone.X.ToString(c)).Append('\t').Append(zone.Y.ToString(c))
              .Append('\t').Append(r.Type.ToString(c))
              .Append('\t').Append(r.Cursed ? '1' : '0')
              .Append('\t').Append(r.Day.ToString(c)).Append('\n');
        }

        private static string ResolvePath()
        {
            if (!string.IsNullOrEmpty(OverridePath)) return OverridePath;
            return Persistence.ResolveSiblingPath(FileStem);
        }

        private static void TryQuarantine(string path)
        {
            try
            {
                string dead = path + ".corrupt";
                if (File.Exists(dead)) File.Delete(dead);
                File.Move(path, dead);
            }
            catch { }
        }
    }
}
