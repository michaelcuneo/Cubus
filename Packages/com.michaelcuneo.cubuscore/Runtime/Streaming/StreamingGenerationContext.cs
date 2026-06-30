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
    private static HybridTerrainLayerGenerationMode hybridBlockLayerMode = HybridTerrainLayerGenerationMode.SparseOnly;
    private static HybridTerrainLayerGenerationMode hybridDensityLayerMode = HybridTerrainLayerGenerationMode.ProceduralTerrain;

    public static void Set(WorldSettings settings)
    {
      lock (SyncRoot)
      {
        if (settings == null)
        {
          hasContext = false;
          terrainSystem = default;
          worldSnapshot = default;
          hybridBlockLayerMode = HybridTerrainLayerGenerationMode.SparseOnly;
          hybridDensityLayerMode = HybridTerrainLayerGenerationMode.ProceduralTerrain;
          return;
        }

        terrainSystem = settings.TerrainSystem;
        worldSnapshot = WorldGenerationSnapshot.FromSettings(settings);
        hybridBlockLayerMode = terrainSystem == TerrainSystem.Hybrid
          ? HybridTerrainLayerGenerationMode.SparseOnly
          : HybridTerrainLayerGenerationMode.ProceduralTerrain;
        hybridDensityLayerMode = HybridTerrainLayerGenerationMode.ProceduralTerrain;
        hasContext = true;
      }
    }

    public static void SetHybridLayerGenerationModes(
        HybridTerrainLayerGenerationMode blockLayerMode,
        HybridTerrainLayerGenerationMode densityLayerMode)
    {
      lock (SyncRoot)
      {
        hybridBlockLayerMode = blockLayerMode;
        hybridDensityLayerMode = densityLayerMode;
      }
    }

    public static bool TryGet(out TerrainSystem activeTerrainSystem, out WorldGenerationSnapshot snapshot)
    {
      return TryGet(out activeTerrainSystem, out snapshot, out _, out _);
    }

    public static bool TryGet(
        out TerrainSystem activeTerrainSystem,
        out WorldGenerationSnapshot snapshot,
        out HybridTerrainLayerGenerationMode blockLayerMode,
        out HybridTerrainLayerGenerationMode densityLayerMode)
    {
      lock (SyncRoot)
      {
        if (hasContext)
        {
          activeTerrainSystem = terrainSystem;
          snapshot = worldSnapshot;
          blockLayerMode = hybridBlockLayerMode;
          densityLayerMode = hybridDensityLayerMode;
          return true;
        }
      }

      activeTerrainSystem = default;
      snapshot = default;
      blockLayerMode = HybridTerrainLayerGenerationMode.SparseOnly;
      densityLayerMode = HybridTerrainLayerGenerationMode.ProceduralTerrain;
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