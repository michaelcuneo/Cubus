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
  public static class BlockChunkBuilder
  {
    public static BlockChunkData GenerateChunkData(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        IReadOnlyDictionary<int, ushort> overrides)
    {
      return GenerateChunkData(chunkCoord, snapshot, overrides, out _);
    }

    public static BlockChunkData GenerateChunkData(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        IReadOnlyDictionary<int, ushort> overrides,
        out bool hasAnySolidVoxel)
    {
      const int size = VoxelConstants.ChunkSize;

      BlockChunkData chunkData = new(chunkCoord);
      Voxel[] voxels = chunkData.GetRawVoxelArray();

      hasAnySolidVoxel = false;

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          for (int y = 0; y < size; y++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSample sample = BiomeTerrainSampler.Sample(
                snapshot,
                worldVoxel,
                1.0f
            );

            ushort materialId = sample.Density > 0.0f
                ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
                : (ushort)0;

            int voxelIndex = VoxelMath.FlattenIndex(x, y, z);
            voxels[voxelIndex] = new Voxel(materialId);

            if (materialId != 0)
            {
              hasAnySolidVoxel = true;
            }
          }
        }
      }

      if (overrides != null)
      {
        ApplyOverrides(chunkData, overrides);

        Voxel[] finalVoxels = chunkData.GetRawVoxelArray();
        hasAnySolidVoxel = false;

        for (int i = 0; i < finalVoxels.Length; i++)
        {
          if (finalVoxels[i].IsSolid)
          {
            hasAnySolidVoxel = true;
            break;
          }
        }
      }

      return chunkData;
    }

    public static ushort SampleMaterialAtWorldVoxel(
        Vector3Int worldVoxel,
        WorldGenerationSnapshot snapshot)
    {
      TerrainSample sample = BiomeTerrainSampler.Sample(
          snapshot,
          worldVoxel,
          1.0f
      );

      return sample.Density > 0.0f
          ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
          : (ushort)0;
    }

    public static void ApplyOverrides(
        BlockChunkData chunkData,
        IReadOnlyDictionary<int, ushort> overrides)
    {
      if (chunkData == null || overrides == null)
      {
        return;
      }

      foreach (KeyValuePair<int, ushort> pair in overrides)
      {
        int voxelIndex = pair.Key;

        if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
        {
          continue;
        }

        int x = voxelIndex % VoxelConstants.ChunkSize;
        int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
        int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

        chunkData.SetVoxel(x, y, z, new Voxel(pair.Value));
      }
    }

    public static MeshData GenerateMesh(
        BlockChunkData centerChunk,
        BlockChunkNeighborhood neighborhood,
        float voxelSize)
    {
      if (centerChunk == null || !centerChunk.HasAnySolidVoxel())
      {
        return null;
      }

      return BlockGreedyMesher.GenerateNeighbourAware(
          centerChunk,
          neighborhood,
          voxelSize
      );
    }

  }
}