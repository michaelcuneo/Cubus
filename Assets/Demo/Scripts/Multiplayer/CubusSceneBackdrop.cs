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

      CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
      scaler.matchWidthOrHeight = 0.5f;

      GraphicRaycaster raycaster = canvasObject.AddComponent<GraphicRaycaster>();
      raycaster.enabled = false;

      RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
      canvasRect.anchorMin = Vector2.zero;
      canvasRect.anchorMax = Vector2.one;
      canvasRect.offsetMin = Vector2.zero;
      canvasRect.offsetMax = Vector2.zero;

      CreateImage(canvasRect, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.02f, 0.025f, 0.035f, 1.0f));
      CreateImage(canvasRect, "Center Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(760.0f, 860.0f), Vector2.zero, new Color(0.08f, 0.12f, 0.18f, 0.92f));
    }

    private static bool ShouldUseBackdrop(string sceneName)
    {
      return string.Equals(sceneName, "CubusLauncher", System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(sceneName, "CubusLoading", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition, Color color)
    {
      GameObject imageObject = new(name);
      imageObject.transform.SetParent(parent, false);

      RectTransform rect = imageObject.AddComponent<RectTransform>();
      rect.anchorMin = anchorMin;
      rect.anchorMax = anchorMax;
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.sizeDelta = sizeDelta;
      rect.anchoredPosition = anchoredPosition;

      Image image = imageObject.AddComponent<Image>();
      image.color = color;
      image.raycastTarget = false;
    }
  }
}
