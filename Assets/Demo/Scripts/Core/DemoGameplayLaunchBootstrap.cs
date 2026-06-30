using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assets.Demo.Scripts.Multiplayer;
using Assets.Demo.Scripts.Spawn;

namespace Assets.Demo.Scripts.Core
{
  [DefaultExecutionOrder(-10000)]
  public sealed class DemoGameplayLaunchBootstrap : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private DemoNetworkManager network;
    [SerializeField] private bool requireLauncherSelection = true;
    [SerializeField] private DemoInitialTerrainSpawnGate spawnGate;
    [SerializeField] private string launcherSceneName = "DemoLauncher";
    [SerializeField] private float connectedWorldReadyTimeoutSeconds = 30.0f;
    [SerializeField] private bool logLoadingDiagnostics = true;

    private string loadingStage = "Waiting for launch context";
    private string loadingDetail = string.Empty;
    private string lastWarning = string.Empty;
    private float loadingStartedAt;
    private bool isPreparingWorld;
    private bool hasFinishedPreparation;
    private bool disabledStreamerAutoStartForLaunch;
    private WorldStreamer cachedStreamer;

    private void Awake()
    {
      WorldPersistence.SuppressAutoLoadOnStart = true;
      CubusWorld.SuppressGenerateOnStart = true;
      loadingStartedAt = Time.realtimeSinceStartup;
      FindReferences();
      DisableStreamerAutoStartForLaunch();
    }

    private IEnumerator Start()
    {
      SetStage("Checking launch context", string.Empty);

      if (!DemoGameLaunchContext.HasLaunch)
      {
        if (requireLauncherSelection)
        {
          SetStage("No launch context", "Gameplay scene was opened directly. Loading launcher scene instead.");
          Debug.LogWarning("[DemoLaunch] Gameplay scene loaded without launcher selection. Loading launcher scene instead.");
          network?.Disconnect();

          Cursor.lockState = CursorLockMode.None;
          Cursor.visible = true;

          if (!string.IsNullOrWhiteSpace(launcherSceneName))
          {
            SceneManager.LoadScene(launcherSceneName);
          }
        }

        yield break;
      }

      isPreparingWorld = true;
      SetStage("Applying launch settings", DescribeLaunchSettings());

      if (!ApplyLaunchSettingsToWorld())
      {
        isPreparingWorld = false;
        yield break;
      }

      yield return null;

      if (DemoGameLaunchContext.Mode == DemoGameLaunchMode.Local)
      {
        yield return PrepareLocalWorldThenSpawn();
      }
      else if (DemoGameLaunchContext.Mode == DemoGameLaunchMode.Connected)
      {
        yield return PrepareConnectedWorldThenSpawn();
      }
      else
      {
        lastWarning = $"Unsupported launch mode: {DemoGameLaunchContext.Mode}";
        Debug.LogWarning($"[DemoLaunch] {lastWarning}");
      }

      isPreparingWorld = false;
      hasFinishedPreparation = world != null && world.IsInitialTerrainReady;
      LogDiagnosticsSnapshot("Preparation finished");
    }

    private bool ApplyLaunchSettingsToWorld()
    {
      if (world == null || world.Settings == null)
      {
        lastWarning = "No DemoWorld found in gameplay scene.";
        Debug.LogError($"[DemoLaunch] {lastWarning}");
        return false;
      }

      WorldSettings settings = world.Settings;
      settings.TerrainSystem = DemoGameLaunchContext.TerrainSystem;
      settings.WorldSeed = DemoGameLaunchContext.WorldSeed;
      settings.VoxelSize = DemoGameLaunchContext.VoxelSize;
      settings.UseWorldBounds = true;
      settings.WorldMinChunkXZ = DemoGameLaunchContext.WorldMinChunkXZ;
      settings.WorldMaxChunkXZ = DemoGameLaunchContext.WorldMaxChunkXZ;
      settings.BlockMinChunkY = DemoGameLaunchContext.MinChunkY;
      settings.BlockMaxChunkY = DemoGameLaunchContext.MaxChunkY;
      settings.DensityMinChunkY = DemoGameLaunchContext.MinChunkY;
      settings.DensityMaxChunkY = DemoGameLaunchContext.MaxChunkY;
      settings.InvalidateBiomeVariableCache();

      if (DemoGameLaunchContext.TerrainSystem == TerrainSystem.Hybrid)
      {
        StreamingGenerationContext.SetHybridLayerGenerationModes(
          HybridTerrainLayerGenerationMode.SparseOnly,
          HybridTerrainLayerGenerationMode.ProceduralTerrain);
      }
      else
      {
        StreamingGenerationContext.SetHybridLayerGenerationModes(
          HybridTerrainLayerGenerationMode.ProceduralTerrain,
          HybridTerrainLayerGenerationMode.ProceduralTerrain);
      }

      if (network != null)
      {
        network.WorldId = DemoGameLaunchContext.WorldId;
      }

      return true;
    }

    private IEnumerator PrepareLocalWorldThenSpawn()
    {
      SetStage("Preparing local world", "Disconnecting network and clearing local runtime state.");
      network?.Disconnect();

      if (world == null)
      {
        yield break;
      }

      world.ClearWorldAndOverrides();

      WorldStreamer streamer = GetStreamer();
      if (streamer != null)
      {
        SetStage("Prewarming streamed terrain", "Starting the WorldStreamer under launcher control.");
        Debug.Log($"[DemoLaunch] Preparing local streamed world '{DemoGameLaunchContext.WorldId}' before spawning.");
        yield return EnableStreamerForBootstrap(streamer);
        streamer.ClearStreamingState();
        streamer.RegenerateStreamedWorld();
        if (spawnGate != null)
        {
          yield return spawnGate.WaitAndRelease();
        }
        else
        {
          yield return WaitForInitialTerrainReady("local streamed world", 0.0f);
        }
      }
      else
      {
        SetStage("Generating non-streamed world", "Running DemoWorld.GenerateWorldAsync before player spawn.");
        Debug.Log($"[DemoLaunch] Generating local world '{DemoGameLaunchContext.WorldId}' before spawning.");
        yield return world.GenerateWorldAsync();
        world.BroadcastInitialTerrainReady(Vector3.zero);
      }

      SetStage("Local world ready", "Initial terrain is ready. Player spawn released.");
      Debug.Log($"[DemoLaunch] Local world '{DemoGameLaunchContext.WorldId}' is ready. Player spawn released.");
    }

    private IEnumerator PrepareConnectedWorldThenSpawn()
    {
      if (network == null)
      {
        lastWarning = "No DemoNetworkManager found in gameplay scene.";
        Debug.LogError($"[DemoLaunch] {lastWarning}");
        yield break;
      }

      SetStage("Preparing connected world", "Disconnecting any stale connection and starting the streamer under launcher control.");

      if (network.IsConnected || network.Conn != null)
      {
        network.Disconnect();
      }

      WorldStreamer streamer = GetStreamer();
      if (streamer != null)
      {
        yield return EnableStreamerForBootstrap(streamer);
        streamer.ClearStreamingState();
      }

      network.ConfigureServer(
          DemoGameLaunchContext.ServerUri,
          DemoGameLaunchContext.ModuleName,
          DemoGameLaunchContext.WorldId);

      SetStage("Connecting to SpaceTimeDB", $"{DemoGameLaunchContext.ServerUri} / {DemoGameLaunchContext.ModuleName} / {DemoGameLaunchContext.WorldId}");
      network.Connect();

      Debug.Log($"[DemoLaunch] Connecting to {DemoGameLaunchContext.ServerUri} / {DemoGameLaunchContext.ModuleName} / {DemoGameLaunchContext.WorldId}. Waiting for authoritative terrain before spawning.");
      yield return WaitForInitialTerrainReady("connected world", connectedWorldReadyTimeoutSeconds);
    }

    private IEnumerator WaitForInitialTerrainReady(string label, float timeoutSeconds)
    {
      if (world == null)
      {
        yield break;
      }

      float startedAt = Time.realtimeSinceStartup;
      float nextDiagnosticAt = startedAt;
      SetStage($"Waiting for {label} initial terrain", "Waiting for DemoWorld.BroadcastInitialTerrainReady. Streaming details are logged to the console.");

      while (!world.IsInitialTerrainReady)
      {
        if (timeoutSeconds > 0.0f && Time.realtimeSinceStartup - startedAt >= timeoutSeconds)
        {
          lastWarning = $"Timed out waiting for {label} initial terrain.";
          Debug.LogWarning($"[DemoLaunch] {lastWarning}");
          yield break;
        }

        if (logLoadingDiagnostics && Time.realtimeSinceStartup >= nextDiagnosticAt)
        {
          LogDiagnosticsSnapshot($"Still waiting for {label} initial terrain after {Time.realtimeSinceStartup - startedAt:0.0}s");
          nextDiagnosticAt = Time.realtimeSinceStartup + 5.0f;
        }

        yield return null;
      }
    }

    private IEnumerator EnableStreamerForBootstrap(WorldStreamer streamer)
    {
      if (streamer == null)
      {
        yield break;
      }

      if (!streamer.enabled)
      {
        streamer.enabled = true;
      }

      yield return null;
    }

    private void FindReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (storage == null)
      {
        storage = FindAnyObjectByType<CubusWorldStorage>();
      }

      if (network == null)
      {
        network = FindAnyObjectByType<DemoNetworkManager>();
      }

      if (spawnGate == null)
      {
        spawnGate = FindAnyObjectByType<DemoInitialTerrainSpawnGate>();
      }
    }

    private WorldStreamer GetStreamer()
    {
      if (cachedStreamer == null)
      {
        cachedStreamer = world != null ? world.GetComponent<WorldStreamer>() : FindAnyObjectByType<WorldStreamer>();
      }

      return cachedStreamer;
    }

    private void DisableStreamerAutoStartForLaunch()
    {
      WorldStreamer streamer = GetStreamer();
      if (streamer == null)
      {
        return;
      }

      if (!streamer.enabled)
      {
        return;
      }

      streamer.enabled = false;
      disabledStreamerAutoStartForLaunch = true;
      Debug.Log("[DemoLaunch] WorldStreamer disabled before Start so launcher bootstrap can control terrain preparation.");
    }

    private string DescribeLaunchSettings()
    {
      return $"Mode={DemoGameLaunchContext.Mode}, Terrain={DemoGameLaunchContext.TerrainSystem}, WorldId={DemoGameLaunchContext.WorldId}, Seed={DemoGameLaunchContext.WorldSeed}, Y={DemoGameLaunchContext.MinChunkY}..{DemoGameLaunchContext.MaxChunkY}, XZ={DemoGameLaunchContext.WorldMinChunkXZ}..{DemoGameLaunchContext.WorldMaxChunkXZ}";
    }

    private void SetStage(string stage, string detail)
    {
      loadingStage = stage ?? string.Empty;
      loadingDetail = detail ?? string.Empty;
      Debug.Log($"[DemoLaunch] {loadingStage}: {loadingDetail}");
    }

    private void LogDiagnosticsSnapshot(string label)
    {
      if (!logLoadingDiagnostics)
      {
        return;
      }

      WorldStreamer streamer = GetStreamer();
      Debug.Log(
        $"[DemoLaunch] {label}. Stage={loadingStage}, Detail={loadingDetail}, Warning={lastWarning}, " +
        $"Desired={(streamer != null ? streamer.DesiredChunkCount : 0)}, " +
        $"Keep={(streamer != null ? streamer.KeepChunkCount : 0)}, " +
        $"PendingLoad={(streamer != null ? streamer.PendingLoadCount : 0)}/{(streamer != null ? streamer.PendingLoadSetCount : 0)}, " +
        $"PendingRender={(streamer != null ? streamer.PendingRenderCount : 0)}/{(streamer != null ? streamer.PendingRenderSetCount : 0)}, " +
        $"ActiveLoad={(streamer != null ? streamer.ActiveChunkLoadTaskCount : 0)}, " +
        $"ActiveBlock={(streamer != null ? streamer.ActiveBlockBuildTaskCount : 0)}, " +
        $"ActiveDensity={(streamer != null ? streamer.ActiveDensityBuildTaskCount : 0)}, " +
        $"KnownEmpty={(streamer != null ? streamer.KnownEmptyChunkCount : 0)}, " +
        $"LastViewer={(streamer != null && streamer.HasLastViewerChunkCoord ? streamer.LastViewerChunkCoord.ToString() : "none")}, " +
        $"HasLastViewer={(streamer != null && streamer.HasLastViewerChunkCoord)}, " +
        $"InitialReady={(world != null && world.IsInitialTerrainReady)}, " +
        $"Finished={hasFinishedPreparation}, " +
        $"DisabledAutoStart={disabledStreamerAutoStartForLaunch}"
      );
    }
  }
}