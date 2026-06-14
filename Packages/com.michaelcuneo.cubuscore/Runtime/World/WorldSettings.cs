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
    public float BiomeRuleWorldScale = 0.75f;

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
      profile = new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());
      biomeId = GetActiveBiomeId();

      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        return;
      }

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ);

      BiomeWorldRule bestRule = null;
      float bestWeightedScore = -1.0f;

      BiomeWorldRule blendRule0 = null;
      BiomeWorldRule blendRule1 = null;
      BiomeWorldRule blendRule2 = null;
      BiomeWorldRule blendRule3 = null;
      float blendScore0 = 0.0f;
      float blendScore1 = 0.0f;
      float blendScore2 = 0.0f;
      float blendScore3 = 0.0f;

      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null)
        {
          continue;
        }

        float rawScore = rule.GetMatchScore(sample, feather);
        if (rawScore <= 0.0f)
        {
          continue;
        }

        float weightedScore = rawScore + Mathf.Max(0, rule.Priority) * 0.0001f;

        if (weightedScore > bestWeightedScore)
        {
          bestRule = rule;
          bestWeightedScore = weightedScore;
        }

        // Keep the strongest contributors so biome transitions are blended
        // across a region instead of abruptly switching between only two rules.
        if (rawScore > blendScore0)
        {
          blendRule3 = blendRule2;
          blendScore3 = blendScore2;
          blendRule2 = blendRule1;
          blendScore2 = blendScore1;
          blendRule1 = blendRule0;
          blendScore1 = blendScore0;
          blendRule0 = rule;
          blendScore0 = rawScore;
        }
        else if (rawScore > blendScore1)
        {
          blendRule3 = blendRule2;
          blendScore3 = blendScore2;
          blendRule2 = blendRule1;
          blendScore2 = blendScore1;
          blendRule1 = rule;
          blendScore1 = rawScore;
        }
        else if (rawScore > blendScore2)
        {
          blendRule3 = blendRule2;
          blendScore3 = blendScore2;
          blendRule2 = rule;
          blendScore2 = rawScore;
        }
        else if (rawScore > blendScore3)
        {
          blendRule3 = rule;
          blendScore3 = rawScore;
        }
      }

      if (bestRule == null || bestRule.Biome == null)
      {
        return;
      }

      TerrainGenerationProfileSnapshot bestProfile = new(bestRule.Biome.GenerationProfile);
      profile = bestProfile;
      biomeId = (byte)Mathf.Clamp(bestRule.Biome.BiomeId, 0, 255);

      if (blendRule0 == null || blendScore0 <= 0.0f)
      {
        return;
      }

      float feather01 = Mathf.InverseLerp(0.05f, 0.6f, feather);
      float relativeGate = Mathf.Lerp(0.20f, 0.70f, feather01);
      float absoluteGate = Mathf.Lerp(0.01f, 0.05f, feather01);
      float minBlendScore = Mathf.Max(absoluteGate, blendScore0 * relativeGate);

      if (blendRule1 == null || blendScore1 < minBlendScore)
      {
        profile = new TerrainGenerationProfileSnapshot(blendRule0.Biome.GenerationProfile);
        return;
      }

      TerrainGenerationProfileSnapshot blended = new(blendRule0.Biome.GenerationProfile);
      float accumulated = blendScore0;

      if (blendRule1 != null && blendScore1 >= minBlendScore)
      {
        float t = blendScore1 / Mathf.Max(0.0001f, accumulated + blendScore1);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            new TerrainGenerationProfileSnapshot(blendRule1.Biome.GenerationProfile),
            t
        );
        accumulated += blendScore1;
      }

      if (blendRule2 != null && blendScore2 >= minBlendScore)
      {
        float t = blendScore2 / Mathf.Max(0.0001f, accumulated + blendScore2);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            new TerrainGenerationProfileSnapshot(blendRule2.Biome.GenerationProfile),
            t
        );
        accumulated += blendScore2;
      }

      if (blendRule3 != null && blendScore3 >= minBlendScore)
      {
        float t = blendScore3 / Mathf.Max(0.0001f, accumulated + blendScore3);
        blended = TerrainGenerationProfileSnapshot.Blend(
            blended,
            new TerrainGenerationProfileSnapshot(blendRule3.Biome.GenerationProfile),
            t
        );
      }

      profile = blended;
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

      return new BiomeClimateSample(temperature, elevation, rise, harshness, erosion);
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