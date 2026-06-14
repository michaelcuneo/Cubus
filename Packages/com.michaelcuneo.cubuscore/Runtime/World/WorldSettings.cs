using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  [Serializable]
  public sealed class WorldSettings
  {
    [Header("World")]
    public TerrainSystem TerrainSystem = TerrainSystem.SmoothDensity;

    [Header("View Distance")]
    [Min(1)]
    public int ViewDistanceInChunks = 4;

    [Min(0.01f)]
    public float VoxelSize = VoxelConstants.DefaultVoxelSize;

    [Header("Block")]
    public int BlockMinChunkY = -1;
    public int BlockMaxChunkY = 2;

    [Header("Smooth Density")]
    public int DensityMinChunkY = -1;
    public int DensityMaxChunkY = 2;

    [Range(1, 8)]
    public int DensityMeshStep = 1;

    [Tooltip("Multiplies world voxel coordinates before sampling the density field. >1.0 increases frequency (more detail), <1.0 stretches features.")]
    public float DensitySampleScale = 1.0f;

    [Header("Biome World Rules")]
    [Tooltip("Biome climate sampling scale. Lower values create much larger biome regions; higher values create frequent biome changes.")]
    [Min(0.0001f)]
    public float BiomeRuleWorldScale = 8.0f;

    [Range(0.01f, 1.5f)]
    public float BiomeBlendFeather = 0.18f;

    public List<BiomeWorldRule> BiomeWorldRules = new();

    [Header("Pre-Generation Bounds")]
    public bool UseFixedGenerationBounds = false;

    public Vector2Int GenerationMinChunkXZ = new(-4, -4);
    public Vector2Int GenerationMaxChunkXZ = new(4, 4);

    public TerrainGenerationProfile GetActiveGenerationProfile()
    {
      return TryGetFallbackBiome(out BiomeDefinition biome)
          ? biome.GenerationProfile
          : new TerrainGenerationProfile();
    }

    public byte GetActiveBiomeId()
    {
      return TryGetFallbackBiome(out BiomeDefinition biome)
          ? (byte)Mathf.Clamp(biome.BiomeId, 0, 255)
          : (byte)1;
    }

    public void GetGenerationChunkBoundsXZ(
        int fallbackRadius,
        out int minChunkX,
        out int maxChunkX,
        out int minChunkZ,
        out int maxChunkZ)
    {
      if (!UseFixedGenerationBounds)
      {
        int safeRadius = Mathf.Max(0, fallbackRadius);
        minChunkX = -safeRadius;
        maxChunkX = safeRadius;
        minChunkZ = -safeRadius;
        maxChunkZ = safeRadius;
        return;
      }

      minChunkX = Mathf.Min(GenerationMinChunkXZ.x, GenerationMaxChunkXZ.x);
      maxChunkX = Mathf.Max(GenerationMinChunkXZ.x, GenerationMaxChunkXZ.x);
      minChunkZ = Mathf.Min(GenerationMinChunkXZ.y, GenerationMaxChunkXZ.y);
      maxChunkZ = Mathf.Max(GenerationMinChunkXZ.y, GenerationMaxChunkXZ.y);
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
        profile = new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());
        biomeId = GetActiveBiomeId();
        return;
      }

      // Compatibility only. New terrain generation should not use this for final height.
      profile = blend.Contributor0.Profile;
      biomeId = blend.DominantBiomeId;
    }

    public BiomeBlendSample ResolveBiomeBlendAtWorldXZ(float worldX, float worldZ)
    {
      BiomeBlendSample result = new();

      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        TerrainGenerationProfileSnapshot fallbackProfile =
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());

        byte fallbackBiomeId = GetActiveBiomeId();

        result.Contributor0 = new BiomeBlendContributor(
            fallbackProfile,
            fallbackBiomeId,
            1.0f
        );

        result.Count = 1;
        result.DominantBiomeId = fallbackBiomeId;
        return result;
      }

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ);
      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      BiomeWorldRule fallbackRule = null;
      int fallbackPriority = int.MinValue;

      BiomeWorldRule rule0 = null;
      BiomeWorldRule rule1 = null;
      BiomeWorldRule rule2 = null;
      BiomeWorldRule rule3 = null;

      float score0 = 0.0f;
      float score1 = 0.0f;
      float score2 = 0.0f;
      float score3 = 0.0f;

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null)
        {
          continue;
        }

        if (rule.IsFallback)
        {
          if (fallbackRule == null || rule.Priority > fallbackPriority)
          {
            fallbackRule = rule;
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

        if (score > score0)
        {
          rule3 = rule2;
          score3 = score2;

          rule2 = rule1;
          score2 = score1;

          rule1 = rule0;
          score1 = score0;

          rule0 = rule;
          score0 = score;
        }
        else if (score > score1)
        {
          rule3 = rule2;
          score3 = score2;

          rule2 = rule1;
          score2 = score1;

          rule1 = rule;
          score1 = score;
        }
        else if (score > score2)
        {
          rule3 = rule2;
          score3 = score2;

          rule2 = rule;
          score2 = score;
        }
        else if (score > score3)
        {
          rule3 = rule;
          score3 = score;
        }
      }

      if (rule0 == null)
      {
        rule0 = fallbackRule;
        score0 = 1.0f;
      }

      if (rule0 == null || rule0.Biome == null)
      {
        TerrainGenerationProfileSnapshot fallbackProfile =
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());

        byte fallbackBiomeId = GetActiveBiomeId();

        result.Contributor0 = new BiomeBlendContributor(
            fallbackProfile,
            fallbackBiomeId,
            1.0f
        );

        result.Count = 1;
        result.DominantBiomeId = fallbackBiomeId;
        return result;
      }

      float blendRadius = Mathf.Max(0.05f, BiomeBlendFeather);

      float weight0 = score0;
      float weight1 = GetContributorWeight(score0, score1, blendRadius, 0.55f, 0.95f);
      float weight2 = GetContributorWeight(score0, score2, blendRadius, 0.65f, 0.98f);
      float weight3 = GetContributorWeight(score0, score3, blendRadius, 0.75f, 0.99f);

      result.Contributor0 = new BiomeBlendContributor(
          new TerrainGenerationProfileSnapshot(rule0.Biome.GenerationProfile),
          (byte)Mathf.Clamp(rule0.Biome.BiomeId, 0, 255),
          weight0
      );

      result.Count = 1;
      result.DominantBiomeId = result.Contributor0.BiomeId;

      if (rule1 != null && rule1.Biome != null && weight1 > 0.0001f)
      {
        result.Contributor1 = new BiomeBlendContributor(
            new TerrainGenerationProfileSnapshot(rule1.Biome.GenerationProfile),
            (byte)Mathf.Clamp(rule1.Biome.BiomeId, 0, 255),
            weight1
        );

        result.Count = 2;
      }

      if (rule2 != null && rule2.Biome != null && weight2 > 0.0001f)
      {
        result.Contributor2 = new BiomeBlendContributor(
            new TerrainGenerationProfileSnapshot(rule2.Biome.GenerationProfile),
            (byte)Mathf.Clamp(rule2.Biome.BiomeId, 0, 255),
            weight2
        );

        result.Count = 3;
      }

      if (rule3 != null && rule3.Biome != null && weight3 > 0.0001f)
      {
        result.Contributor3 = new BiomeBlendContributor(
            new TerrainGenerationProfileSnapshot(rule3.Biome.GenerationProfile),
            (byte)Mathf.Clamp(rule3.Biome.BiomeId, 0, 255),
            weight3
        );

        result.Count = 4;
      }

      result.Normalize();
      return result;
    }

    private static float GetContributorWeight(
    float bestScore,
    float score,
    float feather,
    float lowerRatio,
    float upperRatio)
    {
      if (score <= 0.0f || bestScore <= 0.0001f)
      {
        return 0.0f;
      }

      float ratio = score / bestScore;
      float t = Mathf.InverseLerp(lowerRatio, upperRatio, ratio);
      t = Mathf.Clamp01(t);

      // Smoothstep.
      t = t * t * (3.0f - 2.0f * t);

      // Keep secondary contributors softer than the dominant biome.
      return score * t * Mathf.Clamp01(feather * 8.0f);
    }

    public string DebugResolveBiomeAtWorldXZ(float worldX, float worldZ)
    {
      TerrainGenerationProfileSnapshot profile;
      byte biomeId;

      ResolveBiomeAtWorldXZ(worldX, worldZ, out profile, out biomeId);

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ);

      System.Text.StringBuilder sb = new System.Text.StringBuilder();

      sb.Append(
          $"Biome Debug XZ=({worldX:0.00}, {worldZ:0.00}) " +
          $"WinnerBiomeId={biomeId}, " +
          $"Temperature={sample.Temperature:0.000}, " +
          $"Elevation={sample.Elevation:0.000}, " +
          $"Rise={sample.Rise:0.000}, " +
          $"Harshness={sample.Harshness:0.000}, " +
          $"Erosion={sample.Erosion:0.000}, " +
          $"Scale={BiomeRuleWorldScale:0.000}"
      );

      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        sb.Append(" | No BiomeWorldRules");
        return sb.ToString();
      }

      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null)
        {
          sb.Append($" | Rule[{i}]=null");
          continue;
        }

        if (rule.Biome == null)
        {
          sb.Append($" | Rule[{i}] {rule.RuleName}=NoBiome");
          continue;
        }

        float score = rule.GetMatchScore(sample, feather);
        int id = Mathf.Clamp(rule.Biome.BiomeId, 0, 255);

        sb.Append(
            $" | Rule[{i}] {rule.RuleName}/Biome={rule.Biome.BiomeName}/Id={id}" +
            $" Score={score:0.000}" +
            $" Priority={rule.Priority}" +
            $" Fallback={rule.IsFallback}"
        );
      }

      return sb.ToString();
    }

    private bool TryGetFallbackBiome(out BiomeDefinition biome)
    {
      biome = null;

      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        return false;
      }

      int bestPriority = int.MinValue;

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null || !rule.IsFallback)
        {
          continue;
        }

        if (biome == null || rule.Priority > bestPriority)
        {
          biome = rule.Biome;
          bestPriority = rule.Priority;
        }
      }

      if (biome != null)
      {
        return true;
      }

      // Compatibility fallback: if no explicit fallback is flagged, use the first valid biome rule.
      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];
        if (rule != null && rule.Biome != null)
        {
          biome = rule.Biome;
          return true;
        }
      }

      return false;
    }

    private BiomeClimateSample SampleBiomeClimate(float worldX, float worldZ)
    {
      float s = Mathf.Max(0.0001f, BiomeRuleWorldScale);
      float x = worldX * s;
      float z = worldZ * s;

      float temperature = Noise01(x, z, 0.0019f, 0.0012f, 17.23f);
      float elevation = Noise01(x, z, 0.0008f, 0.0007f, 29.11f);
      float rise = Noise01(x, z, 0.0035f, 0.0024f, 61.37f);
      float harshness = Noise01(x, z, 0.0058f, 0.0061f, 93.73f);
      float erosion = Noise01(x, z, 0.0021f, 0.0018f, 47.05f);

      return new BiomeClimateSample(
          temperature,
          elevation,
          rise,
          harshness,
          erosion
      );
    }

    private static float Noise01(float x, float z, float fx, float fz, float phase)
    {
      float a = Mathf.Sin((x * fx) + (z * fz) + phase);
      float b = Mathf.Cos((x * (fx * 0.77f)) - (z * (fz * 1.31f)) - phase * 0.53f);
      return Mathf.Clamp01(((a + b) * 0.25f) + 0.5f);
    }

    public bool TryValidateConfiguration(out string message)
    {
      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        message = "BiomeWorldRules is empty. Add at least one rule with a BiomeDefinition.";
        return false;
      }

      if (TerrainSystem == TerrainSystem.SmoothDensity)
      {
        if (DensityMeshStep < 1 || DensityMeshStep > 8)
        {
          message = $"Smooth density mode requires DensityMeshStep in [1..8]. Current={DensityMeshStep}.";
          return false;
        }

        if (DensitySampleScale <= 0.0f)
        {
          message = $"Smooth density mode requires DensitySampleScale > 0. Current={DensitySampleScale}.";
          return false;
        }
      }

      int validRuleCount = 0;
      int fallbackRuleCount = 0;
      HashSet<BiomeDefinition> referencedBiomes = new();

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];
        if (rule == null || rule.Biome == null)
        {
          continue;
        }

        validRuleCount++;
        referencedBiomes.Add(rule.Biome);

        if (rule.IsFallback)
        {
          fallbackRuleCount++;
        }

        if (rule.Biome.GenerationProfile == null)
        {
          message = $"Biome '{rule.Biome.name}' has a null GenerationProfile.";
          return false;
        }

        if (TerrainSystem == TerrainSystem.Block)
        {
          if (rule.Biome.MaterialSet == null)
          {
            message = $"Block mode requires a MaterialSet on biome '{rule.Biome.name}'.";
            return false;
          }
        }

        if (TerrainSystem == TerrainSystem.SmoothDensity)
        {
          TerrainGenerationProfile profile = rule.Biome.GenerationProfile;
          if (profile.MaterialLayers == null || profile.MaterialLayers.Count == 0)
          {
            message = $"Smooth density mode requires at least one MaterialLayer on biome '{rule.Biome.name}'.";
            return false;
          }
        }
      }

      if (validRuleCount == 0)
      {
        message = "BiomeWorldRules has no valid entries. Each rule needs a BiomeDefinition assigned.";
        return false;
      }

      if (fallbackRuleCount == 0)
      {
        message = "No fallback biome rule is marked. Mark one BiomeWorldRule as IsFallback.";
        return false;
      }

      if (validRuleCount > 1 && referencedBiomes.Count < 2)
      {
        message = "Multiple biome rules are configured but all point to the same BiomeDefinition. Assign distinct biomes to see biome-driven variation.";
        return false;
      }

      message = null;
      return true;
    }

    public void GetEffectiveBlockChunkYRange(out int minY, out int maxY)
    {
      minY = BlockMinChunkY;
      maxY = BlockMaxChunkY;

      if (minY > maxY)
      {
        (minY, maxY) = (maxY, minY);
      }
    }

    public void GetEffectiveDensityChunkYRange(out int minY, out int maxY)
    {
      minY = DensityMinChunkY;
      maxY = DensityMaxChunkY;

      if (minY > maxY)
      {
        (minY, maxY) = (maxY, minY);
      }
    }
  }
}