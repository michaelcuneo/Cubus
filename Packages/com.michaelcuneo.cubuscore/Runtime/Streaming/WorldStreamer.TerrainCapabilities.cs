using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    private bool IsAnyTerrainEnabled =>
      IsBlockTerrainEnabled || IsDensityTerrainEnabled;

    private bool IsHybridTerrainEnabled =>
      world != null &&
      world.Settings != null &&
      world.Settings.TerrainSystem == TerrainSystem.Hybrid;

    private bool IsBlockTerrainEnabled =>
      world != null &&
      world.Settings != null &&
      (world.Settings.TerrainSystem == TerrainSystem.Block || world.Settings.TerrainSystem == TerrainSystem.Hybrid);

    private bool IsDensityTerrainEnabled =>
      world != null &&
      world.Settings != null &&
      (world.Settings.TerrainSystem == TerrainSystem.SmoothDensity || world.Settings.TerrainSystem == TerrainSystem.Hybrid);
  }
}
