using System;
using System.Collections.Concurrent;
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

    [Tooltip("Subscribe to every table on connect. Convenient for the demo; replace with scoped queries for larger worlds.")]
    [SerializeField] private bool subscribeToAllTables = true;

    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool verboseLogging = true;

    private readonly ConcurrentQueue<Action> mainThreadActions = new();

    public static CubusNetworkManager Instance { get; private set; }

    public DbConnection Conn { get; private set; }
    public Identity LocalIdentity { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsSubscriptionApplied { get; private set; }
    public string WorldId { get; set; } = "demo_world";

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
    }

    private void Start()
    {
      if (connectOnStart)
      {
        Connect();
      }
    }

    public void Connect()
    {
      if (Conn != null)
      {
        return;
      }

      try
      {
        Conn = DbConnection.Builder()
            .WithUri(serverUri)
            .WithDatabaseName(moduleName)
            .WithToken(AuthToken.Token)
            .OnConnect(HandleConnect)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnect)
            .Build();
      }
      catch (Exception ex)
      {
        Debug.LogError($"[CubusNetwork] Failed to start connection: {ex.Message}");
        Conn = null;
      }
    }

    private void HandleConnect(DbConnection conn, Identity identity, string authToken)
    {
      AuthToken.SaveToken(authToken);
      LocalIdentity = identity;
      IsConnected = true;

      if (verboseLogging)
      {
        Debug.Log($"[CubusNetwork] Connected as {identity}.");
      }

      Connected?.Invoke(conn, identity);

      var builder = conn.SubscriptionBuilder()
          .OnApplied(HandleSubscriptionApplied)
          .OnError(HandleSubscriptionError);

      if (subscribeToAllTables)
      {
        builder.SubscribeToAllTables();
      }
      else
      {
        builder.Subscribe(new[]
        {
          "SELECT * FROM player",
          "SELECT * FROM chat_message",
          $"SELECT * FROM voxel_edit WHERE WorldId = '{WorldId}'",
          $"SELECT * FROM world_chunk WHERE WorldId = '{WorldId}'",
        });
      }
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
    {
      IsSubscriptionApplied = true;
      if (verboseLogging)
      {
        Debug.Log("[CubusNetwork] Subscription applied.");
      }
      SubscriptionApplied?.Invoke(Conn);
    }

    private void HandleSubscriptionError(ErrorContext ctx, Exception ex)
    {
      Debug.LogError($"[CubusNetwork] Subscription error: {ex.Message}");
    }

    private void HandleConnectError(Exception ex)
    {
      Debug.LogError($"[CubusNetwork] Connection error: {ex.Message}");
      IsConnected = false;
    }

    private void HandleDisconnect(DbConnection conn, Exception ex)
    {
      IsConnected = false;
      IsSubscriptionApplied = false;
      if (ex != null)
      {
        Debug.LogWarning($"[CubusNetwork] Disconnected: {ex.Message}");
      }
      Disconnected?.Invoke();
    }

    /// <summary>
    /// Queues an action to run on the main thread just before the next client tick.
    /// Safe to call from background threads (e.g. the streaming chunk store).
    /// </summary>
    public void RunOnMainThread(Action action)
    {
      if (action != null)
      {
        mainThreadActions.Enqueue(action);
      }
    }

    private void Update()
    {
      while (mainThreadActions.TryDequeue(out Action action))
      {
        try
        {
          action();
        }
        catch (Exception ex)
        {
          Debug.LogError($"[CubusNetwork] Main-thread action failed: {ex}");
        }
      }

      Conn?.FrameTick();
    }

    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }

      try
      {
        Conn?.Disconnect();
      }
      catch
      {
        // Ignore disconnect failures during teardown.
      }
      finally
      {
        Conn = null;
      }
    }
  }
}
