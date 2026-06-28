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

    public static bool TryGet(out TerrainSystem activeTerrainSystem, out WorldGenerationSnapshot snapshot)
    {
      lock (SyncRoot)
      {
        if (hasContext)
        {
          activeTerrainSystem = terrainSystem;
          snapshot = worldSnapshot;
          return true;
        }
      }

      activeTerrainSystem = default;
      snapshot = default;
      return false;
    }

    public static bool TryGetBlockSnapshot(out WorldGenerationSnapshot snapshot)
    {
      if (TryGet(out TerrainSystem activeTerrainSystem, out snapshot) &&
          (activeTerrainSystem == TerrainSystem.Block || activeTerrainSystem == TerrainSystem.Hybrid))
      {
        return true;
      }

      snapshot = default;
      return false;
    }
  }
}
