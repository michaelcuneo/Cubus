using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class CubusLoadingLauncherMenu : MonoBehaviour
  {
    private sealed class LocalWorldEntry
    {
      public string WorldId;
      public WorldManifest Manifest;
      public string Path;
    }

    private enum SetupMode
    {
      Local,
      Connected,
    }

    [SerializeField] private string loadingSceneName = "CubusLoading";
    [SerializeField] private string defaultServerUri = "http://cubus.michaelcuneo.com.au";
    [SerializeField] private string defaultModuleName = "cubus";
    [SerializeField] private string defaultWorldId = "demo_world";
    [SerializeField] private Vector2 panelSize = new(760.0f, 820.0f);
    [SerializeField] private int sortingOrder = 1000;

    private readonly List<LocalWorldEntry> localWorlds = new();

    private SetupMode mode = SetupMode.Connected;
    private string serverUri;
    private string moduleName;
    private string worldId;
    private string seed;
    private string voxelSize = "1";
    private string minChunkY = "-1";
    private string maxChunkY = "2";
    private string minChunkX = "-8";
    private string minChunkZ = "-8";
    private string maxChunkX = "8";
    private string maxChunkZ = "8";
    private TerrainSystem terrainSystem = TerrainSystem.Block;
    private string status = "Choose how to start Cubus.";
    private bool isLoading;
    private float loadingProgress;
    private float loadingStartedAt;
    private string loadingScenePath;
    private int selectedLocalWorldIndex = -1;
    private bool deleteLocalWorldBeforeLaunch;
    private bool resetConnectedWorldOnLaunch;

    private CanvasGroup canvasGroup;
    private RectTransform modeSection;
    private RectTransform devSection;
    private RectTransform localWorldListRoot;
    private TMP_Text statusText;
    private TMP_Text loadingText;
    private Image loadingFill;
    private Button localModeButton;
    private Button connectedModeButton;
    private Button blockTerrainButton;
    private Button smoothTerrainButton;
    private Button startButton;
    private Button deleteToggleButton;
    private Button resetConnectedToggleButton;
    private TMP_InputField worldIdInput;
    private TMP_InputField seedInput;
    private TMP_InputField voxelSizeInput;
    private TMP_InputField minChunkYInput;
    private TMP_InputField maxChunkYInput;
    private TMP_InputField minChunkXInput;
    private TMP_InputField minChunkZInput;
    private TMP_InputField maxChunkXInput;
    private TMP_InputField maxChunkZInput;
    private TMP_InputField serverUriInput;
    private TMP_InputField moduleNameInput;
    private bool lastDeveloperMode;

    private void Awake()
    {
      serverUri = defaultServerUri;
      moduleName = defaultModuleName;
      worldId = defaultWorldId;
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
      RefreshLocalWorlds();
      BuildUi();
      RebuildDynamicSections();
      UpdateUiState(forceText: true);
    }

    private void Update()
    {
      if (lastDeveloperMode != CubusDeveloperMode.IsEnabled)
      {
        RebuildDynamicSections();
      }

      UpdateUiState(forceText: false);
    }

    private void StartSelectedGame(bool deleteLocalWorldBeforeLaunch, bool resetConnectedWorld)
    {
      if (isLoading || !TryBuildLaunchContext(deleteLocalWorldBeforeLaunch, resetConnectedWorld)) return;

      if (mode == SetupMode.Local && deleteLocalWorldBeforeLaunch)
      {
        DeleteLocalWorldById(worldId);
        RefreshLocalWorlds();
      }

      if (!TryFindSceneInBuildSettings(loadingSceneName, out string scenePath))
      {
        status = $"Scene '{loadingSceneName}' is not in Build Settings.";
        Debug.LogError($"[CubusLauncher] Scene '{loadingSceneName}' was not found in Build Settings.");
        UpdateUiState(forceText: true);
        return;
      }

      StartCoroutine(LoadLoadingSceneRoutine(scenePath));
    }

    private IEnumerator LoadLoadingSceneRoutine(string scenePath)
    {
      isLoading = true;
      loadingProgress = 0.0f;
      loadingStartedAt = Time.realtimeSinceStartup;
      loadingScenePath = scenePath;
      status = $"Loading scene {scenePath}...";
      UpdateUiState(forceText: true);

      AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
      if (operation == null)
      {
        status = $"Failed to load scene {scenePath}.";
        isLoading = false;
        UpdateUiState(forceText: true);
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        loadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
        UpdateUiState(forceText: false);
        yield return null;
      }
    }

    private bool TryBuildLaunchContext(bool deleteLocalWorldBeforeLaunch, bool resetConnectedWorld)
    {
      if (!int.TryParse(seed, out int parsedSeed)) { status = "Seed must be an integer."; return false; }
      if (!float.TryParse(voxelSize, out float parsedVoxelSize) || parsedVoxelSize <= 0.0f) { status = "Voxel Size must be a positive number."; return false; }
      if (!int.TryParse(minChunkY, out int parsedMinY) || !int.TryParse(maxChunkY, out int parsedMaxY)) { status = "Chunk Y bounds must be integers."; return false; }
      if (!int.TryParse(minChunkX, out int parsedMinX) || !int.TryParse(minChunkZ, out int parsedMinZ) || !int.TryParse(maxChunkX, out int parsedMaxX) || !int.TryParse(maxChunkZ, out int parsedMaxZ)) { status = "World X/Z bounds must be integers."; return false; }
      if (parsedMinY > parsedMaxY || parsedMinX > parsedMaxX || parsedMinZ > parsedMaxZ) { status = "Minimum bounds must be <= maximum bounds."; return false; }

      if (mode == SetupMode.Local)
      {
        int chunkWidthX = parsedMaxX - parsedMinX + 1;
        int chunkWidthZ = parsedMaxZ - parsedMinZ + 1;
        if (chunkWidthX * chunkWidthZ > 1024)
        {
          status = "Local world bounds are too large for starter generation. Use roughly -8..8 first.";
          return false;
        }
      }

      CubusGameLaunchContext.Set(
          mode == SetupMode.Local ? CubusGameLaunchMode.Local : CubusGameLaunchMode.Connected,
          serverUri,
          moduleName,
          worldId,
          terrainSystem,
          parsedSeed,
          parsedVoxelSize,
          parsedMinY,
          parsedMaxY,
          new Vector2Int(parsedMinX, parsedMinZ),
          new Vector2Int(parsedMaxX, parsedMaxZ),
          deleteLocalWorldBeforeLaunch && mode == SetupMode.Local,
          resetConnectedWorld && mode == SetupMode.Connected);

      return true;
    }

    private void SelectLocalWorld(int index)
    {
      if (index < 0 || index >= localWorlds.Count)
      {
        selectedLocalWorldIndex = -1;
        return;
      }

      selectedLocalWorldIndex = index;
      LocalWorldEntry entry = localWorlds[index];
      worldId = entry.WorldId;

      if (entry.Manifest != null)
      {
        terrainSystem = entry.Manifest.TerrainSystem;
        seed = entry.Manifest.WorldSeed.ToString();
        voxelSize = entry.Manifest.VoxelSize.ToString("R");
        minChunkY = entry.Manifest.MinChunkY.ToString();
        maxChunkY = entry.Manifest.MaxChunkY.ToString();
        minChunkX = entry.Manifest.MinChunkX.ToString();
        minChunkZ = entry.Manifest.MinChunkZ.ToString();
        maxChunkX = entry.Manifest.MaxChunkX.ToString();
        maxChunkZ = entry.Manifest.MaxChunkZ.ToString();
      }

      deleteLocalWorldBeforeLaunch = false;
      status = $"Selected local world '{worldId}'.";
      RebuildDynamicSections();
      UpdateUiState(forceText: true);
    }

    private void RefreshLocalWorlds()
    {
      localWorlds.Clear();
      selectedLocalWorldIndex = -1;

      string root = GetLocalWorldRootDirectory();
      if (!Directory.Exists(root))
      {
        status = "No saved local worlds found.";
        return;
      }

      foreach (string dir in Directory.GetDirectories(root))
      {
        string manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
          continue;
        }

        try
        {
          WorldManifest manifest = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(manifestPath));
          string id = !string.IsNullOrWhiteSpace(manifest?.WorldId)
              ? manifest.WorldId
              : Path.GetFileName(dir);

          localWorlds.Add(new LocalWorldEntry
          {
            WorldId = id,
            Manifest = manifest,
            Path = dir
          });
        }
        catch (Exception ex)
        {
          Debug.LogWarning($"[CubusLauncher] Failed to read local world manifest '{manifestPath}': {ex.Message}");
        }
      }

      localWorlds.Sort((a, b) => string.Compare(a.WorldId, b.WorldId, StringComparison.OrdinalIgnoreCase));
      status = $"Found {localWorlds.Count} local world(s).";
    }

    private void DeleteSelectedLocalWorld()
    {
      if (selectedLocalWorldIndex < 0 || selectedLocalWorldIndex >= localWorlds.Count)
      {
        return;
      }

      string id = localWorlds[selectedLocalWorldIndex].WorldId;
      DeleteLocalWorldById(id);
      RefreshLocalWorlds();
      selectedLocalWorldIndex = -1;
      status = $"Deleted local world '{id}'.";
      RebuildDynamicSections();
      UpdateUiState(forceText: true);
    }

    private void DeleteLocalWorldById(string id)
    {
      if (string.IsNullOrWhiteSpace(id))
      {
        return;
      }

      string path = Path.Combine(GetLocalWorldRootDirectory(), SanitizeWorldId(id));
      if (Directory.Exists(path))
      {
        Directory.Delete(path, true);
        Debug.Log($"[CubusLauncher] Deleted local world '{id}' at '{path}'.");
      }
    }

    private void SetMode(SetupMode nextMode)
    {
      if (mode == nextMode)
      {
        return;
      }

      mode = nextMode;
      RebuildDynamicSections();
      UpdateUiState(forceText: true);
    }

    private void SetTerrain(TerrainSystem nextTerrain)
    {
      terrainSystem = nextTerrain;
      UpdateUiState(forceText: false);
    }

    private void PrepareNewRandomWorldId()
    {
      worldId = $"local_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      selectedLocalWorldIndex = -1;
      deleteLocalWorldBeforeLaunch = false;
      status = $"Prepared new local world id '{worldId}'.";
      RebuildDynamicSections();
      UpdateUiState(forceText: true);
    }

    private void RandomizeSeed()
    {
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      UpdateUiState(forceText: true);
    }

    private void BuildUi()
    {
      EnsureEventSystem();

      GameObject canvasObject = new("Cubus Launcher Canvas");
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

      Image dim = CreateImage(root, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.55f));
      dim.raycastTarget = true;
      Stretch(dim.rectTransform);

      GameObject panelObject = new("Launcher Panel");
      panelObject.transform.SetParent(root, false);
      RectTransform panel = panelObject.AddComponent<RectTransform>();
      panel.anchorMin = new Vector2(0.5f, 0.5f);
      panel.anchorMax = new Vector2(0.5f, 0.5f);
      panel.pivot = new Vector2(0.5f, 0.5f);
      panel.sizeDelta = panelSize;
      panel.anchoredPosition = Vector2.zero;

      Image panelImage = panelObject.AddComponent<Image>();
      panelImage.color = new Color(0.015f, 0.02f, 0.03f, 1.0f);
      panelImage.raycastTarget = true;

      GameObject viewportObject = new("Viewport");
      viewportObject.transform.SetParent(panel, false);
      RectTransform viewport = viewportObject.AddComponent<RectTransform>();
      Stretch(viewport);
      viewport.offsetMin = new Vector2(18.0f, 18.0f);
      viewport.offsetMax = new Vector2(-18.0f, -18.0f);
      Image viewportImage = viewportObject.AddComponent<Image>();
      viewportImage.color = Color.clear;
      viewportObject.AddComponent<Mask>().showMaskGraphic = false;

      GameObject contentObject = new("Content");
      contentObject.transform.SetParent(viewportObject.transform, false);
      RectTransform content = contentObject.AddComponent<RectTransform>();
      content.anchorMin = new Vector2(0.0f, 1.0f);
      content.anchorMax = new Vector2(1.0f, 1.0f);
      content.pivot = new Vector2(0.5f, 1.0f);
      content.anchoredPosition = Vector2.zero;
      content.sizeDelta = new Vector2(0.0f, 0.0f);

      VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
      layout.padding = new RectOffset(10, 10, 10, 10);
      layout.spacing = 8.0f;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;

      ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
      fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

      ScrollRect scrollRect = panelObject.AddComponent<ScrollRect>();
      scrollRect.viewport = viewport;
      scrollRect.content = content;
      scrollRect.horizontal = false;
      scrollRect.vertical = true;
      scrollRect.movementType = ScrollRect.MovementType.Clamped;

      AddLabel(content, "Cubus", 34.0f, FontStyles.Bold, TextAlignmentOptions.Center);
      AddLabel(content, "Create a local world, or connect to a hosted Cubus world.", 15.0f, FontStyles.Normal, TextAlignmentOptions.Center);

      RectTransform row = AddRow(content, 42.0f);
      localModeButton = CreateButton(row, "Local Game", () => SetMode(SetupMode.Local));
      connectedModeButton = CreateButton(row, "Connected Game", () => SetMode(SetupMode.Connected));

      AddSectionLabel(content, "World");
      worldIdInput = CreateInputRow(content, "World ID", worldId, value => worldId = value);

      RectTransform terrainRow = AddRow(content, 38.0f);
      AddInlineLabel(terrainRow, "Terrain", 130.0f);
      blockTerrainButton = CreateButton(terrainRow, "Block", () => SetTerrain(TerrainSystem.Block));
      smoothTerrainButton = CreateButton(terrainRow, "Smooth", () => SetTerrain(TerrainSystem.SmoothDensity));

      RectTransform seedRow = AddRow(content, 38.0f);
      AddInlineLabel(seedRow, "Seed", 130.0f);
      seedInput = CreateInput(seedRow, seed, value => seed = value);
      CreateButton(seedRow, "Random", RandomizeSeed, 90.0f);

      voxelSizeInput = CreateInputRow(content, "Voxel Size", voxelSize, value => voxelSize = value);
      minChunkYInput = CreateInputRow(content, "Min Chunk Y", minChunkY, value => minChunkY = value);
      maxChunkYInput = CreateInputRow(content, "Max Chunk Y", maxChunkY, value => maxChunkY = value);
      minChunkXInput = CreateInputRow(content, "Min Chunk X", minChunkX, value => minChunkX = value);
      minChunkZInput = CreateInputRow(content, "Min Chunk Z", minChunkZ, value => minChunkZ = value);
      maxChunkXInput = CreateInputRow(content, "Max Chunk X", maxChunkX, value => maxChunkX = value);
      maxChunkZInput = CreateInputRow(content, "Max Chunk Z", maxChunkZ, value => maxChunkZ = value);

      modeSection = CreateSection(content, "Mode Section");
      devSection = CreateSection(content, "Developer Section");

      startButton = CreateButton(content, "Start Connected Game", () => StartSelectedGame(deleteLocalWorldBeforeLaunch, resetConnectedWorldOnLaunch), -1.0f, 42.0f);

      statusText = AddLabel(content, string.Empty, 15.0f, FontStyles.Bold, TextAlignmentOptions.Left);
      loadingText = AddLabel(content, string.Empty, 13.0f, FontStyles.Normal, TextAlignmentOptions.Left);

      Image loadingBack = CreateImage(content, "Loading Progress Back", new Color(0.0f, 0.0f, 0.0f, 1.0f));
      LayoutElement loadingBackLayout = loadingBack.gameObject.AddComponent<LayoutElement>();
      loadingBackLayout.preferredHeight = 16.0f;
      loadingBackLayout.minHeight = 16.0f;
      GameObject fillObject = new("Loading Progress Fill");
      fillObject.transform.SetParent(loadingBack.rectTransform, false);
      loadingFill = fillObject.AddComponent<Image>();
      loadingFill.color = new Color(0.20f, 0.75f, 1.0f, 1.0f);
      loadingFill.type = Image.Type.Filled;
      loadingFill.fillMethod = Image.FillMethod.Horizontal;
      loadingFill.fillOrigin = 0;
      Stretch(loadingFill.rectTransform);
    }

    private void RebuildDynamicSections()
    {
      if (modeSection == null || devSection == null)
      {
        return;
      }

      ClearChildren(modeSection);
      ClearChildren(devSection);

      if (mode == SetupMode.Connected)
      {
        AddSectionLabel(modeSection, "Server");
        serverUriInput = CreateInputRow(modeSection, "Server URI", serverUri, value => serverUri = value);
        moduleNameInput = CreateInputRow(modeSection, "Module", moduleName, value => moduleName = value);
      }
      else
      {
        serverUriInput = null;
        moduleNameInput = null;
      }

      if (!CubusDeveloperMode.IsEnabled)
      {
        AddLabel(devSection, "Dev tools hidden. Open runtime console with ` and run: dev on", 13.0f, FontStyles.Normal, TextAlignmentOptions.Left);
        lastDeveloperMode = CubusDeveloperMode.IsEnabled;
        return;
      }

      AddSectionLabel(devSection, "Developer World Tools");
      if (mode == SetupMode.Local)
      {
        RectTransform topRow = AddRow(devSection, 34.0f);
        CreateButton(topRow, "Refresh Local Worlds", () => { RefreshLocalWorlds(); RebuildDynamicSections(); UpdateUiState(true); });
        CreateButton(topRow, "New Random World ID", PrepareNewRandomWorldId);

        AddLabel(devSection, $"Storage: {GetLocalWorldRootDirectory()}", 12.0f, FontStyles.Normal, TextAlignmentOptions.Left);
        localWorldListRoot = CreateSection(devSection, "Local Worlds");
        RebuildLocalWorldList();

        RectTransform actionRow = AddRow(devSection, 34.0f);
        CreateButton(actionRow, "Use Selected", () => SelectLocalWorld(selectedLocalWorldIndex));
        CreateButton(actionRow, "Delete Selected", DeleteSelectedLocalWorld);
        CreateButton(actionRow, "Regenerate Selected", () => { deleteLocalWorldBeforeLaunch = true; StartSelectedGame(true, false); });

        deleteToggleButton = CreateButton(devSection, string.Empty, () => { deleteLocalWorldBeforeLaunch = !deleteLocalWorldBeforeLaunch; UpdateUiState(false); }, -1.0f, 32.0f);
        AddLabel(devSection, "Local regenerate deletes the selected local files, then launches using the current World ID/seed/settings.", 12.0f, FontStyles.Normal, TextAlignmentOptions.Left);
      }
      else
      {
        bool isKnownDevServer = (serverUri ?? string.Empty).IndexOf("cubus.michaelcuneo.com.au", StringComparison.OrdinalIgnoreCase) >= 0;
        AddLabel(devSection, "Connected worlds are server-authoritative, so local world listing is hidden.", 13.0f, FontStyles.Normal, TextAlignmentOptions.Left);
        if (!isKnownDevServer)
        {
          AddLabel(devSection, "Reset is intended for the dev server cubus.michaelcuneo.com.au.", 12.0f, FontStyles.Normal, TextAlignmentOptions.Left);
        }

        resetConnectedToggleButton = CreateButton(devSection, string.Empty, () => { resetConnectedWorldOnLaunch = !resetConnectedWorldOnLaunch; UpdateUiState(false); }, -1.0f, 32.0f);
        AddLabel(devSection, "Connected reset publishes current launch settings to SpaceTimeDB and clears server voxel edits/chunks for this World ID.", 12.0f, FontStyles.Normal, TextAlignmentOptions.Left);
      }

      lastDeveloperMode = CubusDeveloperMode.IsEnabled;
    }

    private void RebuildLocalWorldList()
    {
      if (localWorldListRoot == null)
      {
        return;
      }

      ClearChildren(localWorldListRoot);
      if (localWorlds.Count == 0)
      {
        AddLabel(localWorldListRoot, "No saved local worlds found.", 13.0f, FontStyles.Normal, TextAlignmentOptions.Left);
        return;
      }

      for (int i = 0; i < localWorlds.Count; i++)
      {
        int index = i;
        LocalWorldEntry entry = localWorlds[i];
        WorldManifest manifest = entry.Manifest;
        string label = manifest == null
            ? entry.WorldId
            : $"{entry.WorldId} | {manifest.TerrainSystem} | Seed {manifest.WorldSeed} | Chunks {manifest.ChunkCount}";
        CreateButton(localWorldListRoot, label, () => SelectLocalWorld(index), -1.0f, 30.0f);
      }
    }

    private void UpdateUiState(bool forceText)
    {
      if (canvasGroup != null)
      {
        canvasGroup.alpha = CubusUiInput.ConsoleOpen ? 0.0f : 1.0f;
        canvasGroup.interactable = !CubusUiInput.ConsoleOpen;
        canvasGroup.blocksRaycasts = !CubusUiInput.ConsoleOpen;
      }

      UpdateInput(worldIdInput, worldId, forceText);
      UpdateInput(seedInput, seed, forceText);
      UpdateInput(voxelSizeInput, voxelSize, forceText);
      UpdateInput(minChunkYInput, minChunkY, forceText);
      UpdateInput(maxChunkYInput, maxChunkY, forceText);
      UpdateInput(minChunkXInput, minChunkX, forceText);
      UpdateInput(minChunkZInput, minChunkZ, forceText);
      UpdateInput(maxChunkXInput, maxChunkX, forceText);
      UpdateInput(maxChunkZInput, maxChunkZ, forceText);
      UpdateInput(serverUriInput, serverUri, forceText);
      UpdateInput(moduleNameInput, moduleName, forceText);

      SetButtonLabel(localModeButton, mode == SetupMode.Local ? "[ Local Game ]" : "Local Game");
      SetButtonLabel(connectedModeButton, mode == SetupMode.Connected ? "[ Connected Game ]" : "Connected Game");
      SetButtonLabel(blockTerrainButton, terrainSystem == TerrainSystem.Block ? "[ Block ]" : "Block");
      SetButtonLabel(smoothTerrainButton, terrainSystem == TerrainSystem.SmoothDensity ? "[ Smooth ]" : "Smooth");
      SetButtonLabel(startButton, mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game");
      SetButtonLabel(deleteToggleButton, deleteLocalWorldBeforeLaunch ? "[x] Delete/regenerate current local World ID before launch" : "[ ] Delete/regenerate current local World ID before launch");
      SetButtonLabel(resetConnectedToggleButton, resetConnectedWorldOnLaunch ? "[x] Reset/regenerate connected world on launch" : "[ ] Reset/regenerate connected world on launch");

      bool canUseSelected = selectedLocalWorldIndex >= 0 && selectedLocalWorldIndex < localWorlds.Count;
      if (startButton != null) startButton.interactable = !isLoading && !CubusUiInput.ConsoleOpen;
      if (deleteToggleButton != null) deleteToggleButton.interactable = !isLoading && !CubusUiInput.ConsoleOpen;
      if (resetConnectedToggleButton != null) resetConnectedToggleButton.interactable = !isLoading && !CubusUiInput.ConsoleOpen;

      if (statusText != null)
      {
        statusText.text = $"Status: {status}";
      }

      if (loadingText != null)
      {
        if (isLoading)
        {
          float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
          loadingText.text = $"Loading CubusLoading | Scene: {loadingScenePath} | Elapsed: {elapsed:0.0}s | Progress: {loadingProgress * 100.0f:0}%";
        }
        else
        {
          loadingText.text = "";
        }
      }

      if (loadingFill != null)
      {
        loadingFill.fillAmount = isLoading ? loadingProgress : 0.0f;
      }
    }

    private static void UpdateInput(TMP_InputField field, string value, bool force)
    {
      if (field == null)
      {
        return;
      }

      if (!force && field.isFocused)
      {
        return;
      }

      if (!string.Equals(field.text, value ?? string.Empty, StringComparison.Ordinal))
      {
        field.SetTextWithoutNotify(value ?? string.Empty);
      }
    }

    private static void SetButtonLabel(Button button, string label)
    {
      if (button == null)
      {
        return;
      }

      TMP_Text text = button.GetComponentInChildren<TMP_Text>();
      if (text != null)
      {
        text.text = label ?? string.Empty;
      }
    }

    private static RectTransform CreateSection(RectTransform parent, string name)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
      layout.spacing = 6.0f;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;
      ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
      fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
      return rect;
    }

    private static RectTransform AddRow(RectTransform parent, float height)
    {
      GameObject go = new("Row");
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
      layout.spacing = 8.0f;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = true;
      return rect;
    }

    private static TMP_Text AddLabel(RectTransform parent, string value, float size, FontStyles style, TextAlignmentOptions alignment)
    {
      GameObject go = new("Label");
      go.transform.SetParent(parent, false);
      TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
      text.text = value;
      text.fontSize = size;
      text.fontStyle = style;
      text.alignment = alignment;
      text.color = new Color(0.88f, 0.95f, 1.0f, 1.0f);
      text.raycastTarget = false;
      LayoutElement element = go.AddComponent<LayoutElement>();
      element.minHeight = Mathf.Max(24.0f, size + 10.0f);
      element.preferredHeight = Mathf.Max(24.0f, size + 10.0f);
      return text;
    }

    private static TMP_Text AddInlineLabel(RectTransform parent, string value, float width)
    {
      TMP_Text text = AddLabel(parent, value, 14.0f, FontStyles.Normal, TextAlignmentOptions.Left);
      LayoutElement element = text.GetComponent<LayoutElement>();
      element.minWidth = width;
      element.preferredWidth = width;
      element.flexibleWidth = 0.0f;
      return text;
    }

    private static void AddSectionLabel(RectTransform parent, string value)
    {
      AddLabel(parent, value, 18.0f, FontStyles.Bold, TextAlignmentOptions.Left);
    }

    private static TMP_InputField CreateInputRow(RectTransform parent, string label, string value, Action<string> onChanged)
    {
      RectTransform row = AddRow(parent, 36.0f);
      AddInlineLabel(row, label, 130.0f);
      return CreateInput(row, value, onChanged);
    }

    private static TMP_InputField CreateInput(RectTransform parent, string value, Action<string> onChanged)
    {
      GameObject go = new("Input");
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = new Color(0.0f, 0.0f, 0.0f, 0.95f);
      image.raycastTarget = true;

      TMP_InputField input = go.AddComponent<TMP_InputField>();
      input.lineType = TMP_InputField.LineType.SingleLine;

      GameObject textObject = new("Text");
      textObject.transform.SetParent(go.transform, false);
      RectTransform textRect = textObject.AddComponent<RectTransform>();
      Stretch(textRect);
      textRect.offsetMin = new Vector2(8.0f, 4.0f);
      textRect.offsetMax = new Vector2(-8.0f, -4.0f);
      TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
      text.fontSize = 14.0f;
      text.color = Color.white;
      text.alignment = TextAlignmentOptions.Left;
      text.raycastTarget = false;

      GameObject placeholderObject = new("Placeholder");
      placeholderObject.transform.SetParent(go.transform, false);
      RectTransform placeholderRect = placeholderObject.AddComponent<RectTransform>();
      Stretch(placeholderRect);
      placeholderRect.offsetMin = new Vector2(8.0f, 4.0f);
      placeholderRect.offsetMax = new Vector2(-8.0f, -4.0f);
      TextMeshProUGUI placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
      placeholder.text = "...";
      placeholder.fontSize = 14.0f;
      placeholder.color = new Color(0.6f, 0.7f, 0.8f, 0.65f);
      placeholder.alignment = TextAlignmentOptions.Left;
      placeholder.raycastTarget = false;

      input.textComponent = text;
      input.placeholder = placeholder;
      input.SetTextWithoutNotify(value ?? string.Empty);
      input.onValueChanged.AddListener(next => onChanged?.Invoke(next));

      LayoutElement element = go.AddComponent<LayoutElement>();
      element.minHeight = 32.0f;
      element.preferredHeight = 32.0f;
      element.flexibleWidth = 1.0f;
      return input;
    }

    private static Button CreateButton(RectTransform parent, string label, Action onClick, float preferredWidth = -1.0f, float preferredHeight = 34.0f)
    {
      GameObject go = new("Button");
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = new Color(0.08f, 0.15f, 0.22f, 1.0f);
      image.raycastTarget = true;

      Button button = go.AddComponent<Button>();
      ColorBlock colors = button.colors;
      colors.normalColor = new Color(0.08f, 0.15f, 0.22f, 1.0f);
      colors.highlightedColor = new Color(0.12f, 0.24f, 0.34f, 1.0f);
      colors.pressedColor = new Color(0.04f, 0.10f, 0.16f, 1.0f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.04f, 0.05f, 0.06f, 0.55f);
      button.colors = colors;
      if (onClick != null)
      {
        button.onClick.AddListener(() => onClick());
      }

      TMP_Text text = AddLabel(go.GetComponent<RectTransform>(), label, 14.0f, FontStyles.Bold, TextAlignmentOptions.Center);
      Stretch(text.rectTransform);

      LayoutElement element = go.AddComponent<LayoutElement>();
      element.minHeight = preferredHeight;
      element.preferredHeight = preferredHeight;
      if (preferredWidth > 0.0f)
      {
        element.minWidth = preferredWidth;
        element.preferredWidth = preferredWidth;
        element.flexibleWidth = 0.0f;
      }
      else
      {
        element.flexibleWidth = 1.0f;
      }

      return button;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = color;
      return image;
    }

    private static void ClearChildren(RectTransform parent)
    {
      for (int i = parent.childCount - 1; i >= 0; i--)
      {
        Destroy(parent.GetChild(i).gameObject);
      }
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

    private static void EnsureEventSystem()
    {
      if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
      {
        return;
      }

      GameObject go = new("EventSystem");
      go.AddComponent<EventSystem>();
      go.AddComponent<StandaloneInputModule>();
      DontDestroyOnLoad(go);
    }

    private static string GetLocalWorldRootDirectory()
    {
      return Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds");
    }

    private static string SanitizeWorldId(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        return "default_world";
      }

      foreach (char invalid in Path.GetInvalidFileNameChars())
      {
        value = value.Replace(invalid, '_');
      }

      return value.Trim();
    }

    private static bool TryFindSceneInBuildSettings(string requestedSceneName, out string scenePath)
    {
      scenePath = string.Empty;
      if (string.IsNullOrWhiteSpace(requestedSceneName)) return false;

      for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
      {
        string path = SceneUtility.GetScenePathByBuildIndex(i);
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, requestedSceneName, StringComparison.OrdinalIgnoreCase) || string.Equals(path, requestedSceneName, StringComparison.OrdinalIgnoreCase))
        {
          scenePath = path;
          return true;
        }
      }

      return false;
    }
  }
}
