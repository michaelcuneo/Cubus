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
  [DefaultExecutionOrder(32001)]
  public sealed class DemoRuntimeDebugConsoleOverlay : MonoBehaviour
  {
    private const int TopmostSortingOrder = 32767;
    private const string InputObjectName = "CubusTopmostConsoleInput";

    private DemoRuntimeDebugConsole sourceConsole;
    private FieldInfo sourceVisibleField;
    private FieldInfo sourceLogLinesField;
    private MethodInfo sourceExecuteCommandMethod;
    private Canvas canvas;
    private GameObject canvasObject;
    private RectTransform panelRect;
    private Text logText;
    private InputField inputField;
    private ScrollRect scrollRect;
    private DemoFirstPersonController cachedFirstPersonController;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool hasSavedCursorState;
    private bool disabledController;
    private bool visible;
    private bool wasBackquoteDown;
    private bool wasEscapeDown;
    private bool wasEnterDown;
    private string lastRenderedLog = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
      if (FindAnyObjectByType<DemoRuntimeDebugConsoleOverlay>() != null)
      {
        return;
      }

      GameObject go = new("Demo Runtime Debug Console Overlay");
      go.AddComponent<DemoRuntimeDebugConsoleOverlay>();
      DontDestroyOnLoad(go);
    }

    private void Awake()
    {
      BindSourceConsole();
      BuildCanvas();
      SetVisible(false, false);
    }

    private void Update()
    {
      BindSourceConsole();
      KeepCanvasTopmost();
      UpdateToggleInput();

      if (!visible)
      {
        return;
      }

      SyncSourceVisibility(false);
      UpdateSubmitInput();
      RenderLogFromSource();
      FocusInput();
    }

    private void OnDestroy()
    {
      RestoreInputAfterConsole();
      if (canvasObject != null)
      {
        Destroy(canvasObject);
      }
    }

    private void BindSourceConsole()
    {
      if (sourceConsole == null)
      {
        sourceConsole = FindAnyObjectByType<DemoRuntimeDebugConsole>();
      }

      if (sourceConsole == null || sourceExecuteCommandMethod != null)
      {
        return;
      }

      Type type = typeof(DemoRuntimeDebugConsole);
      sourceVisibleField = type.GetField("consoleVisible", BindingFlags.Instance | BindingFlags.NonPublic);
      sourceLogLinesField = type.GetField("logLines", BindingFlags.Instance | BindingFlags.NonPublic);
      sourceExecuteCommandMethod = type.GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void UpdateToggleInput()
    {
      Keyboard keyboard = Keyboard.current;
      bool backquoteDown = keyboard != null && keyboard[Key.Backquote].isPressed;
      bool escapeDown = keyboard != null && keyboard[Key.Escape].isPressed;

      if (backquoteDown && !wasBackquoteDown)
      {
        SetVisible(!visible, true);
      }

      if (visible && escapeDown && !wasEscapeDown)
      {
        SetVisible(false, true);
      }

      wasBackquoteDown = backquoteDown;
      wasEscapeDown = escapeDown;
    }

    private void UpdateSubmitInput()
    {
      Keyboard keyboard = Keyboard.current;
      bool enterDown = keyboard != null && (keyboard[Key.Enter].isPressed || keyboard[Key.NumpadEnter].isPressed);

      if (enterDown && !wasEnterDown)
      {
        SubmitInput();
      }

      wasEnterDown = enterDown;
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

      if (sourceConsole != null && sourceExecuteCommandMethod != null)
      {
        sourceExecuteCommandMethod.Invoke(sourceConsole, new object[] { command });
      }
    }

    private void SetVisible(bool value, bool syncSource)
    {
      visible = value;

      if (canvasObject != null)
      {
        canvasObject.SetActive(visible);
      }

      if (visible)
      {
        PrepareInputForConsole();
        KeepCanvasTopmost();
        RenderLogFromSource(true);
        FocusInput();
      }
      else
      {
        RestoreInputAfterConsole();
      }

      if (syncSource)
      {
        SyncSourceVisibility(false);
      }
    }

    private void SyncSourceVisibility(bool value)
    {
      if (sourceConsole != null && sourceVisibleField != null)
      {
        sourceVisibleField.SetValue(sourceConsole, value);
      }
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

    private void BuildCanvas()
    {
      EnsureEventSystem();

      canvasObject = new GameObject("Cubus Topmost Runtime Console Canvas");
      DontDestroyOnLoad(canvasObject);

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

      Image dim = CreateImage(root, "Dim", new Color(0.0f, 0.0f, 0.0f, 0.58f));
      Stretch(dim.rectTransform);

      GameObject panel = new("Panel");
      panel.transform.SetParent(root, false);
      panelRect = panel.AddComponent<RectTransform>();
      panelRect.anchorMin = new Vector2(0.5f, 0.0f);
      panelRect.anchorMax = new Vector2(0.5f, 0.0f);
      panelRect.pivot = new Vector2(0.5f, 0.0f);
      panelRect.anchoredPosition = new Vector2(0.0f, 34.0f);
      panelRect.sizeDelta = new Vector2(1180.0f, 640.0f);
      Image panelImage = panel.AddComponent<Image>();
      panelImage.color = new Color(0.015f, 0.02f, 0.03f, 0.98f);

      Image header = CreateImage(panelRect, "Header", new Color(0.04f, 0.10f, 0.16f, 1.0f));
      RectTransform headerRect = header.rectTransform;
      headerRect.anchorMin = new Vector2(0.0f, 1.0f);
      headerRect.anchorMax = new Vector2(1.0f, 1.0f);
      headerRect.pivot = new Vector2(0.5f, 1.0f);
      headerRect.anchoredPosition = Vector2.zero;
      headerRect.sizeDelta = new Vector2(0.0f, 46.0f);

      Text title = CreateText(headerRect, "Title", "CUBUS RUNTIME CONSOLE", 18, FontStyle.Bold, new Color(0.72f, 0.93f, 1.0f, 1.0f));
      RectTransform titleRect = title.rectTransform;
      titleRect.anchorMin = Vector2.zero;
      titleRect.anchorMax = Vector2.one;
      titleRect.offsetMin = new Vector2(18.0f, 0.0f);
      titleRect.offsetMax = new Vector2(-18.0f, 0.0f);
      title.alignment = TextAnchor.MiddleLeft;

      Text help = CreateText(panelRect, "Help", "` or Esc to close  |  Enter to run  |  Existing console commands/logs mirrored here", 13, FontStyle.Bold, new Color(0.68f, 0.82f, 0.92f, 1.0f));
      RectTransform helpRect = help.rectTransform;
      helpRect.anchorMin = new Vector2(0.0f, 1.0f);
      helpRect.anchorMax = new Vector2(1.0f, 1.0f);
      helpRect.pivot = new Vector2(0.5f, 1.0f);
      helpRect.anchoredPosition = new Vector2(0.0f, -54.0f);
      helpRect.sizeDelta = new Vector2(-32.0f, 28.0f);
      help.alignment = TextAnchor.MiddleLeft;

      BuildScrollLog(panelRect);
      BuildInput(panelRect);
    }

    private void BuildScrollLog(RectTransform parent)
    {
      GameObject scrollObject = new("Log Scroll");
      scrollObject.transform.SetParent(parent, false);
      RectTransform scrollRectTransform = scrollObject.AddComponent<RectTransform>();
      scrollRectTransform.anchorMin = new Vector2(0.0f, 0.0f);
      scrollRectTransform.anchorMax = new Vector2(1.0f, 1.0f);
      scrollRectTransform.offsetMin = new Vector2(16.0f, 60.0f);
      scrollRectTransform.offsetMax = new Vector2(-16.0f, -92.0f);

      Image scrollBg = scrollObject.AddComponent<Image>();
      scrollBg.color = new Color(0.0f, 0.0f, 0.0f, 0.42f);

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
      RectTransform logRect = logText.rectTransform;
      Stretch(logRect);
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
      GameObject inputObject = new(InputObjectName);
      inputObject.transform.SetParent(parent, false);
      RectTransform inputRect = inputObject.AddComponent<RectTransform>();
      inputRect.anchorMin = new Vector2(0.0f, 0.0f);
      inputRect.anchorMax = new Vector2(1.0f, 0.0f);
      inputRect.pivot = new Vector2(0.5f, 0.0f);
      inputRect.anchoredPosition = new Vector2(0.0f, 16.0f);
      inputRect.sizeDelta = new Vector2(-32.0f, 36.0f);

      Image bg = inputObject.AddComponent<Image>();
      bg.color = new Color(0.0f, 0.0f, 0.0f, 0.82f);

      inputField = inputObject.AddComponent<InputField>();
      inputField.lineType = InputField.LineType.SingleLine;
      inputField.textComponent = CreateText(inputRect, "Text", string.Empty, 15, FontStyle.Bold, Color.white);
      inputField.placeholder = CreateText(inputRect, "Placeholder", "type command...", 15, FontStyle.Normal, new Color(0.65f, 0.74f, 0.82f, 0.75f));

      RectTransform textRect = inputField.textComponent.rectTransform;
      textRect.offsetMin = new Vector2(10.0f, 5.0f);
      textRect.offsetMax = new Vector2(-10.0f, -5.0f);

      RectTransform placeholderRect = inputField.placeholder.rectTransform;
      placeholderRect.offsetMin = new Vector2(10.0f, 5.0f);
      placeholderRect.offsetMax = new Vector2(-10.0f, -5.0f);
    }

    private void RenderLogFromSource(bool force = false)
    {
      if (logText == null || sourceConsole == null || sourceLogLinesField == null)
      {
        return;
      }

      if (sourceLogLinesField.GetValue(sourceConsole) is not IEnumerable lines)
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
      if (canvas == null || canvasObject == null)
      {
        return;
      }

      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.overrideSorting = true;
      canvas.sortingOrder = TopmostSortingOrder;
      canvasObject.transform.SetAsLastSibling();
    }

    private void FocusInput()
    {
      if (inputField == null || !visible)
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
      text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
