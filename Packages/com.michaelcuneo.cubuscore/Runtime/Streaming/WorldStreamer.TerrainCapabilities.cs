using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private bool IsAnyTerrainEnabled =>
      IsBlockTerrainEnabled || IsDensityTerrainEnabled;

    private bool IsBlockTerrainEnabled =>
      world != null &&
      world.Settings != null &&
      world.Settings.TerrainSystem == TerrainSystem.Block;

    private bool IsDensityTerrainEnabled =>
      world != null &&
      world.Settings != null &&
      world.Settings.TerrainSystem == TerrainSystem.SmoothDensity;
  }
}