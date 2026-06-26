using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class CubusSceneBackdrop : MonoBehaviour
  {
    private const string BackdropName = "Cubus Scene Backdrop";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      SceneManager.sceneLoaded -= HandleSceneLoaded;
      SceneManager.sceneLoaded += HandleSceneLoaded;
      SceneManager.sceneUnloaded -= HandleSceneUnloaded;
      SceneManager.sceneUnloaded += HandleSceneUnloaded;
      SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
      SceneManager.activeSceneChanged += HandleActiveSceneChanged;

      SyncBackdrops();
    }

    public static void SyncBackdrops()
    {
      RemoveBackdropsOutsideLauncherAndLoadingScenes();
      EnsureBackdropsForLoadedLauncherAndLoadingScenes();
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

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
      SyncBackdrops();
    }

    private static void HandleSceneUnloaded(Scene scene)
    {
      SyncBackdrops();
    }

    private static void HandleActiveSceneChanged(Scene previous, Scene current)
    {
      SyncBackdrops();
    }

    private static void RemoveBackdropsOutsideLauncherAndLoadingScenes()
    {
      CubusSceneBackdrop[] backdrops = FindObjectsByType<CubusSceneBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = backdrops.Length - 1; i >= 0; i--)
      {
        CubusSceneBackdrop backdrop = backdrops[i];
        if (backdrop == null)
        {
          continue;
        }

        Scene ownerScene = backdrop.gameObject.scene;
        if (!ownerScene.IsValid() || !ownerScene.isLoaded || !ShouldUseBackdrop(ownerScene.name))
        {
          Destroy(backdrop.gameObject);
        }
      }
    }

    private static void EnsureBackdropsForLoadedLauncherAndLoadingScenes()
    {
      for (int i = 0; i < SceneManager.sceneCount; i++)
      {
        Scene scene = SceneManager.GetSceneAt(i);
        if (!scene.IsValid() || !scene.isLoaded || !ShouldUseBackdrop(scene.name))
        {
          continue;
        }

        if (!SceneHasBackdrop(scene))
        {
          CreateBackdrop(scene);
        }
      }
    }

    private static bool SceneHasBackdrop(Scene scene)
    {
      GameObject[] roots = scene.GetRootGameObjects();
      for (int i = 0; i < roots.Length; i++)
      {
        if (roots[i] != null && roots[i].GetComponentInChildren<CubusSceneBackdrop>(true) != null)
        {
          return true;
        }
      }

      return false;
    }

    private static void CreateBackdrop(Scene scene)
    {
      GameObject canvasObject = new(BackdropName);
      SceneManager.MoveGameObjectToScene(canvasObject, scene);
      canvasObject.AddComponent<CubusSceneBackdrop>();

      Canvas canvas = canvasObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = -1000;

      GraphicRaycaster raycaster = canvasObject.AddComponent<GraphicRaycaster>();
      raycaster.enabled = false;

      RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
      StretchToParent(canvasRect);

      Image background = CreateFullScreenImage(canvasRect, "Full Screen Background");
      background.color = new Color(0.02f, 0.025f, 0.035f, 1.0f);
    }

    private static bool ShouldUseBackdrop(string sceneName)
    {
      return string.Equals(sceneName, "CubusLauncher", System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(sceneName, "CubusLoading", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Image CreateFullScreenImage(RectTransform parent, string name)
    {
      GameObject imageObject = new(name);
      imageObject.transform.SetParent(parent, false);

      RectTransform rect = imageObject.AddComponent<RectTransform>();
      StretchToParent(rect);

      Image image = imageObject.AddComponent<Image>();
      image.raycastTarget = false;
      return image;
    }

    private static void StretchToParent(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = Vector2.zero;
      rect.sizeDelta = Vector2.zero;
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.localScale = Vector3.one;
    }
  }
}
