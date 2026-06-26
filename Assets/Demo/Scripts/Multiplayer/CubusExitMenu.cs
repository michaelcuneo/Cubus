using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class CubusExitMenu : MonoBehaviour
  {
    [SerializeField] private Key toggleKey = Key.Escape;
    [SerializeField] private int sortingOrder = 31000;

    private static readonly Vector2 RuntimePanelSize = new(440.0f, 250.0f);

    private CanvasGroup canvasGroup;
    private Text bodyText;
    private Button optionsButton;
    private Button exitButton;
    private Button cancelButton;
    private bool isOpen;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private bool hasSavedCursorState;

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
      BuildUi();
      SetOpen(false, restoreCursor: false);
    }

    private void Update()
    {
      Keyboard keyboard = Keyboard.current;
      if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame)
      {
        return;
      }

      if (CubusUiInput.ShouldSuppressMenuInput)
      {
        return;
      }

      if (!isOpen && CubusUiInput.IsCapturing)
      {
        return;
      }

      SetOpen(!isOpen, restoreCursor: true);
    }

    private void OnDisable()
    {
      if (isOpen)
      {
        SetOpen(false, restoreCursor: true);
      }
    }

    private void OnDestroy()
    {
      if (optionsButton != null)
      {
        optionsButton.onClick.RemoveListener(OpenOptions);
      }

      if (exitButton != null)
      {
        exitButton.onClick.RemoveListener(CloseApplication);
      }

      if (cancelButton != null)
      {
        cancelButton.onClick.RemoveListener(Cancel);
      }

      CubusUiInput.MenuOpen = false;
      RestoreCursorIfNeeded();
    }

    private void SetOpen(bool open, bool restoreCursor)
    {
      if (open == isOpen)
      {
        return;
      }

      isOpen = open;
      CubusUiInput.MenuOpen = open;

      if (bodyText != null)
      {
        bodyText.text = "Pause menu";
      }

      if (canvasGroup != null)
      {
        canvasGroup.alpha = open ? 1.0f : 0.0f;
        canvasGroup.interactable = open;
        canvasGroup.blocksRaycasts = open;
      }

      if (open)
      {
        SaveAndReleaseCursor();
        SelectDefaultButton();
      }
      else if (restoreCursor)
      {
        RestoreCursorIfNeeded();
      }
    }

    private void SaveAndReleaseCursor()
    {
      if (!hasSavedCursorState)
      {
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        hasSavedCursorState = true;
      }

      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }

    private void RestoreCursorIfNeeded()
    {
      if (!hasSavedCursorState)
      {
        return;
      }

      Cursor.lockState = previousLockState;
      Cursor.visible = previousCursorVisible;
      hasSavedCursorState = false;
    }

    private void Cancel()
    {
      SetOpen(false, restoreCursor: true);
    }

    private void OpenOptions()
    {
      if (bodyText != null)
      {
        bodyText.text = "Options are not wired yet. Next step: audio, controls, graphics, and gameplay settings.";
      }
    }

    private void SelectDefaultButton()
    {
      if (EventSystem.current == null || cancelButton == null)
      {
        return;
      }

      EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
    }

    private void BuildUi()
    {
      CubusInputSystemUiGuard.EnsureEventSystem();

      GameObject canvasObject = new("Cubus Exit Menu Canvas");
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
      canvasGroup.alpha = 0.0f;
      canvasGroup.interactable = false;
      canvasGroup.blocksRaycasts = false;

      Image dim = CreateImage(root, "Input Shield", new Color(0.0f, 0.0f, 0.0f, 0.48f));
      dim.raycastTarget = true;
      Stretch(dim.rectTransform);

      Image shadow = CreateImage(root, "Pause Panel Shadow", new Color(0.0f, 0.0f, 0.0f, 0.35f));
      RectTransform shadowRect = shadow.rectTransform;
      shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
      shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
      shadowRect.pivot = new Vector2(0.5f, 0.5f);
      shadowRect.sizeDelta = RuntimePanelSize + new Vector2(18.0f, 18.0f);
      shadowRect.anchoredPosition = new Vector2(10.0f, -10.0f);
      shadow.raycastTarget = false;

      GameObject panelObject = new("Pause Panel");
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

      GameObject contentObject = new("Pause Content");
      contentObject.transform.SetParent(panel, false);
      RectTransform content = contentObject.AddComponent<RectTransform>();
      content.anchorMin = Vector2.zero;
      content.anchorMax = Vector2.one;
      content.offsetMin = new Vector2(30.0f, 28.0f);
      content.offsetMax = new Vector2(-30.0f, -28.0f);

      VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
      layout.childAlignment = TextAnchor.UpperCenter;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;
      layout.spacing = 10.0f;
      layout.padding = new RectOffset(0, 0, 6, 0);

      AddText(content, "CUBUS", 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.96f, 1.0f, 1.0f), 42.0f);
      bodyText = AddText(content, "Pause menu", 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.62f, 0.74f, 0.84f, 1.0f), 44.0f);

      optionsButton = AddButton(content, "Options", OpenOptions, -1.0f, 38.0f, false);
      exitButton = AddButton(content, "Exit to Windows", CloseApplication, -1.0f, 38.0f, true);
      cancelButton = AddButton(content, "Cancel", Cancel, -1.0f, 38.0f, false);

      optionsButton.onClick.AddListener(OpenOptions);
      exitButton.onClick.AddListener(CloseApplication);
      cancelButton.onClick.AddListener(Cancel);
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

    private static void CloseApplication()
    {
#if UNITY_EDITOR
      UnityEditor.EditorApplication.isPlaying = false;
#else
      Application.Quit();
#endif
    }
  }
}
