using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public static class DensityChunkBuilder
  {
    public static DensityChunkData GenerateChunkData(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
      DensityChunkData chunkData = new(chunkCoord, false);
      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();
      TerrainColumnSampler columnSampler = new();
      float scale = Mathf.Max(0.001f, snapshot.DensitySampleScale);

      const int size = VoxelConstants.ChunkSize;
      int baseWorldX = chunkCoord.x * size;
      int baseWorldY = chunkCoord.y * size;
      int baseWorldZ = chunkCoord.z * size;

      for (int z = 0; z < size; z++)
      {
        int worldZ = baseWorldZ + z;
        int zBase = size * size * z;

        for (int x = 0; x < size; x++)
        {
          int worldX = baseWorldX + x;
          columnSampler.Prepare(snapshot, worldX, worldZ, scale);

          for (int y = 0; y < size; y++)
          {
            TerrainSample sample = columnSampler.SampleAt(worldX, baseWorldY + y, worldZ, scale);

            ushort materialId = sample.Density > 0.0f
                ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
                : (ushort)0;

            voxels[x + size * y + zBase] = new DensityVoxel(sample.Density, materialId);
          }
        }
      }

      if (overrides != null)
      {
        ApplyOverrides(chunkData, overrides);
      }

      return chunkData;
    }

    public static void ApplyOverrides(
        DensityChunkData chunkData,
        IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
      if (chunkData == null || overrides == null)
      {
        return;
      }

      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

      foreach (KeyValuePair<int, DensityVoxelOverride> pair in overrides)
      {
        int voxelIndex = pair.Key;

        if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
        {
          continue;
        }

        DensityVoxelOverride edit = pair.Value;
        voxels[voxelIndex] = new DensityVoxel(edit.Density, edit.MaterialId);
      }
    }
  }
}
