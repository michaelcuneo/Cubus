using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public sealed class WorldGenerator
  {
    private readonly WorldSettings settings;

    public WorldGenerator(WorldSettings settings)
    {
      this.settings = settings;
    }

    public void Generate(WorldData worldData)
    {
      worldData.ClearGeneratedChunks();

      switch (settings.TerrainSystem)
      {
        case TerrainSystem.Block:
          GenerateBlockWorld(worldData);
          break;

        case TerrainSystem.SmoothDensity:
        default:
          GenerateSmoothDensityWorld(worldData);
          break;
      }
    }

    public void GenerateBlockWorld(WorldData worldData)
    {
      int safeRadius = Mathf.Max(0, settings.ViewDistanceInChunks);
      settings.GetGenerationChunkBoundsXZ(
          safeRadius,
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      settings.GetEffectiveBlockChunkYRange(out int minChunkY, out int maxChunkY);

      for (int y = minChunkY; y <= maxChunkY; y++)
      {
        for (int z = minChunkZ; z <= maxChunkZ; z++)
        {
          for (int x = minChunkX; x <= maxChunkX; x++)
          {
            Vector3Int chunkCoord = new(x, y, z);
            BlockChunkData chunkData = new(chunkCoord);

            GenerateBlockChunkData(chunkData);

            if (chunkData.HasAnySolidVoxel())
            {
              worldData.BlockChunks.Add(chunkCoord, chunkData);
            }
          }
        }
      }
    }

    public void GenerateSmoothDensityWorld(WorldData worldData)
    {
      int safeRadius = Mathf.Max(0, settings.ViewDistanceInChunks);
      settings.GetGenerationChunkBoundsXZ(
          safeRadius,
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      settings.GetEffectiveDensityChunkYRange(out int minChunkY, out int maxChunkY);

      for (int y = minChunkY; y <= maxChunkY; y++)
      {
        for (int z = minChunkZ; z <= maxChunkZ; z++)
        {
          for (int x = minChunkX; x <= maxChunkX; x++)
          {
            Vector3Int chunkCoord = new(x, y, z);
            DensityChunkData chunkData = new(chunkCoord);

            FillDensityChunkFromTerrainSampler(chunkData);

            if (chunkData.HasAnySolidVoxel())
            {
              worldData.DensityChunks.Add(chunkCoord, chunkData);
            }
          }
        }
      }
    }

    public void GenerateBlockChunkData(BlockChunkData chunkData)
    {
      const int size = VoxelConstants.ChunkSize;

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          for (int y = 0; y < size; y++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSample sample = BiomeTerrainSampler.Sample(
                settings,
                worldVoxel,
                1.0f
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
    }

    public void GenerateBlockChunkDataWithOverrides(
      BlockChunkData chunkData,
      IReadOnlyDictionary<int, ushort> overrides)
    {
      GenerateBlockChunkData(chunkData);

      if (overrides == null)
      {
        return;
      }

      foreach (KeyValuePair<int, ushort> pair in overrides)
      {
        int voxelIndex = pair.Key;
        ushort materialId = pair.Value;

        if (voxelIndex < 0 || voxelIndex >= VoxelConstants.ChunkVolume)
        {
          continue;
        }

        int x = voxelIndex % VoxelConstants.ChunkSize;
        int y = voxelIndex / VoxelConstants.ChunkSize % VoxelConstants.ChunkSize;
        int z = voxelIndex / (VoxelConstants.ChunkSize * VoxelConstants.ChunkSize);

        chunkData.SetVoxel(x, y, z, new Voxel(materialId));
      }
    }

    public int GetSurfaceChunkYForChunkColumn(Vector2Int chunkColumn)
    {
      const int size = VoxelConstants.ChunkSize;

      int worldVoxelX = chunkColumn.x * size + size / 2;
      int worldVoxelZ = chunkColumn.y * size + size / 2;

      TerrainSample sample = BiomeTerrainSampler.Sample(
          settings,
          new Vector3Int(worldVoxelX, 0, worldVoxelZ),
          1.0f
      );

      int surfaceVoxelY = Mathf.FloorToInt(sample.SurfaceHeight);
      return VoxelMath.FloorDiv(surfaceVoxelY, size);
    }

    public void FillDensityChunkFromTerrainSampler(DensityChunkData chunkData)
    {
      const int size = VoxelConstants.ChunkSize;
      float scale = Mathf.Max(0.001f, settings.DensitySampleScale);

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          Vector3Int worldVoxelAtColumnBase = chunkData.LocalToWorldVoxel(x, 0, z);

          settings.ResolveBiomeAtWorldXZ(
              worldVoxelAtColumnBase.x,
              worldVoxelAtColumnBase.z,
              out TerrainGenerationProfileSnapshot profile,
              out byte biomeId
          );

          for (int y = 0; y < size; y++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSample sample = TerrainSampler.Sample(
                profile,
                biomeId,
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
    }
  }
}