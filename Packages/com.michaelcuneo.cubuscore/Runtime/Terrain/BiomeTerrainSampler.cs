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

        return TerrainSampler.Sample(
            fallbackProfile,
            fallbackBiomeId,
            new Vector3(
                worldVoxel.x * densityScale,
                worldVoxel.y * densityScale,
                worldVoxel.z * densityScale
            )
        );
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

        return TerrainSampler.Sample(
            fallbackProfile,
            fallbackBiomeId,
            new Vector3(
                worldVoxel.x * densityScale,
                worldVoxel.y * densityScale,
                worldVoxel.z * densityScale
            )
        );
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

        return TerrainSampler.Sample(
          fallbackProfile,
          fallbackBiomeId,
          new Vector3(
            worldVoxel.x * densityScale,
            worldVoxel.y * densityScale,
            worldVoxel.z * densityScale
          )
        );
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
      result.SolidMaterialId = result.Density > 0.0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;
      result.LiquidMaterialId = 0;
      result.IsLiquid = false;

      return result;
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
        return TerrainSampler.SampleWithSurfaceHeight(
            fallbackProfile,
            fallbackBiomeId,
            samplePosition,
            fallbackSurfaceHeight
        );
      }

      TerrainSample result = new();

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
      result.SolidMaterialId = result.Density > 0.0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;
      result.LiquidMaterialId = 0;
      result.IsLiquid = false;

      return result;
    }
  }
}
