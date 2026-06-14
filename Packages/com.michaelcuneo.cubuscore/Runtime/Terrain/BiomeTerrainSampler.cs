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
}