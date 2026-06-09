using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Player
{
  public sealed class DemoBlockEditInput : MonoBehaviour
  {
    [SerializeField] private BlockEditTool editTool;
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

      if (editCamera == null)
      {
        editCamera = Camera.main;
      }
    }

    private void Update()
    {
      Mouse mouse = Mouse.current;

      if (mouse == null || editCamera == null)
      {
        return;
      }

      Ray ray = new(
          editCamera.transform.position,
          editCamera.transform.forward
      );

      if (mouse.leftButton.wasPressedThisFrame)
      {
        editTool.TryEditFromRay(
            ray,
            traceDistance,
            BlockEditOperation.Remove
        );
      }

      if (mouse.rightButton.wasPressedThisFrame)
      {
        editTool.TryEditFromRay(
            ray,
            traceDistance,
            BlockEditOperation.Add
        );
      }
    }
  }
}