using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;

namespace Assets.Demo.Scripts.Core
{
  public enum DemoGameLaunchMode
  {
    None = 0,
    Local = 1,
    Connected = 2,
  }

  /// <summary>
  /// Static handoff from the starter/menu scene into the gameplay scene.
  /// The starter UI writes this before loading gameplay; the gameplay bootstrap
  /// consumes it once and starts the selected local or connected world flow.
  /// </summary>
  public static class DemoGameLaunchContext
  {
    public static bool HasLaunch { get; private set; }
    public static DemoGameLaunchMode Mode { get; private set; }
    public static string ServerUri { get; private set; }
    public static string ModuleName { get; private set; }
    public static string WorldId { get; private set; }
    public static TerrainSystem TerrainSystem { get; private set; }
    public static int WorldSeed { get; private set; }
    public static float VoxelSize { get; private set; }
    public static int MinChunkY { get; private set; }
    public static int MaxChunkY { get; private set; }
    public static Vector2Int WorldMinChunkXZ { get; private set; }
    public static Vector2Int WorldMaxChunkXZ { get; private set; }
    public static bool DeleteLocalWorldBeforeLaunch { get; private set; }
    public static bool ResetConnectedWorldOnLaunch { get; private set; }

    public static void Set(
        DemoGameLaunchMode mode,
        string serverUri,
        string moduleName,
        string worldId,
        TerrainSystem terrainSystem,
        int worldSeed,
        float voxelSize,
        int minChunkY,
        int maxChunkY,
        Vector2Int worldMinChunkXZ,
        Vector2Int worldMaxChunkXZ,
        bool deleteLocalWorldBeforeLaunch = false,
        bool resetConnectedWorldOnLaunch = false)
    {
      HasLaunch = true;
      Mode = mode;
      ServerUri = serverUri ?? string.Empty;
      ModuleName = moduleName ?? string.Empty;
      WorldId = string.IsNullOrWhiteSpace(worldId) ? "demo_world" : worldId.Trim();
      TerrainSystem = terrainSystem;
      WorldSeed = worldSeed;
      VoxelSize = Mathf.Max(0.001f, voxelSize);
      MinChunkY = minChunkY;
      MaxChunkY = maxChunkY;
      WorldMinChunkXZ = worldMinChunkXZ;
      WorldMaxChunkXZ = worldMaxChunkXZ;
      DeleteLocalWorldBeforeLaunch = deleteLocalWorldBeforeLaunch;
      ResetConnectedWorldOnLaunch = resetConnectedWorldOnLaunch;
    }

    public static void Clear()
    {
      HasLaunch = false;
      Mode = DemoGameLaunchMode.None;
      ServerUri = string.Empty;
      ModuleName = string.Empty;
      WorldId = string.Empty;
      TerrainSystem = TerrainSystem.Block;
      WorldSeed = 0;
      VoxelSize = 1.0f;
      MinChunkY = -1;
      MaxChunkY = 2;
      WorldMinChunkXZ = new Vector2Int(-1024, -1024);
      WorldMaxChunkXZ = new Vector2Int(1024, 1024);
      DeleteLocalWorldBeforeLaunch = false;
      ResetConnectedWorldOnLaunch = false;
    }
  }
}
