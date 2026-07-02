#if UNITY_BURST
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  // Burst-compiled density FIELD generation. One job index == one (x, z)
  // column. This computes the expensive, bit-identical half of the density
  // builder: the biome-blended signed density (surface height + caves) and the
  // per-voxel solid material id, exactly mirroring TerrainColumnSampler.SampleAt.
  //
  // The cheap material half (BiomeTerrainSampler.BuildDensityMaterialSet, which
  // uses Mathf.PerlinNoise and is NOT Burst-compatible) is deliberately left to
  // a managed post-pass in DensityChunkBuilder.GenerateChunkDataJob. Splitting
  // here keeps the strata/vein material output byte-for-byte identical while
  // moving the ~90%+ field cost onto Burst/SIMD worker threads.
  //
  // Unlike BlockChunkGenerationJob (which only needs the top four contributors
  // for material selection and a blended surface for solidity), the density
  // blend must sum over ALL contributors, so contributors are gathered into
  // stack buffers and re-used for every voxel in the column.
  [BurstCompile]
  public struct DensityChunkGenerationJob : IJobParallelFor
  {
    // Sentinel dominant-profile index meaning "no solid contributor was found
    // for this voxel" -> the managed pass must use a default(zero) profile,
    // matching TerrainColumnSampler.SampleAt where dominantProfile stays default.
    public const int NoDominantProfile = int.MinValue;

    // Global fallback (WorldGenerationSnapshot.TerrainProfile) contributor index.
    public const int FallbackProfileIndex = -1;

    private const int MaxContributors = 32;

    // Per-voxel outputs. Writes land at columnBase + size*ly (never the bare job
    // index), so the parallel-for restriction is disabled; the per-column index
    // sets are provably disjoint (unique FlattenIndex per (x, y, z)).
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float> Densities;

    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<ushort> SolidMaterialIds;

    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<int> DominantProfileIndices;

    // Per-column blended surface height (float accumulation, matching
    // TerrainSample.SurfaceHeight) used by the managed material pass for depth.
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<float> MaterialSurfaces;

    [ReadOnly] public NativeArray<BiomeRuleClimate> RuleClimates;
    [ReadOnly] public NativeArray<TerrainGenerationProfileSnapshot> RuleProfiles;

    public int RuleCount;
    public byte UseBiomeWorldRules;
    public float BiomeRuleWorldScale;
    public float BiomeBlendFeather;
    public float DensityScale;

    public TerrainGenerationProfileSnapshot FallbackProfile;

    public Vector3Int ChunkCoord;

    public void Execute(int columnIndex)
    {
      const int size = VoxelConstants.ChunkSize;

      int lx = columnIndex % size;
      int lz = columnIndex / size;

      int worldX = ChunkCoord.x * size + lx;
      int worldZ = ChunkCoord.z * size + lz;
      int baseWorldY = ChunkCoord.y * size;

      // Scaled X/Z (float rounding then widened to double), matching
      // TerrainColumnSampler: samplePosition = worldVoxel * densityScale (float).
      float fsx = worldX * DensityScale;
      float fsz = worldZ * DensityScale;

      // Gather every biome contributor for this column (profile index, weight,
      // and the scaled surface height). Ratios of weights are what matters for
      // the blend, so no normalization is required.
      System.Span<int> contribIndex = stackalloc int[MaxContributors];
      System.Span<float> contribWeight = stackalloc float[MaxContributors];
      System.Span<double> contribSurface = stackalloc double[MaxContributors];

      int contribCount = ResolveColumnContributors(
          fsx,
          fsz,
          worldX,
          worldZ,
          contribIndex,
          contribWeight,
          contribSurface);

      // Air early-out threshold: the double-accumulated blended surface height,
      // matching TerrainColumnSampler.Prepare (SurfaceHeight = (float)(sum(h*w)/
      // sum(w))). Caves never carve at or above the surface, so any voxel with
      // scaledY >= columnSurface is guaranteed air with density columnSurface -
      // scaledY, letting the field blend be skipped entirely for that voxel.
      double weightedHeightD = 0.0;
      double totalWeightD = 0.0;

      // Material-surface (float accumulation) and the shared inverse weight,
      // matching SampleAt (result.SurfaceHeight = blendedSurface * invWeight,
      // result.Density = blendedDensity * invWeight).
      float blendedSurfaceF = 0.0f;
      float totalWeightF = 0.0f;

      for (int i = 0; i < contribCount; i++)
      {
        double h = contribSurface[i];
        float w = contribWeight[i];

        weightedHeightD += h * w;
        totalWeightD += w;

        blendedSurfaceF += (float)h * w;
        totalWeightF += w;
      }

      float columnSurface = totalWeightD > 0.0001
          ? (float)(weightedHeightD / totalWeightD)
          : 0.0f;

      float invWeight = totalWeightF > 0.0001f ? 1.0f / totalWeightF : 0.0f;
      float materialSurface = blendedSurfaceF * invWeight;

      MaterialSurfaces[columnIndex] = materialSurface;

      int columnBase = lx + size * (size * lz); // FlattenIndex(lx, 0, lz)

      for (int ly = 0; ly < size; ly++)
      {
        int outIndex = columnBase + size * ly;

        float scaledY = (baseWorldY + ly) * DensityScale;

        if (scaledY >= columnSurface)
        {
          // Air above the column surface. Density reduces exactly to
          // columnSurface - scaledY (proven identical to the full blend because
          // every contributor's cave term is zero above the surface).
          Densities[outIndex] = columnSurface - scaledY;
          SolidMaterialIds[outIndex] = 0;
          DominantProfileIndices[outIndex] = NoDominantProfile;
          continue;
        }

        SampleColumnVoxel(
            contribCount,
            contribIndex,
            contribWeight,
            contribSurface,
            fsx,
            fsz,
            scaledY,
            worldX,
            baseWorldY + ly,
            worldZ,
            invWeight,
            out float density,
            out ushort solidMaterialId,
            out int dominantProfileIndex);

        Densities[outIndex] = density;
        SolidMaterialIds[outIndex] = solidMaterialId;
        DominantProfileIndices[outIndex] = dominantProfileIndex;
      }
    }

    // Full per-voxel field blend, mirroring TerrainColumnSampler.SampleAt: sum
    // (density_i * w_i) / sum(w_i) with the dominant material taken from the
    // highest-weight contributor that is solid at this voxel.
    private void SampleColumnVoxel(
        int contribCount,
        System.Span<int> contribIndex,
        System.Span<float> contribWeight,
        System.Span<double> contribSurface,
        float fsx,
        float fsz,
        float scaledY,
        int worldX,
        int worldY,
        int worldZ,
        float invWeight,
        out float density,
        out ushort solidMaterialId,
        out int dominantProfileIndex)
    {
      float blendedDensity = 0.0f;
      int dominantMaterialId = 0;
      int dominantIndex = NoDominantProfile;
      float dominantWeight = -1.0f;

      for (int i = 0; i < contribCount; i++)
      {
        float weight = contribWeight[i];

        if (weight <= 0.0f)
        {
          continue;
        }

        TerrainGenerationProfileSnapshot profile = LoadProfile(contribIndex[i]);
        double surfaceHeight = contribSurface[i];

        // Mirrors TerrainSampler.SampleWithSurfaceHeight with a precomputed
        // (hoisted) surface height: density = surfaceHeight - wz - cave.
        double d = surfaceHeight - scaledY;

        if (profile.CaveStrength > 0.0f)
        {
          d -= TerrainCaves.CarveAmount(profile, fsx, fsz, scaledY, surfaceHeight);
        }

        float densityI = (float)d;

        int materialI = TerrainMaterialResolver.ResolveSolidMaterial(
            profile,
            fsx,
            fsz,
            scaledY,
            densityI,
            (float)surfaceHeight);

        blendedDensity += densityI * weight;

        if (weight > dominantWeight && materialI > 0)
        {
          dominantWeight = weight;
          dominantMaterialId = materialI;
          dominantIndex = contribIndex[i];
        }
      }

      density = blendedDensity * invWeight;

      if (density > 0.0f)
      {
        solidMaterialId = (ushort)Mathf.Clamp(dominantMaterialId > 0 ? dominantMaterialId : 1, 1, 65535);
        dominantProfileIndex = dominantIndex;
      }
      else
      {
        solidMaterialId = 0;
        dominantProfileIndex = NoDominantProfile;
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

    private double SurfaceForProfile(int profileIndex, double fsx, double fsz)
    {
      TerrainGenerationProfileSnapshot profile = LoadProfile(profileIndex);
      return TerrainHeight.ComputeSurfaceHeight(profile, fsx, fsz);
    }

    // Gathers all biome contributors for the column, mirroring
    // WorldGenerationSnapshot.ResolveBiomeBlendAtWorldXZ. Returns the count and
    // fills the contributor buffers with (profile index, weight, scaled surface
    // height). No top-N cap: the density blend must sum over every contributor.
    private int ResolveColumnContributors(
        float fsx,
        float fsz,
        int worldX,
        int worldZ,
        System.Span<int> contribIndex,
        System.Span<float> contribWeight,
        System.Span<double> contribSurface)
    {
      int count = 0;

      if (UseBiomeWorldRules == 0 || RuleCount <= 0)
      {
        AddContributor(FallbackProfileIndex, 1.0f, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
        return count;
      }

      SampleBiomeClimate(worldX, worldZ, BiomeRuleWorldScale, out float elevation, out float rise);

      float feather = Mathf.Clamp(BiomeBlendFeather, 0.0001f, 1.0f);

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
        AddFallback(fallbackIndex, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
        return count;
      }

      // Layered priority compositing (mirrors WorldGenerationSnapshot): climate
      // validity decides where a biome may appear, Priority decides who wins in
      // overlaps, and the uncovered remainder goes to the fallback.
      float coverageComplement = 1.0f;

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

        float occlusion = 1.0f;

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

          occlusion *= 1.0f - GetMatchScore(other, elevation, rise, feather);
        }

        coverageComplement *= 1.0f - validity;

        float weight = validity * occlusion;

        if (weight <= 0.0001f)
        {
          continue;
        }

        AddContributor(i, weight, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
      }

      if (coverageComplement > 0.0001f)
      {
        int fallbackProfileIndex = fallbackIndex >= 0 && fallbackIndex < RuleCount ? fallbackIndex : FallbackProfileIndex;
        AddContributor(fallbackProfileIndex, coverageComplement, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
      }

      if (count <= 0)
      {
        AddFallback(fallbackIndex, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
      }

      return count;
    }

    private void AddFallback(
        int fallbackIndex,
        float fsx,
        float fsz,
        ref int count,
        System.Span<int> contribIndex,
        System.Span<float> contribWeight,
        System.Span<double> contribSurface)
    {
      int profileIndex = fallbackIndex >= 0 && fallbackIndex < RuleCount ? fallbackIndex : FallbackProfileIndex;
      AddContributor(profileIndex, 1.0f, fsx, fsz, ref count, contribIndex, contribWeight, contribSurface);
    }

    private void AddContributor(
        int profileIndex,
        float weight,
        float fsx,
        float fsz,
        ref int count,
        System.Span<int> contribIndex,
        System.Span<float> contribWeight,
        System.Span<double> contribSurface)
    {
      if (count >= contribIndex.Length)
      {
        return;
      }

      contribIndex[count] = profileIndex;
      contribWeight[count] = weight;
      contribSurface[count] = SurfaceForProfile(profileIndex, fsx, fsz);
      count++;
    }

    private static float GetMatchScore(BiomeRuleClimate rule, float elevation, float rise, float feather)
    {
      float safeFeather = Mathf.Max(0.0001f, feather);
      float validity = 1.0f;

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
        return 1.0f;
      }

      float distance = value < min ? min - value : value - max;
      float t = Mathf.Clamp01(1.0f - distance / Mathf.Max(0.0001f, feather));
      return t * t * (3.0f - 2.0f * t);
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
