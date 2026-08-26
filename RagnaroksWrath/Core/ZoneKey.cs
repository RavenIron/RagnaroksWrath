using System;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// A zone coordinate, used as the key for all per-zone drift state.
    ///
    /// Deliberately our own struct rather than Vector2i: this value gets serialised and must
    /// stay stable across game updates, and it needs a dependable GetHashCode for use as a
    /// Dictionary key across tens of thousands of entries.
    /// </summary>
    [Serializable]
    public readonly struct ZoneKey : IEquatable<ZoneKey>
    {
        public readonly int X;
        public readonly int Y;

        public ZoneKey(int x, int y)
        {
            X = x;
            Y = y;
        }

        public ZoneKey(Vector2i v)
        {
            X = v.x;
            Y = v.y;
        }

        public Vector2i ToVector2i() => new Vector2i(X, Y);

        /// <summary>Zone containing a world position.</summary>
        public static ZoneKey FromWorldPos(Vector3 pos) => new ZoneKey(ZoneSystem.GetZone(pos));

        /// <summary>
        /// Centre of this zone in world space. Note this is the zone centre, not ground level —
        /// use WorldGenerator.instance.GetHeight() for terrain, which works on unloaded zones
        /// and does not force generation.
        /// </summary>
        public Vector3 ToWorldPos() => ZoneSystem.GetZonePos(ToVector2i());

        public bool Equals(ZoneKey other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is ZoneKey other && Equals(other);

        public override int GetHashCode()
        {
            // Cantor-ish pack. Zone coords are small (roughly ±160 for a full world),
            // so a simple shift-combine has no realistic collision pressure.
            unchecked { return (X * 397) ^ Y; }
        }

        public override string ToString() => $"({X},{Y})";

        /// <summary>Round-trips through ToString via Parse for the persistence layer.</summary>
        public static bool TryParse(string s, out ZoneKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(s)) return false;

            s = s.Trim('(', ')');
            int comma = s.IndexOf(',');
            if (comma <= 0) return false;

            if (!int.TryParse(s.Substring(0, comma), out int x)) return false;
            if (!int.TryParse(s.Substring(comma + 1), out int y)) return false;

            key = new ZoneKey(x, y);
            return true;
        }

        public static bool operator ==(ZoneKey a, ZoneKey b) => a.Equals(b);
        public static bool operator !=(ZoneKey a, ZoneKey b) => !a.Equals(b);
    }
}
