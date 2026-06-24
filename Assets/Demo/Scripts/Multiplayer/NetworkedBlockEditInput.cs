using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Networked replacement for <c>DemoBlockEditInput</c>. Resolves the targeted voxel locally,
  /// then forwards the edit to the server through <see cref="NetworkedBlockEditBridge"/> instead of
  /// mutating the world directly. Disable <c>DemoBlockEditInput</c> when using this component.
  /// </summary>
  public sealed class NetworkedBlockEditInput : MonoBehaviour
  {
    [SerializeField] private BlockEditTool editTool;
    [SerializeField] private NetworkedBlockEditBridge bridge;
    [SerializeField] private Camera editCamera;
    [SerializeField] private float traceDistance = 100.0f;

    private void Awake()
    {
      if (editTool == null)
      {
        editTool = GetComponent<BlockEditTool>();
      }
      if (editTool == null)
      {
        editTool = FindAnyObjectByType<BlockEditTool>();
      }
      if (bridge == null)
      {
        bridge = GetComponent<NetworkedBlockEditBridge>();
      }
      if (bridge == null)
      {
        bridge = FindAnyObjectByType<NetworkedBlockEditBridge>();
      }
      if (editCamera == null)
      {
        editCamera = Camera.main;
      }
    }

    private void Update()
    {
      Mouse mouse = Mouse.current;
      if (mouse == null || editCamera == null || editTool == null || bridge == null)
      {
        return;
      }

      bool remove = mouse.leftButton.wasPressedThisFrame;
      bool add = mouse.rightButton.wasPressedThisFrame;
      if (!remove && !add)
      {
        return;
      }

      Ray ray = new(editCamera.transform.position, editCamera.transform.forward);
      BlockEditOperation operation = add ? BlockEditOperation.Add : BlockEditOperation.Remove;

      if (!editTool.TryResolveEditVoxel(ray, traceDistance, operation, out Vector3Int voxel))
      {
        return;
      }

      ushort material = add ? editTool.AddMaterialId : (ushort)0;
      bridge.SendEdit(voxel, material);
    }
  }
}
