using TMPro;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  [DefaultExecutionOrder(-31999)]
  public sealed class CubusTmpRuntimeFontGuard : MonoBehaviour
  {
    private static TMP_FontAsset runtimeFontAsset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<CubusTmpRuntimeFontGuard>() != null)
      {
        return;
      }

      GameObject go = new("Cubus TMP Runtime Font Guard");
      go.AddComponent<CubusTmpRuntimeFontGuard>();
      DontDestroyOnLoad(go);
    }

    private void Awake()
    {
      ApplyFonts();
    }

    private void Update()
    {
      ApplyFonts();
    }

    public static TMP_FontAsset GetRuntimeFontAsset()
    {
      if (runtimeFontAsset != null)
      {
        return runtimeFontAsset;
      }

      Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      if (font == null)
      {
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
      }

      if (font != null)
      {
        runtimeFontAsset = TMP_FontAsset.CreateFontAsset(font);
      }

      return runtimeFontAsset;
    }

    public static void ApplyFonts()
    {
      TMP_FontAsset fontAsset = GetRuntimeFontAsset();
      if (fontAsset == null)
      {
        return;
      }

      TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = 0; i < texts.Length; i++)
      {
        TMP_Text text = texts[i];
        if (text == null)
        {
          continue;
        }

        if (text.font == null || text.font.name.Contains("LiberationSans", System.StringComparison.OrdinalIgnoreCase))
        {
          text.font = fontAsset;
        }
      }
    }
  }
}
