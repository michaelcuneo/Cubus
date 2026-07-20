using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Demo.Scripts.Rendering
{
  [Serializable]
  public sealed class DemoDetailScatterProfile
  {
    public string DisplayName = "Detail";

    [Range(1, 65535)] public int MaterialId = 1;
    public int[] AtlasTiles = new[] { 0 };
    [Range(0f, 1f)] public float Coverage = 0.35f;
    [Range(0, 8)] public int MaxClustersPerVoxel = 2;
    [Range(0.05f, 4f)] public float MinHeight = 0.45f;
    [Range(0.05f, 4f)] public float MaxHeight = 0.9f;
    [Range(0.05f, 4f)] public float MinWidth = 0.45f;
    [Range(0.05f, 4f)] public float MaxWidth = 0.85f;
    public Color Tint = Color.white;
    [Range(0f, 1f)] public float TintVariation = 0.12f;
    [Range(0f, 2f)] public float SwayStrength = 1f;
  }

  [Serializable]
  public sealed class DemoBiomeDetailScatterRule
  {
    [Range(0, 255)] public int BiomeId = 1;
    public string BiomeName = "Biome";
    public List<DemoDetailScatterProfile> Profiles = new();
  }

  [CreateAssetMenu(
      fileName = "Demo Biome Detail Scatter Settings",
      menuName = "Cubus Demo/Biome Detail Scatter Settings")]
  public sealed class DemoBiomeDetailScatterSettings : ScriptableObject
  {
    [Header("Foliage Atlas")]
    public Texture2D FoliageAtlas;
    [Range(1, 16)] public int AtlasColumns = 4;
    [Range(1, 16)] public int AtlasRows = 4;

    [Header("Authored Per-Biome Overrides")]
    public List<DemoBiomeDetailScatterRule> BiomeRules = new();

    public DemoDetailScatterProfile GetAuthoredProfile(byte biomeId, ushort materialId)
    {
      if (BiomeRules == null)
      {
        return null;
      }

      for (int i = 0; i < BiomeRules.Count; i++)
      {
        DemoBiomeDetailScatterRule rule = BiomeRules[i];
        if (rule == null || Mathf.Clamp(rule.BiomeId, 0, 255) != biomeId || rule.Profiles == null)
        {
          continue;
        }

        for (int j = 0; j < rule.Profiles.Count; j++)
        {
          DemoDetailScatterProfile profile = rule.Profiles[j];
          if (profile != null && Mathf.Clamp(profile.MaterialId, 1, 65535) == materialId)
          {
            return profile;
          }
        }
      }

      return null;
    }
  }
}