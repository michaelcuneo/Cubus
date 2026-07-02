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
using Assets.Demo.Scripts.Core;
using Assets.Demo.Scripts.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Runtime Canvas launcher UI for the dedicated loading-scene flow.
  /// The game demo always launches as smooth-density terrain with a sparse block layer.
  /// Engine/dev terrain options belong in editor/debug tooling, not this player entry screen.
  /// </summary>
  public sealed class DemoLoadingLauncherMenu : MonoBehaviour
  {
    private enum SetupMode
    {
      Local,
      Connected,
    }

    [SerializeField] private string loadingSceneName = "DemoLoading";
    [SerializeField] private string defaultServerUri = "http://cubus.michaelcuneo.com.au";
    [SerializeField] private string defaultModuleName = "demo";
    [SerializeField] private string defaultWorldId = "demo_world";
    [SerializeField] private int sortingOrder = 1000;

    private static readonly Vector2 RuntimePanelSize = new(680.0f, 560.0f);
    private const TerrainSystem GameTerrainSystem = TerrainSystem.Hybrid;
    private const float GameVoxelSize = 1.0f;
    private const int GameMinChunkY = -1;
    private const int GameMaxChunkY = 2;
    private static readonly Vector2Int GameWorldMinChunkXZ = new(-1024, -1024);
    private static readonly Vector2Int GameWorldMaxChunkXZ = new(1024, 1024);

    private SetupMode mode = SetupMode.Connected;
    private string serverUri;
    private string moduleName;
    private string worldId;
    private string seed;
    private string status = "Choose how to start.";
    private bool isLoading;
    private float loadingProgress;
    private float loadingStartedAt;
    private string loadingScenePath;
    private bool resetConnectedWorldOnLaunch;

    private CanvasGroup canvasGroup;
    private RectTransform contentRoot;
    private Text statusText;
    private Text loadingText;
    private Image loadingFill;
    private Button localModeButton;
    private Button connectedModeButton;
    private Button startButton;
    private Button resetConnectedToggleButton;
    private InputField worldIdInput;
    private InputField seedInput;
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
          cachedUiFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana" }, 14);
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
      BuildUi();
      RebuildControls();
      UpdateUiState(true);
    }

    private void Update()
    {
      UpdateUiState(false);
    }

    private void BuildUi()
    {
      DemoInputSystemUiGuard.EnsureEventSystem();

      GameObject canvasObject = new("Demo Launcher Canvas");
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

      Image shadow = CreateImage(root, "Launcher Panel Shadow", new Color(0.0f, 0.0f, 0.0f, 0.35f));
      RectTransform shadowRect = shadow.rectTransform;
      shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
      shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
      shadowRect.pivot = new Vector2(0.5f, 0.5f);
      shadowRect.sizeDelta = RuntimePanelSize + new Vector2(18.0f, 18.0f);
      shadowRect.anchoredPosition = new Vector2(10.0f, -10.0f);
      shadow.raycastTarget = false;

      GameObject panelObject = new("Launcher Panel");
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
      accentRect.sizeDelta = new Vector2(0.0f, 4.0f);
      accent.raycastTarget = false;

      GameObject contentObject = new("Launcher Content");
      contentObject.transform.SetParent(panel, false);
      contentRoot = contentObject.AddComponent<RectTransform>();
      contentRoot.anchorMin = Vector2.zero;
      contentRoot.anchorMax = Vector2.one;
      contentRoot.offsetMin = new Vector2(34.0f, 24.0f);
      contentRoot.offsetMax = new Vector2(-34.0f, -24.0f);

      VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
      layout.childAlignment = TextAnchor.UpperCenter;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;
      layout.spacing = 8.0f;
      layout.padding = new RectOffset(0, 0, 4, 0);
    }

    private void RebuildControls()
    {
      if (contentRoot == null)
      {
        return;
      }

      for (int i = contentRoot.childCount - 1; i >= 0; i--)
      {
        Destroy(contentRoot.GetChild(i).gameObject);
      }

      localModeButton = null;
      connectedModeButton = null;
      startButton = null;
      resetConnectedToggleButton = null;
      worldIdInput = null;
      seedInput = null;
      serverUriInput = null;
      moduleNameInput = null;

      AddText(contentRoot, "DEMO", 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.96f, 1.0f, 1.0f), 42.0f);
      AddText(contentRoot, "Smooth terrain with sparse buildable block voxels.", 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.62f, 0.74f, 0.84f, 1.0f), 24.0f);

      RectTransform modeRow = AddRow(contentRoot, 36.0f, 10.0f);
      localModeButton = AddButton(modeRow, "Local Game", () => SetMode(SetupMode.Local));
      connectedModeButton = AddButton(modeRow, "Connected Game", () => SetMode(SetupMode.Connected));

      AddSection("World");
      worldIdInput = AddInputRow("World ID", worldId, value => worldId = value);
      AddText(contentRoot, "Terrain: Smooth density world + sparse block layer", 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.62f, 0.74f, 0.84f, 1.0f), 24.0f);

      RectTransform seedRow = AddRow(contentRoot, 32.0f, 8.0f);
      AddFixedText(seedRow, "Seed", 120.0f, 14, FontStyle.Bold, TextAnchor.MiddleLeft);
      seedInput = AddInput(seedRow, seed, value => seed = value);
      AddButton(seedRow, "Random", RandomizeSeed, 100.0f);

      if (mode == SetupMode.Connected)
      {
        AddSection("Server");
        serverUriInput = AddInputRow("Server URI", serverUri, value => serverUri = value);
        moduleNameInput = AddInputRow("Module", moduleName, value => moduleName = value);
        resetConnectedToggleButton = AddButton(contentRoot, string.Empty, () => { resetConnectedWorldOnLaunch = !resetConnectedWorldOnLaunch; UpdateUiState(false); }, -1.0f, 30.0f);
      }
      else
      {
        AddText(contentRoot, "Start Local Game creates the World ID above if new, or resumes it from cache.", 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.62f, 0.74f, 0.84f, 1.0f), 24.0f);
        AddSavedWorldsSection();
      }

      AddSpacer(contentRoot, 4.0f);
      startButton = AddButton(contentRoot, mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game", StartSelectedGame, -1.0f, 42.0f, true);
      statusText = AddText(contentRoot, string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.88f, 0.96f, 1.0f, 1.0f), 28.0f);
      loadingText = AddText(contentRoot, string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.65f, 0.76f, 0.86f, 1.0f), 24.0f);
      loadingFill = AddProgressBar(contentRoot, 12.0f);

      UpdateUiState(true);
    }

    private void AddSection(string label)
    {
      AddSpacer(contentRoot, 4.0f);
      AddText(contentRoot, label, 18, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.88f, 0.96f, 1.0f, 1.0f), 26.0f);
      Image rule = CreateImage(contentRoot, label + " Rule", new Color(0.12f, 0.55f, 0.80f, 0.32f));
      LayoutElement ruleLayout = rule.gameObject.AddComponent<LayoutElement>();
      ruleLayout.preferredHeight = 1.0f;
      ruleLayout.minHeight = 1.0f;
      rule.raycastTarget = false;
    }

    private InputField AddInputRow(string label, string value, Action<string> onChanged)
    {
      RectTransform row = AddRow(contentRoot, 32.0f, 8.0f);
      AddFixedText(row, label, 120.0f, 14, FontStyle.Bold, TextAnchor.MiddleLeft);
      return AddInput(row, value, onChanged);
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

    private static void AddSpacer(RectTransform parent, float height)
    {
      GameObject go = new("Spacer");
      go.transform.SetParent(parent, false);
      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = height;
      element.minHeight = height;
      element.flexibleWidth = 1.0f;
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

    private static Text AddFixedText(RectTransform parent, string value, float width, int size, FontStyle style, TextAnchor alignment)
    {
      Text text = AddText(parent, value, size, style, alignment, new Color(0.78f, 0.86f, 0.93f, 1.0f), 32.0f);
      LayoutElement element = text.GetComponent<LayoutElement>();
      element.preferredWidth = width;
      element.minWidth = width;
      element.flexibleWidth = 0.0f;
      return text;
    }

    private static InputField AddInput(RectTransform parent, string value, Action<string> onChanged)
    {
      GameObject go = new("Input");
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = new Color(0.006f, 0.010f, 0.018f, 0.96f);
      image.raycastTarget = true;

      LayoutElement element = go.AddComponent<LayoutElement>();
      element.preferredHeight = 30.0f;
      element.minHeight = 30.0f;
      element.flexibleWidth = 1.0f;

      InputField input = go.AddComponent<InputField>();
      input.lineType = InputField.LineType.SingleLine;
      input.caretColor = Color.white;
      input.selectionColor = new Color(0.20f, 0.55f, 0.80f, 0.45f);

      Text text = AddText(go.GetComponent<RectTransform>(), value ?? string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 30.0f);
      Stretch(text.rectTransform);
      text.rectTransform.offsetMin = new Vector2(8.0f, 3.0f);
      text.rectTransform.offsetMax = new Vector2(-8.0f, -3.0f);

      Text placeholder = AddText(go.GetComponent<RectTransform>(), "...", 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.55f, 0.66f, 0.75f, 0.65f), 30.0f);
      Stretch(placeholder.rectTransform);
      placeholder.rectTransform.offsetMin = new Vector2(8.0f, 3.0f);
      placeholder.rectTransform.offsetMax = new Vector2(-8.0f, -3.0f);

      input.textComponent = text;
      input.placeholder = placeholder;
      input.SetTextWithoutNotify(value ?? string.Empty);
      input.onValueChanged.AddListener(next => onChanged?.Invoke(next));
      return input;
    }

    private static Button AddButton(RectTransform parent, string label, Action onClick, float width = -1.0f, float height = 34.0f, bool primary = false)
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
        button.onClick.AddListener(() => onClick());
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

    private void UpdateUiState(bool forceText)
    {
      if (canvasGroup != null)
      {
        canvasGroup.interactable = !isLoading;
        canvasGroup.blocksRaycasts = true;
      }

      UpdateInput(worldIdInput, worldId, forceText);
      UpdateInput(seedInput, seed, forceText);
      UpdateInput(serverUriInput, serverUri, forceText);
      UpdateInput(moduleNameInput, moduleName, forceText);

      SetButtonLabel(localModeButton, mode == SetupMode.Local ? "LOCAL GAME" : "Local Game");
      SetButtonLabel(connectedModeButton, mode == SetupMode.Connected ? "CONNECTED GAME" : "Connected Game");
      SetButtonLabel(startButton, mode == SetupMode.Local ? "Start Local Game" : "Start Connected Game");
      SetButtonLabel(resetConnectedToggleButton, resetConnectedWorldOnLaunch ? "[x] Reset/regenerate connected world on launch" : "[ ] Reset/regenerate connected world on launch");

      if (startButton != null)
      {
        startButton.interactable = !isLoading;
      }

      if (statusText != null)
      {
        statusText.text = $"Status: {status}";
      }

      if (loadingText != null)
      {
        loadingText.text = isLoading
          ? $"Loading {loadingScenePath} | Elapsed: {Time.realtimeSinceStartup - loadingStartedAt:0.0}s | Progress: {loadingProgress * 100.0f:0}%"
          : string.Empty;
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

    private void StartSelectedGame()
    {
      if (isLoading || !TryBuildLaunchContext())
      {
        UpdateUiState(true);
        return;
      }

      if (!TryFindSceneInBuildSettings(loadingSceneName, out string scenePath))
      {
        status = $"Scene '{loadingSceneName}' is not in Build Settings.";
        Debug.LogError($"[CubusLauncher] Scene '{loadingSceneName}' was not found in Build Settings.");
        UpdateUiState(true);
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
      UpdateUiState(true);

      AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
      if (operation == null)
      {
        status = $"Failed to load scene {scenePath}.";
        isLoading = false;
        UpdateUiState(true);
        yield break;
      }

      operation.allowSceneActivation = true;
      while (!operation.isDone)
      {
        loadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
        UpdateUiState(false);
        yield return null;
      }
    }

    private bool TryBuildLaunchContext()
    {
      if (!int.TryParse(seed, out int parsedSeed))
      {
        status = "Seed must be an integer.";
        return false;
      }

      DemoGameLaunchContext.Set(
        mode == SetupMode.Local ? DemoGameLaunchMode.Local : DemoGameLaunchMode.Connected,
        serverUri,
        moduleName,
        worldId,
        GameTerrainSystem,
        parsedSeed,
        GameVoxelSize,
        GameMinChunkY,
        GameMaxChunkY,
        GameWorldMinChunkXZ,
        GameWorldMaxChunkXZ,
        mode == SetupMode.Local,
        resetConnectedWorldOnLaunch && mode == SetupMode.Connected);

      return true;
    }

    private void SetMode(SetupMode nextMode)
    {
      if (mode == nextMode)
      {
        return;
      }

      mode = nextMode;
      RebuildControls();
      UpdateUiState(true);
    }

    private void RandomizeSeed()
    {
      seed = Random.Range(int.MinValue, int.MaxValue).ToString();
      UpdateUiState(true);
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

    private static void DeleteLocalWorldById(string id)
    {
      if (string.IsNullOrWhiteSpace(id))
      {
        return;
      }

      string path = Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds", SanitizeWorldId(id));
      if (Directory.Exists(path))
      {
        Directory.Delete(path, true);
        Debug.Log($"[CubusLauncher] Deleted local world '{id}' at '{path}'.");
      }
    }

    private void AddSavedWorldsSection()
    {
      AddSection("Saved Worlds");

      List<SavedWorldInfo> worlds = EnumerateSavedWorlds();
      if (worlds.Count == 0)
      {
        AddText(contentRoot, "None yet. Enter a World ID above and Start Local Game to create one.", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.62f, 0.74f, 0.84f, 1.0f), 24.0f);
        return;
      }

      AddText(contentRoot, "Join resumes from cache. Delete clears it so the next launch regenerates.", 12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.55f, 0.66f, 0.75f, 1.0f), 22.0f);

      foreach (SavedWorldInfo info in worlds)
      {
        RectTransform row = AddRow(contentRoot, 34.0f, 8.0f);
        string label = info.Seed != 0 ? $"{info.Id}  (seed {info.Seed})" : info.Id;
        AddText(row, label, 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.82f, 0.90f, 0.97f, 1.0f), 34.0f);

        string capturedId = info.Id;
        int capturedSeed = info.Seed;
        AddButton(row, "Join", () => JoinSavedWorld(capturedId, capturedSeed), 90.0f, 30.0f, true);
        AddButton(row, "Delete", () => DeleteSavedWorldAndRefresh(capturedId), 90.0f, 30.0f);
      }
    }

    private void JoinSavedWorld(string id, int savedSeed)
    {
      if (isLoading)
      {
        return;
      }

      mode = SetupMode.Local;
      worldId = id;
      if (savedSeed != 0)
      {
        seed = savedSeed.ToString();
      }

      UpdateUiState(true);
      StartSelectedGame();
    }

    private void DeleteSavedWorldAndRefresh(string id)
    {
      if (isLoading)
      {
        return;
      }

      DeleteLocalWorldById(id);
      RebuildControls();
      UpdateUiState(true);
    }

    private static List<SavedWorldInfo> EnumerateSavedWorlds()
    {
      List<SavedWorldInfo> results = new();
      string root = Path.Combine(Application.persistentDataPath, "CubusCore", "Worlds");
      if (!Directory.Exists(root))
      {
        return results;
      }

      foreach (string dir in Directory.GetDirectories(root))
      {
        string id = Path.GetFileName(dir);
        int savedSeed = 0;

        string manifestPath = Path.Combine(dir, "manifest.json");
        if (File.Exists(manifestPath))
        {
          try
          {
            WorldManifest manifest = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(manifestPath));
            if (manifest != null)
            {
              if (!string.IsNullOrWhiteSpace(manifest.WorldId))
              {
                id = manifest.WorldId;
              }

              savedSeed = manifest.WorldSeed;
            }
          }
          catch (Exception ex)
          {
            Debug.LogWarning($"[CubusLauncher] Failed to read manifest for '{id}': {ex.Message}");
          }
        }

        results.Add(new SavedWorldInfo { Id = id, Seed = savedSeed });
      }

      results.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
      return results;
    }

    private struct SavedWorldInfo
    {
      public string Id;
      public int Seed;
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
