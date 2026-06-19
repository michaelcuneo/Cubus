using System;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  [Serializable]
  public struct ChunkBounds3D
  {
    public Vector3Int MinChunk;
    public Vector3Int MaxChunk;

    public ChunkBounds3D(Vector3Int minChunk, Vector3Int maxChunk)
    {
      MinChunk = minChunk;
      MaxChunk = maxChunk;
    }

    public Vector3Int NormalizedMin => new(
      Mathf.Min(MinChunk.x, MaxChunk.x),
      Mathf.Min(MinChunk.y, MaxChunk.y),
      Mathf.Min(MinChunk.z, MaxChunk.z)
    );

    public Vector3Int NormalizedMax => new(
      Mathf.Max(MinChunk.x, MaxChunk.x),
      Mathf.Max(MinChunk.y, MaxChunk.y),
      Mathf.Max(MinChunk.z, MaxChunk.z)
    );

    public bool Contains(Vector3Int chunkCoord)
    {
      Vector3Int min = NormalizedMin;
      Vector3Int max = NormalizedMax;

      return chunkCoord.x >= min.x &&
             chunkCoord.x <= max.x &&
             chunkCoord.y >= min.y &&
             chunkCoord.y <= max.y &&
             chunkCoord.z >= min.z &&
             chunkCoord.z <= max.z;
    }

    public void GetBounds(
      out int minChunkX,
      out int maxChunkX,
      out int minChunkY,
      out int maxChunkY,
      out int minChunkZ,
      out int maxChunkZ)
    {
      Vector3Int min = NormalizedMin;
      Vector3Int max = NormalizedMax;

      minChunkX = min.x;
      maxChunkX = max.x;
      minChunkY = min.y;
      maxChunkY = max.y;
      minChunkZ = min.z;
      maxChunkZ = max.z;
    }

    public void GetBoundsXZ(
      out int minChunkX,
      out int maxChunkX,
      out int minChunkZ,
      out int maxChunkZ)
    {
      Vector3Int min = NormalizedMin;
      Vector3Int max = NormalizedMax;

      minChunkX = min.x;
      maxChunkX = max.x;
      minChunkZ = min.z;
      maxChunkZ = max.z;
    }
  }
}
