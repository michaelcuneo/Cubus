using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Water
{
  /// <summary>
  /// Adds the first-pass water system to streamed Cubus worlds without requiring
  /// scene or prefab edits. A manually added VoxelWaterSystem is preserved.
  /// </summary>
  internal static class VoxelWaterAutoInstaller
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
        if (streamer == null || streamer.GetComponent<VoxelWaterSystem>() != null)
        {
          continue;
        }

        streamer.gameObject.AddComponent<VoxelWaterSystem>();
      }
    }
  }
}
