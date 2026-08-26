// Hand-written stand-ins for the handful of game / BepInEx types the tested source
// mentions in its signatures. Deliberately minimal: the stub surface is almost always
// smaller than it looks. Nothing here needs to behave like Valheim — it only needs to
// compile and let the real logic run.

using System;
using System.Collections.Generic;
using UnityEngine;

// ---- UnityEngine ------------------------------------------------------------------
// Must live in the real namespace: the shipping source has `using UnityEngine;`, and the
// point of this harness is to compile that source unmodified.

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public override string ToString() => $"({x},{y},{z})";
    }
}

// ---- Valheim ----------------------------------------------------------------------
// These are global-namespace types in the real game, so the stubs are too.

public struct Vector2i
{
    public int x, y;
    public Vector2i(int x, int y) { this.x = x; this.y = y; }
    public override string ToString() => $"({x},{y})";
}

public static class ZoneSystem
{
    public const float ZoneSize = 64f;

    public static Vector2i GetZone(Vector3 point)
        => new Vector2i(
            (int)Math.Floor((point.x + ZoneSize / 2f) / ZoneSize),
            (int)Math.Floor((point.z + ZoneSize / 2f) / ZoneSize));

    public static Vector3 GetZonePos(Vector2i id)
        => new Vector3(id.x * ZoneSize, 0f, id.y * ZoneSize);
}

// Mirrors assembly_utils. Persistence passes Local explicitly: Auto/Cloud resolve to a
// RELATIVE cloud path, which is not a filesystem location.
public static class FileHelpers
{
    public enum FileSource { Auto = 0, Local = 1, Cloud = 2, Legacy = 3 }
}

public class World
{
    // long, not ulong — matches the real assembly. Persistence casts on the way out.
    public long m_uid;

    // Tests always set Persistence.OverrideDirectory, so this is never the path taken.
    public static string GetWorldSavePath(FileHelpers.FileSource fileSource) => System.IO.Path.GetTempPath();
}

// ZNet is an instance type in the game with a static `instance`. Persistence uses the static
// helper; BiomeStateSystem goes through `instance`, which stays null here so the live
// contact path is never taken and the drift math is tested directly instead.
public class ZNet
{
    public static ZNet instance => null;

    // Null here means "not the host". Tests override the uid, so this stays null.
    public static World GetWorldIfIsHost() => null;

    public List<ZDO> GetAllCharacterZDOS() => new List<ZDO>();
}

public class ZDO
{
    public Vector3 Position;

    public bool IsValid() => true;
    public Vector3 GetPosition() => Position;
}

// ---- HarmonyLib -------------------------------------------------------------------
// Only the members the tested source mentions. Tests never take the reflection path
// (Persistence.OverrideWorldUid short-circuits it), but it still has to compile.

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static TField FieldRefAccess<TObject, TField>(TObject instance, string fieldName)
            => default;
    }
}

// ---- the plugin's logger ----------------------------------------------------------
// The real RagnaroksWrath class derives from BepInEx's BaseUnityPlugin, which cannot run
// off-game. This stand-in supplies only the static Log surface the tested files use, and
// routes it to the console so a failing test shows why.

namespace RavenIron.RagnaroksWrath
{
    public class TestLog
    {
        public void LogInfo(object o)    => Console.WriteLine($"      [info]  {o}");
        public void LogWarning(object o) => Console.WriteLine($"      [warn]  {o}");
        public void LogError(object o)   => Console.WriteLine($"      [error] {o}");
    }

    public static class RagnaroksWrath
    {
        public static readonly TestLog Log = new TestLog();
    }
}

// ---- BepInEx.Configuration --------------------------------------------------------

namespace BepInEx.Configuration
{
    public class AcceptableValueRange<T>
    {
        public readonly T MinValue, MaxValue;
        public AcceptableValueRange(T min, T max) { MinValue = min; MaxValue = max; }
    }

    public class ConfigDescription
    {
        public readonly string Description;
        public readonly object AcceptableValues;

        public ConfigDescription(string description, object acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }
    }

    public class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigEntry(T defaultValue) { Value = defaultValue; }
    }

    public class ConfigFile
    {
        private readonly List<string> _bound = new List<string>();

        public int BoundCount => _bound.Count;

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue,
                                      ConfigDescription description = null)
        {
            _bound.Add($"{section}/{key}");
            return new ConfigEntry<T>(defaultValue);
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            _bound.Add($"{section}/{key}");
            return new ConfigEntry<T>(defaultValue);
        }
    }
}
