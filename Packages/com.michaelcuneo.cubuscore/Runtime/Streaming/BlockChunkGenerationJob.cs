#if UNITY_BURST
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  // Lightweight, blittable biome-rule data used for the per-column biome
  // selection. Kept separate from the fat TerrainGenerationProfileSnapshot
  // (which is ~4 KB each) so the selection pass never has to memcpy profiles.
  public struct BiomeRuleClimate
  {
    public int Priority;
    public byte IsFallback;
    public byte UseElevation;
    public byte UseRise;
    public float ElevationMin;
    public float ElevationMax;
    public float RiseMin;
    public float RiseMax;
  }

  // Burst-compiled block chunk generation. One job index == one (x, z) column;
  // the biome blend is resolved once per column and reused for all voxels in
  // that column, matching BiomeTerrainSampler/BlockChunkBuilder output.
  //
  // The blend is capped at the four highest-weight contributors, which covers
  // effectively all real biome transitions while keeping the contributor
  // profiles in stack locals (no per-voxel NativeArray copies of 4 KB structs).
  [BurstCompile]
  public struct BlockChunkGenerationJob : IJobParallelFor
  {
    // Each job index is one (x, z) column and writes the 32 voxels of that
    // column's Y range: indices columnBase + size*ly, never the bare job index.
    // IJobParallelFor's safety check otherwise aborts (burst_abort -> crash to
    // desktop) because writes land outside [jobIndex]. The per-column index sets
    // are provably disjoint (every (x, y, z) maps to a unique FlattenIndex), so
    // disabling the restriction is safe and there is no write race.
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<ushort> Materials;

    [ReadOnly] public NativeArray<BiomeRuleClimate> RuleClimates;
    [ReadOnly] public NativeArray<TerrainGenerationProfileSnapshot> RuleProfiles;

    public int RuleCount;
    public byte UseBiomeWorldRules;
    public float BiomeRuleWorldScale;
    public float BiomeBlendFeather;

    public TerrainGenerationProfileSnapshot FallbackProfile;

    public Vector3Int ChunkCoord;

    public void Execute(int columnIndex)
    {
      const int size = VoxelConstants.ChunkSize;

      int lx = columnIndex % size;
      int lz = columnIndex / size;

      int worldX = ChunkCoord.x * size + lx;
      int worldZ = ChunkCoord.z * size + lz;
      int chunkMinWorldY = ChunkCoord.y * size;

      // Resolve the column biome blend (X/Z only). The blended surface height
      // is computed over ALL contributors in double precision so the solid/air
      // classification matches the managed boundary sampler exactly; the top
      // four contributors are kept only for per-voxel material selection.
      int count = 0;
      int i0 = -1, i1 = -1, i2 = -1, i3 = -1;
      float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
      double columnSurface;

      ResolveColumnContributors(
          worldX,
          worldZ,
          ref count,
          ref i0, ref i1, ref i2, ref i3,
          ref w0, ref w1, ref w2, ref w3,
          out columnSurface
      );

      TerrainGenerationProfileSnapshot p0 = LoadProfile(i0);
      TerrainGenerationProfileSnapshot p1 = LoadProfile(i1);
      TerrainGenerationProfileSnapshot p2 = LoadProfile(i2);
      TerrainGenerationProfileSnapshot p3 = LoadProfile(i3);

      // (float) cast then floor mirrors TerrainColumnSampler.SurfaceHeight.
      int columnSurfaceFloor = Mathf.FloorToInt((float)columnSurface);
      bool columnAboveChunk = chunkMinWorldY > columnSurfaceFloor;

      int columnBase = lx + size * (size * lz); // FlattenIndex(lx, 0, lz)

      for (int ly = 0; ly < size; ly++)
      {
        ushort material = 0;

        if (!columnAboveChunk)
        {
          int worldY = chunkMinWorldY + ly;
          material = SampleColumnVoxel(
              count,
              in p0, in p1, in p2, in p3,
              w0, w1, w2, w3,
              worldX, worldY, worldZ,
              columnSurfaceFloor
          );
        }

        Materials[columnBase + size * ly] = material;
      }
    }

    private TerrainGenerationProfileSnapshot LoadProfile(int profileIndex)
    {
      if (profileIndex < 0 || profileIndex >= RuleProfiles.Length)
      {
        return FallbackProfile;
      }

      return RuleProfiles[profileIndex];
    }

    private ushort SampleColumnVoxel(
        int count,
        in TerrainGenerationProfileSnapshot p0,
        in TerrainGenerationProfileSnapshot p1,
        in TerrainGenerationProfileSnapshot p2,
        in TerrainGenerationProfileSnapshot p3,
        float w0, float w1, float w2, float w3,
        int worldX, int worldY, int worldZ,
        int columnSurfaceFloor)
    {
      // Solidity is decided purely by the (uncapped, double) column surface so
      // it agrees with the boundary sampler. Material below the surface is
      // blended from the top contributors only.
      if (worldY > columnSurfaceFloor)
      {
        return 0;
      }

      float totalWeight = 0f;
      float blendedDensity = 0f;
      int dominantMaterialId = 0;
      float dominantWeight = -1f;

      if (count > 0)
      {
        Accumulate(in p0, w0, worldX, worldY, worldZ, ref totalWeight, ref blendedDensity, ref dominantMaterialId, ref dominantWeight);
      }
      if (count > 1)
      {
        Accumulate(in p1, w1, worldX, worldY, worldZ, ref totalWeight, ref blendedDensity, ref dominantMaterialId, ref dominantWeight);
      }
      if (count > 2)
      {
        Accumulate(in p2, w2, worldX, worldY, worldZ, ref totalWeight, ref blendedDensity, ref dominantMaterialId, ref dominantWeight);
      }
      if (count > 3)
      {
        Accumulate(in p3, w3, worldX, worldY, worldZ, ref totalWeight, ref blendedDensity, ref dominantMaterialId, ref dominantWeight);
      }

      if (totalWeight <= 0.0001f)
      {
        // Below surface but no material contributors: solid default.
        return 1;
      }

      float densityAverage = blendedDensity / totalWeight;

      int solidMaterialId = densityAverage > 0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;

      return (ushort)Mathf.Clamp(solidMaterialId > 0 ? solidMaterialId : 1, 1, 65535);
    }

    private static void Accumulate(
        in TerrainGenerationProfileSnapshot profile,
        float weight,
        int worldX, int worldY, int worldZ,
        ref float totalWeight,
        ref float blendedDensity,
        ref int dominantMaterialId,
        ref float dominantWeight)
    {
      if (weight <= 0f)
      {
        return;
      }

      // TerrainSamplerBurst expects (wx = X, wy = Z, wz = Y). Block voxels use
      // a density scale of 1.0.
      TerrainSamplerBurst.Sample(
          profile,
          worldX,
          worldZ,
          worldY,
          out float density,
          out int solidMaterialId,
          out float _
      );

      blendedDensity += density * weight;
      totalWeight += weight;

      if (weight > dominantWeight && solidMaterialId > 0)
      {
        dominantWeight = weight;
        dominantMaterialId = solidMaterialId;
      }
    }

    // Surface height for a single contributor profile (column-invariant).
    private double SurfaceForProfile(int profileIndex, int worldX, int worldZ)
    {
      TerrainGenerationProfileSnapshot profile = LoadProfile(profileIndex);
      return TerrainHeight.ComputeSurfaceHeight(profile, worldX, worldZ);
    }

    private void ResolveColumnContributors(
        int worldX,
        int worldZ,
        ref int count,
        ref int i0, ref int i1, ref int i2, ref int i3,
        ref float w0, ref float w1, ref float w2, ref float w3,
        out double columnSurface)
    {
      if (UseBiomeWorldRules == 0 || RuleCount <= 0)
      {
        AddContributor(-1, 1f, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
        columnSurface = SurfaceForProfile(-1, worldX, worldZ);
        return;
      }

      SampleBiomeClimate(worldX, worldZ, BiomeRuleWorldScale, out float elevation, out float rise);

      float feather = Mathf.Clamp(BiomeBlendFeather, 0.0001f, 1f);

      int fallbackIndex = -1;
      int fallbackPriority = int.MinValue;
      bool hasNonFallback = false;

      for (int i = 0; i < RuleCount; i++)
      {
        BiomeRuleClimate rule = RuleClimates[i];

        if (rule.IsFallback != 0)
        {
          if (fallbackIndex < 0 || rule.Priority > fallbackPriority)
          {
            fallbackIndex = i;
            fallbackPriority = rule.Priority;
          }

          continue;
        }

        hasNonFallback = true;
      }

      if (!hasNonFallback)
      {
        AddFallback(fallbackIndex, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
        columnSurface = SurfaceForProfile(
            fallbackIndex >= 0 && fallbackIndex < RuleCount ? fallbackIndex : -1,
            worldX, worldZ);
        return;
      }

      // Layered priority compositing (mirrors WorldGenerationSnapshot): climate
      // validity decides where a biome may appear, Priority decides who wins in
      // overlaps, and the uncovered remainder goes to the fallback. No priority
      // selector noise gates the climate.
      //
      // The blended surface height is accumulated here over ALL contributors in
      // double precision (not just the kept top-four) so block solidity matches
      // the managed boundary sampler and chunk seams do not appear.
      float coverageComplement = 1f;
      double surfaceSum = 0.0;
      double surfaceWeight = 0.0;

      for (int i = 0; i < RuleCount; i++)
      {
        BiomeRuleClimate rule = RuleClimates[i];

        if (rule.IsFallback != 0)
        {
          continue;
        }

        float validity = GetMatchScore(rule, elevation, rise, feather);

        if (validity <= 0.0001f)
        {
          continue;
        }

        float occlusion = 1f;

        for (int j = 0; j < RuleCount; j++)
        {
          if (j == i)
          {
            continue;
          }

          BiomeRuleClimate other = RuleClimates[j];

          if (other.IsFallback != 0)
          {
            continue;
          }

          bool otherDominates =
              other.Priority > rule.Priority ||
              (other.Priority == rule.Priority && j < i);

          if (!otherDominates)
          {
            continue;
          }

          occlusion *= 1f - GetMatchScore(other, elevation, rise, feather);
        }

        coverageComplement *= 1f - validity;

        float weight = validity * occlusion;

        if (weight <= 0.0001f)
        {
          continue;
        }

        AddContributor(i, weight, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
        surfaceSum += SurfaceForProfile(i, worldX, worldZ) * weight;
        surfaceWeight += weight;
      }

      if (coverageComplement > 0.0001f)
      {
        int fallbackProfileIndex = fallbackIndex >= 0 && fallbackIndex < RuleCount ? fallbackIndex : -1;

        AddContributor(
            fallbackProfileIndex,
            coverageComplement,
            ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);

        surfaceSum += SurfaceForProfile(fallbackProfileIndex, worldX, worldZ) * coverageComplement;
        surfaceWeight += coverageComplement;
      }

      if (count <= 0)
      {
        AddFallback(fallbackIndex, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
      }

      columnSurface = surfaceWeight > 0.0001
          ? surfaceSum / surfaceWeight
          : SurfaceForProfile(
              fallbackIndex >= 0 && fallbackIndex < RuleCount ? fallbackIndex : -1,
              worldX, worldZ);
    }

    private void AddFallback(
        int fallbackIndex,
        ref int count,
        ref int i0, ref int i1, ref int i2, ref int i3,
        ref float w0, ref float w1, ref float w2, ref float w3)
    {
      if (fallbackIndex >= 0 && fallbackIndex < RuleCount)
      {
        AddContributor(fallbackIndex, 1f, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
        return;
      }

      AddContributor(-1, 1f, ref count, ref i0, ref i1, ref i2, ref i3, ref w0, ref w1, ref w2, ref w3);
    }

    // Keeps the four highest-weight contributors. Profile index -1 maps to the
    // global fallback profile.
    private static void AddContributor(
        int profileIndex,
        float weight,
        ref int count,
        ref int i0, ref int i1, ref int i2, ref int i3,
        ref float w0, ref float w1, ref float w2, ref float w3)
    {
      if (count < 4)
      {
        switch (count)
        {
          case 0: i0 = profileIndex; w0 = weight; break;
          case 1: i1 = profileIndex; w1 = weight; break;
          case 2: i2 = profileIndex; w2 = weight; break;
          default: i3 = profileIndex; w3 = weight; break;
        }

        count++;
        return;
      }

      // Replace the smallest-weight slot if this contributor is larger.
      int minSlot = 0;
      float minWeight = w0;
      if (w1 < minWeight) { minWeight = w1; minSlot = 1; }
      if (w2 < minWeight) { minWeight = w2; minSlot = 2; }
      if (w3 < minWeight) { minWeight = w3; minSlot = 3; }

      if (weight <= minWeight)
      {
        return;
      }

      switch (minSlot)
      {
        case 0: i0 = profileIndex; w0 = weight; break;
        case 1: i1 = profileIndex; w1 = weight; break;
        case 2: i2 = profileIndex; w2 = weight; break;
        default: i3 = profileIndex; w3 = weight; break;
      }
    }

    private static float GetMatchScore(BiomeRuleClimate rule, float elevation, float rise, float feather)
    {
      float safeFeather = Mathf.Max(0.0001f, feather);
      float validity = 1f;

      if (rule.UseElevation != 0)
      {
        validity *= ScoreRange(elevation, rule.ElevationMin, rule.ElevationMax, safeFeather);
      }

      if (rule.UseRise != 0)
      {
        validity *= ScoreRange(rise, rule.RiseMin, rule.RiseMax, safeFeather);
      }

      return Mathf.Clamp01(validity);
    }

    private static float ScoreRange(float value, float min, float max, float feather)
    {
      if (value >= min && value <= max)
      {
        return 1f;
      }

      float distance = value < min ? min - value : value - max;
      float t = Mathf.Clamp01(1f - distance / Mathf.Max(0.0001f, feather));
      return t * t * (3f - 2f * t);
    }

    private static void SampleBiomeClimate(float worldX, float worldZ, float worldScale, out float elevation, out float rise)
    {
      float s = Mathf.Max(0.0001f, worldScale);
      float x = worldX * s;
      float z = worldZ * s;

      elevation = Noise01(x, z, 0.0008f, 0.0007f, 29.11f);
      rise = Noise01(x, z, 0.0035f, 0.0024f, 61.37f);
    }

    private static float Noise01(float x, float z, float fx, float fz, float phase)
    {
      float a = Mathf.Sin((x * fx) + (z * fz) + phase);
      float b = Mathf.Cos((x * (fx * 0.77f)) - (z * (fz * 1.31f)) - phase * 0.53f);
      return Mathf.Clamp01(((a + b) * 0.25f) + 0.5f);
    }
  }
}
#endif
