using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [CreateAssetMenu(
      fileName = "Biome Definition",
      menuName = "CubusCore/Terrain/Biome Definition"
  )]
  public sealed class BiomeDefinition : ScriptableObject
  {
    [Header("Identity")]
    [Range(0, 255)]
    public int BiomeId = 1;

    public string BiomeName = "Biome";

    [Header("Generation")]
    public TerrainGenerationProfile GenerationProfile = new();

    [Header("Material Set")]
    public BiomeMaterialSet MaterialSet;

    [Tooltip("When enabled, keeps GenerationProfile material IDs synced from MaterialSet.")]
    public bool AutoApplyMaterialSet = true;

    [Header("Generic XYZ Data")]
    public Vector3 SettingsXYZ = Vector3.one;
    public Vector3 TextureXYZ = Vector3.one;
    public Vector3 RuleXYZ = Vector3.one;

    [Header("Biome Variables (Game Mechanics)")]
    [Tooltip("Optional overrides for realistic biome variables (temperature, " +
             "erosion, humidity, ...). Only add the variables this biome should " +
             "differ on; anything not listed uses the world default. These are " +
             "gameplay values and do NOT affect terrain placement.")]
    public List<BiomeVariableOverride> VariableOverrides = new();

    // Returns true and the overridden value if this biome explicitly sets the
    // given variable. Otherwise returns false (caller should use the world
    // default).
    public bool TryGetVariableOverride(BiomeVariableType type, out float value)
    {
      if (VariableOverrides != null)
      {
        for (int i = 0; i < VariableOverrides.Count; i++)
        {
          if (VariableOverrides[i].Type == type)
          {
            value = VariableOverrides[i].Value;
            return true;
          }
        }
      }

      value = 0.0f;
      return false;
    }

    [ContextMenu("Apply Material Set To Generation Profile")]
    public void ApplyMaterialSetToGenerationProfile()
    {
      if (MaterialSet == null || MaterialSet.MaterialLayers == null)
      {
        return;
      }

      if (GenerationProfile == null)
      {
        GenerationProfile = new TerrainGenerationProfile();
      }

      GenerationProfile.MaterialLayers = new List<MaterialLayer>(MaterialSet.MaterialLayers.Count);
      for (int i = 0; i < MaterialSet.MaterialLayers.Count; i++)
      {
        MaterialLayer src = MaterialSet.MaterialLayers[i];
        GenerationProfile.MaterialLayers.Add(new MaterialLayer
        {
          MaxDepthBelowSurface = src.MaxDepthBelowSurface,
          MaterialId = Mathf.Clamp(src.MaterialId, 1, 65535),
          Label = src.Label,
        });
      }
    }

    private void OnValidate()
    {
      if (AutoApplyMaterialSet)
      {
        ApplyMaterialSetToGenerationProfile();
      }
    }
  }
}