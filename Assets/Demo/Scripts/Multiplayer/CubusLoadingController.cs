using System.Collections;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Assets.Demo.Scripts.Core;
using Assets.Demo.Scripts.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Dedicated loading-scene controller. The launcher loads CubusLoading first;
  /// this component then loads CubusGame additively and keeps the screen clean while
  /// detailed world/streamer/network diagnostics are sent to Unity logging.
  /// </summary>
  public sealed class CubusLoadingController : MonoBehaviour
  {
    [SerializeField] private string gameplaySceneName = "CubusGame";
    [SerializeField] private string launcherSceneName = "CubusLauncher";
    [SerializeField] private int sortingOrder = 1200;
    [SerializeField] private float readyHoldSeconds = 0.35f;
    [SerializeField] private float preparationTimeoutSeconds = 180.0f;
    [SerializeField] private float settledTerrainFallbackSeconds = 0.25f;
    [SerializeField] private float fallbackSpawnClearance = 2.0f;
    [SerializeField] private float consoleDiagnosticsIntervalSeconds = 2.0f;
    [SerializeField] private bool unloadLoadingSceneWhenReady = true;
    [SerializeField][Min(1)] private int minVisibleDensityMeshesBeforeRelease = 9;
    [SerializeField][Min(0.0f)] private float minVisibleTerrainWaitSeconds = 2.0f;

    private static readonly Vector2 RuntimePanelSize = new(660.0f, 360.0f);

    private string status = "Starting Cubus loading scene.";
    private string detail = string.Empty;
    private string loadedGameplayScenePath = string.Empty;
    private float startedAt;
    private float sceneLoadProgress;
    private float terrainProgress;
    private float settledTerrainFallbackStartedAt = -1.0f;
    private float lastConsoleDiagnosticsAt = -999.0f;
    private bool sceneLoadDone;
    private bool preparationDone;
    private bool failed;
    private bool forceReleaseRequested;
    private bool wasEnterDown;
    private Scene loadingScene;

    private CubusWorld world;
    private WorldStreamer streamer;
    private DemoNetworkManager network;

    private CanvasGroup canvasGroup;
    private Text statusText;
    private Text detailText;
    private Text elapsedText;
    private Text progressText;
    private Text hintText;
    private Image progressFill;
    private Button forceReleaseButton;

    private static Font cachedUiFont;

    private static Font UiFont
    {
      get
      {
        if (cachedUiFont != null)
        {
          return cachedUiFont;
        }

        cachedUiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cachedUiFont == null)
        {
          cachedUiFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana" }, 14);
        }

        return cachedUiFont;
      }
    }

    private void Awake()
    {
      startedAt = Time.realtimeSinceStartup;
      loadingScene = gameObject.scene;
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      DontDestroyOnLoad(gameObject);
      BuildLoadingUi();
      UpdateLoadingUi();
    }

    private void Update()
    {
      Keyboard keyboard = Keyboard.current;
      bool enterDown = keyboard != null && (keyboard[Key.Enter].isPressed || keyboard[Key.NumpadEnter].isPressed);

      if (!preparationDone && !failed && !DemoUiInput.IsCapturing && enterDown && !wasEnterDown)
      {
        forceReleaseRequested = true;
      }

      wasEnterDown = enterDown;
      UpdateLoadingUi();
    }

    private void OnDestroy()
    {
      if (forceReleaseButton != null)
      {
        forceReleaseButton.onClick.RemoveListener(RequestForceRelease);
      }
    }

    private IEnumerator Start()
    {
      if (!DemoGameLaunchContext.HasLaunch)
      {
        status = "No launch context.";
        detail = "Returning to launcher.";
        UpdateLoadingUi();
        SceneManager.LoadScene(launcherSceneName);
        yield break;
      }

      BuildLoadingUi();
      UpdateLoadingUi();
      yield return LoadGameplaySceneAdditively();

      if (failed)
      {
        yield break;
      }

      yield return WaitForGameplayReferences();

      if (failed)
      {
        yield break;
      }

      yield return WaitForInitialTerrainReady();

      if (failed)
      {
        yield break;
      }

      status = "Ready";
      detail = "Initial terrain is ready. Entering CubusGame.";
      sceneLoadProgress = 1.0f;
      terrainProgress = 1.0f;
      UpdateLoadingUi();
      LogDiagnosticsSnapshot("Ready to enter game", true);

      yield return new WaitForSecondsRealtime(Mathf.Max(0.0f, readyHoldSeconds));

      preparationDone = true;
      if (unloadLoadingSceneWhenReady)
      {
        yield return UnloadLoadingScene();
      }
      else if (canvasGroup != null)
      {
        canvasGroup.alpha = 0.0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
      }
    }

    private IEnumerator LoadGameplaySceneAdditively()
    {
      status = "Loading gameplay scene";
      detail = gameplaySceneName;
      UpdateLoadingUi();
      Debug.Log($"[CubusLoading] Loading gameplay scene additively: {gameplaySceneName}");

      AsyncOperation operation = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Additive);
      if (operation == null)
      {
        Fail($"Could not start loading gameplay scene '{gameplaySceneName}'. Check Build Settings.");
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        sceneLoadProgress = Mathf.Clamp01(operation.progress / 0.9f);
        UpdateLoadingUi();
        yield return null;
      }

      sceneLoadDone = true;
      sceneLoadProgress = 1.0f;
      Scene gameplayScene = SceneManager.GetSceneByName(Path.GetFileNameWithoutExtension(gameplaySceneName));
      if (gameplayScene.IsValid())
      {
        loadedGameplayScenePath = gameplayScene.path;
        SceneManager.SetActiveScene(gameplayScene);
      }

      status = "Gameplay scene loaded";
      detail = "Preparing terrain...";
      UpdateLoadingUi();
      LogDiagnosticsSnapshot("Gameplay scene loaded", true);
      yield return null;
    }

    private IEnumerator WaitForGameplayReferences()
    {
      status = "Finding gameplay systems";
      detail = "Preparing terrain...";
      UpdateLoadingUi();

      float start = Time.realtimeSinceStartup;
      while (world == null)
      {
        FindRuntimeReferences();
        if (world != null)
        {
          break;
        }

        detail = "Waiting for CubusWorld...";
        UpdateLoadingUi();
        if (Time.realtimeSinceStartup - start > 10.0f)
        {
          Fail("Timed out waiting for CubusWorld. Confirm CubusGame contains CubusWorld and is in Build Settings.");
          yield break;
        }

        yield return null;
      }

      FindRuntimeReferences();
      LogDiagnosticsSnapshot("Found gameplay systems", true);
    }

    private IEnumerator WaitForInitialTerrainReady()
    {
      if (world == null)
      {
        Fail("Cannot wait for terrain because CubusWorld is missing.");
        yield break;
      }

      status = DemoGameLaunchContext.Mode == DemoGameLaunchMode.Connected
          ? "Preparing connected terrain"
          : "Preparing local terrain";

      float start = Time.realtimeSinceStartup;
      while (!world.IsInitialTerrainReady)
      {
        FindRuntimeReferences();
        UpdateTerrainProgressAndDetail();
        TryReleaseSettledStreamedTerrain(start);
        UpdateLoadingUi();
        LogDiagnosticsSnapshot("Waiting for initial terrain", false);

        if (preparationTimeoutSeconds > 0.0f && Time.realtimeSinceStartup - start > preparationTimeoutSeconds)
        {
          Fail($"Timed out waiting for initial terrain after {preparationTimeoutSeconds:0.0}s.");
          yield break;
        }

        yield return null;
      }

      terrainProgress = 1.0f;
      UpdateLoadingUi();
    }

    private void UpdateTerrainProgressAndDetail()
    {
      if (world == null)
      {
        terrainProgress = 0.0f;
        detail = "Waiting for world...";
        return;
      }

      if (streamer == null)
      {
        terrainProgress = Mathf.Clamp01(world.GenerationProgress);
        detail = world.IsGeneratingWorld ? "Generating world..." : world.GenerationStatus;
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
        float visibleFirstTarget = Mathf.Max(1.0f, minVisibleDensityMeshesBeforeRelease);
        float fullTarget = Mathf.Max(1.0f, desired - streamer.KnownEmptyChunkCount);
        float progressTarget = DemoGameLaunchContext.Mode == DemoGameLaunchMode.Local
          ? Mathf.Min(fullTarget, visibleFirstTarget)
          : fullTarget;
        terrainProgress = Mathf.Clamp01(applied / progressTarget);
      }
      else if (world.IsGeneratingWorld)
      {
        terrainProgress = Mathf.Clamp01(world.GenerationProgress * 0.5f);
      }
      else
      {
        terrainProgress = 0.05f;
      }

      detail = pending > 0.0f || active > 0.0f
          ? $"Streaming terrain... Pending {pending:0}, Active {active:0}"
          : "Finalising terrain...";
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
      bool visibleLocalTerrainReady =
        DemoGameLaunchContext.Mode == DemoGameLaunchMode.Local &&
        elapsed >= Mathf.Max(0.0f, minVisibleTerrainWaitSeconds) &&
        applied >= Mathf.Max(1, minVisibleDensityMeshesBeforeRelease);
      bool longEnoughToTrustVisibleTerrain = elapsed >= 5.0f && hasUsefulTerrain && workersSettled && loadsSettled;
      bool fullySettled = hasUsefulTerrain && workersSettled && loadsSettled && enoughChunksApplied;

      if (!fullySettled && !visibleLocalTerrainReady && !longEnoughToTrustVisibleTerrain)
      {
        settledTerrainFallbackStartedAt = -1.0f;
        return;
      }

      if (settledTerrainFallbackStartedAt < 0.0f)
      {
        settledTerrainFallbackStartedAt = Time.realtimeSinceStartup;
        detail = "Visible terrain ready. Entering shortly...";
        string label = fullySettled ? "Streamer settled" : visibleLocalTerrainReady ? "Visible local terrain ready" : "Visible terrain fallback armed";
        LogDiagnosticsSnapshot(label, true);
        return;
      }

      if (Time.realtimeSinceStartup - settledTerrainFallbackStartedAt < Mathf.Max(0.0f, settledTerrainFallbackSeconds))
      {
        return;
      }

      ForceBroadcastInitialTerrainReady(fullySettled ? "settled streamer fallback" : visibleLocalTerrainReady ? "visible local terrain" : "visible terrain fallback");
    }

    private void ForceBroadcastInitialTerrainReady(string reason)
    {
      if (world == null || world.IsInitialTerrainReady)
      {
        return;
      }

      Vector3 spawn = CalculateFallbackSpawnLocation();
      Debug.Log($"[CubusLoading] Releasing initial terrain via {reason}. Spawn={spawn}. {BuildDebugSummary()}");
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
      Vector3Int spawnChunkCoord =
        streamer != null && streamer.HasLastViewerChunkCoord
          ? streamer.LastViewerChunkCoord
          : Vector3Int.zero;

      float localVoxelX = spawnChunkCoord.x * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;
      float localVoxelZ = spawnChunkCoord.z * VoxelConstants.ChunkSize + VoxelConstants.ChunkSize * 0.5f;
      int sampleX = Mathf.RoundToInt(localVoxelX);
      int sampleZ = Mathf.RoundToInt(localVoxelZ);
      int sampleY = Mathf.RoundToInt((world.Settings.DensityMinChunkY + world.Settings.DensityMaxChunkY) * 0.5f * VoxelConstants.ChunkSize);

      TerrainSample sample = BiomeTerrainSampler.Sample(
        world.Settings,
        new Vector3Int(sampleX, sampleY, sampleZ),
        densityScale);

      Vector3 local = new(
        localVoxelX * voxelSize,
        (sample.SurfaceHeight + Mathf.Max(0.0f, fallbackSpawnClearance)) * voxelSize,
        localVoxelZ * voxelSize);

      return world.transform.TransformPoint(local);
    }

    private IEnumerator UnloadLoadingScene()
    {
      if (loadingScene.IsValid() && loadingScene.isLoaded)
      {
        yield return SceneManager.UnloadSceneAsync(loadingScene);
      }
    }

    private void RequestForceRelease()
    {
      forceReleaseRequested = true;
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

      if (streamer == null)
      {
        streamer = FindAnyObjectByType<WorldStreamer>();
      }

      if (network == null)
      {
        network = FindAnyObjectByType<DemoNetworkManager>();
      }
    }

    private void LogDiagnosticsSnapshot(string label, bool force)
    {
      if (!force && Time.realtimeSinceStartup - lastConsoleDiagnosticsAt < Mathf.Max(0.1f, consoleDiagnosticsIntervalSeconds))
      {
        return;
      }

      lastConsoleDiagnosticsAt = Time.realtimeSinceStartup;
      Debug.Log($"[CubusLoading] {label}. Status={status}; Detail={detail}; SceneLoaded={sceneLoadDone}; SceneProgress={sceneLoadProgress:0.00}; TerrainProgress={terrainProgress:0.00}; {BuildDebugSummary()}");
    }

    private string BuildDebugSummary()
    {
      string worldSummary = world == null
        ? "World=none"
        : $"WorldReady={world.IsWorldReady}, InitialTerrainReady={world.IsInitialTerrainReady}, Generating={world.IsGeneratingWorld}, GenerationProgress={world.GenerationProgress:0.00}, GenerationStatus={world.GenerationStatus}";

      string streamerSummary = streamer == null
        ? "Streamer=none"
        : $"Streamer enabled={streamer.enabled}, initialStage={streamer.IsInitialStreamingStageActive}, viewerChunk={(streamer.HasLastViewerChunkCoord ? streamer.LastViewerChunkCoord.ToString() : "none")}, desired={streamer.DesiredChunkCount}, keep={streamer.KeepChunkCount}, knownEmpty={streamer.KnownEmptyChunkCount}, pendingLoad={streamer.PendingLoadCount}, pendingRender={streamer.PendingRenderCount}, pendingUnload={streamer.PendingUnloadCount}, activeLoads={streamer.ActiveChunkLoadTaskCount}, activeBlockBuilds={streamer.ActiveBlockBuildTaskCount}, activeDensityBuilds={streamer.ActiveDensityBuildTaskCount}, loadedTotal={streamer.TotalChunkLoadsCompleted}, loadFailures={streamer.TotalChunkLoadFailures}, blockApplies={streamer.TotalBlockMeshApplies}, densityApplies={streamer.TotalDensityMeshApplies}";

      string networkSummary = network == null
        ? "Network=none"
        : $"Network connected={network.IsConnected}, hasConn={network.Conn != null}, server={network.ServerUri}, module={network.ModuleName}, worldId={network.WorldId}";

      return $"{worldSummary}; {streamerSummary}; {networkSummary}";
    }

    private void Fail(string message)
    {
      failed = true;
      status = "Loading failed";
      detail = message;
      Debug.LogError($"[CubusLoading] {message}");
      UpdateLoadingUi();
    }

    private void BuildLoadingUi()
    {
      if (canvasGroup != null)
      {
        return;
      }

      GameObject root = new("Cubus Loading UI");
      root.transform.SetParent(transform, false);
      Canvas canvas = root.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = sortingOrder;
      root.AddComponent<GraphicRaycaster>();
      canvasGroup = root.AddComponent<CanvasGroup>();

      GameObject panel = new("Panel");
      panel.transform.SetParent(root.transform, false);
      Image panelImage = panel.AddComponent<Image>();
      panelImage.color = new Color(0.02f, 0.025f, 0.035f, 0.96f);
      RectTransform panelRect = panel.GetComponent<RectTransform>();
      panelRect.anchorMin = new Vector2(0.5f, 0.5f);
      panelRect.anchorMax = new Vector2(0.5f, 0.5f);
      panelRect.pivot = new Vector2(0.5f, 0.5f);
      panelRect.sizeDelta = RuntimePanelSize;
      panelRect.anchoredPosition = Vector2.zero;

      statusText = CreateText(panel.transform, "Status", 28, FontStyle.Bold, new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0, -48), new Vector2(-56, 36));
      detailText = CreateText(panel.transform, "Detail", 16, FontStyle.Normal, new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0, -96), new Vector2(-56, 72));
      elapsedText = CreateText(panel.transform, "Elapsed", 14, FontStyle.Normal, new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0, -176), new Vector2(-56, 26));
      progressText = CreateText(panel.transform, "Progress", 14, FontStyle.Bold, new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0.5f, 1.0f), new Vector2(0, -216), new Vector2(-56, 26));
      hintText = CreateText(panel.transform, "Hint", 13, FontStyle.Italic, new Vector2(0.5f, 0.0f), new Vector2(0.5f, 0.0f), new Vector2(0.5f, 0.0f), new Vector2(0, 68), new Vector2(-56, 36));

      GameObject track = new("Progress Track");
      track.transform.SetParent(panel.transform, false);
      Image trackImage = track.AddComponent<Image>();
      trackImage.color = new Color(0.12f, 0.14f, 0.18f, 1.0f);
      RectTransform trackRect = track.GetComponent<RectTransform>();
      trackRect.anchorMin = new Vector2(0.5f, 1.0f);
      trackRect.anchorMax = new Vector2(0.5f, 1.0f);
      trackRect.pivot = new Vector2(0.5f, 0.5f);
      trackRect.sizeDelta = new Vector2(RuntimePanelSize.x - 96.0f, 18.0f);
      trackRect.anchoredPosition = new Vector2(0, -256);

      GameObject fill = new("Progress Fill");
      fill.transform.SetParent(track.transform, false);
      progressFill = fill.AddComponent<Image>();
      progressFill.color = new Color(0.35f, 0.7f, 1.0f, 1.0f);
      RectTransform fillRect = fill.GetComponent<RectTransform>();
      fillRect.anchorMin = new Vector2(0.0f, 0.0f);
      fillRect.anchorMax = new Vector2(0.0f, 1.0f);
      fillRect.pivot = new Vector2(0.0f, 0.5f);
      fillRect.sizeDelta = new Vector2(0.0f, 0.0f);
      fillRect.anchoredPosition = Vector2.zero;

      GameObject buttonObject = new("Force Release Button");
      buttonObject.transform.SetParent(panel.transform, false);
      Image buttonImage = buttonObject.AddComponent<Image>();
      buttonImage.color = new Color(0.18f, 0.22f, 0.28f, 1.0f);
      forceReleaseButton = buttonObject.AddComponent<Button>();
      forceReleaseButton.onClick.AddListener(RequestForceRelease);
      RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
      buttonRect.anchorMin = new Vector2(0.5f, 0.0f);
      buttonRect.anchorMax = new Vector2(0.5f, 0.0f);
      buttonRect.pivot = new Vector2(0.5f, 0.5f);
      buttonRect.sizeDelta = new Vector2(220.0f, 42.0f);
      buttonRect.anchoredPosition = new Vector2(0.0f, 28.0f);

      CreateText(buttonObject.transform, "Button Text", 14, FontStyle.Bold, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).text = "Enter now";
    }

    private Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Text text = go.AddComponent<Text>();
      text.font = UiFont;
      text.fontSize = fontSize;
      text.fontStyle = style;
      text.alignment = TextAnchor.MiddleCenter;
      text.color = Color.white;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      RectTransform rect = go.GetComponent<RectTransform>();
      rect.anchorMin = anchorMin;
      rect.anchorMax = anchorMax;
      rect.pivot = pivot;
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = sizeDelta;
      return text;
    }

    private void UpdateLoadingUi()
    {
      if (statusText != null)
      {
        statusText.text = status;
      }

      if (detailText != null)
      {
        detailText.text = detail;
      }

      if (elapsedText != null)
      {
        elapsedText.text = $"Elapsed {Time.realtimeSinceStartup - startedAt:0.0}s";
      }

      float combinedProgress = Mathf.Clamp01(sceneLoadProgress * 0.35f + terrainProgress * 0.65f);
      if (progressText != null)
      {
        progressText.text = $"{combinedProgress * 100.0f:0}%";
      }

      if (progressFill != null)
      {
        RectTransform fillRect = progressFill.GetComponent<RectTransform>();
        Transform parent = progressFill.transform.parent;
        RectTransform parentRect = parent != null ? parent.GetComponent<RectTransform>() : null;
        float width = parentRect != null ? parentRect.rect.width : RuntimePanelSize.x - 96.0f;
        fillRect.sizeDelta = new Vector2(width * combinedProgress, 0.0f);
      }

      if (hintText != null)
      {
        hintText.text = failed
          ? "Return to launcher and try again."
          : "Press Enter or click Enter now to skip waiting once terrain has started.";
      }
    }
  }
}
