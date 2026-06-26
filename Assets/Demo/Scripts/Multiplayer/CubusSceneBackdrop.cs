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
      EnsureBackdrop(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
      EnsureBackdrop(scene);
    }

    private static void EnsureBackdrop(Scene scene)
    {
      if (!scene.IsValid() || !scene.isLoaded || !ShouldUseBackdrop(scene.name))
      {
        return;
      }

      if (GameObject.Find(BackdropName) != null)
      {
        return;
      }

      GameObject canvasObject = new(BackdropName);
      SceneManager.MoveGameObjectToScene(canvasObject, scene);

      Canvas canvas = canvasObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = -1000;

      CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
      scaler.matchWidthOrHeight = 0.5f;

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
      return sceneName == "CubusLauncher" || sceneName == "CubusLoading";
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
