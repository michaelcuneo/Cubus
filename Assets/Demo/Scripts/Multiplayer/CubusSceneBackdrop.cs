using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Legacy launcher/loading backdrop cleanup shim.
  /// Launcher and loading screens now own their own Canvas backgrounds, so this
  /// component no longer creates scene-wide backdrop canvases.
  /// </summary>
  public sealed class CubusSceneBackdrop : MonoBehaviour
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      RemoveAllBackdrops();
    }

    public static void SyncBackdrops()
    {
      RemoveAllBackdrops();
    }

    public static void RemoveAllBackdrops()
    {
      CubusSceneBackdrop[] backdrops = FindObjectsByType<CubusSceneBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = backdrops.Length - 1; i >= 0; i--)
      {
        if (backdrops[i] != null)
        {
          Destroy(backdrops[i].gameObject);
        }
      }
    }
  }
}
