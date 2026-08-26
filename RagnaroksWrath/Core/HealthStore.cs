using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Per-player plague exposure, keyed by `s_playerID` like TitleStore — RELOGGING IS NOT
    /// A CURE is this file's whole reason to exist. Same contract as its siblings: world-
    /// scoped, atomic writes, fail-safe load, quarantine on corruption, no BOM, invariant
    /// culture, tab-separated and hand-editable.
    ///
    /// Unlike titles, exposure changes every few seconds while anyone stands in blight, so
    /// Set does NOT save — it marks dirty, and HealthSystem calls SaveIfDirty on a slow
    /// cadence. A crash can lose up to that cadence of exposure drift, which is a shrug in
    /// both directions (under-sick or over-sick by a minute), unlike losing a title.
    /// </summary>
    public static class HealthStore
    {
        private const int FormatVersion = 1;
        private const string FileStem = "ragnarokswrath_health";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Dictionary<long, float> _exposure = new Dictionary<long, float>(16);

        private static bool _loaded;
        private static bool _dirty;

        /// <summary>Test seam, like TitleStore's: when set, used instead of the world path.</summary>
        internal static string OverridePath;

        public static bool IsLoaded => _loaded;
        public static int Count => _exposure.Count;

        public static float Get(long playerId)
            => _exposure.TryGetValue(playerId, out float e) ? e : 0f;

        /// <summary>Set (or clear with 0) a player's exposure. Marks dirty; does not save.</summary>
        public static void Set(long playerId, float exposure)
        {
            if (playerId == 0) return;   // the placeholder id — never record it

            if (float.IsNaN(exposure) || exposure <= 0f)
            {
                if (_exposure.Remove(playerId)) _dirty = true;
                return;
            }

            _exposure[playerId] = exposure > 1f ? 1f : exposure;
            _dirty = true;
        }

        public static void Load()
        {
            _exposure.Clear();
            _loaded = false;
            _dirty = false;

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

                    string[] p = line.Split('\t');
                    if (p.Length >= 2
                        && long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                        && id != 0
                        && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float e)
                        && !float.IsNaN(e))
                    {
                        if (e > 0f) _exposure[id] = e > 1f ? 1f : e;   // clamp on read; zero rows stay sparse
                        good++;
                    }
                    else bad++;
                }

                _loaded = true;

                if (good == 0 && bad > 0)
                {
                    RagnaroksWrath.Log.LogError(
                        $"HealthStore: {Path.GetFileName(path)} is unreadable — all {bad} content " +
                        "line(s) failed to parse. Exposure reset; file kept for inspection.");
                    TryQuarantine(path);
                    _exposure.Clear();
                    return;
                }

                if (_exposure.Count > 0)
                    RagnaroksWrath.Log.LogInfo($"HealthStore: loaded exposure for {_exposure.Count} player(s).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError(
                    $"HealthStore: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Exposure reset; file kept for inspection.");
                TryQuarantine(path);
                _exposure.Clear();
                _loaded = true;
            }
        }

        /// <summary>Write the ledger if anything changed since the last write. Cheap no-op otherwise.</summary>
        public static void SaveIfDirty()
        {
            if (!_loaded || !_dirty) return;

            string path = ResolvePath();
            if (path == null) return;

            string tmp = path + ".tmp";
            try
            {
                var sb = new StringBuilder(_exposure.Count * 32 + 64);
                sb.Append("version\t").Append(FormatVersion).Append('\n');
                sb.Append("# playerID\texposure\n");
                foreach (KeyValuePair<long, float> kv in _exposure)
                    sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError($"HealthStore: save failed ({ex.Message}). Exposure kept in memory.");
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
