using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.UI
{
  /// <summary>
  /// Minimal IMGUI chat overlay backed by the replicated <c>chat_message</c> table.
  /// Press Enter to start typing, Enter again to send, or Escape to cancel.
  /// The chat panel stays hidden until the player is composing a message.
  /// </summary>
  public sealed class DemoChatUI : MonoBehaviour
  {
    [SerializeField] private int maxVisibleMessages = 12;
    [SerializeField] private KeyCode openKey = KeyCode.Return;
    [SerializeField] private Vector2 panelSize = new(460.0f, 260.0f);

    private readonly List<ChatMessage> messages = new();

    private DemoNetworkManager net;
    private bool callbacksRegistered;
    private bool composing;
    private string draft = string.Empty;
    private Vector2 scroll;

    private void OnEnable()
    {
      net = DemoNetworkManager.Instance;
      if (net != null)
      {
        net.Connected += HandleConnected;
        net.Disconnected += HandleDisconnected;
        if (net.IsConnected)
        {
          HandleConnected(net.Conn, net.LocalIdentity);
        }
      }
    }

    private void OnDisable()
    {
      if (net != null)
      {
        net.Connected -= HandleConnected;
        net.Disconnected -= HandleDisconnected;
      }
      UnregisterCallbacks();
      EndComposing(clearDraft: true);
    }

    private void HandleConnected(DbConnection conn, Identity identity) => RegisterCallbacks(conn);

    private void HandleDisconnected()
    {
      UnregisterCallbacks();
      messages.Clear();
    }

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.ChatMessage.OnInsert += HandleChatInsert;
      conn.Db.ChatMessage.OnDelete += HandleChatDelete;
      callbacksRegistered = true;

      messages.Clear();
      foreach (ChatMessage message in conn.Db.ChatMessage.Iter())
      {
        messages.Add(message);
      }
      SortMessages();
    }

    private void UnregisterCallbacks()
    {
      if (!callbacksRegistered || net == null || net.Conn == null)
      {
        callbacksRegistered = false;
        return;
      }

      net.Conn.Db.ChatMessage.OnInsert -= HandleChatInsert;
      net.Conn.Db.ChatMessage.OnDelete -= HandleChatDelete;
      callbacksRegistered = false;
    }

    private void HandleChatInsert(EventContext ctx, ChatMessage message)
    {
      messages.Add(message);
      SortMessages();
      scroll.y = float.MaxValue;
    }

    private void HandleChatDelete(EventContext ctx, ChatMessage message)
    {
      messages.RemoveAll(m => m.Id == message.Id);
    }

    private void SortMessages()
    {
      messages.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private void OnGUI()
    {
      if (CubusUiInput.ConsoleOpen)
      {
        if (composing)
        {
          EndComposing(clearDraft: true);
        }
        return;
      }

      if (net == null || !net.IsConnected)
      {
        return;
      }

      HandleInputEvents();

      if (!composing)
      {
        return;
      }

      float x = 10.0f;
      float y = Screen.height - panelSize.y - 10.0f;
      GUILayout.BeginArea(new Rect(x, y, panelSize.x, panelSize.y), GUI.skin.box);

      scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
      int start = Mathf.Max(0, messages.Count - maxVisibleMessages);
      for (int i = start; i < messages.Count; i++)
      {
        ChatMessage message = messages[i];
        GUILayout.Label($"<b>{message.SenderName}:</b> {message.Text}");
      }
      GUILayout.EndScrollView();

      GUILayout.Label($"> {draft}<color=#ffffffaa>_</color>");
      GUILayout.Label("<i>Enter to send  -  Esc to cancel</i>");

      GUILayout.EndArea();
    }

    private void HandleInputEvents()
    {
      Event current = Event.current;
      if (current.type != EventType.KeyDown)
      {
        return;
      }

      bool isSubmit = current.keyCode == openKey || current.keyCode == KeyCode.KeypadEnter;

      if (!composing)
      {
        if (isSubmit)
        {
          BeginComposing();
          current.Use();
        }

        return;
      }

      if (isSubmit)
      {
        TrySend();
        EndComposing(clearDraft: true);
        current.Use();
        return;
      }

      if (current.keyCode == KeyCode.Escape)
      {
        EndComposing(clearDraft: true);
        current.Use();
        return;
      }

      if (current.keyCode == KeyCode.Backspace)
      {
        if (draft.Length > 0)
        {
          draft = draft.Substring(0, draft.Length - 1);
        }

        current.Use();
      }
    }

    private void BeginComposing()
    {
      composing = true;
      CubusUiInput.ChatComposing = true;

      Keyboard keyboard = Keyboard.current;
      if (keyboard != null)
      {
        keyboard.onTextInput += HandleTextInput;
      }
    }

    private void EndComposing(bool clearDraft)
    {
      Keyboard keyboard = Keyboard.current;
      if (keyboard != null)
      {
        keyboard.onTextInput -= HandleTextInput;
      }

      if (clearDraft)
      {
        draft = string.Empty;
      }

      composing = false;
      CubusUiInput.ChatComposing = false;
    }

    private void HandleTextInput(char character)
    {
      if (!composing || CubusUiInput.ConsoleOpen)
      {
        return;
      }

      if (character < ' ' || character == (char)127)
      {
        return;
      }

      if (draft.Length >= 500)
      {
        return;
      }

      draft += character;
    }

    private void TrySend()
    {
      string text = draft.Trim();
      draft = string.Empty;

      if (text.Length == 0 || net == null || !net.IsSubscriptionApplied)
      {
        return;
      }

      net.Conn.Reducers.SendChat(text);
    }
  }
}
