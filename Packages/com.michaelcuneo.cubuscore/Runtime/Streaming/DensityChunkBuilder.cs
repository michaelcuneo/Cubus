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
      bool hasSolid = false;
      bool hasAir = false;

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

          // Surface height for this column (already blended across biome
          // contributors). Caves only carve below (surfaceHeight - CaveStartDepth),
          // so any voxel at or above the surface is guaranteed air with zero cave
          // contribution. For those voxels the full per-contributor density blend
          // reduces exactly to (columnSurface - scaledY), which we can write
          // directly and skip the expensive SampleAt sampling entirely.
          float columnSurface = columnSampler.SurfaceHeight;

          for (int y = 0; y < size; y++)
          {
            float scaledY = (baseWorldY + y) * scale;

            if (scaledY >= columnSurface)
            {
              hasAir = true;
              voxels[x + size * y + zBase] = new DensityVoxel(columnSurface - scaledY, DensityMaterialSet.Empty);
              continue;
            }

            TerrainSample sample = columnSampler.SampleAt(worldX, baseWorldY + y, worldZ, scale);

            if (sample.Density > 0.0f) hasSolid = true;
            else hasAir = true;

            DensityMaterialSet materials = sample.Density > 0.0f
              ? sample.Materials
              : DensityMaterialSet.Empty;

            if (sample.Density > 0.0f && materials.DominantMaterialId == 0)
            {
              ushort fallbackMaterialId = (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535);
              materials = DensityMaterialSet.Single(fallbackMaterialId);
            }

            voxels[x + size * y + zBase] = new DensityVoxel(sample.Density, materials);
          }
        }
      }

      chunkData.MarkSurfaceCrossingKnown(hasSolid && hasAir);

      if (overrides != null)
      {
        ApplyOverrides(chunkData, overrides);
      }

      return chunkData;
    }

    // Burst-accelerated density generation. The expensive, bit-identical field
    // half (biome-blended density + caves + solid material id) runs in
    // DensityChunkGenerationJob on a Burst/SIMD worker; the cheap material half
    // (BuildDensityMaterialSet, which uses the non-Burst Mathf.PerlinNoise) is
    // computed here on the managed side for solid voxels only. Falls back to the
    // fully managed sampler when the Burst package is unavailable.
    public static DensityChunkData GenerateChunkDataJob(
        Vector3Int chunkCoord,
        WorldGenerationSnapshot snapshot,
        IReadOnlyDictionary<int, DensityVoxelOverride> overrides)
    {
#if UNITY_BURST
      const int size = VoxelConstants.ChunkSize;
      const int volume = VoxelConstants.ChunkVolume;

      DensityChunkData chunkData = new(chunkCoord, false);
      DensityVoxel[] voxels = chunkData.GetRawVoxelArray();

      float scale = Mathf.Max(0.001f, snapshot.DensitySampleScale);
      int ruleCount = snapshot.BiomeRules?.Length ?? 0;

      // Allocated and disposed within this method, but generation runs on a
      // background load/build worker thread whose wall-clock span can exceed the
      // 4 main-thread frames Allocator.TempJob permits. Allocator.Persistent has
      // no frame-lifetime check and is correct for off-main-thread, manually
      // disposed allocations (matches BlockChunkBuilder.GenerateChunkDataJob).
      NativeArray<float> densities = new(volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      NativeArray<ushort> solidMaterialIds = new(volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      NativeArray<int> dominantProfileIndices = new(volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      NativeArray<float> materialSurfaces = new(size * size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
      NativeArray<BiomeRuleClimate> ruleClimates = new(
          Mathf.Max(1, ruleCount),
          Allocator.Persistent,
          NativeArrayOptions.UninitializedMemory);
      NativeArray<TerrainGenerationProfileSnapshot> ruleProfiles = new(
          Mathf.Max(1, ruleCount),
          Allocator.Persistent,
          NativeArrayOptions.UninitializedMemory);

      bool hasSolid = false;
      bool hasAir = false;

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

        DensityChunkGenerationJob job = new()
        {
          Densities = densities,
          SolidMaterialIds = solidMaterialIds,
          DominantProfileIndices = dominantProfileIndices,
          MaterialSurfaces = materialSurfaces,
          RuleClimates = ruleClimates,
          RuleProfiles = ruleProfiles,
          RuleCount = ruleCount,
          UseBiomeWorldRules = (byte)(snapshot.UseBiomeWorldRules && ruleCount > 0 ? 1 : 0),
          BiomeRuleWorldScale = snapshot.BiomeRuleWorldScale,
          BiomeBlendFeather = snapshot.BiomeBlendFeather,
          DensityScale = scale,
          FallbackProfile = snapshot.TerrainProfile,
          ChunkCoord = chunkCoord
        };

        // Run() Burst-compiles and executes synchronously on the calling worker
        // thread, keeping generation safe on the build queue while retaining
        // per-chunk SIMD acceleration.
        job.Run(size * size);

        for (int z = 0; z < size; z++)
        {
          int worldZ = chunkCoord.z * size + z;

          for (int x = 0; x < size; x++)
          {
            int worldX = chunkCoord.x * size + x;
            float materialSurface = materialSurfaces[x + size * z];

            for (int y = 0; y < size; y++)
            {
              int voxelIndex = x + size * y + size * size * z;
              float density = densities[voxelIndex];

              if (density <= 0.0f)
              {
                hasAir = true;
                voxels[voxelIndex] = new DensityVoxel(density, DensityMaterialSet.Empty);
                continue;
              }

              hasSolid = true;

              ushort solidMaterialId = solidMaterialIds[voxelIndex];
              int dominantIndex = dominantProfileIndices[voxelIndex];

              TerrainGenerationProfileSnapshot profile =
                  dominantIndex == DensityChunkGenerationJob.NoDominantProfile ? default
                  : dominantIndex == DensityChunkGenerationJob.FallbackProfileIndex ? snapshot.TerrainProfile
                  : dominantIndex >= 0 && dominantIndex < ruleCount ? snapshot.BiomeRules[dominantIndex].Profile
                  : snapshot.TerrainProfile;

              // The managed material builder only reads Density and
              // SolidMaterialId off the sample; surface height and profile are
              // passed explicitly, matching TerrainColumnSampler.SampleAt.
              TerrainSample sample = new()
              {
                Density = density,
                SolidMaterialId = solidMaterialId
              };

              DensityMaterialSet materials = BiomeTerrainSampler.BuildDensityMaterialSet(
                  sample,
                  new Vector3Int(worldX, chunkCoord.y * size + y, worldZ),
                  materialSurface,
                  profile);

              if (materials.DominantMaterialId == 0)
              {
                materials = DensityMaterialSet.Single((ushort)Mathf.Clamp(solidMaterialId, 1, 65535));
              }

              voxels[voxelIndex] = new DensityVoxel(density, materials);
            }
          }
        }
      }
      finally
      {
        densities.Dispose();
        solidMaterialIds.Dispose();
        dominantProfileIndices.Dispose();
        materialSurfaces.Dispose();
        ruleClimates.Dispose();
        ruleProfiles.Dispose();
      }

      chunkData.MarkSurfaceCrossingKnown(hasSolid && hasAir);

      if (overrides != null)
      {
        ApplyOverrides(chunkData, overrides);
      }

      return chunkData;
#else
      return GenerateChunkData(chunkCoord, snapshot, overrides);
#endif
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

      chunkData.InvalidateSurfaceCrossingCache();
    }
  }
}
