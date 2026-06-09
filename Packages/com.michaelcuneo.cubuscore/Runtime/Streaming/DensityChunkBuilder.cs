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
      DensityChunkData chunkData = new(chunkCoord);
      float scale = Mathf.Max(0.001f, snapshot.DensitySampleScale);

      TerrainSampler sampler = new(
          snapshot.TerrainProfile,
          snapshot.BiomeId
      );

      const int size = VoxelConstants.ChunkSize;

      for (int y = 0; y < size; y++)
      {
        for (int z = 0; z < size; z++)
        {
          for (int x = 0; x < size; x++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSample sample = sampler.Sample(
                new Vector3(
                worldVoxel.x * scale,
                worldVoxel.y * scale,
                worldVoxel.z * scale
                )
            );

            ushort materialId = sample.Density > 0.0f
                ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
                : (ushort)0;

            chunkData.SetVoxel(
                x,
                y,
                z,
                new DensityVoxel(
                    sample.Density,
                    materialId
                )
            );
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

      foreach (KeyValuePair<int, DensityVoxelOverride> pair in overrides)
      {
        int voxelIndex = pair.Key;

        if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
        {
          continue;
        }

        int x = voxelIndex % VoxelConstants.ChunkSize;
        int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
        int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

        DensityVoxelOverride edit = pair.Value;

        chunkData.SetVoxel(
            x,
            y,
            z,
            new DensityVoxel(
                edit.Density,
                edit.MaterialId
            )
        );
      }
    }
  }
}