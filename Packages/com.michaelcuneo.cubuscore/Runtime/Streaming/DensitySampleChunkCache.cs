using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  /// <summary>
  /// Caches the neighbour sample data needed by density marching-cubes builds.
  ///
  /// This is intentionally not a full chunk-generation cache. A root density mesh
  /// only samples local coordinates where x/y/z may equal ChunkSize, which maps
  /// into neighbour chunks at local x/y/z == 0. Therefore each cached sample chunk
  /// only fills the zero boundary planes used by adjacent root chunks.
  /// </summary>
  internal static class DensitySampleChunkCache
  {
    private static readonly object CacheLock = new();
    private static readonly Dictionary<Vector3Int, DensityChunkData> SampleChunks = new();

    public static void Clear()
    {
      lock (CacheLock)
      {
        SampleChunks.Clear();
      }
    }

    public static bool TryGet(Vector3Int chunkCoord, out DensityChunkData chunkData)
    {
      lock (CacheLock)
      {
        return SampleChunks.TryGetValue(chunkCoord, out chunkData) && chunkData != null;
      }
    }

    public static DensityChunkData GetOrCreate(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
      lock (CacheLock)
      {
        if (SampleChunks.TryGetValue(chunkCoord, out DensityChunkData cached) && cached != null)
        {
          ApplyBoundaryOverrides(cached, overrides);
          return cached;
        }
      }

      DensityChunkData generated = CreateBoundarySampleChunk(chunkCoord, snapshot, overrides);

      lock (CacheLock)
      {
        SampleChunks[chunkCoord] = generated;
      }

      return generated;
    }

    private static DensityChunkData CreateBoundarySampleChunk(
      Vector3Int chunkCoord,
      WorldGenerationSnapshot snapshot,
      IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
      DensityChunkData chunkData = new(chunkCoord);
      const int size = VoxelConstants.ChunkSize;

      double scale = snapshot.DensitySampleScale <= 0.0f ? 1.0 : snapshot.DensitySampleScale;

      for (int z = 0; z < size; z++)
      {
        for (int y = 0; y < size; y++)
        {
          for (int x = 0; x < size; x++)
          {
            if (x != 0 && y != 0 && z != 0)
            {
              continue;
            }

            int index = VoxelMath.FlattenIndex(x, y, z);
            if (TryGetOverride(overrides, index, out DensityVoxel overridden))
            {
              chunkData.SetVoxel(x, y, z, overridden);
              continue;
            }

            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);
            TerrainSample sample = BiomeTerrainSampler.Sample(snapshot, worldVoxel, (float)scale);
            chunkData.SetVoxel(x, y, z, new DensityVoxel(sample.Density, sample.Materials));
          }
        }
      }

      return chunkData;
    }

    private static void ApplyBoundaryOverrides(
      DensityChunkData chunkData,
      IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
      if (chunkData == null || overrides == null || overrides.Count == 0)
      {
        return;
      }

      const int size = VoxelConstants.ChunkSize;
      foreach (KeyValuePair<int, DensityVoxelOverride> pair in overrides)
      {
        int index = pair.Key;
        int x = index % size;
        int yz = index / size;
        int y = yz % size;
        int z = yz / size;

        if ((uint)x >= size || (uint)y >= size || (uint)z >= size)
        {
          continue;
        }

        if (x != 0 && y != 0 && z != 0)
        {
          continue;
        }

        chunkData.SetVoxel(x, y, z, new DensityVoxel(pair.Value.Density, pair.Value.MaterialId));
      }
    }

    private static bool TryGetOverride(
      IReadOnlyDictionary<int, DensityVoxelOverride> overrides,
      int index,
      out DensityVoxel voxel)
    {
      if (overrides != null && overrides.TryGetValue(index, out DensityVoxelOverride densityOverride))
      {
        voxel = new DensityVoxel(densityOverride.Density, densityOverride.MaterialId);
        return true;
      }

      voxel = default;
      return false;
    }
  }
}