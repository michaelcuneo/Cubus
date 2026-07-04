using System;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public static class BiomeTerrainSampler
  {
    public static TerrainSample Sample(
        WorldSettings settings,
        Vector3Int worldVoxel,
        float densityScale)
    {
      BiomeBlendSample blend = settings.ResolveBiomeBlendAtWorldXZ(
          worldVoxel.x,
          worldVoxel.z
      );

      if (!blend.IsValid)
      {
        settings.ResolveBiomeAtWorldXZ(
            worldVoxel.x,
            worldVoxel.z,
            out TerrainGenerationProfileSnapshot fallbackProfile,
            out byte fallbackBiomeId
        );

        TerrainSample sample = TerrainSampler.Sample(
            fallbackProfile,
            fallbackBiomeId,
            new Vector3(
                worldVoxel.x * densityScale,
                worldVoxel.y * densityScale,
                worldVoxel.z * densityScale
            )
        );

        sample.Profile = fallbackProfile;
        sample.Materials = BuildDensityMaterialSet(
          sample,
          worldVoxel,
          sample.SurfaceHeight,
          fallbackProfile);

        return sample;
      }

      return SampleBlend(
          blend,
          worldVoxel,
          densityScale
      );
    }

    public static TerrainSample Sample(
        WorldGenerationSnapshot snapshot,
        Vector3Int worldVoxel,
        float densityScale)
    {
      BiomeBlendSample blend = snapshot.ResolveBiomeBlendAtWorldXZ(
          worldVoxel.x,
          worldVoxel.z
      );

      if (!blend.IsValid)
      {
        snapshot.ResolveBiomeAtWorldXZ(
            worldVoxel.x,
            worldVoxel.z,
            out TerrainGenerationProfileSnapshot fallbackProfile,
            out byte fallbackBiomeId
        );

        TerrainSample sample = TerrainSampler.Sample(
            fallbackProfile,
            fallbackBiomeId,
            new Vector3(
                worldVoxel.x * densityScale,
                worldVoxel.y * densityScale,
                worldVoxel.z * densityScale
            )
        );

        sample.Profile = fallbackProfile;
        sample.Materials = BuildDensityMaterialSet(
          sample,
          worldVoxel,
          sample.SurfaceHeight,
          fallbackProfile);

        return sample;
      }

      return SampleBlend(
          blend,
          worldVoxel,
          densityScale
      );
    }

    public static TerrainSample Sample(
      WorldGenerationSnapshot snapshot,
      BiomeBlendSample blend,
      Vector3Int worldVoxel,
      float densityScale)
    {
      if (blend == null || !blend.IsValid)
      {
        snapshot.ResolveBiomeAtWorldXZ(
          worldVoxel.x,
          worldVoxel.z,
          out TerrainGenerationProfileSnapshot fallbackProfile,
          out byte fallbackBiomeId
        );

        TerrainSample sample = TerrainSampler.Sample(
          fallbackProfile,
          fallbackBiomeId,
          new Vector3(
            worldVoxel.x * densityScale,
            worldVoxel.y * densityScale,
            worldVoxel.z * densityScale
          )
        );

        sample.Profile = fallbackProfile;
        sample.Materials = BuildDensityMaterialSet(
          sample,
          worldVoxel,
          sample.SurfaceHeight,
          fallbackProfile);

        return sample;
      }

      return SampleBlend(
        blend,
        worldVoxel,
        densityScale
      );
    }

    private static TerrainSample SampleBlend(
        BiomeBlendSample blend,
        Vector3Int worldVoxel,
        float densityScale)
    {
      Vector3 samplePosition = new(
          worldVoxel.x * densityScale,
          worldVoxel.y * densityScale,
          worldVoxel.z * densityScale
      );

      TerrainSample result = new();
      TerrainGenerationProfileSnapshot dominantProfile = default;

      float totalWeight = 0.0f;
      float blendedDensity = 0.0f;
      float blendedSurfaceHeight = 0.0f;
      float blendedCaveAmount = 0.0f;

      int dominantMaterialId = 0;
      byte dominantBiomeId = blend.DominantBiomeId;
      float dominantWeight = -1.0f;

      for (int i = 0; i < blend.Count; i++)
      {
        BiomeBlendContributor contributor = blend.GetContributor(i);

        if (contributor.Weight <= 0.0f)
        {
          continue;
        }

        TerrainSample sample = TerrainSampler.Sample(
            contributor.Profile,
            contributor.BiomeId,
            samplePosition
        );

        float weight = Mathf.Max(0.0f, contributor.Weight);

        blendedDensity += sample.Density * weight;
        blendedSurfaceHeight += sample.SurfaceHeight * weight;
        blendedCaveAmount += sample.CaveAmount * weight;
        totalWeight += weight;

        if (weight > dominantWeight && sample.SolidMaterialId > 0)
        {
          dominantWeight = weight;
          dominantMaterialId = sample.SolidMaterialId;
          dominantBiomeId = contributor.BiomeId;
          dominantProfile = contributor.Profile;
        }
      }

      if (totalWeight <= 0.0001f)
      {
        return default;
      }

      float invWeight = 1.0f / totalWeight;

      result.Density = blendedDensity * invWeight;
      result.SurfaceHeight = blendedSurfaceHeight * invWeight;
      result.CaveAmount = blendedCaveAmount * invWeight;
      result.BiomeId = dominantBiomeId;
      result.Profile = dominantProfile;
      result.SolidMaterialId = result.Density > 0.0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;
      result.Materials = BuildDensityMaterialSet(
        result,
        worldVoxel,
        result.SurfaceHeight,
        dominantProfile);
      result.LiquidMaterialId = 0;
      result.IsLiquid = false;

      return result;
    }

    internal static DensityMaterialSet BuildDensityMaterialSet(
      TerrainSample sample,
      Vector3Int worldVoxel,
      float surfaceHeight,
      TerrainGenerationProfileSnapshot profile)
    {
      if (sample.Density <= 0.0f || sample.SolidMaterialId <= 0)
      {
        return DensityMaterialSet.Empty;
      }

      float depth = Mathf.Max(0.0f, surfaceHeight - worldVoxel.y);
      ushort fallbackMaterial = (ushort)Mathf.Clamp(sample.SolidMaterialId, 1, 65535);

      int layerCount = Mathf.Min(profile.LayerCount, 8);

      if (layerCount <= 0)
      {
        return DensityMaterialSet.Single(fallbackMaterial);
      }

      Span<ushort> ids = stackalloc ushort[8];
      Span<float> weights = stackalloc float[8];
      int count = 0;

      for (int i = 0; i < layerCount; i++)
      {
        MaterialLayerSnapshotEntry layer = profile.MaterialLayers[i];

        ushort materialId = (ushort)Mathf.Clamp(layer.MaterialId, 1, 65535);

        float previousDepth = i == 0
          ? 0.0f
          : Mathf.Max(0.0f, profile.MaterialLayers[i - 1].MaxDepthBelowSurface);

        float layerDepth = Mathf.Max(
          previousDepth + 0.001f,
          layer.MaxDepthBelowSurface);

        float center = (previousDepth + layerDepth) * 0.5f;

        float halfWidth = Mathf.Max(
          Mathf.Max(1.0f, (layerDepth - previousDepth) * 0.5f),
          layer.BlendWidth);

        float layerWeight =
          1.0f - Mathf.Clamp01(Mathf.Abs(depth - center) / halfWidth);

        layerWeight *= Mathf.Max(0.0f, layer.Weight);

        if (i == 0)
        {
          layerWeight = Mathf.Max(
            layerWeight,
            1.0f - Mathf.Clamp01(depth / Mathf.Max(0.01f, layer.BlendWidth)));
        }

        if (i == layerCount - 1 && depth >= center)
        {
          layerWeight = Mathf.Max(
            layerWeight,
            Mathf.Clamp01((depth - previousDepth) / Mathf.Max(1.0f, halfWidth)));
        }

        float noiseScale = Mathf.Max(0.0001f, layer.NoiseScale);
        float noiseStrength = Mathf.Clamp01(layer.NoiseStrength);

        float strataNoise = Mathf.PerlinNoise(
          worldVoxel.x * noiseScale + i * 17.13f,
          worldVoxel.z * noiseScale - i * 9.71f);

        float noiseMultiplier = Mathf.Lerp(
          1.0f - noiseStrength,
          1.0f + noiseStrength,
          strataNoise);

        layerWeight *= noiseMultiplier;

        AccumulateLayerMaterial(
          ref count,
          ids,
          weights,
          materialId,
          layerWeight);
      }

      ApplyMaterialVeins(
        profile,
        worldVoxel,
        depth,
        ref count,
        ids,
        weights);

      if (count == 0)
      {
        return DensityMaterialSet.Single(fallbackMaterial);
      }

      return PackTopFourLayerWeights(ids, weights, count);
    }

    private static void AccumulateLayerMaterial(
      ref int count,
      Span<ushort> ids,
      Span<float> weights,
      ushort materialId,
      float weight)
    {
      if (materialId == 0 || weight <= 0.0001f)
      {
        return;
      }

      for (int i = 0; i < count; i++)
      {
        if (ids[i] == materialId)
        {
          weights[i] += weight;
          return;
        }
      }

      if (count >= ids.Length)
      {
        return;
      }

      ids[count] = materialId;
      weights[count] = weight;
      count++;
    }



    private static DensityMaterialSet PackTopFourLayerWeights(
      Span<ushort> ids,
      Span<float> weights,
      int count)
    {
      int safeCount = Mathf.Min(count, ids.Length);

      for (int i = 0; i < safeCount - 1; i++)
      {
        int best = i;

        for (int j = i + 1; j < safeCount; j++)
        {
          if (weights[j] > weights[best])
          {
            best = j;
          }
        }

        if (best != i)
        {
          (ids[i], ids[best]) = (ids[best], ids[i]);
          (weights[i], weights[best]) = (weights[best], weights[i]);
        }
      }

      ushort material0 = safeCount > 0 ? ids[0] : (ushort)1;
      ushort material1 = safeCount > 1 ? ids[1] : (ushort)0;
      ushort material2 = safeCount > 2 ? ids[2] : (ushort)0;
      ushort material3 = safeCount > 3 ? ids[3] : (ushort)0;

      float weight0 = safeCount > 0 ? weights[0] : 1.0f;
      float weight1 = safeCount > 1 ? weights[1] : 0.0f;
      float weight2 = safeCount > 2 ? weights[2] : 0.0f;
      float weight3 = safeCount > 3 ? weights[3] : 0.0f;

      return PackTopFourMaterialSet(
        material0,
        Mathf.Max(0.0f, weight0),
        material1,
        Mathf.Max(0.0f, weight1),
        material2,
        Mathf.Max(0.0f, weight2),
        material3,
        Mathf.Max(0.0f, weight3));
    }

    private static void ApplyMaterialVeins(
  TerrainGenerationProfileSnapshot profile,
  Vector3Int worldVoxel,
  float depth,
  ref int count,
  Span<ushort> ids,
  Span<float> weights)
    {
      int veinCount = Mathf.Min(profile.VeinCount, 8);

      for (int i = 0; i < veinCount; i++)
      {
        MaterialVeinSnapshotEntry vein = profile.MaterialVeins[i];

        if (depth < vein.MinDepthBelowSurface || depth > vein.MaxDepthBelowSurface)
        {
          continue;
        }

        float depthFadeIn = Mathf.Clamp01(
          (depth - vein.MinDepthBelowSurface) / Mathf.Max(0.001f, vein.BlendWidth));

        float depthFadeOut = Mathf.Clamp01(
          (vein.MaxDepthBelowSurface - depth) / Mathf.Max(0.001f, vein.BlendWidth));

        float depthMask = Mathf.Min(depthFadeIn, depthFadeOut);

        if (depthMask <= 0.0001f)
        {
          continue;
        }

        float n = SampleVeinNoise(
          worldVoxel,
          vein.NoiseScale,
          vein.VerticalScale,
          i);

        float thresholdMin = Mathf.Clamp01(vein.Threshold - vein.BlendWidth);
        float thresholdMax = Mathf.Clamp01(vein.Threshold + vein.BlendWidth);

        float veinMask = Mathf.SmoothStep(thresholdMin, thresholdMax, n);

        float weight = veinMask * depthMask * Mathf.Max(0.0f, vein.Weight);

        AccumulateLayerMaterial(
          ref count,
          ids,
          weights,
          (ushort)Mathf.Clamp(vein.MaterialId, 1, 65535),
          weight);
      }
    }

    private static float SampleVeinNoise(
      Vector3Int worldVoxel,
      float noiseScale,
      float verticalScale,
      int salt)
    {
      float x = worldVoxel.x * noiseScale + salt * 31.17f;
      float y = worldVoxel.y * noiseScale * verticalScale + salt * 11.73f;
      float z = worldVoxel.z * noiseScale - salt * 19.41f;

      float xy = Mathf.PerlinNoise(x, y);
      float yz = Mathf.PerlinNoise(y, z);
      float xz = Mathf.PerlinNoise(x, z);

      return (xy + yz + xz) * 0.3333333f;
    }

    private static DensityMaterialSet PackTopFourMaterialSet(
      ushort material0,
      float weight0,
      ushort material1,
      float weight1,
      ushort material2,
      float weight2,
      ushort material3,
      float weight3)
    {
      float total = weight0 + weight1 + weight2 + weight3;

      if (total <= 0.0001f || material0 == 0)
      {
        return DensityMaterialSet.Single(1);
      }

      float invTotal = 1.0f / total;

      int packed0 = Mathf.Clamp(
        Mathf.RoundToInt(weight0 * invTotal * 255.0f),
        0,
        255);

      int packed1 = Mathf.Clamp(
        Mathf.RoundToInt(weight1 * invTotal * 255.0f),
        0,
        255);

      int packed2 = Mathf.Clamp(
        Mathf.RoundToInt(weight2 * invTotal * 255.0f),
        0,
        255);

      int used = packed0 + packed1 + packed2;
      int packed3 = Mathf.Clamp(255 - used, 0, 255);

      return new DensityMaterialSet
      {
        Material0 = material0,
        Material1 = material1,
        Material2 = material2,
        Material3 = material3,
        Weight0 = (byte)packed0,
        Weight1 = (byte)packed1,
        Weight2 = (byte)packed2,
        Weight3 = (byte)packed3
      };
    }
  }

  // Reusable per-column terrain sampler. Resolves the biome blend and each
  // contributing biome's (X/Z-only) surface height a single time per column,
  // then reuses them for every voxel in that column. This avoids recomputing
  // the expensive surface noise and re-allocating the biome blend for all 32
  // voxels of a column, while producing output identical to
  // BiomeTerrainSampler.Sample.
  public sealed class TerrainColumnSampler
  {
    private BiomeBlendSample blend;
    private bool blendValid;
    private int contributorCount;
    private double[] contributorSurfaceHeights = new double[8];

    private TerrainGenerationProfileSnapshot fallbackProfile;
    private byte fallbackBiomeId;
    private double fallbackSurfaceHeight;

    // Blended surface height for the column (matches TerrainSample.SurfaceHeight).
    public float SurfaceHeight { get; private set; }

    public void Prepare(
        WorldGenerationSnapshot snapshot,
        int worldX,
        int worldZ,
        float densityScale)
    {
      double sx = worldX * densityScale;
      double sz = worldZ * densityScale;

      blend = snapshot.ResolveBiomeBlendAtWorldXZ(worldX, worldZ);
      blendValid = blend != null && blend.IsValid;

      if (!blendValid)
      {
        snapshot.ResolveBiomeAtWorldXZ(
            worldX,
            worldZ,
            out fallbackProfile,
            out fallbackBiomeId
        );

        fallbackSurfaceHeight = TerrainSampler.ComputeSurfaceHeight(
            fallbackProfile,
            sx,
            sz
        );

        SurfaceHeight = (float)fallbackSurfaceHeight;
        return;
      }

      contributorCount = blend.Count;

      if (contributorSurfaceHeights.Length < contributorCount)
      {
        contributorSurfaceHeights = new double[contributorCount];
      }

      double weightedHeight = 0.0;
      double totalWeight = 0.0;

      for (int i = 0; i < contributorCount; i++)
      {
        BiomeBlendContributor contributor = blend.GetContributor(i);

        double height = TerrainSampler.ComputeSurfaceHeight(
            contributor.Profile,
            sx,
            sz
        );

        contributorSurfaceHeights[i] = height;

        if (contributor.Weight > 0.0f)
        {
          weightedHeight += height * contributor.Weight;
          totalWeight += contributor.Weight;
        }
      }

      SurfaceHeight = totalWeight > 0.0001
          ? (float)(weightedHeight / totalWeight)
          : 0.0f;
    }

    public TerrainSample SampleAt(Vector3Int worldVoxel, float densityScale)
    {
      return SampleAt(worldVoxel.x, worldVoxel.y, worldVoxel.z, densityScale);
    }

    public TerrainSample SampleAt(int worldX, int worldY, int worldZ, float densityScale)
    {
      Vector3 samplePosition = new(
          worldX * densityScale,
          worldY * densityScale,
          worldZ * densityScale
      );

      if (!blendValid)
      {
        TerrainSample sample = TerrainSampler.SampleWithSurfaceHeight(
            fallbackProfile,
            fallbackBiomeId,
            samplePosition,
            fallbackSurfaceHeight
        );

        sample.Profile = fallbackProfile;
        sample.Materials = BiomeTerrainSampler.BuildDensityMaterialSet(
          sample,
          new Vector3Int(worldX, worldY, worldZ),
          sample.SurfaceHeight,
          fallbackProfile);

        return sample;
      }

      TerrainSample result = new();
      TerrainGenerationProfileSnapshot dominantProfile = default;

      float totalWeight = 0.0f;
      float blendedDensity = 0.0f;
      float blendedSurfaceHeight = 0.0f;
      float blendedCaveAmount = 0.0f;

      int dominantMaterialId = 0;
      byte dominantBiomeId = blend.DominantBiomeId;
      float dominantWeight = -1.0f;

      for (int i = 0; i < contributorCount; i++)
      {
        BiomeBlendContributor contributor = blend.GetContributor(i);

        if (contributor.Weight <= 0.0f)
        {
          continue;
        }

        TerrainSample sample = TerrainSampler.SampleWithSurfaceHeight(
            contributor.Profile,
            contributor.BiomeId,
            samplePosition,
            contributorSurfaceHeights[i]
        );

        float weight = Mathf.Max(0.0f, contributor.Weight);

        blendedDensity += sample.Density * weight;
        blendedSurfaceHeight += sample.SurfaceHeight * weight;
        blendedCaveAmount += sample.CaveAmount * weight;
        totalWeight += weight;

        if (weight > dominantWeight && sample.SolidMaterialId > 0)
        {
          dominantWeight = weight;
          dominantMaterialId = sample.SolidMaterialId;
          dominantBiomeId = contributor.BiomeId;
          dominantProfile = contributor.Profile;
        }
      }

      if (totalWeight <= 0.0001f)
      {
        return default;
      }

      float invWeight = 1.0f / totalWeight;

      result.Density = blendedDensity * invWeight;
      result.SurfaceHeight = blendedSurfaceHeight * invWeight;
      result.CaveAmount = blendedCaveAmount * invWeight;
      result.BiomeId = dominantBiomeId;
      result.Profile = dominantProfile;
      result.SolidMaterialId = result.Density > 0.0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;
      result.Materials = BiomeTerrainSampler.BuildDensityMaterialSet(
        result,
        new Vector3Int(worldX, worldY, worldZ),
        result.SurfaceHeight,
        dominantProfile);
      result.LiquidMaterialId = 0;
      result.IsLiquid = false;

      return result;
    }
  }
}