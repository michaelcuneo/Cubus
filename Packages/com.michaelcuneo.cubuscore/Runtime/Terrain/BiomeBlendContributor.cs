using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World
{
  public readonly struct BiomeBlendContributor
  {
    public readonly TerrainGenerationProfileSnapshot Profile;
    public readonly byte BiomeId;
    public readonly float Weight;

    public BiomeBlendContributor(
        TerrainGenerationProfileSnapshot profile,
        byte biomeId,
        float weight)
    {
      Profile = profile;
      BiomeId = biomeId;
      Weight = weight;
    }
  }
}