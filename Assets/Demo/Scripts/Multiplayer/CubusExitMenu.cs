using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Demo.Scripts.Multiplayer
{
  public sealed class CubusExitMenu : MonoBehaviour
  {
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [SerializeField] private Vector2 panelSize = new(360.0f, 170.0f);
    [SerializeField] private int sortingOrder = 31000;

    private CanvasGroup canvasGroup;
    private Button exitButton;
    private Button cancelButton;
    private bool isOpen;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private bool hasSavedCursorState;

    private void Awake()
    {
      BuildUi();
      SetOpen(false, restoreCursor: false);
    }

    private void Update()
    {
      if (!Input.GetKeyDown(toggleKey))
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
      EnsureEventSystem();

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

      Image dim = CreateImage(root, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.62f));
      dim.raycastTarget = true;
      Stretch(dim.rectTransform);

      GameObject panelObject = new("Exit Panel");
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

      TMP_Text title = CreateText(panel, "Title", "Exit to Windows?", 28.0f, FontStyles.Bold, TextAlignmentOptions.Center);
      RectTransform titleRect = title.rectTransform;
      titleRect.anchorMin = new Vector2(0.0f, 1.0f);
      titleRect.anchorMax = new Vector2(1.0f, 1.0f);
      titleRect.pivot = new Vector2(0.5f, 1.0f);
      titleRect.anchoredPosition = new Vector2(0.0f, -24.0f);
      titleRect.sizeDelta = new Vector2(-32.0f, 42.0f);

      TMP_Text body = CreateText(panel, "Body", "Are you sure you want to quit Cubus?", 17.0f, FontStyles.Normal, TextAlignmentOptions.Center);
      RectTransform bodyRect = body.rectTransform;
      bodyRect.anchorMin = new Vector2(0.0f, 0.5f);
      bodyRect.anchorMax = new Vector2(1.0f, 0.5f);
      bodyRect.pivot = new Vector2(0.5f, 0.5f);
      bodyRect.anchoredPosition = new Vector2(0.0f, 8.0f);
      bodyRect.sizeDelta = new Vector2(-32.0f, 34.0f);

      exitButton = CreateButton(panel, "Exit Button", "Exit", new Vector2(-82.0f, -50.0f));
      cancelButton = CreateButton(panel, "Cancel Button", "Cancel", new Vector2(82.0f, -50.0f));

      exitButton.onClick.AddListener(CloseApplication);
      cancelButton.onClick.AddListener(Cancel);
    }

    private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchoredPosition)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);

      RectTransform rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(0.5f, 0.0f);
      rect.anchorMax = new Vector2(0.5f, 0.0f);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = new Vector2(140.0f, 38.0f);

      Image image = go.AddComponent<Image>();
      image.color = new Color(0.08f, 0.15f, 0.22f, 1.0f);
      image.raycastTarget = true;

      Button button = go.AddComponent<Button>();
      ColorBlock colors = button.colors;
      colors.normalColor = new Color(0.08f, 0.15f, 0.22f, 1.0f);
      colors.highlightedColor = new Color(0.12f, 0.24f, 0.34f, 1.0f);
      colors.pressedColor = new Color(0.04f, 0.10f, 0.16f, 1.0f);
      colors.selectedColor = colors.highlightedColor;
      colors.disabledColor = new Color(0.06f, 0.06f, 0.06f, 0.75f);
      button.colors = colors;

      TMP_Text text = CreateText(rect, "Label", label, 17.0f, FontStyles.Bold, TextAlignmentOptions.Center);
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

    private static TMP_Text CreateText(RectTransform parent, string name, string value, float size, FontStyles style, TextAlignmentOptions alignment)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
      text.text = value;
      text.fontSize = size;
      text.fontStyle = style;
      text.alignment = alignment;
      text.color = new Color(0.88f, 0.95f, 1.0f, 1.0f);
      text.raycastTarget = false;
      return text;
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
