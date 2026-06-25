using System.Globalization;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Demo-game bridge for authoritative multiplayer world settings.
  /// SpaceTimeDB owns the tiny deterministic generation contract; Cubus Core only
  /// consumes the active runtime settings and generates terrain locally.
  /// </summary>
  [DefaultExecutionOrder(-450)]
  public sealed class NetworkedWorldStateBridge : MonoBehaviour
  {
    [Header("World")]
    [SerializeField] private CubusWorld world;

    [Header("Publishing")]
    [Tooltip("If this client joins an empty server world, publish the current scene's CubusWorld settings as the initial server world state. Disable this for normal non-dev clients once the server world exists.")]
    [SerializeField] private bool publishSceneSettingsIfMissing = true;

    [Tooltip("Increment when procedural generation code/settings interpretation changes and cached terrain must be regenerated.")]
    [SerializeField] private uint generatorVersion = 1;

    [Tooltip("Optional demo/profile id stored with the server world state. Keep stable for the same generation rules.")]
    [SerializeField] private string generationProfileId = "demo_default";

    private CubusNetworkManager net;
    private bool callbacksRegistered;
    private bool sawServerWorldState;
    private string appliedSignature;

    public bool HasServerWorldState => sawServerWorldState;
    public string AppliedSignature => appliedSignature;

    private void Awake()
    {
      if (world == null)
      {
        world = FindObjectOfType<CubusWorld>();
      }
    }

    private void OnEnable()
    {
      net = CubusNetworkManager.Instance;

      if (net == null)
      {
        return;
      }

      net.Connected += HandleConnected;
      net.SubscriptionApplied += HandleSubscriptionApplied;
      net.Disconnected += HandleDisconnected;

      if (net.IsConnected && net.Conn != null)
      {
        HandleConnected(net.Conn, net.LocalIdentity);
      }

      if (net.IsSubscriptionApplied && net.Conn != null)
      {
        HandleSubscriptionApplied(net.Conn);
      }
    }

    private void OnDisable()
    {
      if (net != null)
      {
        net.Connected -= HandleConnected;
        net.SubscriptionApplied -= HandleSubscriptionApplied;
        net.Disconnected -= HandleDisconnected;
      }

      UnregisterCallbacks();
    }

    private void HandleConnected(DbConnection conn, Identity identity)
    {
      RegisterCallbacks(conn);
    }

    private void HandleSubscriptionApplied(DbConnection conn)
    {
      RegisterCallbacks(conn);

      if (!sawServerWorldState && publishSceneSettingsIfMissing)
      {
        PublishCurrentSceneSettings();
      }
    }

    private void HandleDisconnected()
    {
      callbacksRegistered = false;
      sawServerWorldState = false;
    }

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.WorldState.OnInsert += HandleWorldStateInsert;
      conn.Db.WorldState.OnUpdate += HandleWorldStateUpdate;
      callbacksRegistered = true;

      foreach (WorldState state in conn.Db.WorldState.Iter())
      {
        Apply(state);
      }
    }

    private void UnregisterCallbacks()
    {
      if (!callbacksRegistered || net == null || net.Conn == null)
      {
        callbacksRegistered = false;
        return;
      }

      net.Conn.Db.WorldState.OnInsert -= HandleWorldStateInsert;
      net.Conn.Db.WorldState.OnUpdate -= HandleWorldStateUpdate;
      callbacksRegistered = false;
    }

    private void HandleWorldStateInsert(EventContext ctx, WorldState state)
    {
      Apply(state);
    }

    private void HandleWorldStateUpdate(EventContext ctx, WorldState oldState, WorldState newState)
    {
      Apply(newState);
    }

    private void Apply(WorldState state)
    {
      if (net != null && state.WorldId != net.WorldId)
      {
        return;
      }

      if (world == null || world.Settings == null)
      {
        Debug.LogWarning("[CubusWorldState] Cannot apply server world state: no CubusWorld found.");
        return;
      }

      sawServerWorldState = true;

      string signature = BuildSignature(state);
      bool changed = appliedSignature != signature;

      if (!changed)
      {
        return;
      }

      appliedSignature = signature;
      ApplyToWorldSettings(state);

      Debug.Log(
          $"[CubusWorldState] Applied server world state. " +
          $"WorldId={state.WorldId}, Seed={state.WorldSeed}, GeneratorVersion={state.GeneratorVersion}, " +
          $"Terrain={(TerrainSystem)state.TerrainSystem}, Signature={state.GenerationSignature}"
      );

      ResetGeneratedTerrainAfterWorldStateChange();
    }

    [ContextMenu("Publish Current Scene World State")]
    public void PublishCurrentSceneSettings()
    {
      SendCurrentSceneSettings(resetWorldEdits: false);
    }

    [ContextMenu("Reset Server World From Current Scene Settings")]
    public void ResetServerWorldFromCurrentSceneSettings()
    {
      SendCurrentSceneSettings(resetWorldEdits: true);
    }

    private void SendCurrentSceneSettings(bool resetWorldEdits)
    {
      if (net == null)
      {
        net = CubusNetworkManager.Instance;
      }

      if (world == null)
      {
        world = FindObjectOfType<CubusWorld>();
      }

      if (net == null || !net.IsConnected || net.Conn == null)
      {
        Debug.LogWarning("[CubusWorldState] Cannot publish world state: not connected.");
        return;
      }

      if (world == null || world.Settings == null)
      {
        Debug.LogWarning("[CubusWorldState] Cannot publish world state: no CubusWorld found.");
        return;
      }

      WorldSettings settings = world.Settings;
      settings.GetActiveVerticalChunkBounds(out int minChunkY, out int maxChunkY);

      string signature = BuildSignature(
          net.WorldId,
          settings.WorldSeed,
          generatorVersion,
          (byte)settings.TerrainSystem,
          settings.VoxelSize,
          minChunkY,
          maxChunkY,
          settings.WorldMinChunkXZ.x,
          settings.WorldMinChunkXZ.y,
          settings.WorldMaxChunkXZ.x,
          settings.WorldMaxChunkXZ.y,
          generationProfileId);

      if (resetWorldEdits)
      {
        net.Conn.Reducers.ResetWorldState(
            net.WorldId,
            settings.WorldSeed,
            generatorVersion,
            (byte)settings.TerrainSystem,
            settings.VoxelSize,
            minChunkY,
            maxChunkY,
            settings.WorldMinChunkXZ.x,
            settings.WorldMinChunkXZ.y,
            settings.WorldMaxChunkXZ.x,
            settings.WorldMaxChunkXZ.y,
            generationProfileId,
            signature);
      }
      else
      {
        net.Conn.Reducers.SetWorldState(
            net.WorldId,
            settings.WorldSeed,
            generatorVersion,
            (byte)settings.TerrainSystem,
            settings.VoxelSize,
            minChunkY,
            maxChunkY,
            settings.WorldMinChunkXZ.x,
            settings.WorldMinChunkXZ.y,
            settings.WorldMaxChunkXZ.x,
            settings.WorldMaxChunkXZ.y,
            generationProfileId,
            signature);
      }

      Debug.Log(
          $"[CubusWorldState] {(resetWorldEdits ? "Reset" : "Published")} server world state. " +
          $"WorldId={net.WorldId}, Seed={settings.WorldSeed}, GeneratorVersion={generatorVersion}, Signature={signature}"
      );
    }

    private void ApplyToWorldSettings(WorldState state)
    {
      WorldSettings settings = world.Settings;

      settings.WorldSeed = state.WorldSeed;
      settings.TerrainSystem = (TerrainSystem)state.TerrainSystem;
      settings.VoxelSize = Mathf.Max(0.001f, state.VoxelSize);

      settings.UseWorldBounds = true;
      settings.WorldMinChunkXZ = new Vector2Int(state.WorldMinChunkX, state.WorldMinChunkZ);
      settings.WorldMaxChunkXZ = new Vector2Int(state.WorldMaxChunkX, state.WorldMaxChunkZ);

      settings.BlockMinChunkY = state.MinChunkY;
      settings.BlockMaxChunkY = state.MaxChunkY;
      settings.DensityMinChunkY = state.MinChunkY;
      settings.DensityMaxChunkY = state.MaxChunkY;

      settings.InvalidateBiomeVariableCache();
    }

    private void ResetGeneratedTerrainAfterWorldStateChange()
    {
      if (world == null)
      {
        return;
      }

      WorldStreamer streamer = world.GetComponent<WorldStreamer>();

      world.ClearWorld();

      // Streamed worlds will refill chunks around the viewer. Non-streamed demo scenes
      // need an explicit full generation pass after the settings swap.
      if (streamer == null)
      {
        world.GenerateWorld();
      }
    }

    private static string BuildSignature(WorldState state)
    {
      if (!string.IsNullOrWhiteSpace(state.GenerationSignature))
      {
        return state.GenerationSignature;
      }

      return BuildSignature(
          state.WorldId,
          state.WorldSeed,
          state.GeneratorVersion,
          state.TerrainSystem,
          state.VoxelSize,
          state.MinChunkY,
          state.MaxChunkY,
          state.WorldMinChunkX,
          state.WorldMinChunkZ,
          state.WorldMaxChunkX,
          state.WorldMaxChunkZ,
          state.GenerationProfileId);
    }

    private static string BuildSignature(
        string worldId,
        int worldSeed,
        uint generatorVersion,
        byte terrainSystem,
        float voxelSize,
        int minChunkY,
        int maxChunkY,
        int worldMinChunkX,
        int worldMinChunkZ,
        int worldMaxChunkX,
        int worldMaxChunkZ,
        string profileId)
    {
      return string.Join(
          "|",
          worldId ?? string.Empty,
          worldSeed.ToString(CultureInfo.InvariantCulture),
          generatorVersion.ToString(CultureInfo.InvariantCulture),
          terrainSystem.ToString(CultureInfo.InvariantCulture),
          voxelSize.ToString("R", CultureInfo.InvariantCulture),
          minChunkY.ToString(CultureInfo.InvariantCulture),
          maxChunkY.ToString(CultureInfo.InvariantCulture),
          worldMinChunkX.ToString(CultureInfo.InvariantCulture),
          worldMinChunkZ.ToString(CultureInfo.InvariantCulture),
          worldMaxChunkX.ToString(CultureInfo.InvariantCulture),
          worldMaxChunkZ.ToString(CultureInfo.InvariantCulture),
          profileId ?? string.Empty);
    }
  }
}
