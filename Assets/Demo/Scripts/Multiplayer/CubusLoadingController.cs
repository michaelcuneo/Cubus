using System.Collections;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Dedicated loading-scene controller. The launcher loads CubusLoading first;
  /// this component then loads CubusGame additively and keeps drawing real terrain
  /// preparation diagnostics until the gameplay world reports initial terrain ready.
  /// </summary>
  public sealed class CubusLoadingController : MonoBehaviour
  {
    [SerializeField] private string gameplaySceneName = "CubusGame";
    [SerializeField] private string launcherSceneName = "CubusLauncher";
    [SerializeField] private Vector2 panelSize = new(760.0f, 560.0f);
    [SerializeField] private float readyHoldSeconds = 0.35f;
    [SerializeField] private float preparationTimeoutSeconds = 180.0f;
    [SerializeField] private float settledTerrainFallbackSeconds = 2.0f;
    [SerializeField] private float fallbackSpawnClearance = 2.0f;
    [SerializeField] private bool unloadLoadingSceneWhenReady = true;

    private string status = "Starting Cubus loading scene.";
    private string detail = string.Empty;
    private string loadedGameplayScenePath = string.Empty;
    private float startedAt;
    private float sceneLoadProgress;
    private float terrainProgress;
    private float settledTerrainFallbackStartedAt = -1.0f;
    private bool sceneLoadDone;
    private bool preparationDone;
    private bool failed;
    private bool forceReleaseRequested;

    private CubusWorld world;
    private WorldStreamer streamer;
    private CubusNetworkManager network;

    private void Awake()
    {
      startedAt = Time.realtimeSinceStartup;
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
      if (!preparationDone && !failed && Input.GetKeyDown(KeyCode.Return))
      {
        forceReleaseRequested = true;
      }
    }

    private IEnumerator Start()
    {
      if (!CubusGameLaunchContext.HasLaunch)
      {
        Fail("No Cubus launch context was found. Returning to launcher.");
        yield return LoadLauncherScene();
        yield break;
      }

      if (string.IsNullOrWhiteSpace(gameplaySceneName))
      {
        Fail("Gameplay Scene Name is empty on CubusLoadingController.");
        yield break;
      }

      if (!TryFindSceneInBuildSettings(gameplaySceneName.Trim(), out string gameplayScenePath))
      {
        Fail($"Scene '{gameplaySceneName}' is not in Build Settings. Add CubusGame and CubusLoading to Build Settings.");
        yield break;
      }

      loadedGameplayScenePath = gameplayScenePath;
      yield return LoadGameplaySceneAdditively(gameplayScenePath);

      if (failed)
      {
        yield break;
      }

      yield return WaitForGameplayReferences();
      yield return WaitForInitialTerrainReady();

      if (failed)
      {
        yield break;
      }

      preparationDone = true;
      status = "Ready";
      detail = "Initial terrain is ready. Entering CubusGame.";
      terrainProgress = 1.0f;

      if (readyHoldSeconds > 0.0f)
      {
        yield return new WaitForSecondsRealtime(readyHoldSeconds);
      }

      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;

      if (unloadLoadingSceneWhenReady)
      {
        Scene loadingScene = gameObject.scene;
        Destroy(gameObject);
        if (loadingScene.IsValid() && loadingScene.isLoaded)
        {
          SceneManager.UnloadSceneAsync(loadingScene);
        }
      }
    }

    private void OnGUI()
    {
      Rect panelRect = new Rect(
          Mathf.Max(8.0f, (Screen.width - panelSize.x) * 0.5f),
          Mathf.Max(8.0f, (Screen.height - panelSize.y) * 0.5f),
          Mathf.Min(panelSize.x, Screen.width - 16.0f),
          Mathf.Min(panelSize.y, Screen.height - 16.0f));

      GUILayout.BeginArea(panelRect, GUI.skin.window);
      GUILayout.Label("<b>Cubus Loading</b>");
      GUILayout.Space(8.0f);

      float elapsed = Time.realtimeSinceStartup - startedAt;
      float combinedProgress = GetCombinedProgress();

      GUILayout.Label($"Status: {status}");
      GUILayout.Label($"Detail: {detail}");
      GUILayout.Label($"Elapsed: {elapsed:0.0}s");
      GUILayout.Label($"Overall: {combinedProgress * 100.0f:0}%");
      DrawProgressBar(combinedProgress, 18.0f);

      GUILayout.Space(10.0f);
      GUILayout.Label("<b>Scene</b>");
      GUILayout.Label($"Gameplay Scene: {(string.IsNullOrWhiteSpace(loadedGameplayScenePath) ? gameplaySceneName : loadedGameplayScenePath)}");
      GUILayout.Label($"Scene Loaded: {sceneLoadDone} | Scene Progress: {sceneLoadProgress * 100.0f:0}%");
      DrawProgressBar(sceneLoadProgress, 14.0f);

      GUILayout.Space(10.0f);
      DrawWorldDiagnostics();
      GUILayout.Space(10.0f);
      DrawStreamerDiagnostics();
      GUILayout.Space(10.0f);
      DrawNetworkDiagnostics();

      GUILayout.Space(10.0f);
      if (!preparationDone && !failed && GUILayout.Button("Enter Game Now", GUILayout.Height(30.0f)))
      {
        forceReleaseRequested = true;
      }
      GUILayout.Label("Press Enter or click Enter Game Now to force release if terrain is visibly ready.");

      if (failed)
      {
        GUILayout.Space(10.0f);
        GUILayout.Label("Loading failed. Check the Console for the exact error.");
      }

      GUILayout.EndArea();
    }

    private IEnumerator LoadGameplaySceneAdditively(string gameplayScenePath)
    {
      status = "Loading gameplay scene";
      detail = gameplayScenePath;
      sceneLoadProgress = 0.0f;
      terrainProgress = 0.0f;

      Debug.Log($"[CubusLoading] Loading gameplay scene additively: {gameplayScenePath}");
      AsyncOperation operation = SceneManager.LoadSceneAsync(gameplayScenePath, LoadSceneMode.Additive);
      if (operation == null)
      {
        Fail($"Failed to start loading gameplay scene '{gameplayScenePath}'.");
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        sceneLoadProgress = Mathf.Clamp01(operation.progress / 0.9f);
        detail = $"Unity scene load progress: {sceneLoadProgress * 100.0f:0}%";
        yield return null;
      }

      sceneLoadProgress = 1.0f;
      sceneLoadDone = true;

      Scene gameplayScene = SceneManager.GetSceneByPath(gameplayScenePath);
      if (!gameplayScene.IsValid())
      {
        gameplayScene = SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(gameplayScenePath));
      }

      if (gameplayScene.IsValid() && gameplayScene.isLoaded)
      {
        SceneManager.SetActiveScene(gameplayScene);
      }

      status = "Gameplay scene loaded";
      detail = "Waiting for CubusGame bootstrap and terrain systems.";
    }

    private IEnumerator WaitForGameplayReferences()
    {
      status = "Finding gameplay systems";
      float start = Time.realtimeSinceStartup;

      while (world == null)
      {
        FindRuntimeReferences();
        if (world != null)
        {
          break;
        }

        detail = "Waiting for CubusWorld in the additively loaded CubusGame scene.";
        if (Time.realtimeSinceStartup - start > 10.0f)
        {
          Fail("Timed out waiting for CubusWorld. Confirm CubusGame contains CubusWorld and is in Build Settings.");
          yield break;
        }

        yield return null;
      }

      FindRuntimeReferences();
    }

    private IEnumerator WaitForInitialTerrainReady()
    {
      if (world == null)
      {
        Fail("Cannot wait for terrain because CubusWorld is missing.");
        yield break;
      }

      status = CubusGameLaunchContext.Mode == CubusGameLaunchMode.Connected
          ? "Preparing connected terrain"
          : "Preparing local terrain";

      float start = Time.realtimeSinceStartup;
      while (!world.IsInitialTerrainReady)
      {
        FindRuntimeReferences();
        UpdateTerrainProgressAndDetail();
        TryReleaseSettledStreamedTerrain(start);

        if (preparationTimeoutSeconds > 0.0f && Time.realtimeSinceStartup - start > preparationTimeoutSeconds)
        {
          Fail($"Timed out waiting for initial terrain after {preparationTimeoutSeconds:0.0}s.");
          yield break;
        }

        yield return null;
      }

      terrainProgress = 1.0f;
    }

    private void UpdateTerrainProgressAndDetail()
    {
      if (world == null)
      {
        terrainProgress = 0.0f;
        detail = "CubusWorld not found yet.";
        return;
      }

      if (streamer == null)
      {
        terrainProgress = Mathf.Clamp01(world.GenerationProgress);
        detail = $"World generation: {world.GenerationStatus}";
        return;
      }

      if (world.IsInitialTerrainReady)
      {
        terrainProgress = 1.0f;
        detail = "Initial terrain is ready.";
        return;
      }

      float desired = Mathf.Max(0.0f, streamer.DesiredChunkCount);
      float applied = Mathf.Max(streamer.TotalBlockMeshApplies, streamer.TotalDensityMeshApplies);
      float pending = streamer.PendingLoadCount + streamer.PendingRenderCount;
      float active = streamer.ActiveChunkLoadTaskCount + streamer.ActiveBlockBuildTaskCount + streamer.ActiveDensityBuildTaskCount;

      if (desired > 0.0f)
      {
        float nonEmptyTarget = Mathf.Max(1.0f, desired - streamer.KnownEmptyChunkCount);
        terrainProgress = Mathf.Clamp01(applied / nonEmptyTarget);
      }
      else if (world.IsGeneratingWorld)
      {
        terrainProgress = Mathf.Clamp01(world.GenerationProgress * 0.5f);
      }
      else
      {
        terrainProgress = 0.05f;
      }

      detail = $"Desired={desired:0}, Applied={applied:0}, Pending={pending:0}, Active={active:0}, WorldStatus={world.GenerationStatus}";
    }

    private void TryReleaseSettledStreamedTerrain(float waitStartedAt)
    {
      if (world == null || world.IsInitialTerrainReady)
      {
        settledTerrainFallbackStartedAt = -1.0f;
        return;
      }

      if (forceReleaseRequested)
      {
        ForceBroadcastInitialTerrainReady("manual force release");
        return;
      }

      if (!world.IsWorldReady || streamer == null)
      {
        settledTerrainFallbackStartedAt = -1.0f;
        return;
      }

      float desired = streamer.DesiredChunkCount;
      float loaded = streamer.TotalChunkLoadsCompleted;
      float applied = Mathf.Max(streamer.TotalBlockMeshApplies, streamer.TotalDensityMeshApplies);
      float active = streamer.ActiveChunkLoadTaskCount + streamer.ActiveBlockBuildTaskCount + streamer.ActiveDensityBuildTaskCount;
      float requiredApplied = Mathf.Max(1.0f, desired - streamer.KnownEmptyChunkCount);
      float elapsed = Time.realtimeSinceStartup - waitStartedAt;

      bool hasUsefulTerrain = applied > 0.0f || loaded > 0.0f;
      bool workersSettled = active <= 0.0f;
      bool loadsSettled = streamer.PendingLoadCount == 0;
      bool enoughChunksApplied = desired <= 0.0f || applied >= Mathf.Min(requiredApplied, desired);
      bool longEnoughToTrustVisibleTerrain = elapsed >= 5.0f && hasUsefulTerrain && workersSettled && loadsSettled;
      bool fullySettled = hasUsefulTerrain && workersSettled && loadsSettled && enoughChunksApplied;

      if (!fullySettled && !longEnoughToTrustVisibleTerrain)
      {
        settledTerrainFallbackStartedAt = -1.0f;
        return;
      }

      if (settledTerrainFallbackStartedAt < 0.0f)
      {
        settledTerrainFallbackStartedAt = Time.realtimeSinceStartup;
        detail = fullySettled
            ? "Terrain has settled; releasing player shortly."
            : "Terrain appears ready but streamer did not broadcast; releasing shortly.";
        return;
      }

      if (Time.realtimeSinceStartup - settledTerrainFallbackStartedAt < Mathf.Max(0.0f, settledTerrainFallbackSeconds))
      {
        return;
      }

      ForceBroadcastInitialTerrainReady(fullySettled ? "settled streamer fallback" : "visible terrain fallback");
    }

    private void ForceBroadcastInitialTerrainReady(string reason)
    {
      if (world == null || world.IsInitialTerrainReady)
      {
        return;
      }

      Vector3 spawn = CalculateFallbackSpawnLocation();
      Debug.Log($"[CubusLoading] Releasing initial terrain via {reason}. Spawn={spawn}, Streamer={(streamer != null ? streamer.name : "none")}.");
      world.BroadcastInitialTerrainReady(spawn);
      forceReleaseRequested = false;
    }

    private Vector3 CalculateFallbackSpawnLocation()
    {
      if (world == null || world.Settings == null)
      {
        return Vector3.zero;
      }

      float voxelSize = Mathf.Max(0.0001f, world.Settings.VoxelSize);
      float densityScale = Mathf.Max(0.001f, world.Settings.DensitySampleScale);
      Vector3Int spawnChunkCoord = streamer != null ? streamer.SpawnTargetChunkCoord : Vector3Int.zero;

      float localVoxelX = spawnChunkCoord.x * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;
      float localVoxelZ = spawnChunkCoord.z * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;

      Vector3Int sampleVoxel = new(
        Mathf.FloorToInt(localVoxelX),
        0,
        Mathf.FloorToInt(localVoxelZ)
      );

      TerrainSample sample = BiomeTerrainSampler.Sample(world.Settings, sampleVoxel, densityScale);
      float localVoxelY = sample.SurfaceHeight + Mathf.Max(0.0f, fallbackSpawnClearance);

      return world.transform.TransformPoint(new Vector3(
        localVoxelX * voxelSize,
        localVoxelY * voxelSize,
        localVoxelZ * voxelSize
      ));
    }

    private void DrawWorldDiagnostics()
    {
      GUILayout.Label("<b>World</b>");
      if (world == null)
      {
        GUILayout.Label("CubusWorld: missing");
        return;
      }

      GUILayout.Label($"Mode: {CubusGameLaunchContext.Mode} | WorldId: {CubusGameLaunchContext.WorldId}");
      GUILayout.Label($"WorldReady: {world.IsWorldReady} | InitialTerrainReady: {world.IsInitialTerrainReady}");
      GUILayout.Label($"Generating: {world.IsGeneratingWorld} | GenerationProgress: {world.GenerationProgress * 100.0f:0}%");
      GUILayout.Label($"Status: {world.GenerationStatus}");
      GUILayout.Label($"Spawn: {world.SuggestedSpawnLocation}");
    }

    private void DrawStreamerDiagnostics()
    {
      GUILayout.Label("<b>Streamer</b>");
      if (streamer == null)
      {
        GUILayout.Label("WorldStreamer: missing");
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

      GUILayout.Label($"Connected: {network.IsConnected} | HasConn: {network.Conn != null} | SubscriptionApplied: {network.IsSubscriptionApplied}");
      GUILayout.Label($"Server: {network.ServerUri} | Module: {network.ModuleName} | WorldId: {network.WorldId}");
    }

    private void DrawProgressBar(float value, float height)
    {
      Rect progressRect = GUILayoutUtility.GetRect(1.0f, height, GUILayout.ExpandWidth(true));
      GUI.Box(progressRect, GUIContent.none);
      Rect fill = progressRect;
      fill.width *= Mathf.Clamp01(value);
      GUI.Box(fill, GUIContent.none);
    }

    private float GetCombinedProgress()
    {
      if (failed)
      {
        return 1.0f;
      }

      if (preparationDone)
      {
        return 1.0f;
      }

      return Mathf.Clamp01((sceneLoadProgress * 0.25f) + (terrainProgress * 0.75f));
    }

    private void FindRuntimeReferences()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }

      if (streamer == null && world != null)
      {
        streamer = world.GetComponent<WorldStreamer>();
      }

      if (network == null)
      {
        network = CubusNetworkManager.Instance != null
            ? CubusNetworkManager.Instance
            : FindAnyObjectByType<CubusNetworkManager>();
      }
    }

    private IEnumerator LoadLauncherScene()
    {
      if (string.IsNullOrWhiteSpace(launcherSceneName))
      {
        yield break;
      }

      yield return null;
      SceneManager.LoadScene(launcherSceneName);
    }

    private void Fail(string message)
    {
      failed = true;
      status = "Loading failed";
      detail = message;
      Debug.LogError($"[CubusLoading] {message}");
    }

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;

      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);

        if (string.Equals(name, requestedSceneName, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, requestedSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }

      return false;
    }
  }
}
