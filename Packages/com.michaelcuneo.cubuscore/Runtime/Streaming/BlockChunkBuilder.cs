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

      for (int y = 0; y < size; y++)
      {
        for (int z = 0; z < size; z++)
        {
          for (int x = 0; x < size; x++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSamplerBurst.Sample(
                snapshot.TerrainProfile,
                worldVoxel.x,
                worldVoxel.z,
                worldVoxel.y,
                out float density,
                out int solidMaterialId
            );

            ushort materialId = density > 0.0f
                ? (ushort)Mathf.Clamp(solidMaterialId, 1, 65535)
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
        BlockChunkBuilder.ApplyOverrides(chunkData, overrides);
      }

      return chunkData;
    }

    public static ushort SampleMaterialAtWorldVoxel(
        Vector3Int worldVoxel,
        WorldGenerationSnapshot snapshot)
    {
      TerrainSamplerBurst.Sample(
          snapshot.TerrainProfile,
          worldVoxel.x,
          worldVoxel.z,
          worldVoxel.y,
          out float density,
          out int solidMaterialId
      );

      return density > 0.0f
          ? (ushort)Mathf.Clamp(solidMaterialId, 1, 65535)
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