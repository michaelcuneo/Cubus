using System.Collections;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Persistence;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Multiplayer
{
  [DefaultExecutionOrder(-10000)]
  public sealed class CubusGameplayLaunchBootstrap : MonoBehaviour
  {
    [SerializeField] private CubusWorld world;
    [SerializeField] private CubusWorldStorage storage;
    [SerializeField] private CubusNetworkManager network;
    [SerializeField] private bool requireLauncherSelection = true;
    [SerializeField] private string launcherSceneName = "CubusLauncher";
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

      if (!CubusGameLaunchContext.HasLaunch)
      {
        if (requireLauncherSelection)
        {
          SetStage("No launch context", "Gameplay scene was opened directly. Loading launcher scene instead.");
          Debug.LogWarning("[CubusLaunch] Gameplay scene loaded without launcher selection. Loading launcher scene instead.");
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

      if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Local)
      {
        yield return PrepareLocalWorldThenSpawn();
      }
      else if (CubusGameLaunchContext.Mode == CubusGameLaunchMode.Connected)
      {
        yield return PrepareConnectedWorldThenSpawn();
      }
      else
      {
        lastWarning = $"Unsupported launch mode: {CubusGameLaunchContext.Mode}";
        Debug.LogWarning($"[CubusLaunch] {lastWarning}");
      }

      isPreparingWorld = false;
      hasFinishedPreparation = world != null && world.IsInitialTerrainReady;
      LogDiagnosticsSnapshot("Preparation finished");
    }

    private bool ApplyLaunchSettingsToWorld()
    {
      if (world == null || world.Settings == null)
      {
        lastWarning = "No CubusWorld found in gameplay scene.";
        Debug.LogError($"[CubusLaunch] {lastWarning}");
        return false;
      }

      WorldSettings settings = world.Settings;
      settings.TerrainSystem = CubusGameLaunchContext.TerrainSystem;
      settings.WorldSeed = CubusGameLaunchContext.WorldSeed;
      settings.VoxelSize = CubusGameLaunchContext.VoxelSize;
      settings.UseWorldBounds = true;
      settings.WorldMinChunkXZ = CubusGameLaunchContext.WorldMinChunkXZ;
      settings.WorldMaxChunkXZ = CubusGameLaunchContext.WorldMaxChunkXZ;
      settings.BlockMinChunkY = CubusGameLaunchContext.MinChunkY;
      settings.BlockMaxChunkY = CubusGameLaunchContext.MaxChunkY;
      settings.DensityMinChunkY = CubusGameLaunchContext.MinChunkY;
      settings.DensityMaxChunkY = CubusGameLaunchContext.MaxChunkY;
      settings.InvalidateBiomeVariableCache();

      if (network != null)
      {
        network.WorldId = CubusGameLaunchContext.WorldId;
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
        Debug.Log($"[CubusLaunch] Preparing local streamed world '{CubusGameLaunchContext.WorldId}' before spawning.");
        yield return EnableStreamerForBootstrap(streamer);
        streamer.ClearStreamingState();
        streamer.RegenerateStreamedWorld();
        yield return WaitForInitialTerrainReady("local streamed world", 0.0f);
      }
      else
      {
        SetStage("Generating non-streamed world", "Running CubusWorld.GenerateWorldAsync before player spawn.");
        Debug.Log($"[CubusLaunch] Generating local world '{CubusGameLaunchContext.WorldId}' before spawning.");
        yield return world.GenerateWorldAsync();
        world.BroadcastInitialTerrainReady(Vector3.zero);
      }

      SetStage("Local world ready", "Initial terrain is ready. Player spawn released.");
      Debug.Log($"[CubusLaunch] Local world '{CubusGameLaunchContext.WorldId}' is ready. Player spawn released.");
    }

    private IEnumerator PrepareConnectedWorldThenSpawn()
    {
      if (network == null)
      {
        lastWarning = "No CubusNetworkManager found in gameplay scene.";
        Debug.LogError($"[CubusLaunch] {lastWarning}");
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
          CubusGameLaunchContext.ServerUri,
          CubusGameLaunchContext.ModuleName,
          CubusGameLaunchContext.WorldId);

      SetStage("Connecting to SpaceTimeDB", $"{CubusGameLaunchContext.ServerUri} / {CubusGameLaunchContext.ModuleName} / {CubusGameLaunchContext.WorldId}");
      network.Connect();

      Debug.Log($"[CubusLaunch] Connecting to {CubusGameLaunchContext.ServerUri} / {CubusGameLaunchContext.ModuleName} / {CubusGameLaunchContext.WorldId}. Waiting for authoritative terrain before spawning.");
      yield return WaitForInitialTerrainReady("connected world", connectedWorldReadyTimeoutSeconds);
    }

    private IEnumerator WaitForInitialTerrainReady(string label, float timeoutSeconds)
    {
      if (world == null)
      {
        yield break;
      }

      SetStage($"Waiting for {label} terrain", "Waiting for CubusWorld.BroadcastInitialTerrainReady. Streaming details are logged to the console.");
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
          lastWarning = $"Timed out waiting for {label} terrain after {timeoutSeconds:0.0}s. Player remains gated until CubusWorld broadcasts initial terrain ready.";
          Debug.LogWarning($"[CubusLaunch] {lastWarning} {BuildDebugSummary()}");
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
      Debug.Log("[CubusLaunch] WorldStreamer disabled before Start so launcher bootstrap can control terrain preparation.");
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
      return $"Mode={CubusGameLaunchContext.Mode}, WorldId={CubusGameLaunchContext.WorldId}, Terrain={CubusGameLaunchContext.TerrainSystem}, Seed={CubusGameLaunchContext.WorldSeed}, Bounds={CubusGameLaunchContext.WorldMinChunkXZ}..{CubusGameLaunchContext.WorldMaxChunkXZ}, Y={CubusGameLaunchContext.MinChunkY}..{CubusGameLaunchContext.MaxChunkY}";
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
        world = FindObjectOfType<CubusWorld>();
      }

      if (storage == null && world != null)
      {
        storage = world.GetComponent<CubusWorldStorage>();
      }

      cachedStreamer = world != null ? world.GetComponent<WorldStreamer>() : null;

      if (network == null)
      {
        network = CubusNetworkManager.Instance != null
            ? CubusNetworkManager.Instance
            : FindObjectOfType<CubusNetworkManager>();
      }
    }
  }
}
