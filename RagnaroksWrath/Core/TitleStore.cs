using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Earned titles, keyed by the PLAYER — which per docs/reference/PLAYER-IDENTITY-FACTS.md
    /// means `s_playerID`, the one long of the four look-alikes that survives across sessions
    /// and servers. Session ids and connection ids would fill this file with numbers that match
    /// nobody by next login.
    ///
    /// Same three properties as Persistence, same reasons, smaller stakes: world-scoped file
    /// (titles are world history), atomic writes, fail-safe load. Tab-separated text so an
    /// admin can grant or strip a title in an editor — the same hand-editability the zone
    /// store's plague test proved is a real write path, not a nicety.
    /// </summary>
    public static class TitleStore
    {
        private const int FormatVersion = 1;
        private const string FileStem = "ragnarokswrath_titles";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Dictionary<long, string> _titles = new Dictionary<long, string>(16);

        private static bool _loaded;

        /// <summary>Test seam, like Persistence's: when set, used instead of the world path.</summary>
        internal static string OverridePath;

        public static bool IsLoaded => _loaded;
        public static int Count => _titles.Count;

        public static string Get(long playerId)
            => _titles.TryGetValue(playerId, out string t) ? t : null;

        public static IEnumerable<KeyValuePair<long, string>> All() => _titles;

        /// <summary>Set (or clear with null/empty) a player's title. Saves immediately: titles
        /// change rarely and losing one to a crash reads as the mod breaking a promise.</summary>
        public static void Set(long playerId, string title)
        {
            if (playerId == 0) return;   // "Stranger" territory — never record the placeholder id

            if (string.IsNullOrEmpty(title)) _titles.Remove(playerId);
            else _titles[playerId] = title;

            Save();
        }

        public static void Load()
        {
            _titles.Clear();
            _loaded = false;

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
                        && !string.IsNullOrWhiteSpace(p[1]))
                    {
                        _titles[id] = p[1].Trim();
                        good++;
                    }
                    else bad++;
                }

                _loaded = true;

                if (good == 0 && bad > 0)
                {
                    // Same rule as the zone store: content that parsed as nothing is a corrupt
                    // file, not an empty one. Quarantine the evidence, start clean.
                    RagnaroksWrath.Log.LogError(
                        $"TitleStore: {Path.GetFileName(path)} is unreadable — all {bad} content " +
                        "line(s) failed to parse. Titles reset; file kept for inspection.");
                    TryQuarantine(path);
                    _titles.Clear();
                    return;
                }

                if (_titles.Count > 0)
                    RagnaroksWrath.Log.LogInfo($"TitleStore: loaded {_titles.Count} title(s).");
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError(
                    $"TitleStore: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Titles reset; file kept for inspection.");
                TryQuarantine(path);
                _titles.Clear();
                _loaded = true;
            }
        }

        private static void Save()
        {
            if (!_loaded) return;

            string path = ResolvePath();
            if (path == null) return;

            string tmp = path + ".tmp";
            try
            {
                var sb = new StringBuilder(_titles.Count * 32 + 64);
                sb.Append("version\t").Append(FormatVersion).Append('\n');
                sb.Append("# playerID\ttitle\n");
                foreach (KeyValuePair<long, string> kv in _titles)
                    sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value).Append('\n');

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                RagnaroksWrath.Log.LogError($"TitleStore: save failed ({ex.Message}). Titles kept in memory.");
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
