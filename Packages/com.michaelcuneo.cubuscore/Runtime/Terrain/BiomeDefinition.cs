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

    [Header("Climate Tags (0..1)")]
    [Range(0.0f, 1.0f)]
    public float Temperature = 0.5f;

    [Range(0.0f, 1.0f)]
    public float Elevation = 0.5f;

    [Range(0.0f, 1.0f)]
    public float Rise = 0.5f;

    [Range(0.0f, 1.0f)]
    public float Harshness = 0.5f;

    [Range(0.0f, 1.0f)]
    public float Erosion = 0.5f;

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

      GenerationProfile.MaterialLayers = new System.Collections.Generic.List<MaterialLayer>(MaterialSet.MaterialLayers.Count);
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