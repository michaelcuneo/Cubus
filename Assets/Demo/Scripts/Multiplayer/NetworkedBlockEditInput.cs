using System.Collections.Generic;
using Assets.Demo.Scripts.Player;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Demo driver for the data-driven voxel edit system. Holds this game's list of tools
  /// (pickaxe, placer, shovel, trowel, steam roller, ...) and drives the engine's
  /// <see cref="CubusEditController"/>: keyboard selects the active tool, the left mouse button
  /// uses it, and block edits are routed through <see cref="NetworkedBlockEditBridge"/> so they
  /// stay authoritative online and apply locally offline.
  /// <para>
  /// The engine owns none of this — a real game would replace this driver with its inventory,
  /// equipping a tool by setting <see cref="CubusEditController.EquippedTool"/> from the held item.
  /// Disable <c>DemoBlockEditInput</c> / <c>DemoSmoothDensityEditInput</c> when using this component.
  /// </para>
  /// </summary>
  public sealed class NetworkedBlockEditInput : MonoBehaviour
  {
    [SerializeField] private CubusEditController controller;
    [SerializeField] private NetworkedBlockEditBridge bridge;
    [SerializeField] private NetworkedDensityEditBridge densityBridge;
    [SerializeField] private Camera editCamera;

    [Tooltip("This game's tools. Left empty, a demo set (Pickaxe/Placer/Shovel/Mound/Trowel/Steam Roller) is created at runtime.")]
    [SerializeField] private List<CubusEditToolDefinition> tools = new();
    [SerializeField] private bool showToolbar = true;

    private static readonly Key[] SlotKeys =
    {
      Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
      Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };

    private int activeIndex;
    private float lastApplyTime;

    private void Awake()
    {
      if (controller == null)
      {
        controller = FindAnyObjectByType<CubusEditController>();
      }
      if (controller == null)
      {
        controller = gameObject.AddComponent<CubusEditController>();
      }

      if (bridge == null)
      {
        bridge = GetComponent<NetworkedBlockEditBridge>();
      }
      if (bridge == null)
      {
        bridge = FindAnyObjectByType<NetworkedBlockEditBridge>();
      }

      // Route single-voxel block edits through the network bridge (authoritative when
      // connected, local when offline). Density sculpts stay local.
      if (bridge != null)
      {
        controller.BlockEditForwarder = bridge.SendEdit;
      }

      // Route density sculpt edits (per changed voxel, batched) through the density bridge so
      // they replicate over SpacetimeDB too. Offline, SendEdits no-ops and the local apply stands.
      if (densityBridge == null)
      {
        densityBridge = FindAnyObjectByType<NetworkedDensityEditBridge>();
      }
      if (densityBridge == null)
      {
        GameObject host = bridge != null ? bridge.gameObject : gameObject;
        densityBridge = host.AddComponent<NetworkedDensityEditBridge>();
      }
      controller.DensityEditForwarder = densityBridge.SendEdits;

      if (editCamera == null)
      {
        editCamera = Camera.main;
      }

      if (tools.Count == 0)
      {
        CreateDemoToolset();
      }

      // The tool driver replaces the standalone legacy inputs. Disable any still in the scene so
      // they don't fire their own add/remove on the same click (double-editing).
      DisableLegacyEditInputs();
    }

    private static void DisableLegacyEditInputs()
    {
      foreach (DemoSmoothDensityEditInput legacy in FindObjectsByType<DemoSmoothDensityEditInput>(FindObjectsSortMode.None))
      {
        legacy.enabled = false;
      }

      foreach (DemoBlockEditInput legacy in FindObjectsByType<DemoBlockEditInput>(FindObjectsSortMode.None))
      {
        legacy.enabled = false;
      }
    }

    private void Update()
    {
      if (controller == null || editCamera == null || tools.Count == 0)
      {
        return;
      }

      HandleToolSelection();

      CubusEditToolDefinition active = tools[Mathf.Clamp(activeIndex, 0, tools.Count - 1)];
      controller.EquippedTool = active;

      Mouse mouse = Mouse.current;
      if (mouse == null)
      {
        return;
      }

      bool use = active.continuous
          ? mouse.leftButton.isPressed && Time.time - lastApplyTime >= Mathf.Max(0.0f, active.repeatInterval)
          : mouse.leftButton.wasPressedThisFrame;

      if (!use)
      {
        return;
      }

      Ray ray = new(editCamera.transform.position, editCamera.transform.forward);
      if (controller.Apply(active, ray))
      {
        lastApplyTime = Time.time;
      }
    }

    private void HandleToolSelection()
    {
      Keyboard keyboard = Keyboard.current;
      if (keyboard == null)
      {
        return;
      }

      // Number-key slots select by order.
      int slots = Mathf.Min(tools.Count, SlotKeys.Length);
      for (int i = 0; i < slots; i++)
      {
        if (keyboard[SlotKeys[i]].wasPressedThisFrame)
        {
          activeIndex = i;
          return;
        }
      }

      // Per-tool assigned hotkey.
      for (int i = 0; i < tools.Count; i++)
      {
        if (WasKeyPressed(keyboard, tools[i].activationKey))
        {
          activeIndex = i;
          return;
        }
      }
    }

    private static bool WasKeyPressed(Keyboard keyboard, KeyCode keyCode)
    {
      Key key = ToInputSystemKey(keyCode);
      return key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    private static Key ToInputSystemKey(KeyCode keyCode)
    {
      switch (keyCode)
      {
        case KeyCode.None: return Key.None;
        case KeyCode.Alpha0: return Key.Digit0;
        case KeyCode.Alpha1: return Key.Digit1;
        case KeyCode.Alpha2: return Key.Digit2;
        case KeyCode.Alpha3: return Key.Digit3;
        case KeyCode.Alpha4: return Key.Digit4;
        case KeyCode.Alpha5: return Key.Digit5;
        case KeyCode.Alpha6: return Key.Digit6;
        case KeyCode.Alpha7: return Key.Digit7;
        case KeyCode.Alpha8: return Key.Digit8;
        case KeyCode.Alpha9: return Key.Digit9;
        case KeyCode.Space: return Key.Space;
        case KeyCode.LeftShift: return Key.LeftShift;
        case KeyCode.LeftControl: return Key.LeftCtrl;
        case KeyCode.LeftAlt: return Key.LeftAlt;
        default:
          // Letters (A..Z) and function keys (F1..F12) share names with the Key enum.
          return System.Enum.TryParse(keyCode.ToString(), out Key parsed) ? parsed : Key.None;
      }
    }

    private void CreateDemoToolset()
    {
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Pickaxe",
        effect = EditToolEffect.RemoveBlock,
        shape = EditToolShape.Point,
        activationKey = KeyCode.Alpha1,
        continuous = false,
      });
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Placer",
        effect = EditToolEffect.PlaceBlock,
        shape = EditToolShape.Point,
        materialId = 1,
        activationKey = KeyCode.Alpha2,
        continuous = false,
      });
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Shovel",
        effect = EditToolEffect.Lower,
        range = 4.0f,
        strength = 2.5f,
        activationKey = KeyCode.Alpha3,
        continuous = true,
      });
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Mound",
        effect = EditToolEffect.Raise,
        range = 4.0f,
        strength = 2.5f,
        materialId = 1,
        activationKey = KeyCode.Alpha4,
        continuous = true,
      });
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Trowel",
        effect = EditToolEffect.Smooth,
        range = 5.0f,
        strength = 0.5f,
        activationKey = KeyCode.Alpha5,
        continuous = true,
      });
      tools.Add(new CubusEditToolDefinition
      {
        displayName = "Steam Roller",
        effect = EditToolEffect.Flatten,
        range = 8.0f,
        strength = 0.6f,
        activationKey = KeyCode.Alpha6,
        continuous = true,
      });
    }

    private void OnGUI()
    {
      if (!showToolbar || tools.Count == 0)
      {
        return;
      }

      GUI.Label(new Rect(12.0f, 10.0f, 420.0f, 20.0f), "Voxel Tools  (number keys select, LMB use)");
      for (int i = 0; i < tools.Count; i++)
      {
        CubusEditToolDefinition tool = tools[i];
        string marker = i == activeIndex ? "\u25B6 " : "   ";
        GUI.Label(
            new Rect(12.0f, 30.0f + i * 18.0f, 420.0f, 18.0f),
            $"{marker}{i + 1}. {tool.displayName}  ({tool.effect})");
      }
    }
  }
}
