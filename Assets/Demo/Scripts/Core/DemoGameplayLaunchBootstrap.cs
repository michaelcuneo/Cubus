using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assets.Demo.Scripts.Multiplayer;
using Assets.Demo.Scripts.UI;
using Assets.Demo.Scripts.Persistence;
using Assets.Demo.Scripts.Core;

namespace Assets.Demo.Scripts.Core
{
  [DefaultExecutionOrder(-10000)]
  public sealed class DemoGameplayLaunchBootstrap : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private DemoNetworkManager network;
    [SerializeField] private bool requireLauncherSelection = true;
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
        yield return WaitForInitialTerrainReady("local streamed world", 0.0f);
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

      SetStage($"Waiting for {label} terrain", "Waiting for DemoWorld.BroadcastInitialTerrainReady. Streaming details are logged to the console.");
      float startTime = Time.realtimeSinceStartup;
      int lastLoggedSecond = -1;

      while (!world.IsInitialTerrainReady)
      {
        float elapsed = Time.realtimeSinceStartup - startTime;
        int elapsedSecond = Mathf.FloorToInt(elapsed);
        if (elapsedSecond != lastLoggedSecond && elapsedSecond % 5 == 0)
        {
          lastLoggedSecond = elapsedSecond;
          LogDiagnosticsSnapshot($"Still waiting for {label} initial terrain after {elapsed:0.0}s");
        }

        if (timeoutSeconds > 0.0f && elapsed > timeoutSeconds)
        {
          lastWarning = $"Timed out waiting for {label} terrain after {timeoutSeconds:0.0}s. Player remains gated until DemoWorld broadcasts initial terrain ready.";
          Debug.LogWarning($"[DemoLaunch] {lastWarning} {BuildDebugSummary()}");
          yield break;
        }

        yield return null;
      }
    }

    private void DisableStreamerAutoStartForLaunch()
    {
      WorldStreamer streamer = GetStreamer();
      if (streamer == null || !streamer.enabled)
      {
        return;
      }

      streamer.enabled = false;
      disabledStreamerAutoStartForLaunch = true;
      Debug.Log("[DemoLaunch] WorldStreamer disabled before Start so launcher bootstrap can control terrain preparation.");
    }

    private IEnumerator EnableStreamerForBootstrap(WorldStreamer streamer)
    {
      if (streamer == null)
      {
        yield break;
      }

      if (!streamer.enabled)
      {
        disabledStreamerAutoStartForLaunch = false;
        streamer.enabled = true;
        yield return null;
      }
    }

    private string DescribeLaunchSettings()
    {
      return $"Mode={DemoGameLaunchContext.Mode}, WorldId={DemoGameLaunchContext.WorldId}, Terrain={DemoGameLaunchContext.TerrainSystem}, Seed={DemoGameLaunchContext.WorldSeed}, Bounds={DemoGameLaunchContext.WorldMinChunkXZ}..{DemoGameLaunchContext.WorldMaxChunkXZ}, Y={DemoGameLaunchContext.MinChunkY}..{DemoGameLaunchContext.MaxChunkY}";
    }

    private void SetStage(string stage, string detail)
    {
      loadingStage = stage;
      loadingDetail = detail;

      if (logLoadingDiagnostics)
      {
        Debug.Log($"[CubusLaunch] {stage}. {detail}");
      }
    }

    private void LogDiagnosticsSnapshot(string reason)
    {
      if (!logLoadingDiagnostics)
      {
        return;
      }

      float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
      Debug.Log($"[CubusLaunch] {reason}. Stage={loadingStage}; Detail={loadingDetail}; Elapsed={elapsed:0.0}s; Preparing={isPreparingWorld}; {BuildDebugSummary()}");
    }

    private string BuildDebugSummary()
    {
      WorldStreamer streamer = GetStreamer();
      string streamerSummary = streamer == null
          ? "Streamer=none"
          : $"Streamer enabled={streamer.enabled}, heldByBootstrap={disabledStreamerAutoStartForLaunch}, initialStage={streamer.IsInitialStreamingStageActive}, broadcastReady={streamer.HasBroadcastInitialTerrainReady}, viewerChunk={(streamer.HasLastViewerChunkCoord ? streamer.LastViewerChunkCoord.ToString() : "none")}, spawnTarget={streamer.SpawnTargetChunkCoord}, desired={streamer.DesiredChunkCount}, keep={streamer.KeepChunkCount}, knownEmpty={streamer.KnownEmptyChunkCount}, pendingLoad={streamer.PendingLoadCount}, pendingRender={streamer.PendingRenderCount}, pendingUnload={streamer.PendingUnloadCount}, activeLoads={streamer.ActiveChunkLoadTaskCount}, activeBlockBuilds={streamer.ActiveBlockBuildTaskCount}, activeDensityBuilds={streamer.ActiveDensityBuildTaskCount}, loadedTotal={streamer.TotalChunkLoadsCompleted}, loadFailures={streamer.TotalChunkLoadFailures}, blockApplies={streamer.TotalBlockMeshApplies}, densityApplies={streamer.TotalDensityMeshApplies}";

      string worldSummary = world == null
          ? "World=none"
          : $"WorldReady={world.IsWorldReady}, InitialReady={world.IsInitialTerrainReady}, Generating={world.IsGeneratingWorld}, Progress={world.GenerationProgress:0.00}, Status={world.GenerationStatus}, Spawn={world.SuggestedSpawnLocation}";

      string networkSummary = network == null
          ? "Network=none"
          : $"Network worldId={network.WorldId}, connected={network.IsConnected}, hasConn={network.Conn != null}, subscriptionApplied={network.IsSubscriptionApplied}";

      return $"{worldSummary}; {streamerSummary}; {networkSummary}";
    }

    private WorldStreamer GetStreamer()
    {
      if (cachedStreamer == null && world != null)
      {
        cachedStreamer = world.GetComponent<WorldStreamer>();
      }

      return cachedStreamer;
    }

    private void FindReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (storage == null && world != null)
      {
        storage = world.GetComponent<CubusWorldStorage>();
      }

      cachedStreamer = world != null ? world.GetComponent<WorldStreamer>() : null;

      if (network == null)
      {
        network = DemoNetworkManager.Instance != null
            ? DemoNetworkManager.Instance
            : FindAnyObjectByType<DemoNetworkManager>();
      }
    }
  }
}
