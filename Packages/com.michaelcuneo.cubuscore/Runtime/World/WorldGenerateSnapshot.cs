using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public readonly struct WorldGenerationSnapshot
  {
    public readonly float VoxelSize;
    public readonly byte BiomeId;
    public readonly TerrainGenerationProfileSnapshot TerrainProfile;
    public readonly float DensitySampleScale;
    public readonly bool UseBiomeWorldRules;
    public readonly float BiomeRuleWorldScale;
    public readonly float BiomeBlendFeather;
    public readonly BiomeRuleSnapshot[] BiomeRules;

    public WorldGenerationSnapshot(
        float voxelSize,
        byte biomeId,
        TerrainGenerationProfileSnapshot terrainProfile,
        float densitySampleScale,
        bool useBiomeWorldRules,
        float biomeRuleWorldScale,
        float biomeBlendFeather,
        BiomeRuleSnapshot[] biomeRules)
    {
      VoxelSize = voxelSize;
      BiomeId = biomeId;
      TerrainProfile = terrainProfile;
      DensitySampleScale = densitySampleScale;
      UseBiomeWorldRules = useBiomeWorldRules;
      BiomeRuleWorldScale = biomeRuleWorldScale;
      BiomeBlendFeather = biomeBlendFeather;
      BiomeRules = biomeRules;
    }

    public static WorldGenerationSnapshot FromSettings(WorldSettings settings)
    {
      TerrainGenerationProfileSnapshot fallbackProfile =
          new TerrainGenerationProfileSnapshot(settings.GetActiveGenerationProfile());

      BiomeRuleSnapshot[] biomeRules = null;
      if (settings.BiomeWorldRules != null &&
        settings.BiomeWorldRules.Count > 0)
      {
        int count = 0;
        for (int i = 0; i < settings.BiomeWorldRules.Count; i++)
        {
          BiomeWorldRule rule = settings.BiomeWorldRules[i];
          if (rule != null && rule.Biome != null)
          {
            count++;
          }
        }

        if (count > 0)
        {
          biomeRules = new BiomeRuleSnapshot[count];
          int index = 0;

          for (int i = 0; i < settings.BiomeWorldRules.Count; i++)
          {
            BiomeWorldRule rule = settings.BiomeWorldRules[i];

            if (rule == null || rule.Biome == null)
            {
              continue;
            }

            biomeRules[index++] = new BiomeRuleSnapshot(rule);
          }
        }
      }

      return new WorldGenerationSnapshot(
          settings.VoxelSize,
          settings.GetActiveBiomeId(),
          fallbackProfile,
          Mathf.Max(0.001f, settings.DensitySampleScale),
          biomeRules != null && biomeRules.Length > 0,
          Mathf.Max(0.0001f, settings.BiomeRuleWorldScale),
          Mathf.Max(0.01f, settings.BiomeBlendFeather),
          biomeRules
      );
    }

    public void ResolveBiomeAtWorldXZ(
        float worldX,
        float worldZ,
        out TerrainGenerationProfileSnapshot profile,
        out byte biomeId)
    {
      profile = TerrainProfile;
      biomeId = BiomeId;

      if (!UseBiomeWorldRules || BiomeRules == null || BiomeRules.Length == 0)
      {
        return;
      }

      BiomeClimateSample sample = SampleBiomeClimate(
          worldX,
          worldZ,
          BiomeRuleWorldScale
      );

      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      int fallbackIndex = -1;
      int fallbackPriority = int.MinValue;

      int bestIndex = -1;
      float bestScore = 0.0f;

      for (int i = 0; i < BiomeRules.Length; i++)
      {
        BiomeRuleSnapshot rule = BiomeRules[i];

        if (rule.IsFallback)
        {
          if (fallbackIndex < 0 || rule.Priority > fallbackPriority)
          {
            fallbackIndex = i;
            fallbackPriority = rule.Priority;
          }

          continue;
        }

        float score = rule.GetMatchScore(sample, feather);

        if (score <= 0.0f)
        {
          continue;
        }

        score += Mathf.Max(0, rule.Priority) * 0.00001f;

        if (score > bestScore)
        {
          bestScore = score;
          bestIndex = i;
        }
      }

      if (bestIndex < 0)
      {
        bestIndex = fallbackIndex;
      }

      if (bestIndex < 0)
      {
        return;
      }

      BiomeRuleSnapshot bestRule = BiomeRules[bestIndex];

      profile = bestRule.Profile;
      biomeId = bestRule.BiomeId;
    }

    public BiomeBlendSample ResolveBiomeBlendAtWorldXZ(float worldX, float worldZ)
    {
      ResolveBiomeAtWorldXZ(
          worldX,
          worldZ,
          out TerrainGenerationProfileSnapshot profile,
          out byte biomeId
      );

      BiomeBlendSample result = new();

      result.Contributor0 = new BiomeBlendContributor(
          profile,
          biomeId,
          1.0f
      );

      result.Count = 1;
      result.DominantBiomeId = biomeId;

      return result;
    }

    private static BiomeClimateSample SampleBiomeClimate(float worldX, float worldZ, float worldScale)
    {
      float s = Mathf.Max(0.0001f, worldScale);
      float x = worldX * s;
      float z = worldZ * s;

      float temperature = Noise01(x, z, 0.0019f, 0.0012f, 17.23f);
      float elevation = Noise01(x, z, 0.0008f, 0.0007f, 29.11f);
      float rise = Noise01(x, z, 0.0035f, 0.0024f, 61.37f);
      float harshness = Noise01(x, z, 0.0058f, 0.0061f, 93.73f);
      float erosion = Noise01(x, z, 0.0021f, 0.0018f, 47.05f);

      return new BiomeClimateSample(temperature, elevation, rise, harshness, erosion);
    }

    private static float Noise01(float x, float z, float fx, float fz, float phase)
    {
      float a = Mathf.Sin((x * fx) + (z * fz) + phase);
      float b = Mathf.Cos((x * (fx * 0.77f)) - (z * (fz * 1.31f)) - phase * 0.53f);
      return Mathf.Clamp01(((a + b) * 0.25f) + 0.5f);
    }

    public readonly struct BiomeRuleSnapshot
    {
      public readonly int Priority;
      public readonly bool IsFallback;
      public readonly byte BiomeId;
      public readonly TerrainGenerationProfileSnapshot Profile;

      public readonly bool UseTemperature;
      public readonly Vector2 TemperatureRange;

      public readonly bool UseElevation;
      public readonly Vector2 ElevationRange;

      public readonly bool UseRise;
      public readonly Vector2 RiseRange;

      public readonly bool UseHarshness;
      public readonly Vector2 HarshnessRange;

      public readonly bool UseErosion;
      public readonly Vector2 ErosionRange;

      public BiomeRuleSnapshot(BiomeWorldRule rule)
      {
        Priority = rule.Priority;
        IsFallback = rule.IsFallback;
        BiomeId = (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255);
        Profile = new TerrainGenerationProfileSnapshot(rule.Biome.GenerationProfile);

        UseTemperature = rule.UseTemperature;
        TemperatureRange = rule.TemperatureRange;

        UseElevation = rule.UseElevation;
        ElevationRange = rule.ElevationRange;

        UseRise = rule.UseRise;
        RiseRange = rule.RiseRange;

        UseHarshness = rule.UseHarshness;
        HarshnessRange = rule.HarshnessRange;

        UseErosion = rule.UseErosion;
        ErosionRange = rule.ErosionRange;
      }

      public bool Matches(in BiomeClimateSample sample)
      {
        return GetMatchScore(sample, 0.0001f) > 0.0f;
      }

      public float GetMatchScore(in BiomeClimateSample sample, float feather)
      {
        float safeFeather = Mathf.Max(0.0001f, feather);
        float score = 1.0f;

        if (UseTemperature)
        {
          float s = ScoreRange(sample.Temperature, TemperatureRange, safeFeather);
          if (s <= 0.0f)
          {
            return 0.0f;
          }

          score *= s;
        }

        if (UseElevation)
        {
          float s = ScoreRange(sample.Elevation, ElevationRange, safeFeather);
          if (s <= 0.0f)
          {
            return 0.0f;
          }

          score *= s;
        }

        if (UseRise)
        {
          float s = ScoreRange(sample.Rise, RiseRange, safeFeather);
          if (s <= 0.0f)
          {
            return 0.0f;
          }

          score *= s;
        }

        if (UseHarshness)
        {
          float s = ScoreRange(sample.Harshness, HarshnessRange, safeFeather);
          if (s <= 0.0f)
          {
            return 0.0f;
          }

          score *= s;
        }

        if (UseErosion)
        {
          float s = ScoreRange(sample.Erosion, ErosionRange, safeFeather);
          if (s <= 0.0f)
          {
            return 0.0f;
          }

          score *= s;
        }

        return Mathf.Clamp01(score);
      }

      private static bool InRange(float value, Vector2 range)
      {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return value >= min && value <= max;
      }

      private static float ScoreRange(float value, Vector2 range, float feather)
      {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);

        float distance = 0.0f;

        if (value < min)
        {
          distance = min - value;
        }
        else if (value > max)
        {
          distance = value - max;
        }

        if (distance <= 0.0f)
        {
          return 1.0f;
        }

        float sigma = Mathf.Max(0.0001f, feather);
        float normalized = distance / sigma;
        float score = Mathf.Exp(-normalized * normalized);

        return Mathf.Clamp01(score);
      }
    }
  }
}