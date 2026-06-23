using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
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

      int chunkMinWorldY = chunkCoord.y * size;

      // One reusable column sampler per chunk: the expensive surface-height
      // noise and the biome blend are resolved once per (x, z) column instead
      // of once per voxel.
      TerrainColumnSampler column = new();

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          Vector3Int columnBaseWorldVoxel = chunkData.LocalToWorldVoxel(x, 0, z);

          column.Prepare(
              snapshot,
              columnBaseWorldVoxel.x,
              columnBaseWorldVoxel.z,
              1.0f
          );

          if (chunkMinWorldY > Mathf.FloorToInt(column.SurfaceHeight))
          {
            continue;
          }

          for (int y = 0; y < size; y++)
          {
            Vector3Int worldVoxel = chunkData.LocalToWorldVoxel(x, y, z);

            TerrainSample sample = column.SampleAt(worldVoxel, 1.0f);

            // Use the column's double-blended surface (matches the Burst job and
            // the boundary sampler) so solidity is consistent everywhere.
            bool isBelowSurface = worldVoxel.y <= Mathf.FloorToInt(column.SurfaceHeight);

            ushort materialId = isBelowSurface
                ? (ushort)Mathf.Clamp(sample.SolidMaterialId > 0 ? sample.SolidMaterialId : 1, 1, 65535)
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

    // Burst-accelerated chunk generation. Falls back to the managed column
    // sampler when the Burst package is unavailable.
    public static BlockChunkData GenerateChunkDataJob(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        IReadOnlyDictionary<int, ushort> overrides,
        out bool hasAnySolidVoxel)
    {
#if UNITY_BURST
      const int size = VoxelConstants.ChunkSize;
      const int volume = VoxelConstants.ChunkVolume;

      BlockChunkData chunkData = new(chunkCoord);
      Voxel[] voxels = chunkData.GetRawVoxelArray();

      hasAnySolidVoxel = false;

      int ruleCount = snapshot.BiomeRules?.Length ?? 0;

      NativeArray<ushort> materials = new(volume, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
      NativeArray<BiomeRuleClimate> ruleClimates = new(
          Mathf.Max(1, ruleCount),
          Allocator.TempJob,
          NativeArrayOptions.UninitializedMemory
      );
      NativeArray<TerrainGenerationProfileSnapshot> ruleProfiles = new(
          Mathf.Max(1, ruleCount),
          Allocator.TempJob,
          NativeArrayOptions.UninitializedMemory
      );

      try
      {
        for (int i = 0; i < ruleCount; i++)
        {
          WorldGenerationSnapshot.BiomeRuleSnapshot rule = snapshot.BiomeRules[i];

          ruleClimates[i] = new BiomeRuleClimate
          {
            Priority = rule.Priority,
            IsFallback = (byte)(rule.IsFallback ? 1 : 0),
            UseElevation = (byte)(rule.UseElevation ? 1 : 0),
            UseRise = (byte)(rule.UseRise ? 1 : 0),
            ElevationMin = Mathf.Min(rule.ElevationRange.x, rule.ElevationRange.y),
            ElevationMax = Mathf.Max(rule.ElevationRange.x, rule.ElevationRange.y),
            RiseMin = Mathf.Min(rule.RiseRange.x, rule.RiseRange.y),
            RiseMax = Mathf.Max(rule.RiseRange.x, rule.RiseRange.y)
          };

          ruleProfiles[i] = rule.Profile;
        }

        BlockChunkGenerationJob job = new()
        {
          Materials = materials,
          RuleClimates = ruleClimates,
          RuleProfiles = ruleProfiles,
          RuleCount = ruleCount,
          UseBiomeWorldRules = (byte)(snapshot.UseBiomeWorldRules && ruleCount > 0 ? 1 : 0),
          BiomeRuleWorldScale = snapshot.BiomeRuleWorldScale,
          BiomeBlendFeather = snapshot.BiomeBlendFeather,
          FallbackProfile = snapshot.TerrainProfile,
          ChunkCoord = chunkCoord
        };

        // Run() Burst-compiles and executes synchronously on the calling
        // thread, which keeps generation safe on the build queue's worker
        // threads while retaining per-chunk SIMD acceleration.
        job.Run(size * size);

        for (int i = 0; i < volume; i++)
        {
          ushort materialId = materials[i];
          voxels[i] = new Voxel(materialId);

          if (materialId != 0)
          {
            hasAnySolidVoxel = true;
          }
        }
      }
      finally
      {
        materials.Dispose();
        ruleClimates.Dispose();
        ruleProfiles.Dispose();
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
#else
      return GenerateChunkData(chunkCoord, snapshot, overrides, out hasAnySolidVoxel);
#endif
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

      // Solidity must match GenerateChunkData: a block voxel is solid when it
      // sits at or below the surface height, NOT based on raw density. Using a
      // different rule here causes the greedy mesher to cull/emit faces that
      // disagree with the neighbouring chunk's actual geometry, producing
      // vertical walls along chunk boundaries.
      bool isBelowSurface = worldVoxel.y <= Mathf.FloorToInt(sample.SurfaceHeight);

      return isBelowSurface
          ? (ushort)Mathf.Clamp(sample.SolidMaterialId > 0 ? sample.SolidMaterialId : 1, 1, 65535)
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
