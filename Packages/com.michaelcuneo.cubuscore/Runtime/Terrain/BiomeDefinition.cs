using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  [CreateAssetMenu(
      fileName = "Biome Definition",
      menuName = "CubusCore/Terrain/Biome Definition"
  )]
  public sealed class BiomeDefinition : ScriptableObject
  {
    [Range(0, 255)]
    public int BiomeId = 1;

    public string BiomeName = "Biome";

    public TerrainGenerationProfile GenerationProfile = new();
  }
}