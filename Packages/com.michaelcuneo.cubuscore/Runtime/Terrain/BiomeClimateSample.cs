namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain
{
  public readonly struct BiomeClimateSample
  {
    public readonly float Elevation;
    public readonly float Rise;

    public BiomeClimateSample(float elevation, float rise)
    {
      Elevation = elevation;
      Rise = rise;
    }
  }
}