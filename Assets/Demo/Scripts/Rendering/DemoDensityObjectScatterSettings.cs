using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Demo.Scripts.Rendering
{
  [Serializable]
  public sealed class DemoDensityObjectScatterGroup
  {
    public string DisplayName = "Objects";
    public bool Enabled = true;
    public GameObject[] Prefabs;

    [Header("Population")]
    [Range(0f, 1f)] public float SpawnChance = 0.18f;
    [Min(1)] public int TriangleStride = 18;
    [Min(0)] public int MinimumPerChunk;
    [Min(0)] public int MaximumPerChunk = 8;
    [Min(0f)] public float MinimumSpacing = 2.5f;

    [Header("Surface Rules")]
    [Range(-1f, 1f)] public float MinimumUpwardNormal = 0.72f;
    public float MinimumWorldHeight = -10000f;
    public float MaximumWorldHeight = 10000f;
    public bool AlignToSurface = true;
    public bool RandomYaw = true;

    [Header("Transform")]
    public Vector2 UniformScaleRange = new(0.7f, 1.3f);
    public Vector3 PositionOffset;
  }

  [Serializable]
  public sealed class DemoDensityBiomeScatterRule
  {
    [Range(0, 255)] public int BiomeId;
    public string BiomeName = "Biome";
    public List<DemoDensityObjectScatterGroup> Groups = new();
  }

  [CreateAssetMenu(
    fileName = "Demo Density Object Scatter Settings",
    menuName = "Cubus Demo/Density Object Scatter Settings")]
  public sealed class DemoDensityObjectScatterSettings : ScriptableObject
  {
    [Header("Fallback Groups")]
    public List<DemoDensityObjectScatterGroup> DefaultGroups = new();

    [Header("Biome Rules")]
    public List<DemoDensityBiomeScatterRule> BiomeRules = new();

    public IReadOnlyList<DemoDensityObjectScatterGroup> GetGroups(byte biomeId)
    {
      if (BiomeRules != null)
      {
        for (int i = 0; i < BiomeRules.Count; i++)
        {
          DemoDensityBiomeScatterRule rule = BiomeRules[i];
          if (rule != null && Mathf.Clamp(rule.BiomeId, 0, 255) == biomeId)
          {
            return rule.Groups;
          }
        }
      }

      return DefaultGroups;
    }
  }
}
