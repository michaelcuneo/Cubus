using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  /// <summary>
  /// Releases initial terrain readiness once enough useful terrain has rendered
  /// around the streaming focus. Full view-distance settlement is not required
  /// before player placement; remaining chunks continue streaming normally.
  /// </summary>
  [DisallowMultipleComponent]
  [DefaultExecutionOrder(-20)]
  internal sealed class InitialTerrainReadinessFallback : MonoBehaviour
  {
    private const int RequiredMeshApplies = 9;
    private const float SettledDelaySeconds = 2.0f;
    private const float SpawnClearanceVoxels = 2.0f;

    private CubusWorld world;
    private WorldStreamer streamer;
    private float settledSince = -1.0f;

    private void Awake()
    {
      world = GetComponent<CubusWorld>();
      streamer = GetComponent<WorldStreamer>();
    }

    private void Update()
    {
      if (world == null || streamer == null || world.IsInitialTerrainReady)
      {
        return;
      }

      if (!world.IsWorldReady)
      {
        settledSince = -1.0f;
        return;
      }

      long applied = Mathf.Max(
          (float)streamer.TotalBlockMeshApplies,
          (float)streamer.TotalDensityMeshApplies) >= RequiredMeshApplies
          ? RequiredMeshApplies
          : 0;

      bool enoughTerrainApplied = applied >= RequiredMeshApplies;
      bool loadsSettled = streamer.PendingLoadCount == 0 &&
                          streamer.ActiveChunkLoadTaskCount == 0;
      bool workersSettled = streamer.ActiveBlockBuildTaskCount == 0 &&
                            streamer.ActiveDensityBuildTaskCount == 0;

      if (!enoughTerrainApplied || !loadsSettled || !workersSettled)
      {
        settledSince = -1.0f;
        return;
      }

      if (settledSince < 0.0f)
      {
        settledSince = Time.realtimeSinceStartup;
        return;
      }

      if (Time.realtimeSinceStartup - settledSince < SettledDelaySeconds)
      {
        return;
      }

      Vector3Int focusChunk = streamer.HasLastViewerChunkCoord
          ? streamer.LastViewerChunkCoord
          : Vector3Int.zero;

      Vector3Int focusVoxel = new(
          focusChunk.x * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize / 2,
          focusChunk.y * VoxelConstants.ChunkSize,
          focusChunk.z * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize / 2);

      Vector3 spawn = streamer.CalculateSurfaceWorldPositionFromVoxel(
          focusVoxel,
          SpawnClearanceVoxels);

      Debug.Log(
          $"[Cubus] Releasing initial terrain after spawn-area coverage rendered. " +
          $"PendingRender={streamer.PendingRenderCount}, Spawn={spawn}.");

      world.BroadcastInitialTerrainReady(spawn);
      enabled = false;
    }
  }

  internal static class InitialTerrainReadinessFallbackInstaller
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterInitialSceneLoad()
    {
      InstallInLoadedScenes();
      SceneManager.sceneLoaded -= OnSceneLoaded;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
      InstallInLoadedScenes();
    }

    private static void InstallInLoadedScenes()
    {
      WorldStreamer[] streamers = Object.FindObjectsByType<WorldStreamer>(
          FindObjectsInactive.Include,
          FindObjectsSortMode.None);

      for (int i = 0; i < streamers.Length; i++)
      {
        WorldStreamer streamer = streamers[i];
        if (streamer == null ||
            streamer.GetComponent<InitialTerrainReadinessFallback>() != null)
        {
          continue;
        }

        streamer.gameObject.AddComponent<InitialTerrainReadinessFallback>();
      }
    }
  }
}
