using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Minimal IMGUI pause overlay. Press Escape to open a centered
  /// "Exit to Windows?" confirmation; confirm quits the application, cancel
  /// (or Escape again) dismisses it. While open the cursor is unlocked and
  /// shown so the buttons are clickable even when a player controller locks it.
  /// </summary>
  public sealed class CubusExitMenu : MonoBehaviour
  {
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [SerializeField] private Vector2 panelSize = new(320.0f, 140.0f);

    private bool isOpen;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;

    private void OnGUI()
    {
      Event current = Event.current;
      if (current.type == EventType.KeyDown && current.keyCode == toggleKey)
      {
        // Don't pop the menu while the player is typing in chat (Esc cancels chat).
        if (!isOpen && CubusUiInput.ChatComposing)
        {
          return;
        }

        SetOpen(!isOpen);
        current.Use();
      }

      if (!isOpen)
      {
        return;
      }

      float x = (Screen.width - panelSize.x) * 0.5f;
      float y = (Screen.height - panelSize.y) * 0.5f;
      GUILayout.BeginArea(new Rect(x, y, panelSize.x, panelSize.y), GUI.skin.box);

      GUILayout.FlexibleSpace();
      GUILayout.Label("Exit to Windows?");
      GUILayout.FlexibleSpace();

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Exit", GUILayout.Height(32.0f)))
      {
        QuitToDesktop();
      }
      if (GUILayout.Button("Cancel", GUILayout.Height(32.0f)))
      {
        SetOpen(false);
      }
      GUILayout.EndHorizontal();

      GUILayout.EndArea();
    }

    private void SetOpen(bool open)
    {
      if (open == isOpen)
      {
        return;
      }

      isOpen = open;
      CubusUiInput.MenuOpen = open;

      if (open)
      {
        // Remember how the game had the cursor (player controllers usually lock
        // and hide it) so it can be restored when the menu closes.
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
      }
      else
      {
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
      }
    }

    private void OnDisable()
    {
      if (isOpen)
      {
        SetOpen(false);
      }
    }

    private static void QuitToDesktop()
    {
#if UNITY_EDITOR
      UnityEditor.EditorApplication.isPlaying = false;
#else
      Application.Quit();
#endif
    }
  }
}
