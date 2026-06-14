using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public readonly struct BiomeTerrainColumnSample
  {
    public readonly float SurfaceHeight;
    public readonly float CaveAmount;
    public readonly int SolidMaterialId;
    public readonly byte BiomeId;

    public BiomeTerrainColumnSample(
        float surfaceHeight,
        float caveAmount,
        int solidMaterialId,
        byte biomeId)
    {
      SurfaceHeight = surfaceHeight;
      CaveAmount = caveAmount;
      SolidMaterialId = solidMaterialId;
      BiomeId = biomeId;
    }
  }

  public static class BiomeTerrainSampler
  {
    public static BiomeTerrainColumnSample SampleColumn(
        in BiomeBlendSample biomeBlend,
        float worldX,
        float worldZ,
        float materialDepthBelowSurface)
    {
      if (!biomeBlend.IsValid)
      {
        return new BiomeTerrainColumnSample(
            0.0f,
            0.0f,
            1,
            1
        );
      }

      float blendedSurfaceHeight = 0.0f;
      float blendedCaveAmount = 0.0f;

      int dominantMaterialId = 1;
      byte dominantBiomeId = biomeBlend.DominantBiomeId;

      for (int i = 0; i < biomeBlend.Count; i++)
      {
        BiomeBlendContributor contributor = biomeBlend.GetContributor(i);

        TerrainSamplerBurst.Sample(
            contributor.Profile,
            worldX,
            worldZ,
            0.0f,
            out float densityAtZero,
            out int materialIdAtZero
        );

        float surfaceHeight = densityAtZero;

        blendedSurfaceHeight += surfaceHeight * contributor.Weight;

        if (i == 0)
        {
          dominantMaterialId = contributor.Profile.GetMaterialId(materialDepthBelowSurface);
          dominantBiomeId = contributor.BiomeId;
        }
      }

      return new BiomeTerrainColumnSample(
          blendedSurfaceHeight,
          blendedCaveAmount,
          Mathf.Clamp(dominantMaterialId, 1, 65535),
          dominantBiomeId
      );
    }

    public static TerrainSample SampleVoxel(
        in BiomeBlendSample biomeBlend,
        Vector3 worldVoxelPosition)
    {
      TerrainSample result = new();

      if (!biomeBlend.IsValid)
      {
        result.Density = -1.0f;
        result.SurfaceHeight = 0.0f;
        result.SolidMaterialId = 0;
        result.BiomeId = 1;
        return result;
      }

      float blendedSurfaceHeight = 0.0f;
      float blendedCaveAmount = 0.0f;

      int dominantMaterialId = 1;
      byte dominantBiomeId = biomeBlend.DominantBiomeId;

      for (int i = 0; i < biomeBlend.Count; i++)
      {
        BiomeBlendContributor contributor = biomeBlend.GetContributor(i);

        TerrainSample sample = TerrainSampler.Sample(
            contributor.Profile,
            contributor.BiomeId,
            worldVoxelPosition
        );

        blendedSurfaceHeight += sample.SurfaceHeight * contributor.Weight;
        blendedCaveAmount += sample.CaveAmount * contributor.Weight;

        if (i == 0)
        {
          dominantMaterialId = sample.SolidMaterialId;
          dominantBiomeId = contributor.BiomeId;
        }
      }

      float density = blendedSurfaceHeight - worldVoxelPosition.y - blendedCaveAmount;

      result.SurfaceHeight = blendedSurfaceHeight;
      result.CaveAmount = blendedCaveAmount;
      result.Density = density;
      result.SolidMaterialId = density > 0.0f
          ? Mathf.Clamp(dominantMaterialId, 1, 65535)
          : 0;
      result.LiquidMaterialId = 0;
      result.BiomeId = dominantBiomeId;
      result.IsLiquid = false;

      return result;
    }
  }
}