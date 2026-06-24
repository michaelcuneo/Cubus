namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Shared flag set by in-game IMGUI overlays (chat, exit menu) while they are
  /// capturing keyboard/mouse. Gameplay input that reads devices directly (the
  /// first-person controller's look/movement/cursor-lock) checks this and stands
  /// down so typing and button clicks do not leak into the player.
  /// </summary>
  public static class CubusUiInput
  {
    /// <summary>True while the chat input field is focused for typing.</summary>
    public static bool ChatComposing;

    /// <summary>True while the exit confirmation menu is open.</summary>
    public static bool MenuOpen;

    /// <summary>True when any overlay is capturing input; gameplay should pause.</summary>
    public static bool IsCapturing => ChatComposing || MenuOpen;
  }
}
