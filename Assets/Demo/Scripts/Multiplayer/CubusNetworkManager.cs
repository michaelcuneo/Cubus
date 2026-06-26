using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Owns the SpacetimeDB <see cref="DbConnection"/> for the demo: connects, pumps the
  /// client message loop every frame, marshals work from background threads onto the main
  /// thread, and exposes connection state to the other multiplayer components.
  /// </summary>
  [DefaultExecutionOrder(-500)]
  public sealed class CubusNetworkManager : MonoBehaviour
  {
    [Header("Server")]
    [Tooltip("SpacetimeDB host URI. Use http://127.0.0.1:3000 for a local `spacetime start`.")]
    [SerializeField] private string serverUri = "http://127.0.0.1:3000";

    [Tooltip("Published module name (e.g. the name used in `spacetime publish <name>`).")]
    [SerializeField] private string moduleName = "cubus";

    [Tooltip("Subscribe to the authoritative world_chunk table. Off by default: a blanket subscription downloads every stored chunk at once, which does not scale (use a near-player subscription before enabling).")]
    [SerializeField] private bool subscribeToWorldChunks = false;

    [Tooltip("Leave this off for launcher-driven gameplay. The CubusLauncher/CubusGameplayLaunchBootstrap decides whether Local mode disconnects or Connected mode calls Connect().")]
    [SerializeField] private bool connectOnStart = false;
    [SerializeField] private bool verboseLogging = true;

    [Tooltip("Automatically attach the in-game chat overlay (CubusChatUI) to this object on startup.")]
    [SerializeField] private bool enableChatOverlay = true;

    [Tooltip("Automatically attach the Escape exit-to-Windows overlay (CubusExitMenu) to this object on startup.")]
    [SerializeField] private bool enableExitMenu = true;

    [Tooltip("If the first connect fails (commonly a stale auth token saved while connected to a different server), clear the saved token and retry once with a fresh identity.")]
    [SerializeField] private bool retryFreshIdentityOnConnectError = false;
    private readonly ConcurrentQueue<Action> mainThreadActions = new();

    private bool hasRetriedWithFreshToken;

    public static CubusNetworkManager Instance { get; private set; }

    public DbConnection Conn { get; private set; }
    public Identity LocalIdentity { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsSubscriptionApplied { get; private set; }
    public string WorldId { get; set; } = "demo_world";
    public string ServerUri => serverUri;
    public string ModuleName => moduleName;

    /// <summary>Whether authoritative world_chunk sync (subscription + uploads) is enabled.</summary>
    public bool WorldChunkSyncEnabled => subscribeToWorldChunks;

    /// <summary>Raised on the main thread once the connection is established and identity known.</summary>
    public event Action<DbConnection, Identity> Connected;

    /// <summary>Raised on the main thread once the initial subscription set has been applied.</summary>
    public event Action<DbConnection> SubscriptionApplied;

    /// <summary>Raised on the main thread when the connection drops.</summary>
    public event Action Disconnected;

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }

      Instance = this;
      DontDestroyOnLoad(gameObject);

      if (enableChatOverlay && GetComponent<CubusChatUI>() == null)
      {
        gameObject.AddComponent<CubusChatUI>();
      }

      if (enableExitMenu && GetComponent<CubusExitMenu>() == null)
      {
        gameObject.AddComponent<CubusExitMenu>();
      }
    }

    private void Start()
    {
      if (connectOnStart)
      {
        Connect();
      }
    }

    public void ConfigureServer(string uri, string module, string worldId)
    {
      if (Conn != null)
      {
        Debug.LogWarning("[CubusNetwork] ConfigureServer ignored while connected. Disconnect first.");
        return;
      }

      if (!string.IsNullOrWhiteSpace(uri))
      {
        serverUri = uri.Trim();
      }

      if (!string.IsNullOrWhiteSpace(module))
      {
        moduleName = module.Trim();
      }

      if (!string.IsNullOrWhiteSpace(worldId))
      {
        WorldId = worldId.Trim();
      }
    }

    public void Connect()
    {
      if (Conn != null)
      {
        Debug.LogWarning("[CubusNetwork] Already connected/connecting.");
        return;
      }

      hasRetriedWithFreshToken = false;
      ConnectInternal();
    }

    private void ConnectInternal()
    {
      string authToken = AuthTokenStore.LoadToken();

      if (verboseLogging)
      {
        Debug.Log($"[CubusNetwork] Connecting to {serverUri} / {moduleName} as world '{WorldId}'...");
      }

      DbConnection.Builder builder = DbConnection.Builder()
          .WithUri(serverUri)
          .WithModuleName(moduleName)
          .OnConnect(HandleConnected)
          .OnConnectError(HandleConnectError)
          .OnDisconnect(HandleDisconnected);

      if (!string.IsNullOrWhiteSpace(authToken))
      {
        builder = builder.WithToken(authToken);
      }

      Conn = builder.Build();
    }

    public void Disconnect()
    {
      if (Conn == null && !IsConnected)
      {
        return;
      }

      try
      {
        Conn?.Disconnect();
      }
      catch (Exception ex)
      {
        Debug.LogWarning($"[CubusNetwork] Disconnect threw: {ex.Message}");
      }

      Conn = null;
      IsConnected = false;
      IsSubscriptionApplied = false;
      LocalIdentity = default;
      mainThreadActions.Enqueue(() => Disconnected?.Invoke());
    }

    private void Update()
    {
      while (mainThreadActions.TryDequeue(out Action action))
      {
        action?.Invoke();
      }

      Conn?.FrameTick();
    }

    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }

      Disconnect();
    }

    private void HandleConnected(DbConnection conn, Identity identity, string token)
    {
      if (!string.IsNullOrWhiteSpace(token))
      {
        AuthTokenStore.SaveToken(token);
      }

      mainThreadActions.Enqueue(() =>
      {
        IsConnected = true;
        LocalIdentity = identity;

        if (verboseLogging)
        {
          Debug.Log($"[CubusNetwork] Connected as {identity}.");
        }

        Connected?.Invoke(conn, identity);
        Subscribe(conn);
      });
    }

    private void HandleConnectError(Exception error)
    {
      mainThreadActions.Enqueue(() =>
      {
        Debug.LogError($"[CubusNetwork] Connection failed: {error.Message}");

        Conn = null;
        IsConnected = false;
        IsSubscriptionApplied = false;
        LocalIdentity = default;

        if (retryFreshIdentityOnConnectError && !hasRetriedWithFreshToken)
        {
          hasRetriedWithFreshToken = true;
          AuthTokenStore.ClearToken();
          Debug.LogWarning("[CubusNetwork] Cleared saved auth token and retrying once with a fresh identity.");
          ConnectInternal();
        }
      });
    }

    private void HandleDisconnected(DbConnection conn, Exception error)
    {
      mainThreadActions.Enqueue(() =>
      {
        if (verboseLogging)
        {
          Debug.Log(error == null
              ? "[CubusNetwork] Disconnected."
              : $"[CubusNetwork] Disconnected with error: {error.Message}");
        }

        Conn = null;
        IsConnected = false;
        IsSubscriptionApplied = false;
        LocalIdentity = default;
        Disconnected?.Invoke();
      });
    }

    private void Subscribe(DbConnection conn)
    {
      List<string> queries = new()
      {
        "SELECT * FROM player",
        $"SELECT * FROM chat_message WHERE world_id = '{EscapeSqlLiteral(WorldId)}'",
        $"SELECT * FROM voxel_edit WHERE world_id = '{EscapeSqlLiteral(WorldId)}'",
        $"SELECT * FROM world_state WHERE world_id = '{EscapeSqlLiteral(WorldId)}'"
      };

      if (subscribeToWorldChunks)
      {
        queries.Add($"SELECT * FROM world_chunk WHERE world_id = '{EscapeSqlLiteral(WorldId)}'");
      }

      conn.SubscriptionBuilder()
          .OnApplied(_ => mainThreadActions.Enqueue(() =>
          {
            IsSubscriptionApplied = true;

            if (verboseLogging)
            {
              Debug.Log("[CubusNetwork] Initial subscription applied.");
            }

            SubscriptionApplied?.Invoke(conn);
          }))
          .Subscribe(queries.ToArray());
    }

    private static string EscapeSqlLiteral(string value)
    {
      return (value ?? string.Empty).Replace("'", "''");
    }
  }
}
