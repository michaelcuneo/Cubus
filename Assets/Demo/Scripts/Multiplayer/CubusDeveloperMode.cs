using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Runtime-only developer mode switch. The console owns toggling this; launcher
  /// and gameplay UI can read it to expose destructive/reset tools during dev.
  /// </summary>
  public static class CubusDeveloperMode
  {
    private const string PrefKey = "Cubus.DeveloperMode.Enabled";

    public static bool IsEnabled
    {
      get => PlayerPrefs.GetInt(PrefKey, 0) != 0;
      set
      {
        PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[CubusDev] Developer mode {(value ? "enabled" : "disabled")}.");
      }
    }
  }
}
