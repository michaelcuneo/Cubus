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
    [SerializeField] private bool showLoadingDiagnostics = true;
    [SerializeField] private Vector2 diagnosticsPanelSize = new(680.0f, 430.0f);

    private string loadingStage = "Waiting for launch context";
    private string loadingDetail = string.Empty;
    private string lastWarning = string.Empty;
    private float loadingStartedAt;
    private bool isPreparingWorld;
    private bool hasFinishedPreparation;
    private WorldStreamer cachedStreamer;

    private void Awake()
    {
      WorldPersistence.SuppressAutoLoadOnStart = true;
      CubusWorld.SuppressGenerateOnStart = true;
      loadingStartedAt = Time.realtimeSinceStartup;
      FindReferences();
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
    }

    private void OnGUI()
    {
      if (!showLoadingDiagnostics || hasFinishedPreparation)
      {
        return;
      }

      DrawDiagnosticsOverlay();
    }

    private void DrawDiagnosticsOverlay()
    {
      float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
      Rect panelRect = new Rect(
          Mathf.Max(8.0f, (Screen.width - diagnosticsPanelSize.x) * 0.5f),
          Mathf.Max(8.0f, (Screen.height - diagnosticsPanelSize.y) * 0.5f),
          Mathf.Min(diagnosticsPanelSize.x, Screen.width - 16.0f),
          Mathf.Min(diagnosticsPanelSize.y, Screen.height - 16.0f));

      GUILayout.BeginArea(panelRect, GUI.skin.window);
      GUILayout.Label("<b>Cubus Loading</b>");
      GUILayout.Space(6.0f);
      GUILayout.Label($"Stage: {loadingStage}");
      GUILayout.Label($"Detail: {loadingDetail}");
      GUILayout.Label($"Elapsed: {elapsed:0.0}s");
      GUILayout.Label($"Preparing: {isPreparingWorld}");

      GUILayout.Space(8.0f);
      DrawWorldDiagnostics();
      GUILayout.Space(8.0f);
      DrawStreamerDiagnostics();
      GUILayout.Space(8.0f);
      DrawNetworkDiagnostics();

      if (!string.IsNullOrWhiteSpace(lastWarning))
      {
        GUILayout.Space(8.0f);
        GUILayout.Label($"Warning: {lastWarning}");
      }

      GUILayout.EndArea();
    }

    private void DrawWorldDiagnostics()
    {
      GUILayout.Label("<b>World</b>");
      if (world == null)
      {
        GUILayout.Label("CubusWorld: missing");
        return;
      }

      GUILayout.Label($"WorldReady: {world.IsWorldReady} | InitialTerrainReady: {world.IsInitialTerrainReady}");
      GUILayout.Label($"Generating: {world.IsGeneratingWorld} | Progress: {world.GenerationProgress * 100.0f:0}% | Status: {world.GenerationStatus}");
      GUILayout.Label($"Spawn: {world.SuggestedSpawnLocation}");
    }

    private void DrawStreamerDiagnostics()
    {
      GUILayout.Label("<b>Streamer</b>");
      WorldStreamer streamer = GetStreamer();
      if (streamer == null)
      {
        GUILayout.Label("WorldStreamer: missing. Non-streamed GenerateWorldAsync path should run.");
        return;
      }

      GUILayout.Label($"Enabled: {streamer.enabled} | InitialStage: {streamer.IsInitialStreamingStageActive} | BroadcastReady: {streamer.HasBroadcastInitialTerrainReady}");
      GUILayout.Label($"ViewerChunk: {(streamer.HasLastViewerChunkCoord ? streamer.LastViewerChunkCoord.ToString() : "none")} | SpawnTargetChunk: {streamer.SpawnTargetChunkCoord}");
      GUILayout.Label($"Desired: {streamer.DesiredChunkCount} | Keep: {streamer.KeepChunkCount} | KnownEmpty: {streamer.KnownEmptyChunkCount}");
      GUILayout.Label($"PendingLoad: {streamer.PendingLoadCount} | PendingRender: {streamer.PendingRenderCount} | PendingUnload: {streamer.PendingUnloadCount}");
      GUILayout.Label($"ActiveLoadTasks: {streamer.ActiveChunkLoadTaskCount} | ActiveBlockBuilds: {streamer.ActiveBlockBuildTaskCount} | ActiveDensityBuilds: {streamer.ActiveDensityBuildTaskCount}");
      GUILayout.Label($"LoadedTotal: {streamer.TotalChunkLoadsCompleted} | LoadFailures: {streamer.TotalChunkLoadFailures} | BlockApplies: {streamer.TotalBlockMeshApplies} | DensityApplies: {streamer.TotalDensityMeshApplies}");
    }

    private void DrawNetworkDiagnostics()
    {
      GUILayout.Label("<b>Network</b>");
      if (network == null)
      {
        GUILayout.Label("CubusNetworkManager: missing");
        return;
      }

      GUILayout.Label($"WorldId: {network.WorldId} | Connected: {network.IsConnected} | HasConn: {network.Conn != null} | SubscriptionApplied: {network.IsSubscriptionApplied}");
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
      SetStage("Preparing local world", "Disconnecting network and clearing local world state.");
      network?.Disconnect();
      storage?.DeleteWorldDatabase();

      if (world == null)
      {
        yield break;
      }

      world.ClearWorldAndOverrides();

      WorldStreamer streamer = GetStreamer();
      if (streamer != null)
      {
        SetStage("Prewarming streamed terrain", "Clearing streaming queues and starting initial spawn-area terrain generation.");
        Debug.Log($"[CubusLaunch] Preparing local streamed world '{CubusGameLaunchContext.WorldId}' before spawning.");
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

      SetStage("Preparing connected world", "Disconnecting any stale connection and clearing streamer state.");

      if (network.IsConnected || network.Conn != null)
      {
        network.Disconnect();
      }

      WorldStreamer streamer = GetStreamer();
      if (streamer != null)
      {
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

      SetStage($"Waiting for {label} terrain", "Waiting for CubusWorld.BroadcastInitialTerrainReady. Watch streamer queue counts below.");
      float startTime = Time.realtimeSinceStartup;
      int lastLoggedSecond = -1;

      while (!world.IsInitialTerrainReady)
      {
        float elapsed = Time.realtimeSinceStartup - startTime;
        int elapsedSecond = Mathf.FloorToInt(elapsed);
        if (elapsedSecond != lastLoggedSecond && elapsedSecond % 5 == 0)
        {
          lastLoggedSecond = elapsedSecond;
          Debug.Log($"[CubusLaunch] Still waiting for {label} initial terrain. {BuildDebugSummary()}");
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

    private string DescribeLaunchSettings()
    {
      return $"Mode={CubusGameLaunchContext.Mode}, WorldId={CubusGameLaunchContext.WorldId}, Terrain={CubusGameLaunchContext.TerrainSystem}, Seed={CubusGameLaunchContext.WorldSeed}, Bounds={CubusGameLaunchContext.WorldMinChunkXZ}..{CubusGameLaunchContext.WorldMaxChunkXZ}, Y={CubusGameLaunchContext.MinChunkY}..{CubusGameLaunchContext.MaxChunkY}";
    }

    private void SetStage(string stage, string detail)
    {
      loadingStage = stage;
      loadingDetail = detail;
      Debug.Log($"[CubusLaunch] {stage}. {detail}");
    }

    private string BuildDebugSummary()
    {
      WorldStreamer streamer = GetStreamer();
      string streamerSummary = streamer == null
          ? "Streamer=none"
          : $"Streamer enabled={streamer.enabled}, desired={streamer.DesiredChunkCount}, pendingLoad={streamer.PendingLoadCount}, pendingRender={streamer.PendingRenderCount}, activeLoads={streamer.ActiveChunkLoadTaskCount}, activeBlockBuilds={streamer.ActiveBlockBuildTaskCount}, activeDensityBuilds={streamer.ActiveDensityBuildTaskCount}, blockApplies={streamer.TotalBlockMeshApplies}, densityApplies={streamer.TotalDensityMeshApplies}";

      string worldSummary = world == null
          ? "World=none"
          : $"WorldReady={world.IsWorldReady}, InitialReady={world.IsInitialTerrainReady}, Generating={world.IsGeneratingWorld}, Progress={world.GenerationProgress:0.00}, Status={world.GenerationStatus}";

      return $"{worldSummary}; {streamerSummary}";
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
