using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using System;
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
      StreamingGenerationContext.Set(settings);
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

    public IEnumerator<object> GenerateAsync(
      WorldData worldData,
      int chunksPerStep,
      Action<float, string> onProgress
    )
    {
      worldData.ClearGeneratedChunks();

      settings.GetGenerationChunkBoundsXZ(
          out int minChunkX,
          out int maxChunkX,
          out int minChunkZ,
          out int maxChunkZ
      );

      int minChunkY;
      int maxChunkY;

      if (settings.TerrainSystem == TerrainSystem.Block)
      {
        settings.GetEffectiveBlockChunkYRange(out minChunkY, out maxChunkY);
      }
      else
      {
        settings.GetEffectiveDensityChunkYRange(out minChunkY, out maxChunkY);
      }

      int totalChunks =
          (maxChunkX - minChunkX + 1) *
          (maxChunkY - minChunkY + 1) *
          (maxChunkZ - minChunkZ + 1);

      totalChunks = Mathf.Max(1, totalChunks);

      int generatedChunks = 0;
      int generatedThisStep = 0;
      int safeChunksPerStep = Mathf.Max(1, chunksPerStep);

      for (int y = minChunkY; y <= maxChunkY; y++)
      {
        for (int z = minChunkZ; z <= maxChunkZ; z++)
        {
          for (int x = minChunkX; x <= maxChunkX; x++)
          {
            Vector3Int chunkCoord = new(x, y, z);

            if (settings.TerrainSystem == TerrainSystem.Block)
            {
              BlockChunkData chunkData = new(chunkCoord);
              GenerateBlockChunkData(chunkData);
              worldData.BlockChunks[chunkCoord] = chunkData;
            }
            else
            {
              DensityChunkData chunkData = new(chunkCoord);
              FillDensityChunkFromTerrainSampler(chunkData);
              worldData.DensityChunks[chunkCoord] = chunkData;
            }

            generatedChunks++;
            generatedThisStep++;

            onProgress?.Invoke(
                (float)generatedChunks / totalChunks,
                $"Generated {generatedChunks}/{totalChunks} chunks. Current={chunkCoord}"
            );

            if (generatedThisStep >= safeChunksPerStep)
            {
              generatedThisStep = 0;
              yield return null;
            }
          }
        }
      }

      onProgress?.Invoke(1.0f, $"Generated {generatedChunks}/{totalChunks} chunks.");
    }

    public void GenerateBlockWorld(WorldData worldData)
    {
      settings.GetGenerationChunkBoundsXZ(
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
            worldData.BlockChunks[chunkCoord] = chunkData;
          }
        }
      }
    }

    public void GenerateSmoothDensityWorld(WorldData worldData)
    {
      settings.GetGenerationChunkBoundsXZ(
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
            worldData.DensityChunks[chunkCoord] = chunkData;
          }
        }
      }
    }

    public void GenerateBlockChunkData(BlockChunkData chunkData)
    {
      FillBlockChunkFromTerrainSampler(chunkData, null);
    }

    public void GenerateBlockChunkDataWithOverrides(
      BlockChunkData chunkData,
      IReadOnlyDictionary<int, ushort> overrides)
    {
      FillBlockChunkFromTerrainSampler(chunkData, overrides);
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

    private void FillBlockChunkFromTerrainSampler(
  BlockChunkData chunkData,
  IReadOnlyDictionary<int, ushort> overrides)
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

            int voxelIndex = VoxelMath.FlattenIndex(x, y, z);

            if (overrides != null &&
                overrides.TryGetValue(voxelIndex, out ushort overrideMaterialId))
            {
              materialId = overrideMaterialId;
            }

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
