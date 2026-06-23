using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Identifies a single Distant-Horizon LOD tile: its level (stride 2^Level) and
  /// its coordinate within that level's tile grid. Used as a dictionary key by the
  /// LOD renderer/streamer, so it is an immutable, value-equatable struct.
  /// </summary>
  public readonly struct LodTileKey : IEquatable<LodTileKey>
  {
    public readonly int Level;
    public readonly Vector3Int Coord;

    public LodTileKey(int level, Vector3Int coord)
    {
      Level = level;
      Coord = coord;
    }

    public bool Equals(LodTileKey other)
    {
      return Level == other.Level && Coord == other.Coord;
    }

    public override bool Equals(object obj)
    {
      return obj is LodTileKey other && Equals(other);
    }

    public override int GetHashCode()
    {
      return HashCode.Combine(Level, Coord);
    }

    public override string ToString()
    {
      return $"LOD{Level} ({Coord.x}, {Coord.y}, {Coord.z})";
    }
  }
}
