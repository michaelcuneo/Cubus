using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Minimal IMGUI chat overlay backed by the replicated <c>chat_message</c> table.
  /// Press Enter to focus the input, type a message, and press Enter again to send.
  /// </summary>
  public sealed class CubusChatUI : MonoBehaviour
  {
    [SerializeField] private int maxVisibleMessages = 12;
    [SerializeField] private KeyCode toggleKey = KeyCode.Return;
    [SerializeField] private Vector2 panelSize = new(460.0f, 260.0f);

    private readonly List<ChatMessage> messages = new();

    private CubusNetworkManager net;
    private bool callbacksRegistered;
    private bool inputFocused;
    private string draft = string.Empty;
    private Vector2 scroll;
    private bool focusRequested;

    private void OnEnable()
    {
      net = CubusNetworkManager.Instance;
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
      if (net == null || !net.IsConnected)
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

      GUI.SetNextControlName("ChatInput");
      draft = GUILayout.TextField(draft, 500);

      if (focusRequested)
      {
        GUI.FocusControl("ChatInput");
        focusRequested = false;
      }

      GUILayout.EndArea();

      HandleInputEvents();
    }

    private void HandleInputEvents()
    {
      Event current = Event.current;
      if (current.type != EventType.KeyDown || current.keyCode != toggleKey)
      {
        return;
      }

      if (!inputFocused)
      {
        inputFocused = true;
        focusRequested = true;
        current.Use();
        return;
      }

      TrySend();
      inputFocused = false;
      current.Use();
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
