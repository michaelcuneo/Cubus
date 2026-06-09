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
      BlockChunkData chunkData = new(chunkCoord);

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
                    worldVoxel.x,
                    worldVoxel.y,
                    worldVoxel.z
                )
            );

            ushort materialId = sample.Density > 0.0f
                ? (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535)
                : (ushort)0;

            chunkData.SetVoxel(
                x,
                y,
                z,
                new Voxel(materialId)
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