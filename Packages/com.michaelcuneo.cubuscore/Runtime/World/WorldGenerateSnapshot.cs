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
    public readonly int WorldSeed;
    public readonly BiomeRuleSnapshot[] BiomeRules;

    public WorldGenerationSnapshot(
        float voxelSize,
        byte biomeId,
        TerrainGenerationProfileSnapshot terrainProfile,
        float densitySampleScale,
        bool useBiomeWorldRules,
        float biomeRuleWorldScale,
        float biomeBlendFeather,
        int worldSeed,
        BiomeRuleSnapshot[] biomeRules)
    {
      VoxelSize = voxelSize;
      BiomeId = biomeId;
      TerrainProfile = terrainProfile;
      DensitySampleScale = densitySampleScale;
      UseBiomeWorldRules = useBiomeWorldRules;
      BiomeRuleWorldScale = biomeRuleWorldScale;
      BiomeBlendFeather = biomeBlendFeather;
      WorldSeed = worldSeed;
      BiomeRules = biomeRules;
    }

    public static WorldGenerationSnapshot FromSettings(WorldSettings settings)
    {
      TerrainGenerationProfileSnapshot fallbackProfile =
          new TerrainGenerationProfileSnapshot(settings.GetActiveGenerationProfile(), settings.WorldSeed);

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

            biomeRules[index++] = new BiomeRuleSnapshot(rule, settings.WorldSeed);
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
          settings.WorldSeed,
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

      float feather = Mathf.Clamp(BiomeBlendFeather, 0.0001f, 1.0f);

      // Highest-priority IsFallback rule fills any area no biome claims.
      int fallbackIndex = -1;
      int fallbackPriority = int.MinValue;
      bool hasNonFallback = false;

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

        hasNonFallback = true;
      }

      if (!hasNonFallback)
      {
        AddFallbackBiome(result, fallbackIndex);
        result.Normalize();
        return result;
      }

      // Layered priority compositing. Climate validity decides WHERE a biome
      // may appear; Priority only decides WHO wins where ranges overlap. The
      // highest-priority valid biome claims a voxel fully, and lower-priority
      // biomes show through only where the higher one feathers out, producing
      // smooth, climate-driven transitions.
      //
      // Previously a separate priority-selector noise picked a target priority
      // band independently of the climate, which suppressed climate-correct
      // biomes (e.g. snow on flat ground, deserts on peaks). That noise gate is
      // gone: weight_i = validity_i * Product_over_dominating_j(1 - validity_j),
      // and the leftover coverage goes to the fallback.
      float coverageComplement = 1.0f;

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

        float occlusion = 1.0f;

        for (int j = 0; j < BiomeRules.Length; j++)
        {
          if (j == i)
          {
            continue;
          }

          BiomeRuleSnapshot other = BiomeRules[j];

          if (other.IsFallback)
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

          occlusion *= 1.0f - other.GetMatchScore(climate, feather);
        }

        coverageComplement *= 1.0f - validity;

        float weight = validity * occlusion;

        if (weight <= 0.0001f)
        {
          continue;
        }

        result.Add(new BiomeBlendContributor(
            rule.Profile,
            rule.BiomeId,
            weight
        ));
      }

      if (coverageComplement > 0.0001f)
      {
        AddFallbackBiome(result, fallbackIndex, coverageComplement);
      }

      if (result.Count <= 0)
      {
        AddFallbackBiome(result, fallbackIndex);
      }

      result.Normalize();
      return result;
    }

    private void AddFallbackBiome(BiomeBlendSample result, int fallbackIndex)
    {
      AddFallbackBiome(result, fallbackIndex, 1.0f);
    }

    private void AddFallbackBiome(BiomeBlendSample result, int fallbackIndex, float weight)
    {
      if (fallbackIndex >= 0 &&
          BiomeRules != null &&
          fallbackIndex < BiomeRules.Length)
      {
        BiomeRuleSnapshot fallbackRule = BiomeRules[fallbackIndex];

        result.Add(new BiomeBlendContributor(
            fallbackRule.Profile,
            fallbackRule.BiomeId,
            weight
        ));

        return;
      }

      result.Add(new BiomeBlendContributor(
          TerrainProfile,
          BiomeId,
          weight
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

      public BiomeRuleSnapshot(BiomeWorldRule rule, int worldSeed)
      {
        Priority = rule.Priority;
        IsFallback = rule.IsFallback;
        BiomeId = (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255);
        Profile = new TerrainGenerationProfileSnapshot(rule.Biome.GenerationProfile, worldSeed);

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