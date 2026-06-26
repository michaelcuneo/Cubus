using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Runtime launcher UI for the dedicated loading-scene flow.
  /// Uses Unity UI Text/InputField instead of TextMeshPro so the first screen never
  /// depends on TMP font asset initialisation.
  /// </summary>
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
    private bool lastDeveloperMode;

    private CanvasGroup canvasGroup;
    private RectTransform panel;
    private Text statusText;
    private Text loadingText;
    private Image loadingFill;
    private Button localModeButton;
    private Button connectedModeButton;
    private Button blockTerrainButton;
    private Button smoothTerrainButton;
    private Button startButton;
    private Button deleteToggleButton;
    private Button resetConnectedToggleButton;
    private InputField worldIdInput;
    private InputField seedInput;
    private InputField voxelSizeInput;
    private InputField minChunkYInput;
    private InputField maxChunkYInput;
    private InputField minChunkXInput;
    private InputField minChunkZInput;
    private InputField maxChunkXInput;
    private InputField maxChunkZInput;
    private InputField serverUriInput;
    private InputField moduleNameInput;

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
          cachedUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return cachedUiFont;
      }
    }

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
      RebuildControls();
      UpdateUiState(forceText: true);
    }

    private void Update()
    {
      if (lastDeveloperMode != CubusDeveloperMode.IsEnabled)
      {
        RebuildControls();
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
      RebuildControls();
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
      RebuildControls();
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
      RebuildControls();
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
      RebuildControls();
      UpdateUiState(forceText: true);
    }

    private void RandomizeSeed()
    {
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      UpdateUiState(forceText: true);
    }

    private void BuildUi()
    {
      CubusInputSystemUiGuard.EnsureEventSystem();

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

      Image baseLayer = CreateImage(root, "Background Base", new Color(0.006f, 0.010f, 0.018f, 1.0f));
      baseLayer.raycastTarget = false;
      Stretch(baseLayer.rectTransform);

      Image glow = CreateImage(root, "Background Glow", new Color(0.03f, 0.12f, 0.18f, 0.82f));
      glow.raycastTarget = false;
      RectTransform glowRect = glow.rectTransform;
      glowRect.anchorMin = new Vector2(0.0f, 0.55f);
      glowRect.anchorMax = new Vector2(1.0f, 1.0f);
      glowRect.offsetMin = Vector2.zero;
      glowRect.offsetMax = Vector2.zero;

      Image horizon = CreateImage(root, "Horizon Band", new Color(0.10f, 0.30f, 0.42f, 0.28f));
      horizon.raycastTarget = false;
      RectTransform horizonRect = horizon.rectTransform;
      horizonRect.anchorMin = new Vector2(0.0f, 0.46f);
      horizonRect.anchorMax = new Vector2(1.0f, 0.52f);
      horizonRect.offsetMin = Vector2.zero;
      horizonRect.offsetMax = Vector2.zero;

      GameObject panelObject = new("Launcher Panel");
      panelObject.transform.SetParent(root, false);
      panel = panelObject.AddComponent<RectTransform>();
      panel.anchorMin = new Vector2(0.5f, 0.5f);
      panel.anchorMax = new Vector2(0.5f, 0.5f);
      panel.pivot = new Vector2(0.5f, 0.5f);
      panel.sizeDelta = panelSize;
      panel.anchoredPosition = Vector2.zero;

      Image shadow = CreateImage(root, "Launcher Panel Shadow", new Color(0.0f, 0.0f, 0.0f, 0.30f));
      RectTransform shadowRect = shadow.rectTransform;
      shadowRect.anchorMin = panel.anchorMin;
      shadowRect.anchorMax = panel.anchorMax;
      shadowRect.pivot = panel.pivot;
      shadowRect.sizeDelta = panelSize + new Vector2(18.0f, 18.0f);
      shadowRect.anchoredPosition = new Vector2(10.0f, -10.0f);
      shadow.transform.SetSiblingIndex(panelObject.transform.GetSiblingIndex());
      shadow.raycastTarget = false;

      Image panelImage = panelObject.AddComponent<Image>();
      panelImage.color = new Color(0.018f, 0.026f, 0.038f, 0.98f);
      panelImage.raycastTarget = true;

      Image accent = CreateImage(panel, "Top Accent", new Color(0.12f, 0.55f, 0.80f, 0.95f));
      RectTransform accentRect = accent.rectTransform;
      accentRect.anchorMin = new Vector2(0.0f, 1.0f);
      accentRect.anchorMax = new Vector2(1.0f, 1.0f);
      accentRect.pivot = new Vector2(0.5f, 1.0f);
      accentRect.anchoredPosition = Vector2.zero;
      accentRect.sizeDelta = new Vector2(0.0f, 4.0f);
      accent.raycastTarget = false;
    }

    private void RebuildControls()
    {
      if (panel == null)
      {
        return;
      }

      for (int i = panel.childCount - 1; i >= 0; i--)
      {
        GameObject child = panel.GetChild(i).gameObject;
        if (child.name == "Top Accent")
        {
          continue;
        }

        Destroy(child);
      }

      localModeButton = null;
      connectedModeButton = null;
      blockTerrainButton = null;
      smoothTerrainButton = null;
      startButton = null;
      deleteToggleButton = null;
      resetConnectedToggleButton = null;
      serverUriInput = null;
      moduleNameInput = null;

      lastDeveloperMode = CubusDeveloperMode.IsEnabled;
      float y = -32.0f;

      CreateText(panel, "Title", "CUBUS", 34, FontStyle.Bold, TextAnchor.MiddleCenter, 0.0f, y, 700.0f, 42.0f, new Color(0.88f, 0.96f, 1.0f, 1.0f));
      y -= 38.0f;
      CreateText(panel, "Subtitle", "Create a local world, or connect to a hosted Cubus world.", 15, FontStyle.Normal, TextAnchor.MiddleCenter, 0.0f, y, 700.0f, 28.0f, new Color(0.65f, 0.76f, 0.86f, 1.0f));
      y -= 46.0f;

      localModeButton = CreateButton(panel, "Local Game", -178.0f, y, 170.0f, 36.0f, () => SetMode(SetupMode.Local));
      connectedModeButton = CreateButton(panel, "Connected Game", 178.0f, y, 170.0f, 36.0f, () => SetMode(SetupMode.Connected));
      y -= 50.0f;

      CreateSectionLabel("World", y);
      y -= 36.0f;
      worldIdInput = CreateInputRow("World ID", worldId, y, value => worldId = value);
      y -= 38.0f;

      CreateText(panel, "Terrain Label", "Terrain", 14, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 120.0f, 30.0f, new Color(0.78f, 0.86f, 0.93f, 1.0f));
      blockTerrainButton = CreateButton(panel, "Block", -70.0f, y, 120.0f, 32.0f, () => SetTerrain(TerrainSystem.Block));
      smoothTerrainButton = CreateButton(panel, "Smooth", 70.0f, y, 120.0f, 32.0f, () => SetTerrain(TerrainSystem.SmoothDensity));
      y -= 42.0f;

      seedInput = CreateInputRow("Seed", seed, y, value => seed = value, 390.0f);
      CreateButton(panel, "Random", 258.0f, y, 104.0f, 32.0f, RandomizeSeed);
      y -= 38.0f;

      voxelSizeInput = CreateInputRow("Voxel Size", voxelSize, y, value => voxelSize = value);
      y -= 38.0f;
      minChunkYInput = CreateInputRow("Min Chunk Y", minChunkY, y, value => minChunkY = value);
      y -= 38.0f;
      maxChunkYInput = CreateInputRow("Max Chunk Y", maxChunkY, y, value => maxChunkY = value);
      y -= 38.0f;
      minChunkXInput = CreateInputRow("Min Chunk X", minChunkX, y, value => minChunkX = value);
      y -= 38.0f;
      minChunkZInput = CreateInputRow("Min Chunk Z", minChunkZ, y, value => minChunkZ = value);
      y -= 38.0f;
      maxChunkXInput = CreateInputRow("Max Chunk X", maxChunkX, y, value => maxChunkX = value);
      y -= 38.0f;
      maxChunkZInput = CreateInputRow("Max Chunk Z", maxChunkZ, y, value => maxChunkZ = value);
      y -= 48.0f;

      if (mode == SetupMode.Connected)
      {
        CreateSectionLabel("Server", y);
        y -= 36.0f;
        serverUriInput = CreateInputRow("Server URI", serverUri, y, value => serverUri = value);
        y -= 38.0f;
        moduleNameInput = CreateInputRow("Module", moduleName, y, value => moduleName = value);
        y -= 44.0f;
      }

      if (CubusDeveloperMode.IsEnabled)
      {
        CreateSectionLabel("Developer World Tools", y);
        y -= 36.0f;

        if (mode == SetupMode.Local)
        {
          CreateButton(panel, "Refresh Local Worlds", -190.0f, y, 180.0f, 30.0f, () => { RefreshLocalWorlds(); RebuildControls(); UpdateUiState(true); });
          CreateButton(panel, "New Random World ID", 40.0f, y, 210.0f, 30.0f, PrepareNewRandomWorldId);
          y -= 38.0f;

          CreateText(panel, "Storage", $"Storage: {GetLocalWorldRootDirectory()}", 12, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 600.0f, 22.0f, new Color(0.56f, 0.68f, 0.76f, 1.0f));
          y -= 28.0f;

          int visibleCount = Mathf.Min(localWorlds.Count, 4);
          if (visibleCount == 0)
          {
            CreateText(panel, "No Worlds", "No saved local worlds found.", 13, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 600.0f, 24.0f, new Color(0.78f, 0.86f, 0.93f, 1.0f));
            y -= 30.0f;
          }
          else
          {
            for (int i = 0; i < visibleCount; i++)
            {
              int index = i;
              LocalWorldEntry entry = localWorlds[i];
              WorldManifest manifest = entry.Manifest;
              string label = manifest == null
                  ? entry.WorldId
                  : $"{entry.WorldId} | {manifest.TerrainSystem} | Seed {manifest.WorldSeed} | Chunks {manifest.ChunkCount}";
              CreateButton(panel, label, 0.0f, y, 600.0f, 28.0f, () => SelectLocalWorld(index));
              y -= 32.0f;
            }
          }

          CreateButton(panel, "Use Selected", -210.0f, y, 130.0f, 30.0f, () => SelectLocalWorld(selectedLocalWorldIndex));
          CreateButton(panel, "Delete Selected", -60.0f, y, 140.0f, 30.0f, DeleteSelectedLocalWorld);
          CreateButton(panel, "Regenerate Selected", 120.0f, y, 170.0f, 30.0f, () => { deleteLocalWorldBeforeLaunch = true; StartSelectedGame(true, false); });
          y -= 38.0f;

          deleteToggleButton = CreateButton(panel, string.Empty, 0.0f, y, 600.0f, 30.0f, () => { deleteLocalWorldBeforeLaunch = !deleteLocalWorldBeforeLaunch; UpdateUiState(false); });
        }
        else
        {
          CreateText(panel, "Connected Dev Info", "Connected worlds are server-authoritative, so local world listing is hidden.", 13, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 600.0f, 24.0f, new Color(0.78f, 0.86f, 0.93f, 1.0f));
          y -= 32.0f;
          resetConnectedToggleButton = CreateButton(panel, string.Empty, 0.0f, y, 600.0f, 30.0f, () => { resetConnectedWorldOnLaunch = !resetConnectedWorldOnLaunch; UpdateUiState(false); });
        }
      }
      else
      {
        CreateText(panel, "Dev Hidden", "Dev tools hidden. Open runtime console with ` and run: dev on", 13, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 600.0f, 24.0f, new Color(0.56f, 0.68f, 0.76f, 1.0f));
      }

      startButton = CreateButton(panel, mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game", 0.0f, -675.0f, 320.0f, 42.0f, () => StartSelectedGame(deleteLocalWorldBeforeLaunch, resetConnectedWorldOnLaunch), true);
      statusText = CreateText(panel, "Status", string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleLeft, -300.0f, -724.0f, 600.0f, 28.0f, new Color(0.88f, 0.96f, 1.0f, 1.0f));
      loadingText = CreateText(panel, "Loading", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, -752.0f, 600.0f, 24.0f, new Color(0.65f, 0.76f, 0.86f, 1.0f));

      Image loadingBack = CreateImage(panel, "Loading Progress Back", new Color(0.0f, 0.0f, 0.0f, 1.0f));
      RectTransform loadingBackRect = loadingBack.rectTransform;
      loadingBackRect.anchorMin = new Vector2(0.5f, 1.0f);
      loadingBackRect.anchorMax = new Vector2(0.5f, 1.0f);
      loadingBackRect.pivot = new Vector2(0.5f, 0.5f);
      loadingBackRect.anchoredPosition = new Vector2(0.0f, -786.0f);
      loadingBackRect.sizeDelta = new Vector2(600.0f, 12.0f);
      loadingBack.raycastTarget = false;

      GameObject fillObject = new("Loading Progress Fill");
      fillObject.transform.SetParent(loadingBackRect, false);
      loadingFill = fillObject.AddComponent<Image>();
      loadingFill.color = new Color(0.20f, 0.75f, 1.0f, 1.0f);
      loadingFill.type = Image.Type.Filled;
      loadingFill.fillMethod = Image.FillMethod.Horizontal;
      loadingFill.fillOrigin = 0;
      loadingFill.raycastTarget = false;
      Stretch(loadingFill.rectTransform);

      UpdateUiState(forceText: true);
    }

    private void CreateSectionLabel(string label, float y)
    {
      Image line = CreateImage(panel, label + " Rule", new Color(0.12f, 0.55f, 0.80f, 0.30f));
      RectTransform lineRect = line.rectTransform;
      lineRect.anchorMin = new Vector2(0.5f, 1.0f);
      lineRect.anchorMax = new Vector2(0.5f, 1.0f);
      lineRect.pivot = new Vector2(0.0f, 0.5f);
      lineRect.anchoredPosition = new Vector2(-300.0f, y - 15.0f);
      lineRect.sizeDelta = new Vector2(600.0f, 1.0f);
      line.raycastTarget = false;

      CreateText(panel, label + " Label", label, 18, FontStyle.Bold, TextAnchor.MiddleLeft, -300.0f, y, 600.0f, 26.0f, new Color(0.88f, 0.96f, 1.0f, 1.0f));
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

      SetButtonLabel(localModeButton, mode == SetupMode.Local ? "LOCAL GAME" : "Local Game");
      SetButtonLabel(connectedModeButton, mode == SetupMode.Connected ? "CONNECTED GAME" : "Connected Game");
      SetButtonLabel(blockTerrainButton, terrainSystem == TerrainSystem.Block ? "BLOCK" : "Block");
      SetButtonLabel(smoothTerrainButton, terrainSystem == TerrainSystem.SmoothDensity ? "SMOOTH" : "Smooth");
      SetButtonLabel(startButton, mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game");
      SetButtonLabel(deleteToggleButton, deleteLocalWorldBeforeLaunch ? "[x] Delete/regenerate current local World ID before launch" : "[ ] Delete/regenerate current local World ID before launch");
      SetButtonLabel(resetConnectedToggleButton, resetConnectedWorldOnLaunch ? "[x] Reset/regenerate connected world on launch" : "[ ] Reset/regenerate connected world on launch");

      if (startButton != null) startButton.interactable = !isLoading && !CubusUiInput.ConsoleOpen;
      if (statusText != null) statusText.text = $"Status: {status}";

      if (loadingText != null)
      {
        if (isLoading)
        {
          float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
          loadingText.text = $"Loading CubusLoading | Scene: {loadingScenePath} | Elapsed: {elapsed:0.0}s | Progress: {loadingProgress * 100.0f:0}%";
        }
        else
        {
          loadingText.text = string.Empty;
        }
      }

      if (loadingFill != null)
      {
        loadingFill.fillAmount = isLoading ? loadingProgress : 0.0f;
      }
    }

    private static void UpdateInput(InputField field, string value, bool force)
    {
      if (field == null || (!force && field.isFocused))
      {
        return;
      }

      string safeValue = value ?? string.Empty;
      if (!string.Equals(field.text, safeValue, StringComparison.Ordinal))
      {
        field.SetTextWithoutNotify(safeValue);
      }
    }

    private static void SetButtonLabel(Button button, string label)
    {
      if (button == null)
      {
        return;
      }

      Text text = button.GetComponentInChildren<Text>();
      if (text != null)
      {
        text.text = label ?? string.Empty;
      }
    }

    private InputField CreateInputRow(string label, string value, float y, Action<string> onChanged, float inputWidth = 470.0f)
    {
      CreateText(panel, label + " Label", label, 14, FontStyle.Normal, TextAnchor.MiddleLeft, -300.0f, y, 120.0f, 30.0f, new Color(0.78f, 0.86f, 0.93f, 1.0f));
      return CreateInput(panel, value, -170.0f, y, inputWidth, 30.0f, onChanged);
    }

    private static InputField CreateInput(RectTransform parent, string value, float x, float y, float width, float height, Action<string> onChanged)
    {
      GameObject go = new("Input");
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(0.5f, 1.0f);
      rect.anchorMax = new Vector2(0.5f, 1.0f);
      rect.pivot = new Vector2(0.0f, 0.5f);
      rect.anchoredPosition = new Vector2(x, y);
      rect.sizeDelta = new Vector2(width, height);

      Image image = go.AddComponent<Image>();
      image.color = new Color(0.006f, 0.010f, 0.018f, 0.96f);
      image.raycastTarget = true;

      InputField input = go.AddComponent<InputField>();
      input.lineType = InputField.LineType.SingleLine;
      input.caretColor = new Color(0.88f, 0.96f, 1.0f, 1.0f);
      input.selectionColor = new Color(0.20f, 0.55f, 0.80f, 0.45f);

      Text text = CreateText(rect, "Text", value ?? string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, 0.0f, 0.0f, width - 16.0f, height - 8.0f, Color.white);
      Stretch(text.rectTransform);
      text.rectTransform.offsetMin = new Vector2(8.0f, 4.0f);
      text.rectTransform.offsetMax = new Vector2(-8.0f, -4.0f);

      Text placeholder = CreateText(rect, "Placeholder", "...", 14, FontStyle.Normal, TextAnchor.MiddleLeft, 0.0f, 0.0f, width - 16.0f, height - 8.0f, new Color(0.55f, 0.66f, 0.75f, 0.65f));
      Stretch(placeholder.rectTransform);
      placeholder.rectTransform.offsetMin = new Vector2(8.0f, 4.0f);
      placeholder.rectTransform.offsetMax = new Vector2(-8.0f, -4.0f);

      input.textComponent = text;
      input.placeholder = placeholder;
      input.SetTextWithoutNotify(value ?? string.Empty);
      input.onValueChanged.AddListener(next => onChanged?.Invoke(next));
      return input;
    }

    private static Button CreateButton(RectTransform parent, string label, float x, float y, float width, float height, Action onClick, bool primary = false)
    {
      GameObject go = new("Button");
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(0.5f, 1.0f);
      rect.anchorMax = new Vector2(0.5f, 1.0f);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = new Vector2(x, y);
      rect.sizeDelta = new Vector2(width, height);

      Image image = go.AddComponent<Image>();
      image.color = primary
          ? new Color(0.12f, 0.42f, 0.62f, 1.0f)
          : new Color(0.055f, 0.110f, 0.165f, 1.0f);
      image.raycastTarget = true;

      Button button = go.AddComponent<Button>();
      button.targetGraphic = image;
      ColorBlock colors = button.colors;
      colors.normalColor = image.color;
      colors.highlightedColor = primary
          ? new Color(0.18f, 0.55f, 0.78f, 1.0f)
          : new Color(0.09f, 0.19f, 0.28f, 1.0f);
      colors.pressedColor = new Color(0.035f, 0.075f, 0.110f, 1.0f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.035f, 0.040f, 0.050f, 0.65f);
      button.colors = colors;

      if (onClick != null)
      {
        button.onClick.AddListener(() => onClick());
      }

      CreateText(rect, "Label", label, 14, FontStyle.Bold, TextAnchor.MiddleCenter, 0.0f, 0.0f, width, height, new Color(0.88f, 0.96f, 1.0f, 1.0f));
      return button;
    }

    private static Text CreateText(RectTransform parent, string name, string value, int size, FontStyle style, TextAnchor alignment, float x, float y, float width, float height, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      RectTransform rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(0.5f, 1.0f);
      rect.anchorMax = new Vector2(0.5f, 1.0f);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = new Vector2(x, y);
      rect.sizeDelta = new Vector2(width, height);

      Text text = go.AddComponent<Text>();
      text.text = value ?? string.Empty;
      text.font = UiFont;
      text.fontSize = size;
      text.fontStyle = style;
      text.alignment = alignment;
      text.color = color;
      text.raycastTarget = false;
      text.horizontalOverflow = HorizontalWrapMode.Overflow;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      return text;
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
