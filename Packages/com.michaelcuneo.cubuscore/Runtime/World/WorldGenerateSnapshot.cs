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
      BiomeBlendSample blend = ResolveBiomeBlendAtWorldXZ(worldX, worldZ);

      if (!blend.IsValid)
      {
        profile = TerrainProfile;
        biomeId = BiomeId;
        return;
      }

      BiomeBlendContributor dominant = blend.GetContributor(0);
      float bestWeight = dominant.Weight;

      for (int i = 1; i < blend.Count; i++)
      {
        BiomeBlendContributor contributor = blend.GetContributor(i);

        if (contributor.Weight > bestWeight)
        {
          dominant = contributor;
          bestWeight = contributor.Weight;
        }
      }

      profile = dominant.Profile;
      biomeId = dominant.BiomeId;
    }

    public BiomeBlendSample ResolveBiomeBlendAtWorldXZ(float worldX, float worldZ)
    {
      BiomeBlendSample result = new();

      if (!UseBiomeWorldRules || BiomeRules == null || BiomeRules.Length == 0)
      {
        result.Add(new BiomeBlendContributor(
            TerrainProfile,
            BiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      BiomeClimateSample climate = SampleBiomeClimate(
          worldX,
          worldZ,
          BiomeRuleWorldScale
      );

      float feather = Mathf.Max(0.0001f, BiomeBlendFeather);

      int fallbackIndex = -1;
      int fallbackPriority = int.MinValue;

      int minPriority = int.MaxValue;
      int maxPriority = int.MinValue;

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

        minPriority = Mathf.Min(minPriority, rule.Priority);
        maxPriority = Mathf.Max(maxPriority, rule.Priority);
      }

      if (minPriority == int.MaxValue || maxPriority == int.MinValue)
      {
        AddFallbackBiome(result, fallbackIndex);
        result.Normalize();
        return result;
      }

      float selector = SampleBiomePrioritySelector(
          worldX,
          worldZ,
          BiomeRuleWorldScale
      );

      float targetPriority = Mathf.Lerp(
          minPriority,
          maxPriority,
          selector
      );

      int lowerIndex = -1;
      int upperIndex = -1;

      int lowerPriority = int.MinValue;
      int upperPriority = int.MaxValue;

      float lowerValidity = 0.0f;
      float upperValidity = 0.0f;

      for (int i = 0; i < BiomeRules.Length; i++)
      {
        BiomeRuleSnapshot rule = BiomeRules[i];

        if (rule.IsFallback)
        {
          continue;
        }

        float validity = rule.GetMatchScore(climate, feather);

        if (validity <= 0.0001f)
        {
          continue;
        }

        if (rule.Priority <= targetPriority && rule.Priority > lowerPriority)
        {
          lowerPriority = rule.Priority;
          lowerIndex = i;
          lowerValidity = validity;
        }

        if (rule.Priority >= targetPriority && rule.Priority < upperPriority)
        {
          upperPriority = rule.Priority;
          upperIndex = i;
          upperValidity = validity;
        }
      }

      if (lowerIndex < 0 && upperIndex < 0)
      {
        int nearestIndex = FindNearestValidBiomeRuleIndex(
            targetPriority,
            climate,
            feather
        );

        if (nearestIndex >= 0)
        {
          BiomeRuleSnapshot nearestRule = BiomeRules[nearestIndex];

          result.Add(new BiomeBlendContributor(
              nearestRule.Profile,
              nearestRule.BiomeId,
              1.0f
          ));

          result.Normalize();
          return result;
        }

        AddFallbackBiome(result, fallbackIndex);
        result.Normalize();
        return result;
      }

      if (lowerIndex < 0)
      {
        BiomeRuleSnapshot upperOnly = BiomeRules[upperIndex];

        result.Add(new BiomeBlendContributor(
            upperOnly.Profile,
            upperOnly.BiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      if (upperIndex < 0)
      {
        BiomeRuleSnapshot lowerOnly = BiomeRules[lowerIndex];

        result.Add(new BiomeBlendContributor(
            lowerOnly.Profile,
            lowerOnly.BiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      if (lowerIndex == upperIndex || lowerPriority == upperPriority)
      {
        BiomeRuleSnapshot rule = BiomeRules[lowerIndex];

        result.Add(new BiomeBlendContributor(
            rule.Profile,
            rule.BiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      float priorityT = Mathf.InverseLerp(
          lowerPriority,
          upperPriority,
          targetPriority
      );

      float edge = Mathf.Clamp01(feather);
      float blendT;

      if (edge <= 0.0001f)
      {
        blendT = priorityT < 0.5f ? 0.0f : 1.0f;
      }
      else if (priorityT < 0.5f - edge * 0.5f)
      {
        blendT = 0.0f;
      }
      else if (priorityT > 0.5f + edge * 0.5f)
      {
        blendT = 1.0f;
      }
      else
      {
        blendT = Mathf.InverseLerp(
            0.5f - edge * 0.5f,
            0.5f + edge * 0.5f,
            priorityT
        );

        blendT = blendT * blendT * (3.0f - 2.0f * blendT);
      }

      float lowerWeight = (1.0f - blendT) * lowerValidity;
      float upperWeight = blendT * upperValidity;

      if (lowerWeight <= 0.0001f && upperWeight <= 0.0001f)
      {
        int nearestIndex = FindNearestValidBiomeRuleIndex(
            targetPriority,
            climate,
            feather
        );

        if (nearestIndex >= 0)
        {
          BiomeRuleSnapshot nearestRule = BiomeRules[nearestIndex];

          result.Add(new BiomeBlendContributor(
              nearestRule.Profile,
              nearestRule.BiomeId,
              1.0f
          ));
        }
        else
        {
          AddFallbackBiome(result, fallbackIndex);
        }

        result.Normalize();
        return result;
      }

      BiomeRuleSnapshot lowerRule = BiomeRules[lowerIndex];
      BiomeRuleSnapshot upperRule = BiomeRules[upperIndex];

      if (lowerWeight > 0.0001f)
      {
        result.Add(new BiomeBlendContributor(
            lowerRule.Profile,
            lowerRule.BiomeId,
            lowerWeight
        ));
      }

      if (upperWeight > 0.0001f)
      {
        result.Add(new BiomeBlendContributor(
            upperRule.Profile,
            upperRule.BiomeId,
            upperWeight
        ));
      }

      result.Normalize();
      return result;
    }

    private int FindNearestValidBiomeRuleIndex(
    float targetPriority,
    in BiomeClimateSample climate,
    float feather)
    {
      int bestIndex = -1;
      float bestDistance = float.PositiveInfinity;

      for (int i = 0; i < BiomeRules.Length; i++)
      {
        BiomeRuleSnapshot rule = BiomeRules[i];

        if (rule.IsFallback)
        {
          continue;
        }

        float validity = rule.GetMatchScore(climate, feather);

        if (validity <= 0.0001f)
        {
          continue;
        }

        float distance = Mathf.Abs(rule.Priority - targetPriority);

        if (distance < bestDistance)
        {
          bestDistance = distance;
          bestIndex = i;
        }
      }

      return bestIndex;
    }

    private void AddFallbackBiome(BiomeBlendSample result, int fallbackIndex)
    {
      if (fallbackIndex >= 0 &&
          BiomeRules != null &&
          fallbackIndex < BiomeRules.Length)
      {
        BiomeRuleSnapshot fallbackRule = BiomeRules[fallbackIndex];

        result.Add(new BiomeBlendContributor(
            fallbackRule.Profile,
            fallbackRule.BiomeId,
            1.0f
        ));

        return;
      }

      result.Add(new BiomeBlendContributor(
          TerrainProfile,
          BiomeId,
          1.0f
      ));
    }

    private static BiomeClimateSample SampleBiomeClimate(float worldX, float worldZ, float worldScale)
    {
      float s = Mathf.Max(0.0001f, worldScale);
      float x = worldX * s;
      float z = worldZ * s;

      float elevation = Noise01(x, z, 0.0008f, 0.0007f, 29.11f);
      float rise = Noise01(x, z, 0.0035f, 0.0024f, 61.37f);

      return new BiomeClimateSample(
          elevation,
          rise
      );
    }

    private static float Noise01(float x, float z, float fx, float fz, float phase)
    {
      float a = Mathf.Sin((x * fx) + (z * fz) + phase);
      float b = Mathf.Cos((x * (fx * 0.77f)) - (z * (fz * 1.31f)) - phase * 0.53f);
      return Mathf.Clamp01(((a + b) * 0.25f) + 0.5f);
    }

    private static float SampleBiomePrioritySelector(
    float worldX,
    float worldZ,
    float worldScale)
    {
      float s = Mathf.Max(0.0001f, worldScale);
      float x = worldX * s;
      float z = worldZ * s;

      float broad = Noise01(x, z, 0.0009f, 0.0007f, 11.17f);
      float detail = Noise01(x, z, 0.0021f, 0.0018f, 43.71f);

      return Mathf.Clamp01((broad * 0.8f) + (detail * 0.2f));
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
        float validity = 1.0f;

        if (UseElevation)
        {
          validity *= ScoreRange(sample.Elevation, ElevationRange, safeFeather);
        }

        if (UseRise)
        {
          validity *= ScoreRange(sample.Rise, RiseRange, safeFeather);
        }

        return Mathf.Clamp01(validity);
      }

      private static float ScoreRange(float value, Vector2 range, float feather)
      {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);

        if (value >= min && value <= max)
        {
          return 1.0f;
        }

        float distance = value < min
            ? min - value
            : value - max;

        float t = Mathf.Clamp01(
            1.0f - distance / Mathf.Max(0.0001f, feather)
        );

        return t * t * (3.0f - 2.0f * t);
      }
    }
  }
}