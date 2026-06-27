using UnityEngine;

namespace Assets.Demo.Scripts.Core
{
  /// <summary>
  /// Runtime-only developer mode switch. The console owns toggling this; launcher
  /// and gameplay UI can read it to expose destructive/reset tools during dev.
  /// </summary>
  public static class DemoDeveloperMode
  {
    private const string PrefKey = "Demo.DeveloperMode.Enabled";

    public static bool IsEnabled
    {
      get => PlayerPrefs.GetInt(PrefKey, 0) != 0;
      set
      {
        PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[DemoDev] Developer mode {(value ? "enabled" : "disabled")}.");
      }
    }
  }
}
