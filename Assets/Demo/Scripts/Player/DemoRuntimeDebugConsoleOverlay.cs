using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Assets.Demo.Scripts.Player
{
  /// <summary>
  /// App-wide runtime console UI. This is the only visual console.
  /// The legacy DemoRuntimeDebugConsole remains as the command/log backend only.
  /// </summary>
  [DefaultExecutionOrder(32001)]
  public sealed class DemoRuntimeDebugConsoleOverlay : MonoBehaviour
  {
    private const int TopmostSortingOrder = 32767;
    private const float SlideSeconds = 0.16f;

    private DemoRuntimeDebugConsole backend;
    private FieldInfo backendVisibleField;
    private FieldInfo backendLogLinesField;
    private MethodInfo backendExecuteCommandMethod;
    private MethodInfo backendRestoreInputMethod;

    private Canvas canvas;
    private CanvasGroup rootGroup;
    private RectTransform panel;
    private Text logText;
    private InputField inputField;
    private ScrollRect scrollRect;

    private DemoFirstPersonController cachedFirstPersonController;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool hasSavedCursorState;
    private bool disabledController;
    private bool isOpen;
    private bool wasBackquoteDown;
    private bool wasEscapeDown;
    private bool wasEnterDown;
    private float slideVelocity;
    private string lastRenderedLog = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<DemoRuntimeDebugConsoleOverlay>() != null)
      {
        return;
      }

      GameObject go = new("Cubus Runtime Console Scene");
      go.AddComponent<DemoRuntimeDebugConsoleOverlay>();
      DontDestroyOnLoad(go);
    }

    private void Awake()
    {
      BindBackend();
      BuildUi();
      ForceClosed();
    }

    private void Start()
    {
      ForceClosed();
    }

    private void Update()
    {
      BindBackend();
      SuppressLegacyImguiConsole();
      KeepCanvasTopmost();
      HandleToggleKeys();

      if (!isOpen)
      {
        return;
      }

      HandleSubmitKey();
      RenderLogFromBackend();
      FocusInput();
    }

    private void OnDestroy()
    {
      RestoreInputAfterConsole();
    }

    private void BindBackend()
    {
      if (backend == null)
      {
        backend = FindAnyObjectByType<DemoRuntimeDebugConsole>();
      }

      if (backend == null || backendExecuteCommandMethod != null)
      {
        return;
      }

      Type type = typeof(DemoRuntimeDebugConsole);
      backendVisibleField = type.GetField("consoleVisible", BindingFlags.Instance | BindingFlags.NonPublic);
      backendLogLinesField = type.GetField("logLines", BindingFlags.Instance | BindingFlags.NonPublic);
      backendExecuteCommandMethod = type.GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic);
      backendRestoreInputMethod = type.GetMethod("RestoreInputAfterConsole", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void SuppressLegacyImguiConsole()
    {
      if (backend == null || backendVisibleField == null)
      {
        return;
      }

      bool backendWasVisible = backendVisibleField.GetValue(backend) is bool value && value;
      if (!backendWasVisible)
      {
        return;
      }

      backendVisibleField.SetValue(backend, false);
      backendRestoreInputMethod?.Invoke(backend, null);
    }

    private void HandleToggleKeys()
    {
      Keyboard keyboard = Keyboard.current;
      bool backquoteDown = keyboard != null && keyboard[Key.Backquote].isPressed;
      bool escapeDown = keyboard != null && keyboard[Key.Escape].isPressed;

      if (backquoteDown && !wasBackquoteDown)
      {
        SetOpen(!isOpen);
      }

      if (isOpen && escapeDown && !wasEscapeDown)
      {
        SetOpen(false);
      }

      wasBackquoteDown = backquoteDown;
      wasEscapeDown = escapeDown;
    }

    private void HandleSubmitKey()
    {
      Keyboard keyboard = Keyboard.current;
      bool enterDown = keyboard != null && (keyboard[Key.Enter].isPressed || keyboard[Key.NumpadEnter].isPressed);

      if (enterDown && !wasEnterDown)
      {
        SubmitInput();
      }

      wasEnterDown = enterDown;
    }

    private void SetOpen(bool open)
    {
      if (isOpen == open)
      {
        return;
      }

      isOpen = open;
      StopAllCoroutines();
      StartCoroutine(SlidePanel(open));

      if (open)
      {
        PrepareInputForConsole();
        RenderLogFromBackend(true);
        FocusInput();
      }
      else
      {
        RestoreInputAfterConsole();
      }
    }

    private void ForceClosed()
    {
      isOpen = false;
      StopAllCoroutines();

      if (rootGroup != null)
      {
        rootGroup.alpha = 0.0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;
      }

      if (panel != null)
      {
        panel.anchoredPosition = GetClosedPosition();
      }

      RestoreInputAfterConsole();
      SuppressLegacyImguiConsole();
    }

    private IEnumerator SlidePanel(bool open)
    {
      if (rootGroup == null || panel == null)
      {
        yield break;
      }

      rootGroup.interactable = open;
      rootGroup.blocksRaycasts = open;

      Vector2 start = panel.anchoredPosition;
      Vector2 end = open ? GetOpenPosition() : GetClosedPosition();
      float startAlpha = rootGroup.alpha;
      float endAlpha = open ? 1.0f : 0.0f;
      float t = 0.0f;

      while (t < 1.0f)
      {
        t += Time.unscaledDeltaTime / Mathf.Max(0.01f, SlideSeconds);
        float eased = 1.0f - Mathf.Pow(1.0f - Mathf.Clamp01(t), 3.0f);
        panel.anchoredPosition = Vector2.LerpUnclamped(start, end, eased);
        rootGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, eased);
        yield return null;
      }

      panel.anchoredPosition = end;
      rootGroup.alpha = endAlpha;
      rootGroup.interactable = open;
      rootGroup.blocksRaycasts = open;
    }

    private Vector2 GetOpenPosition()
    {
      return new Vector2(0.0f, 32.0f);
    }

    private Vector2 GetClosedPosition()
    {
      return new Vector2(0.0f, -720.0f);
    }

    private void SubmitInput()
    {
      if (inputField == null)
      {
        return;
      }

      string command = (inputField.text ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(command))
      {
        return;
      }

      inputField.text = string.Empty;
      inputField.ActivateInputField();
      inputField.Select();
      backendExecuteCommandMethod?.Invoke(backend, new object[] { command });
      RenderLogFromBackend(true);
    }

    private void PrepareInputForConsole()
    {
      if (!hasSavedCursorState)
      {
        previousCursorLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        hasSavedCursorState = true;
      }

      if (cachedFirstPersonController == null)
      {
        cachedFirstPersonController = FindAnyObjectByType<DemoFirstPersonController>();
      }

      if (cachedFirstPersonController != null && cachedFirstPersonController.enabled)
      {
        cachedFirstPersonController.enabled = false;
        disabledController = true;
      }

      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }

    private void RestoreInputAfterConsole()
    {
      if (disabledController && cachedFirstPersonController != null)
      {
        cachedFirstPersonController.enabled = true;
      }

      disabledController = false;

      if (hasSavedCursorState)
      {
        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
        hasSavedCursorState = false;
      }
    }

    private void BuildUi()
    {
      EnsureEventSystem();

      GameObject canvasObject = new("Cubus Runtime Console Canvas");
      canvasObject.transform.SetParent(transform, false);

      canvas = canvasObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.overrideSorting = true;
      canvas.sortingOrder = TopmostSortingOrder;

      CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
      scaler.matchWidthOrHeight = 0.5f;

      canvasObject.AddComponent<GraphicRaycaster>();
      RectTransform root = canvasObject.GetComponent<RectTransform>();
      Stretch(root);

      rootGroup = canvasObject.AddComponent<CanvasGroup>();
      rootGroup.alpha = 0.0f;
      rootGroup.interactable = false;
      rootGroup.blocksRaycasts = false;

      Image dim = CreateImage(root, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.72f));
      dim.raycastTarget = true;
      Stretch(dim.rectTransform);

      GameObject panelObject = new("Console Panel");
      panelObject.transform.SetParent(root, false);
      panel = panelObject.AddComponent<RectTransform>();
      panel.anchorMin = new Vector2(0.5f, 0.0f);
      panel.anchorMax = new Vector2(0.5f, 0.0f);
      panel.pivot = new Vector2(0.5f, 0.0f);
      panel.sizeDelta = new Vector2(1180.0f, 640.0f);
      panel.anchoredPosition = GetClosedPosition();

      Image panelImage = panelObject.AddComponent<Image>();
      panelImage.color = new Color(0.015f, 0.02f, 0.03f, 1.0f);
      panelImage.raycastTarget = true;

      Image header = CreateImage(panel, "Header", new Color(0.04f, 0.10f, 0.16f, 1.0f));
      RectTransform headerRect = header.rectTransform;
      headerRect.anchorMin = new Vector2(0.0f, 1.0f);
      headerRect.anchorMax = new Vector2(1.0f, 1.0f);
      headerRect.pivot = new Vector2(0.5f, 1.0f);
      headerRect.anchoredPosition = Vector2.zero;
      headerRect.sizeDelta = new Vector2(0.0f, 46.0f);

      Text title = CreateText(headerRect, "Title", "CUBUS RUNTIME CONSOLE", 18, FontStyle.Bold, new Color(0.72f, 0.93f, 1.0f, 1.0f));
      Stretch(title.rectTransform);
      title.rectTransform.offsetMin = new Vector2(18.0f, 0.0f);
      title.rectTransform.offsetMax = new Vector2(-18.0f, 0.0f);
      title.alignment = TextAnchor.MiddleLeft;

      Text help = CreateText(panel, "Help", "` or Esc to close  |  Enter to run", 13, FontStyle.Bold, new Color(0.68f, 0.82f, 0.92f, 1.0f));
      RectTransform helpRect = help.rectTransform;
      helpRect.anchorMin = new Vector2(0.0f, 1.0f);
      helpRect.anchorMax = new Vector2(1.0f, 1.0f);
      helpRect.pivot = new Vector2(0.5f, 1.0f);
      helpRect.anchoredPosition = new Vector2(0.0f, -54.0f);
      helpRect.sizeDelta = new Vector2(-32.0f, 28.0f);
      help.alignment = TextAnchor.MiddleLeft;

      BuildLog(panel);
      BuildInput(panel);
    }

    private void BuildLog(RectTransform parent)
    {
      GameObject scrollObject = new("Log Scroll");
      scrollObject.transform.SetParent(parent, false);
      RectTransform scrollTransform = scrollObject.AddComponent<RectTransform>();
      scrollTransform.anchorMin = new Vector2(0.0f, 0.0f);
      scrollTransform.anchorMax = new Vector2(1.0f, 1.0f);
      scrollTransform.offsetMin = new Vector2(16.0f, 60.0f);
      scrollTransform.offsetMax = new Vector2(-16.0f, -92.0f);

      Image scrollBg = scrollObject.AddComponent<Image>();
      scrollBg.color = Color.black;
      scrollBg.raycastTarget = true;

      scrollRect = scrollObject.AddComponent<ScrollRect>();
      scrollRect.horizontal = false;
      scrollRect.vertical = true;
      scrollRect.movementType = ScrollRect.MovementType.Clamped;

      GameObject viewportObject = new("Viewport");
      viewportObject.transform.SetParent(scrollObject.transform, false);
      RectTransform viewport = viewportObject.AddComponent<RectTransform>();
      Stretch(viewport);
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
      content.sizeDelta = new Vector2(-16.0f, 1200.0f);

      logText = CreateText(content, "Log Text", string.Empty, 13, FontStyle.Normal, new Color(0.90f, 0.95f, 1.0f, 1.0f));
      Stretch(logText.rectTransform);
      logText.alignment = TextAnchor.UpperLeft;
      logText.horizontalOverflow = HorizontalWrapMode.Wrap;
      logText.verticalOverflow = VerticalWrapMode.Overflow;

      ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
      fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

      scrollRect.viewport = viewport;
      scrollRect.content = content;
    }

    private void BuildInput(RectTransform parent)
    {
      GameObject inputObject = new("Runtime Console Input");
      inputObject.transform.SetParent(parent, false);
      RectTransform inputRect = inputObject.AddComponent<RectTransform>();
      inputRect.anchorMin = new Vector2(0.0f, 0.0f);
      inputRect.anchorMax = new Vector2(1.0f, 0.0f);
      inputRect.pivot = new Vector2(0.5f, 0.0f);
      inputRect.anchoredPosition = new Vector2(0.0f, 16.0f);
      inputRect.sizeDelta = new Vector2(-32.0f, 36.0f);

      Image bg = inputObject.AddComponent<Image>();
      bg.color = Color.black;
      bg.raycastTarget = true;

      inputField = inputObject.AddComponent<InputField>();
      inputField.lineType = InputField.LineType.SingleLine;
      inputField.textComponent = CreateText(inputRect, "Text", string.Empty, 15, FontStyle.Bold, Color.white);
      inputField.placeholder = CreateText(inputRect, "Placeholder", "type command...", 15, FontStyle.Normal, new Color(0.65f, 0.74f, 0.82f, 0.75f));

      inputField.textComponent.rectTransform.offsetMin = new Vector2(10.0f, 5.0f);
      inputField.textComponent.rectTransform.offsetMax = new Vector2(-10.0f, -5.0f);
      inputField.placeholder.rectTransform.offsetMin = new Vector2(10.0f, 5.0f);
      inputField.placeholder.rectTransform.offsetMax = new Vector2(-10.0f, -5.0f);
    }

    private void RenderLogFromBackend(bool force = false)
    {
      if (logText == null || backend == null || backendLogLinesField == null)
      {
        return;
      }

      if (backendLogLinesField.GetValue(backend) is not IEnumerable lines)
      {
        return;
      }

      List<string> copy = new();
      foreach (object line in lines)
      {
        if (line != null)
        {
          copy.Add(line.ToString());
        }
      }

      string next = string.Join("\n", copy);
      if (!force && string.Equals(next, lastRenderedLog, StringComparison.Ordinal))
      {
        return;
      }

      lastRenderedLog = next;
      logText.text = next;
      Canvas.ForceUpdateCanvases();
      if (scrollRect != null)
      {
        scrollRect.verticalNormalizedPosition = 0.0f;
      }
    }

    private void KeepCanvasTopmost()
    {
      if (canvas == null)
      {
        return;
      }

      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.overrideSorting = true;
      canvas.sortingOrder = TopmostSortingOrder;
      canvas.transform.SetAsLastSibling();
    }

    private void FocusInput()
    {
      if (inputField == null || !isOpen)
      {
        return;
      }

      if (!inputField.isFocused)
      {
        inputField.ActivateInputField();
        inputField.Select();
      }
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Image image = go.AddComponent<Image>();
      image.color = color;
      return image;
    }

    private static Text CreateText(RectTransform parent, string name, string value, int size, FontStyle style, Color color)
    {
      GameObject go = new(name);
      go.transform.SetParent(parent, false);
      Text text = go.AddComponent<Text>();
      text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      text.fontSize = size;
      text.fontStyle = style;
      text.color = color;
      text.text = value;
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
      if (FindAnyObjectByType<EventSystem>() != null)
      {
        return;
      }

      GameObject go = new("EventSystem");
      go.AddComponent<EventSystem>();
      DontDestroyOnLoad(go);
    }
  }
}
