using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Water
{
  /// <summary>
  /// Keeps the first-pass water system disabled while water rendering is being
  /// reworked. This prevents runtime auto-installation from affecting startup.
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
      VoxelWaterSystem[] waterSystems = Object.FindObjectsByType<VoxelWaterSystem>(
          FindObjectsInactive.Include);

      for (int i = 0; i < waterSystems.Length; i++)
      {
        VoxelWaterSystem water = waterSystems[i];
        if (water == null)
        {
          continue;
        }

        water.GenerateWater = false;
        water.enabled = false;
      }
    }
  }
}
