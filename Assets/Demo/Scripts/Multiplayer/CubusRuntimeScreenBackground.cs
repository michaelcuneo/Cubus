using UnityEngine;
using UnityEngine.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class CubusRuntimeScreenBackground : MonoBehaviour
  {
    private sealed class AppliedBackground : MonoBehaviour
    {
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<CubusRuntimeScreenBackground>() != null)
      {
        return;
      }

      GameObject go = new("Cubus Runtime Screen Backgrounds");
      go.AddComponent<CubusRuntimeScreenBackground>();
      DontDestroyOnLoad(go);
    }

    private void LateUpdate()
    {
      ApplyToCanvas("Cubus Launcher Canvas");
      ApplyToCanvas("Cubus Loading Canvas");
    }

    private static void ApplyToCanvas(string canvasName)
    {
      Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
      for (int i = 0; i < canvases.Length; i++)
      {
        Canvas canvas = canvases[i];
        if (canvas == null || canvas.gameObject.name != canvasName)
        {
          continue;
        }

        if (canvas.GetComponentInChildren<AppliedBackground>(true) != null)
        {
          continue;
        }

        CreateBackground(canvas.transform);
      }
    }

    private static void CreateBackground(Transform canvasTransform)
    {
      GameObject rootObject = new("Runtime Screen Background");
      rootObject.transform.SetParent(canvasTransform, false);
      rootObject.transform.SetAsFirstSibling();
      rootObject.AddComponent<AppliedBackground>();

      RectTransform root = rootObject.AddComponent<RectTransform>();
      Stretch(root);

      Image baseImage = CreateImage(root, "Base", new Color(0.006f, 0.010f, 0.018f, 1.0f));
      Stretch(baseImage.rectTransform);

      Image upperGlow = CreateImage(root, "Upper Atmospheric Glow", new Color(0.035f, 0.100f, 0.145f, 0.82f));
      RectTransform upperRect = upperGlow.rectTransform;
      upperRect.anchorMin = new Vector2(0.0f, 0.55f);
      upperRect.anchorMax = new Vector2(1.0f, 1.0f);
      upperRect.offsetMin = Vector2.zero;
      upperRect.offsetMax = Vector2.zero;

      Image lowerShade = CreateImage(root, "Lower Depth Shade", new Color(0.000f, 0.000f, 0.000f, 0.55f));
      RectTransform lowerRect = lowerShade.rectTransform;
      lowerRect.anchorMin = new Vector2(0.0f, 0.0f);
      lowerRect.anchorMax = new Vector2(1.0f, 0.48f);
      lowerRect.offsetMin = Vector2.zero;
      lowerRect.offsetMax = Vector2.zero;

      Image horizon = CreateImage(root, "Voxel Horizon Band", new Color(0.12f, 0.32f, 0.42f, 0.24f));
      RectTransform horizonRect = horizon.rectTransform;
      horizonRect.anchorMin = new Vector2(0.0f, 0.47f);
      horizonRect.anchorMax = new Vector2(1.0f, 0.51f);
      horizonRect.offsetMin = Vector2.zero;
      horizonRect.offsetMax = Vector2.zero;

      CreateGrid(root, 48.0f, new Color(0.35f, 0.76f, 1.0f, 0.055f));
      CreateVignette(root);
    }

    private static void CreateGrid(RectTransform parent, float spacing, Color color)
    {
      for (float x = -960.0f; x <= 960.0f; x += spacing)
      {
        Image line = CreateImage(parent, "Grid Vertical", color);
        RectTransform rect = line.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.0f);
        rect.anchorMax = new Vector2(0.5f, 1.0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0.0f);
        rect.sizeDelta = new Vector2(1.0f, 0.0f);
      }

      for (float y = -540.0f; y <= 540.0f; y += spacing)
      {
        Image line = CreateImage(parent, "Grid Horizontal", color);
        RectTransform rect = line.rectTransform;
        rect.anchorMin = new Vector2(0.0f, 0.5f);
        rect.anchorMax = new Vector2(1.0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0.0f, y);
        rect.sizeDelta = new Vector2(0.0f, 1.0f);
      }
    }

    private static void CreateVignette(RectTransform parent)
    {
      Image top = CreateImage(parent, "Vignette Top", new Color(0.0f, 0.0f, 0.0f, 0.34f));
      RectTransform topRect = top.rectTransform;
      topRect.anchorMin = new Vector2(0.0f, 0.82f);
      topRect.anchorMax = new Vector2(1.0f, 1.0f);
      topRect.offsetMin = Vector2.zero;
      topRect.offsetMax = Vector2.zero;

      Image bottom = CreateImage(parent, "Vignette Bottom", new Color(0.0f, 0.0f, 0.0f, 0.46f));
      RectTransform bottomRect = bottom.rectTransform;
      bottomRect.anchorMin = new Vector2(0.0f, 0.0f);
      bottomRect.anchorMax = new Vector2(1.0f, 0.22f);
      bottomRect.offsetMin = Vector2.zero;
      bottomRect.offsetMax = Vector2.zero;

      Image left = CreateImage(parent, "Vignette Left", new Color(0.0f, 0.0f, 0.0f, 0.30f));
      RectTransform leftRect = left.rectTransform;
      leftRect.anchorMin = new Vector2(0.0f, 0.0f);
      leftRect.anchorMax = new Vector2(0.16f, 1.0f);
      leftRect.offsetMin = Vector2.zero;
      leftRect.offsetMax = Vector2.zero;

      Image right = CreateImage(parent, "Vignette Right", new Color(0.0f, 0.0f, 0.0f, 0.30f));
      RectTransform rightRect = right.rectTransform;
      rightRect.anchorMin = new Vector2(0.84f, 0.0f);
      rightRect.anchorMax = new Vector2(1.0f, 1.0f);
      rightRect.offsetMin = Vector2.zero;
      rightRect.offsetMax = Vector2.zero;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = color;
      image.raycastTarget = false;
      return image;
    }

    private static void Stretch(RectTransform rect)
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
