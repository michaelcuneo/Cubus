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
    [SerializeField] private float settledTerrainFallbackSeconds = 2.0f;
    [SerializeField] private float fallbackSpawnClearance = 2.0f;
    [SerializeField] private float consoleDiagnosticsIntervalSeconds = 2.0f;
    [SerializeField] private bool unloadLoadingSceneWhenReady = true;

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
    private CubusNetworkManager network;

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

      if (!preparationDone && !failed && !CubusUiInput.IsCapturing && enterDown && !wasEnterDown)
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
      UpdateLoadingUi();
      LogDiagnosticsSnapshot("Ready to enter game", true);

      if (readyHoldSeconds > 0.0f)
      {
        yield return new WaitForSecondsRealtime(readyHoldSeconds);
      }

      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
      CubusSceneBackdrop.RemoveAllBackdrops();

      if (unloadLoadingSceneWhenReady)
      {
        Scene sceneToUnload = loadingScene;
        Destroy(gameObject);
        if (sceneToUnload.IsValid() && sceneToUnload.isLoaded)
        {
          SceneManager.UnloadSceneAsync(sceneToUnload);
        }
      }
    }

    private IEnumerator LoadGameplaySceneAdditively(string gameplayScenePath)
    {
      status = "Loading gameplay scene";
      detail = gameplayScenePath;
      sceneLoadProgress = 0.0f;
      terrainProgress = 0.0f;
      UpdateLoadingUi();

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
        detail = $"Scene load {sceneLoadProgress * 100.0f:0}%";
        UpdateLoadingUi();
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
      detail = "Preparing terrain...";
      UpdateLoadingUi();
      LogDiagnosticsSnapshot("Gameplay scene loaded", true);
    }

    private IEnumerator WaitForGameplayReferences()
    {
      status = "Finding gameplay systems";
      float start = Time.realtimeSinceStartup;
      UpdateLoadingUi();

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

      status = CubusGameLaunchContext.Mode == CubusGameLaunchMode.Connected
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
        detail = "Terrain ready. Entering shortly...";
        LogDiagnosticsSnapshot(fullySettled ? "Streamer settled" : "Visible terrain fallback armed", true);
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

    private void LogDiagnosticsSnapshot(string reason, bool force)
    {
      float now = Time.realtimeSinceStartup;
      if (!force && now - lastConsoleDiagnosticsAt < Mathf.Max(0.25f, consoleDiagnosticsIntervalSeconds))
      {
        return;
      }

      lastConsoleDiagnosticsAt = now;
      Debug.Log($"[CubusLoading] {reason}. Status={status}; Detail={detail}; SceneLoaded={sceneLoadDone}; SceneProgress={sceneLoadProgress:0.00}; TerrainProgress={terrainProgress:0.00}; {BuildDebugSummary()}");
    }

    private string BuildDebugSummary()
    {
      string worldSummary = world == null
          ? "World=none"
          : $"WorldReady={world.IsWorldReady}, InitialTerrainReady={world.IsInitialTerrainReady}, Generating={world.IsGeneratingWorld}, GenerationProgress={world.GenerationProgress:0.00}, GenerationStatus={world.GenerationStatus}, Spawn={world.SuggestedSpawnLocation}";

      string streamerSummary = streamer == null
          ? "Streamer=none"
          : $"Streamer enabled={streamer.enabled}, initialStage={streamer.IsInitialStreamingStageActive}, broadcastReady={streamer.HasBroadcastInitialTerrainReady}, viewerChunk={(streamer.HasLastViewerChunkCoord ? streamer.LastViewerChunkCoord.ToString() : "none")}, spawnTarget={streamer.SpawnTargetChunkCoord}, desired={streamer.DesiredChunkCount}, keep={streamer.KeepChunkCount}, knownEmpty={streamer.KnownEmptyChunkCount}, pendingLoad={streamer.PendingLoadCount}, pendingRender={streamer.PendingRenderCount}, pendingUnload={streamer.PendingUnloadCount}, activeLoads={streamer.ActiveChunkLoadTaskCount}, activeBlockBuilds={streamer.ActiveBlockBuildTaskCount}, activeDensityBuilds={streamer.ActiveDensityBuildTaskCount}, loadedTotal={streamer.TotalChunkLoadsCompleted}, loadFailures={streamer.TotalChunkLoadFailures}, blockApplies={streamer.TotalBlockMeshApplies}, densityApplies={streamer.TotalDensityMeshApplies}";

      string networkSummary = network == null
          ? "Network=none"
          : $"Network connected={network.IsConnected}, hasConn={network.Conn != null}, subscriptionApplied={network.IsSubscriptionApplied}, server={network.ServerUri}, module={network.ModuleName}, worldId={network.WorldId}";

      return $"{worldSummary}; {streamerSummary}; {networkSummary}";
    }

    private void UpdateLoadingUi()
    {
      if (canvasGroup == null)
      {
        return;
      }

      canvasGroup.alpha = CubusUiInput.ConsoleOpen ? 0.0f : 1.0f;
      canvasGroup.interactable = !CubusUiInput.ConsoleOpen;
      canvasGroup.blocksRaycasts = !CubusUiInput.ConsoleOpen;

      float elapsed = Time.realtimeSinceStartup - startedAt;
      float combinedProgress = GetCombinedProgress();

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
        elapsedText.text = $"Elapsed: {elapsed:0.0}s";
      }

      if (progressText != null)
      {
        progressText.text = $"Progress: {combinedProgress * 100.0f:0}%";
      }

      if (progressFill != null)
      {
        progressFill.fillAmount = combinedProgress;
      }

      if (hintText != null)
      {
        hintText.text = failed
            ? "Loading failed. Check the runtime console."
            : "Detailed diagnostics are in the runtime console. Press Enter to force release if terrain is visibly ready.";
      }

      if (forceReleaseButton != null)
      {
        forceReleaseButton.interactable = !preparationDone && !failed && !CubusUiInput.ConsoleOpen;
      }
    }

    private float GetCombinedProgress()
    {
      if (failed || preparationDone)
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
      UpdateLoadingUi();
      Debug.LogError($"[CubusLoading] {message} {BuildDebugSummary()}");
    }

    private void RequestForceRelease()
    {
      if (preparationDone || failed || CubusUiInput.ConsoleOpen)
      {
        return;
      }

      forceReleaseRequested = true;
      UpdateLoadingUi();
    }

    private void BuildLoadingUi()
    {
      CubusInputSystemUiGuard.EnsureEventSystem();

      GameObject canvasObject = new("Cubus Loading Canvas");
      canvasObject.transform.SetParent(transform, false);

      Canvas canvas = canvasObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.overrideSorting = true;
      canvas.sortingOrder = sortingOrder;

      CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
      scaler.matchWidthOrHeight = 0.5f;

      canvasObject.AddComponent<GraphicRaycaster>();
      RectTransform root = canvasObject.GetComponent<RectTransform>();
      Stretch(root);

      canvasGroup = canvasObject.AddComponent<CanvasGroup>();
      canvasGroup.alpha = 1.0f;
      canvasGroup.interactable = true;
      canvasGroup.blocksRaycasts = true;

      Image baseLayer = CreateImage(root, "Background Base", new Color(0.006f, 0.010f, 0.018f, 1.0f));
      Stretch(baseLayer.rectTransform);
      baseLayer.raycastTarget = false;

      Image upperGlow = CreateImage(root, "Background Glow", new Color(0.025f, 0.120f, 0.185f, 0.85f));
      RectTransform glowRect = upperGlow.rectTransform;
      glowRect.anchorMin = new Vector2(0.0f, 0.56f);
      glowRect.anchorMax = new Vector2(1.0f, 1.0f);
      glowRect.offsetMin = Vector2.zero;
      glowRect.offsetMax = Vector2.zero;
      upperGlow.raycastTarget = false;

      Image horizon = CreateImage(root, "Horizon Band", new Color(0.10f, 0.32f, 0.43f, 0.26f));
      RectTransform horizonRect = horizon.rectTransform;
      horizonRect.anchorMin = new Vector2(0.0f, 0.46f);
      horizonRect.anchorMax = new Vector2(1.0f, 0.52f);
      horizonRect.offsetMin = Vector2.zero;
      horizonRect.offsetMax = Vector2.zero;
      horizon.raycastTarget = false;

      Image shadow = CreateImage(root, "Loading Panel Shadow", new Color(0.0f, 0.0f, 0.0f, 0.35f));
      RectTransform shadowRect = shadow.rectTransform;
      shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
      shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
      shadowRect.pivot = new Vector2(0.5f, 0.5f);
      shadowRect.sizeDelta = RuntimePanelSize + new Vector2(18.0f, 18.0f);
      shadowRect.anchoredPosition = new Vector2(10.0f, -10.0f);
      shadow.raycastTarget = false;

      GameObject panelObject = new("Loading Panel");
      panelObject.transform.SetParent(root, false);
      RectTransform panel = panelObject.AddComponent<RectTransform>();
      panel.anchorMin = new Vector2(0.5f, 0.5f);
      panel.anchorMax = new Vector2(0.5f, 0.5f);
      panel.pivot = new Vector2(0.5f, 0.5f);
      panel.sizeDelta = RuntimePanelSize;
      panel.anchoredPosition = Vector2.zero;

      Image panelImage = panelObject.AddComponent<Image>();
      panelImage.color = new Color(0.018f, 0.026f, 0.038f, 0.98f);
      panelImage.raycastTarget = true;
      panelObject.AddComponent<RectMask2D>();

      Image accent = CreateImage(panel, "Top Accent", new Color(0.12f, 0.58f, 0.82f, 1.0f));
      RectTransform accentRect = accent.rectTransform;
      accentRect.anchorMin = new Vector2(0.0f, 1.0f);
      accentRect.anchorMax = new Vector2(1.0f, 1.0f);
      accentRect.pivot = new Vector2(0.5f, 1.0f);
      accentRect.anchoredPosition = Vector2.zero;
      accentRect.sizeDelta = new Vector2(0.0f, 4.0f);
      accent.raycastTarget = false;

      GameObject contentObject = new("Loading Content");
      contentObject.transform.SetParent(panel, false);
      RectTransform content = contentObject.AddComponent<RectTransform>();
      content.anchorMin = Vector2.zero;
      content.anchorMax = Vector2.one;
      content.offsetMin = new Vector2(34.0f, 30.0f);
      content.offsetMax = new Vector2(-34.0f, -30.0f);

      VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
      layout.childAlignment = TextAnchor.UpperCenter;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;
      layout.spacing = 10.0f;
      layout.padding = new RectOffset(0, 0, 6, 0);

      AddText(content, "CUBUS LOADING", 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.96f, 1.0f, 1.0f), 42.0f);
      statusText = AddText(content, string.Empty, 18, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.96f, 1.0f, 1.0f), 30.0f);
      detailText = AddText(content, string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.62f, 0.74f, 0.84f, 1.0f), 42.0f);

      RectTransform statsRow = AddRow(content, 28.0f, 10.0f);
      elapsedText = AddText(statsRow, string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.78f, 0.86f, 0.93f, 1.0f), 28.0f);
      progressText = AddText(statsRow, string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.78f, 0.86f, 0.93f, 1.0f), 28.0f);

      progressFill = AddProgressBar(content, 18.0f);
      forceReleaseButton = AddButton(content, "Enter Game Now", RequestForceRelease, -1.0f, 38.0f, true);
      hintText = AddText(content, string.Empty, 12, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.58f, 0.70f, 0.80f, 1.0f), 42.0f);
    }

    private static RectTransform AddRow(RectTransform parent, float height, float spacing)
    {
      GameObject go = new("Row");
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
      layout.childAlignment = TextAnchor.MiddleCenter;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = true;
      layout.spacing = spacing;
      return rect;
    }

    private static Text AddText(RectTransform parent, string value, int size, FontStyle style, TextAnchor alignment, Color color, float height)
    {
      GameObject go = new("Text");
      go.transform.SetParent(parent, false);
      Text text = go.AddComponent<Text>();
      text.text = value ?? string.Empty;
      text.font = UiFont;
      text.fontSize = size;
      text.fontStyle = style;
      text.alignment = alignment;
      text.color = color;
      text.raycastTarget = false;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      element.flexibleWidth = 1.0f;
      return text;
    }

    private static Button AddButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick, float width = -1.0f, float height = 34.0f, bool primary = false)
    {
      GameObject go = new("Button");
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = primary ? new Color(0.10f, 0.36f, 0.58f, 1.0f) : new Color(0.045f, 0.095f, 0.145f, 1.0f);
      image.raycastTarget = true;

      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      if (width > 0.0f)
      {
        element.preferredWidth = width;
        element.minWidth = width;
        element.flexibleWidth = 0.0f;
      }
      else
      {
        element.flexibleWidth = 1.0f;
      }

      Button button = go.AddComponent<Button>();
      button.targetGraphic = image;
      ColorBlock colors = button.colors;
      colors.normalColor = image.color;
      colors.highlightedColor = primary ? new Color(0.16f, 0.52f, 0.78f, 1.0f) : new Color(0.08f, 0.18f, 0.26f, 1.0f);
      colors.pressedColor = new Color(0.025f, 0.055f, 0.085f, 1.0f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.035f, 0.040f, 0.050f, 0.65f);
      button.colors = colors;

      if (onClick != null)
      {
        button.onClick.AddListener(onClick);
      }

      Text text = AddText(go.GetComponent<RectTransform>(), label, primary ? 15 : 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.96f, 1.0f, 1.0f), height);
      Stretch(text.rectTransform);
      return button;
    }

    private static Image AddProgressBar(RectTransform parent, float height)
    {
      GameObject backObject = new("Progress Back");
      backObject.transform.SetParent(parent, false);
      Image back = backObject.AddComponent<Image>();
      back.color = new Color(0.0f, 0.0f, 0.0f, 1.0f);
      back.raycastTarget = false;
      LayoutElement element = backObject.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      element.flexibleWidth = 1.0f;

      GameObject fillObject = new("Progress Fill");
      fillObject.transform.SetParent(backObject.transform, false);
      Image fill = fillObject.AddComponent<Image>();
      fill.color = new Color(0.20f, 0.75f, 1.0f, 1.0f);
      fill.type = Image.Type.Filled;
      fill.fillMethod = Image.FillMethod.Horizontal;
      fill.fillOrigin = 0;
      fill.raycastTarget = false;
      Stretch(fill.rectTransform);
      return fill;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = color;
      return image;
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = Vector2.zero;
      rect.sizeDelta = Vector2.zero;
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.localScale = Vector3.one;
    }

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;
      if (string.IsNullOrWhiteSpace(requestedSceneName)) return false;

      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, requestedSceneName, System.StringComparison.OrdinalIgnoreCase) || string.Equals(path, requestedSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }

      return false;
    }
  }
}
