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

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ, BiomeRuleWorldScale);

      float bestWeightedScore = -1.0f;

      int bestIndex = -1;
      int blendIndex0 = -1;
      int blendIndex1 = -1;
      int blendIndex2 = -1;
      int blendIndex3 = -1;
      float blendScore0 = 0.0f;
      float blendScore1 = 0.0f;
      float blendScore2 = 0.0f;
      float blendScore3 = 0.0f;

      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      for (int i = 0; i < BiomeRules.Length; i++)
      {
        float rawScore = BiomeRules[i].GetMatchScore(sample, feather);
        if (rawScore <= 0.0f)
        {
          continue;
        }

        float weightedScore = rawScore + Mathf.Max(0, BiomeRules[i].Priority) * 0.0001f;

        if (weightedScore > bestWeightedScore)
        {
          bestIndex = i;
          bestWeightedScore = weightedScore;
        }

        if (rawScore > blendScore0)
        {
          blendIndex3 = blendIndex2;
          blendScore3 = blendScore2;
          blendIndex2 = blendIndex1;
          blendScore2 = blendScore1;
          blendIndex1 = blendIndex0;
          blendScore1 = blendScore0;
          blendIndex0 = i;
          blendScore0 = rawScore;
        }
        else if (rawScore > blendScore1)
        {
          blendIndex3 = blendIndex2;
          blendScore3 = blendScore2;
          blendIndex2 = blendIndex1;
          blendScore2 = blendScore1;
          blendIndex1 = i;
          blendScore1 = rawScore;
        }
        else if (rawScore > blendScore2)
        {
          blendIndex3 = blendIndex2;
          blendScore3 = blendScore2;
          blendIndex2 = i;
          blendScore2 = rawScore;
        }
        else if (rawScore > blendScore3)
        {
          blendIndex3 = i;
          blendScore3 = rawScore;
        }
      }

      if (bestIndex < 0)
      {
        return;
      }

      profile = BiomeRules[bestIndex].Profile;
      biomeId = BiomeRules[bestIndex].BiomeId;

      if (blendIndex0 < 0 || blendScore0 <= 0.0f)
      {
        return;
      }

      float feather01 = Mathf.InverseLerp(0.05f, 0.6f, feather);
      float relativeGate = Mathf.Lerp(0.20f, 0.70f, feather01);
      float absoluteGate = Mathf.Lerp(0.01f, 0.05f, feather01);
      float minBlendScore = Mathf.Max(absoluteGate, blendScore0 * relativeGate);

      if (blendIndex1 < 0 || blendScore1 < minBlendScore)
      {
        profile = BiomeRules[blendIndex0].Profile;
        return;
      }

      TerrainGenerationProfileSnapshot blended = BiomeRules[blendIndex0].Profile;
      float accumulated = blendScore0;

      if (blendIndex1 >= 0 && blendScore1 >= minBlendScore)
      {
        float t = blendScore1 / Mathf.Max(0.0001f, accumulated + blendScore1);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            BiomeRules[blendIndex1].Profile,
            t
        );
        accumulated += blendScore1;
      }

      if (blendIndex2 >= 0 && blendScore2 >= minBlendScore)
      {
        float t = blendScore2 / Mathf.Max(0.0001f, accumulated + blendScore2);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            BiomeRules[blendIndex2].Profile,
            t
        );
        accumulated += blendScore2;
      }

      if (blendIndex3 >= 0 && blendScore3 >= minBlendScore)
      {
        float t = blendScore3 / Mathf.Max(0.0001f, accumulated + blendScore3);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            BiomeRules[blendIndex3].Profile,
            t
        );
      }

      profile = blended;
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