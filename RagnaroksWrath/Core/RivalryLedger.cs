using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The influence ledger — task 13's spine. Per zone, per player, two columns: HARM
    /// (attributed damage to the land) and CARE (tending and healing presence). Every later
    /// phase — grudges, contests, the spawn war's sides — reads these rows; this file only
    /// stores them.
    ///
    /// Same disk contract as its siblings: world-scoped beside the zone store, atomic
    /// writes, fail-safe load, quarantine on corruption, no BOM, invariant culture, plain
    /// TSV an admin can hand-edit. Write-behind like HealthStore (dirty flag, cadence saves,
    /// and the WorldTick.OnDestroy flush the 0.8.2 rule demands of every store shaped like
    /// this).
    ///
    /// The TENDING WATERMARK is persisted in the file: the newest plantTime already
    /// credited. It is what makes planting book care exactly once across restarts — without
    /// it, every reboot would re-credit every standing field.
    /// </summary>
    public static class RivalryLedger
    {
        public struct Row
        {
            public float Harm;
            public float Care;
        }

        public struct Key : IEquatable<Key>
        {
            public readonly ZoneKey Zone;
            public readonly long Player;

            public Key(ZoneKey zone, long player) { Zone = zone; Player = player; }

            public bool Equals(Key other) => Zone == other.Zone && Player == other.Player;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode()
                => (Zone.GetHashCode() * 397) ^ Player.GetHashCode();
        }

        private const int FormatVersion = 1;
        private const string FileStem = "ragnarokswrath_rivalry";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Dictionary<Key, Row> _rows = new Dictionary<Key, Row>(32);

        private static bool _loaded;
        private static bool _dirty;
        private static long _plantWatermark;

        /// <summary>Test seam, like its siblings': when set, used instead of the world path.</summary>
        internal static string OverridePath;

        public static bool IsLoaded => _loaded;
        public static int Count => _rows.Count;

        /// <summary>Newest plantTime already credited for tending. Persisted.</summary>
        public static long PlantWatermark
        {
            get => _plantWatermark;
            set
            {
                if (value <= _plantWatermark) return;   // watermarks only rise
                _plantWatermark = value;
                _dirty = true;
            }
        }

        public static Row Get(ZoneKey zone, long playerId)
            => _rows.TryGetValue(new Key(zone, playerId), out Row r) ? r : default;

        public static IEnumerable<KeyValuePair<Key, Row>> All() => _rows;

        /// <summary>The worst grudge any zone holds against this player — the title layer's
        /// question. Rows are few by construction (sparse, decaying), so a scan is fine.</summary>
        public static float MaxGrudgeFor(long playerId, float scale)
        {
            float worst = 0f;
            foreach (KeyValuePair<Key, Row> kv in _rows)
            {
                if (kv.Key.Player != playerId) continue;
                float g = RivalryMath.GrudgeFor(kv.Value.Harm, kv.Value.Care, scale);
                if (g > worst) worst = g;
            }
            return worst;
        }

        public static void AddHarm(ZoneKey zone, long playerId, float amount)
            => Add(zone, playerId, amount, 0f);

        public static void AddCare(ZoneKey zone, long playerId, float amount)
            => Add(zone, playerId, 0f, amount);

        private static void Add(ZoneKey zone, long playerId, float harm, float care)
        {
            if (playerId == 0) return;   // the placeholder id — never record it
            if ((float.IsNaN(harm) || harm <= 0f) && (float.IsNaN(care) || care <= 0f)) return;

            var key = new Key(zone, playerId);
            _rows.TryGetValue(key, out Row row);
            if (!float.IsNaN(harm) && harm > 0f) row.Harm += harm;
            if (!float.IsNaN(care) && care > 0f) row.Care += care;
            _rows[key] = row;
            _dirty = true;
        }

        /// <summary>Fade every row by one factor and drop what has faded to nothing. The
        /// caller computes the factor from real elapsed time (RivalryMath.DecayFactor).</summary>
        public static void DecayAll(float factor)
        {
            if (float.IsNaN(factor) || factor >= 1f || factor < 0f) return;
            if (_rows.Count == 0) return;

            List<Key> dead = null;
            var keys = new List<Key>(_rows.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                Row row = _rows[keys[i]];
                row.Harm *= factor;
                row.Care *= factor;

                if (row.Harm < RivalryMath.PruneEpsilon && row.Care < RivalryMath.PruneEpsilon)
                    (dead = dead ?? new List<Key>(4)).Add(keys[i]);
                else
                    _rows[keys[i]] = row;
            }

            if (dead != null)
                for (int i = 0; i < dead.Count; i++)
                    _rows.Remove(dead[i]);

            _dirty = true;
        }

        public static void Load()
        {
            _rows.Clear();
            _loaded = false;
            _dirty = false;
            _plantWatermark = 0;

            string path = ResolvePath();
            if (path == null) return;

            if (!File.Exists(path)) { _loaded = true; return; }

            try
            {
                string[] lines = File.ReadAllLines(path);
                int good = 0, bad = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
                    if (line.StartsWith("version", StringComparison.Ordinal)) continue;

                    if (line.StartsWith("watermark", StringComparison.Ordinal))
                    {
                        string[] w = line.Split('\t');
                        if (w.Length >= 2 && long.TryParse(w[1], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out long mark) && mark >= 0)
                        { _plantWatermark = mark; good++; }
                        else bad++;
                        continue;
                    }

                    string[] p = line.Split('\t');
                    if (p.Length >= 5
                        && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zx)
                        && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zy)
                        && long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                        && id != 0
                        && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float harm)
                        && float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float care)
                        && !float.IsNaN(harm) && !float.IsNaN(care))
                    {
                        // Clamp on read: negatives cannot exist by construction, so a
                        // negative in the FILE is a hand-edit gone wrong, floored not trusted.
                        var row = new Row
                        {
                            Harm = harm < 0f ? 0f : harm,
                            Care = care < 0f ? 0f : care,
                        };
                        if (row.Harm >= RivalryMath.PruneEpsilon || row.Care >= RivalryMath.PruneEpsilon)
                            _rows[new Key(new ZoneKey(zx, zy), id)] = row;
                        good++;
                    }
                    else bad++;
                }

                _loaded = true;

                if (good == 0 && bad > 0)
                {
                    RagnaroksWrath.Log.LogError(
                        $"RivalryLedger: {Path.GetFileName(path)} is unreadable — all {bad} content " +
                        "line(s) failed to parse. Ledger reset; file kept for inspection.");
                    TryQuarantine(path);
                    _rows.Clear();
                    _plantWatermark = 0;
                    return;
                }

                if (_rows.Count > 0)
                    RagnaroksWrath.Log.LogInfo($"RivalryLedger: loaded {_rows.Count} row(s).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError(
                    $"RivalryLedger: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Ledger reset; file kept for inspection.");
                TryQuarantine(path);
                _rows.Clear();
                _plantWatermark = 0;
                _loaded = true;
            }
        }

        /// <summary>Write the ledger if anything changed. Cheap no-op otherwise.</summary>
        public static void SaveIfDirty()
        {
            if (!_loaded || !_dirty) return;

            string path = ResolvePath();
            if (path == null) return;

            string tmp = path + ".tmp";
            try
            {
                var c = CultureInfo.InvariantCulture;
                var sb = new StringBuilder(_rows.Count * 48 + 96);
                sb.Append("version\t").Append(FormatVersion).Append('\n');
                sb.Append("watermark\t").Append(_plantWatermark.ToString(c)).Append('\n');
                sb.Append("# zoneX\tzoneY\tplayerID\tharm\tcare\n");

                foreach (KeyValuePair<Key, Row> kv in _rows)
                {
                    if (kv.Value.Harm < RivalryMath.PruneEpsilon
                        && kv.Value.Care < RivalryMath.PruneEpsilon) continue;   // sparse at the boundary

                    sb.Append(kv.Key.Zone.X.ToString(c)).Append('\t')
                      .Append(kv.Key.Zone.Y.ToString(c)).Append('\t')
                      .Append(kv.Key.Player.ToString(c)).Append('\t')
                      .Append(kv.Value.Harm.ToString("0.####", c)).Append('\t')
                      .Append(kv.Value.Care.ToString("0.####", c)).Append('\n');
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError($"RivalryLedger: save failed ({ex.Message}). Ledger kept in memory.");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static string ResolvePath()
        {
            if (!string.IsNullOrEmpty(OverridePath)) return OverridePath;
            return Persistence.ResolveSiblingPath($"{FileStem}");
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
