using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod;
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

    [Tooltip("Global world seed. Changing it reshuffles all procedural noise " +
             "(terrain, caves, biome placement) deterministically.")]
    public int WorldSeed = 0;

    [Header("View Distance")]
    [Min(1)]
    public int ViewDistanceInChunks = 4;

    [Min(0.01f)]
    public float VoxelSize = VoxelConstants.DefaultVoxelSize;

    [Header("Distant Horizon LOD")]
    [Tooltip("Render coarse low-detail terrain in concentric bands beyond the " +
             "full-detail view distance, so terrain reaches the horizon cheaply. " +
             "Block terrain only.")]
    public bool EnableLodTerrain = true;

    [Tooltip("Number of LOD bands beyond the full-detail ring. Each band L uses " +
             "voxel stride 2^L (level 1 = 2x, level 2 = 4x, ...), so each band " +
             "covers exponentially more ground for a similar tile count.")]
    [Range(1, LodConstants.MaxLodLevel)]
    public int LodLevelCount = 4;

    [Tooltip("Thickness of each LOD band measured in that level's own tiles. The " +
             "world-space width of band L is therefore LodRingWidthInTiles * 2^L " +
             "base chunks.")]
    [Min(1)]
    public int LodRingWidthInTiles = 4;

    [Tooltip("Vertical extent of LOD tiles above and below the sampled surface, " +
             "measured in that level's tiles. Distant underground is never seen, " +
             "so keep this small (1 is usually enough).")]
    [Min(0)]
    public int LodVerticalRadiusInTiles = 1;

    [Tooltip("Depth of the downward skirt added around each LOD tile's perimeter, " +
             "in that tile's coarse voxels. Skirts hide the vertical cracks that " +
             "appear where neighbouring bands meet at different resolutions. 0 " +
             "disables skirts.")]
    [Range(0, 8)]
    public int LodSkirtDepthInCells = 2;

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

    [Header("World Default Biome Variables")]
    [Tooltip("World-wide default values for biome variables. Individual biomes " +
             "override only what they need; everything else uses these. Any " +
             "variable not listed here falls back to its built-in catalog default.")]
    public List<BiomeVariableOverride> WorldDefaultVariables = new();

    [NonSerialized] private Dictionary<byte, BiomeDefinition> biomeIdToDefinition;

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

    // World-wide default for a biome variable: an explicit WorldDefaultVariables
    // entry if present, otherwise the variable's built-in catalog default.
    public float GetWorldDefaultVariable(BiomeVariableType type)
    {
      if (WorldDefaultVariables != null)
      {
        for (int i = 0; i < WorldDefaultVariables.Count; i++)
        {
          if (WorldDefaultVariables[i].Type == type)
          {
            return WorldDefaultVariables[i].Value;
          }
        }
      }

      return BiomeVariableCatalog.DefaultValue(type);
    }

    // Call when BiomeWorldRules change at runtime so the BiomeId lookup rebuilds.
    public void InvalidateBiomeVariableCache()
    {
      biomeIdToDefinition = null;
    }

    private void EnsureBiomeIdLookup()
    {
      if (biomeIdToDefinition != null)
      {
        return;
      }

      biomeIdToDefinition = new Dictionary<byte, BiomeDefinition>();

      if (BiomeWorldRules == null)
      {
        return;
      }

      for (int i = 0; i < BiomeWorldRules.Count; i++)
      {
        BiomeWorldRule rule = BiomeWorldRules[i];

        if (rule == null || rule.Biome == null)
        {
          continue;
        }

        byte id = (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255);
        biomeIdToDefinition[id] = rule.Biome;
      }
    }

    // Blended value of a biome variable at a world position. Each contributing
    // biome supplies its override (or the world default if it has none), and the
    // contributions are weighted by the biome blend so the value transitions
    // smoothly across biome borders. Gameplay-facing; does not affect terrain.
    public float SampleBiomeVariable(float worldX, float worldZ, BiomeVariableType type)
    {
      float worldDefault = GetWorldDefaultVariable(type);

      BiomeBlendSample blend = ResolveBiomeBlendAtWorldXZ(worldX, worldZ);

      if (blend == null || !blend.IsValid || blend.Count <= 0)
      {
        return worldDefault;
      }

      EnsureBiomeIdLookup();

      float accumulated = 0.0f;
      float totalWeight = 0.0f;

      for (int i = 0; i < blend.Count; i++)
      {
        BiomeBlendContributor contributor = blend.GetContributor(i);

        if (contributor.Weight <= 0.0f)
        {
          continue;
        }

        float effective = worldDefault;

        if (biomeIdToDefinition.TryGetValue(contributor.BiomeId, out BiomeDefinition def) &&
            def != null &&
            def.TryGetVariableOverride(type, out float overrideValue))
        {
          effective = overrideValue;
        }

        accumulated += effective * contributor.Weight;
        totalWeight += contributor.Weight;
      }

      return totalWeight > 0.0001f ? accumulated / totalWeight : worldDefault;
    }

    // Value of a biome variable for the single dominant biome at a world
    // position (no blending across borders).
    public float SampleBiomeVariableDominant(float worldX, float worldZ, BiomeVariableType type)
    {
      float worldDefault = GetWorldDefaultVariable(type);

      ResolveBiomeAtWorldXZ(worldX, worldZ, out _, out byte biomeId);

      EnsureBiomeIdLookup();

      if (biomeIdToDefinition.TryGetValue(biomeId, out BiomeDefinition def) &&
          def != null &&
          def.TryGetVariableOverride(type, out float overrideValue))
      {
        return overrideValue;
      }

      return worldDefault;
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

    // Vertical chunk extent of the world for the active terrain system. This is
    // the authoritative "how tall is the world" answer used by streaming: the
    // full-detail and LOD systems fill this entire span rather than a thin band
    // around the surface, so undulating terrain never leaves voids.
    public void GetActiveVerticalChunkBounds(out int minChunkY, out int maxChunkY)
    {
      int lo;
      int hi;
      if (TerrainSystem == TerrainSystem.SmoothDensity)
      {
        lo = DensityMinChunkY;
        hi = DensityMaxChunkY;
      }
      else
      {
        lo = BlockMinChunkY;
        hi = BlockMaxChunkY;
      }

      minChunkY = Mathf.Min(lo, hi);
      maxChunkY = Mathf.Max(lo, hi);
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
        profile = new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile(), WorldSeed);
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
            new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile(), WorldSeed),
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
      bool hasNonFallback = false;

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

        hasNonFallback = true;
      }

      if (!hasNonFallback)
      {
        AddFallbackBiome(result, fallbackRule);
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

        float occlusion = 1.0f;

        for (int j = 0; j < BiomeWorldRules.Count; j++)
        {
          if (j == i)
          {
            continue;
          }

          BiomeWorldRule other = BiomeWorldRules[j];

          if (other == null || other.Biome == null || other.IsFallback)
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

        AddRuleBiome(result, rule, weight);
      }

      if (coverageComplement > 0.0001f)
      {
        AddFallbackBiome(result, fallbackRule, coverageComplement);
      }

      if (result.Count <= 0)
      {
        AddFallbackBiome(result, fallbackRule);
      }

      result.Normalize();
      return result;
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
          new TerrainGenerationProfileSnapshot(rule.Biome.GenerationProfile, WorldSeed),
          (byte)Mathf.Clamp(rule.Biome.BiomeId, 0, 255),
          weight
      ));
    }

    private void AddFallbackBiome(
        BiomeBlendSample result,
        BiomeWorldRule fallbackRule)
    {
      AddFallbackBiome(result, fallbackRule, 1.0f);
    }

    private void AddFallbackBiome(
        BiomeBlendSample result,
        BiomeWorldRule fallbackRule,
        float weight)
    {
      if (fallbackRule != null && fallbackRule.Biome != null)
      {
        AddRuleBiome(result, fallbackRule, weight);
        return;
      }

      result.Add(new BiomeBlendContributor(
          new TerrainGenerationProfileSnapshot(GetActiveGenerationProfile(), WorldSeed),
          GetActiveBiomeId(),
          weight
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
