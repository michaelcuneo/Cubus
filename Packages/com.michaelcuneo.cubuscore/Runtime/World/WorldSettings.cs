using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
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

    [Header("Smooth Density")]
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

    [Tooltip("Minimum chunk coordinate included when building the initial generated world database.")]
    public Vector3Int GenerationMinChunk = new(-4, -4, -4);

    [Tooltip("Maximum chunk coordinate included when building the initial generated world database.")]
    public Vector3Int GenerationMaxChunk = new(4, 4, 4);

    [HideInInspector] public Vector2Int GenerationMinChunkXZ = new(-4, -4);
    [HideInInspector] public Vector2Int GenerationMaxChunkXZ = new(4, 4);
    [HideInInspector] public int BlockMinChunkY = -1;
    [HideInInspector] public int BlockMaxChunkY = 2;
    [HideInInspector] public int DensityMinChunkY = -1;
    [HideInInspector] public int DensityMaxChunkY = 2;

    [Header("World Bounds")]
    public bool UseWorldBounds = true;
    public Vector2Int WorldMinChunkXZ = new(-1024, -1024);
    public Vector2Int WorldMaxChunkXZ = new(1024, 1024);

    [Header("Missing Chunk Policy")]
    public MissingChunkPolicy MissingChunkPolicy = MissingChunkPolicy.TreatAsEmpty;

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

    public void GetGenerationChunkBounds3D(
        out int minChunkX,
        out int maxChunkX,
        out int minChunkY,
        out int maxChunkY,
        out int minChunkZ,
        out int maxChunkZ)
    {
      minChunkX = Mathf.Min(GenerationMinChunk.x, GenerationMaxChunk.x);
      maxChunkX = Mathf.Max(GenerationMinChunk.x, GenerationMaxChunk.x);
      minChunkY = Mathf.Min(GenerationMinChunk.y, GenerationMaxChunk.y);
      maxChunkY = Mathf.Max(GenerationMinChunk.y, GenerationMaxChunk.y);
      minChunkZ = Mathf.Min(GenerationMinChunk.z, GenerationMaxChunk.z);
      maxChunkZ = Mathf.Max(GenerationMinChunk.z, GenerationMaxChunk.z);
    }

    public void GetGenerationChunkBoundsXZ(
        out int minChunkX,
        out int maxChunkX,
        out int minChunkZ,
        out int maxChunkZ)
    {
      GetGenerationChunkBounds3D(
        out minChunkX,
        out maxChunkX,
        out _,
        out _,
        out minChunkZ,
        out maxChunkZ
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
        result.Add(new BiomeBlendContributor(
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile()),
            GetActiveBiomeId(),
            1.0f
        ));

        result.Normalize();
        return result;
      }

      BiomeClimateSample climate = SampleBiomeClimate(worldX, worldZ);
      float feather = Mathf.Clamp(BiomeBlendFeather, 0.0001f, 1.0f);

      BiomeWorldRule fallbackRule = null;
      int fallbackPriority = int.MinValue;

      int minPriority = int.MaxValue;
      int maxPriority = int.MinValue;

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

        minPriority = Mathf.Min(minPriority, rule.Priority);
        maxPriority = Mathf.Max(maxPriority, rule.Priority);
      }

      if (minPriority == int.MaxValue || maxPriority == int.MinValue)
      {
        AddFallbackBiome(result, fallbackRule);
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

      float priorityRange = Mathf.Max(1.0f, maxPriority - minPriority);

      // Bigger window = actual biome terrain blending.
      // This is intentionally wider than the previous lower/upper resolver.
      float priorityWindow = Mathf.Max(
          2.0f,
          priorityRange * Mathf.Lerp(0.10f, 0.65f, feather)
      );

      float totalWeight = 0.0f;

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null || rule.IsFallback)
        {
          continue;
        }

        float validity = rule.GetMatchScore(climate, feather);

        if (validity <= 0.0001f)
        {
          continue;
        }

        float priorityDistance = Mathf.Abs(rule.Priority - targetPriority);
        float priorityT = Mathf.Clamp01(1.0f - priorityDistance / priorityWindow);
        float priorityWeight = priorityT * priorityT * (3.0f - 2.0f * priorityT);

        if (priorityWeight <= 0.0001f)
        {
          continue;
        }

        float weight = validity * priorityWeight;

        if (weight <= 0.0001f)
        {
          continue;
        }

        AddRuleBiome(result, rule, weight);
        totalWeight += weight;
      }

      if (totalWeight <= 0.0001f || result.Count <= 0)
      {
        int nearestIndex = FindNearestValidBiomeRuleIndex(
            targetPriority,
            climate,
            feather
        );

        if (nearestIndex >= 0)
        {
          AddRuleBiome(result, BiomeWorldRules[nearestIndex], 1.0f);
        }
        else
        {
          AddFallbackBiome(result, fallbackRule);
        }
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

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null || rule.IsFallback)
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

    private void AddRuleBiome(
        BiomeBlendSample result,
        BiomeWorldRule rule,
        float weight)
    {
      if (rule == null || rule.Biome == null)
      {
        return;
      }

      result.Add(new BiomeBlendContributor(
          new TerrainGenerationProfileSnapshot(rule.Biome.GenerationProfile),
          (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255),
          weight
      ));
    }

    private void AddFallbackBiome(
        BiomeBlendSample result,
        BiomeWorldRule fallbackRule)
    {
      if (fallbackRule != null && fallbackRule.Biome != null)
      {
        AddRuleBiome(result, fallbackRule, 1.0f);
        return;
      }

      result.Add(new BiomeBlendContributor(
          new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile()),
          GetActiveBiomeId(),
          1.0f
      ));
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
      GetGenerationChunkBounds3D(
        out _,
        out _,
        out minY,
        out maxY,
        out _,
        out _
      );
    }

    public void GetEffectiveDensityChunkYRange(out int minY, out int maxY)
    {
      GetGenerationChunkBounds3D(
        out _,
        out _,
        out minY,
        out maxY,
        out _,
        out _
      );
    }

    public bool IsInsideWorldBounds(Vector3Int chunkCoord)
    {
      if (!UseWorldBounds)
      {
        return true;
      }

      int minX = Mathf.Min(WorldMinChunkXZ.x, WorldMaxChunkXZ.x);
      int maxX = Mathf.Max(WorldMinChunkXZ.x, WorldMaxChunkXZ.x);
      int minZ = Mathf.Min(WorldMinChunkXZ.y, WorldMaxChunkXZ.y);
      int maxZ = Mathf.Max(WorldMinChunkXZ.y, WorldMaxChunkXZ.y);

      return chunkCoord.x >= minX &&
             chunkCoord.x <= maxX &&
             chunkCoord.z >= minZ &&
             chunkCoord.z <= maxZ;
    }

    public bool IsInsideGeneratedBounds(Vector3Int chunkCoord)
    {
      GetGenerationChunkBounds3D(
          out int minChunkX,
          out int maxChunkX,
          out int minChunkY,
          out int maxChunkY,
          out int minChunkZ,
          out int maxChunkZ
      );

      return chunkCoord.x >= minChunkX &&
             chunkCoord.x <= maxChunkX &&
             chunkCoord.y >= minChunkY &&
             chunkCoord.y <= maxChunkY &&
             chunkCoord.z >= minChunkZ &&
             chunkCoord.z <= maxChunkZ;
    }
  }
}
