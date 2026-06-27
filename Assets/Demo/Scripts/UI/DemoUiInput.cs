using UnityEngine;

namespace Assets.Demo.Scripts.UI
{
  /// <summary>
  /// Shared flag set by in-game overlays while they are capturing keyboard/mouse.
  /// Gameplay input that reads devices directly checks this and stands down so UI
  /// keystrokes and mouse input do not leak into the player or other overlays.
  /// </summary>
  public static class DemoUIInput
  {
    /// <summary>True while the chat input field is focused for typing.</summary>
    public static bool ChatComposing;

    /// <summary>True while the exit confirmation menu is open.</summary>
    public static bool MenuOpen;

    /// <summary>True while the runtime console is open and owns keyboard/mouse input.</summary>
    public static bool ConsoleOpen;

    /// <summary>Frame number through which Escape/menu toggles should be swallowed.</summary>
    public static int SuppressMenuInputUntilFrame = -1;

    /// <summary>True when any overlay is capturing input; gameplay should pause.</summary>
    public static bool IsCapturing => ChatComposing || MenuOpen || ConsoleOpen;

    public static bool ShouldSuppressMenuInput => ConsoleOpen || SuppressMenuInputUntilFrame >= Time.frameCount;

    public static void SuppressMenuInputForCurrentFrame()
    {
      SuppressMenuInputUntilFrame = Time.frameCount;
    }
  }
}
