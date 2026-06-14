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
    public float BiomeBlendFeather = 0.10f;

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

      if (BiomeWorldRules == null || BiomeWorldRules.Count == 0)
      {
        TerrainGenerationProfileSnapshot fallbackProfile =
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());

        byte fallbackBiomeId = GetActiveBiomeId();

        result.Add(new BiomeBlendContributor(
            fallbackProfile,
            fallbackBiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ);
      float feather = Mathf.Max(0.01f, BiomeBlendFeather);

      BiomeWorldRule fallbackRule = null;
      int fallbackPriority = int.MinValue;

      float bestScore = 0.0f;

      // First pass: find fallback and strongest real biome score.
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

        if (score > bestScore)
        {
          bestScore = score;
        }
      }

      // No real biome matched. Use fallback only.
      if (bestScore <= 0.0001f)
      {
        if (fallbackRule != null && fallbackRule.Biome != null)
        {
          result.Add(new BiomeBlendContributor(
              new TerrainGenerationProfileSnapshot(fallbackRule.Biome.GenerationProfile),
              (byte)Mathf.Clamp(fallbackRule.Biome.BiomeId, 0, 255),
              1.0f
          ));

          result.Normalize();
          return result;
        }

        TerrainGenerationProfileSnapshot fallbackProfile =
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile());

        byte fallbackBiomeId = GetActiveBiomeId();

        result.Add(new BiomeBlendContributor(
            fallbackProfile,
            fallbackBiomeId,
            1.0f
        ));

        result.Normalize();
        return result;
      }

      const float MinAbsoluteScore = 0.05f;
      const float MinRelativeScore = 0.75f;
      const float BlendSharpness = 5.0f;

      // Second pass: add only strong local contenders.
      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null || rule.IsFallback)
        {
          continue;
        }

        float score = rule.GetMatchScore(sample, feather);

        if (score < MinAbsoluteScore)
        {
          continue;
        }

        float relativeScore = score / bestScore;

        if (relativeScore < MinRelativeScore)
        {
          continue;
        }

        // Priority may break near-ties, but must not make weak biomes dominate terrain.
        float priorityBias = Mathf.Max(0, rule.Priority) * 0.00001f;
        float sharpenedWeight = Mathf.Pow(Mathf.Clamp01(relativeScore), BlendSharpness);

        float weight = sharpenedWeight + priorityBias;

        result.Add(new BiomeBlendContributor(
            new TerrainGenerationProfileSnapshot(rule.Biome.GenerationProfile),
            (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255),
            weight
        ));
      }

      if (result.Count <= 0)
      {
        if (fallbackRule != null && fallbackRule.Biome != null)
        {
          result.Add(new BiomeBlendContributor(
              new TerrainGenerationProfileSnapshot(fallbackRule.Biome.GenerationProfile),
              (byte)Mathf.Clamp(fallbackRule.Biome.BiomeId, 0, 255),
              1.0f
          ));
        }
        else
        {
          result.Add(new BiomeBlendContributor(
              new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile()),
              GetActiveBiomeId(),
              1.0f
          ));
        }
      }

      result.Normalize();
      return result;
    }

    public string DebugResolveBiomeAtWorldXZ(float worldX, float worldZ)
    {
      TerrainGenerationProfileSnapshot profile;
      byte biomeId;

      ResolveBiomeAtWorldXZ(worldX, worldZ, out profile, out biomeId);

      BiomeClimateSample sample = SampleBiomeClimate(worldX, worldZ);
      BiomeBlendSample blend = ResolveBiomeBlendAtWorldXZ(worldX, worldZ);

      System.Text.StringBuilder sb = new System.Text.StringBuilder();

      sb.Append(
          $"Biome Debug XZ=({worldX:0.00}, {worldZ:0.00}) " +
          $"WinnerBiomeId={biomeId}, " +
          $"Elevation={sample.Elevation:0.000}, " +
          $"Rise={sample.Rise:0.000}, " +
          $"Scale={BiomeRuleWorldScale:0.000}, " +
          $"BlendCount={blend.Count}"
      );

      if (blend.IsValid)
      {
        for (int i = 0; i < blend.Count; i++)
        {
          BiomeBlendContributor contributor = blend.GetContributor(i);

          sb.Append(
              $" | Blend[{i}] Id={contributor.BiomeId}" +
              $" Weight={contributor.Weight:0.000}"
          );
        }
      }

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

      float rise = Noise01(x, z, 0.0035f, 0.0024f, 61.37f);
      float harshness = Noise01(x, z, 0.0058f, 0.0061f, 93.73f);

      return new BiomeClimateSample(
          rise,
          harshness
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