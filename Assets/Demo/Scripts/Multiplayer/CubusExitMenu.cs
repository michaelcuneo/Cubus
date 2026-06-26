using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
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
        if (CubusUiInput.ShouldSuppressMenuInput)
        {
          current.Use();
          return;
        }

        if (!isOpen && CubusUiInput.IsCapturing)
        {
          current.Use();
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
        CloseApplication();
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

    private static void CloseApplication()
    {
#if UNITY_EDITOR
      UnityEditor.EditorApplication.isPlaying = false;
#else
      Application.Quit();
#endif
    }
  }
}
