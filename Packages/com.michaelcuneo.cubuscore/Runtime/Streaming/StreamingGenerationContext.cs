using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public static class StreamingGenerationContext
  {
    private static readonly object SyncRoot = new();

    private static bool hasContext;
    private static TerrainSystem terrainSystem;
    private static WorldGenerationSnapshot worldSnapshot;

    public static void Set(WorldSettings settings)
    {
      lock (SyncRoot)
      {
        if (settings == null)
        {
          hasContext = false;
          terrainSystem = default;
          worldSnapshot = default;
          return;
        }

        terrainSystem = settings.TerrainSystem;
        worldSnapshot = WorldGenerationSnapshot.FromSettings(settings);
        hasContext = true;
      }
    }

    public static bool TryGetBlockSnapshot(out WorldGenerationSnapshot snapshot)
    {
      lock (SyncRoot)
      {
        if (hasContext && terrainSystem == TerrainSystem.Block)
        {
          snapshot = worldSnapshot;
          return true;
        }
      }

      snapshot = default;
      return false;
    }
  }
}
