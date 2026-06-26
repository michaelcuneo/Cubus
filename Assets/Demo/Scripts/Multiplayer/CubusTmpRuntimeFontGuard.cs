using TMPro;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  [DefaultExecutionOrder(-31999)]
  public sealed class CubusTmpRuntimeFontGuard : MonoBehaviour
  {
    private static TMP_FontAsset runtimeFontAsset;
    private static bool attemptedResolve;
    private static bool loggedMissingFont;

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

      if (attemptedResolve)
      {
        return null;
      }

      attemptedResolve = true;

      runtimeFontAsset = TMP_Settings.defaultFontAsset;
      if (runtimeFontAsset != null)
      {
        return runtimeFontAsset;
      }

      runtimeFontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
      if (runtimeFontAsset != null)
      {
        return runtimeFontAsset;
      }

      runtimeFontAsset = Resources.Load<TMP_FontAsset>("LiberationSans SDF");
      if (runtimeFontAsset != null)
      {
        return runtimeFontAsset;
      }

      if (!loggedMissingFont)
      {
        loggedMissingFont = true;
        Debug.LogWarning("[CubusUI] No TMP font asset was found. Import TMP Essential Resources or assign a default TMP font asset in Project Settings > TextMeshPro.");
      }

      return null;
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
        ConfigureText(texts[i]);
      }
    }

    public static void ConfigureText(TMP_Text text)
    {
      if (text == null)
      {
        return;
      }

      TMP_FontAsset fontAsset = GetRuntimeFontAsset();
      if (fontAsset == null)
      {
        text.enabled = false;
        return;
      }

      text.font = fontAsset;
      text.enabled = true;
    }
  }
}
