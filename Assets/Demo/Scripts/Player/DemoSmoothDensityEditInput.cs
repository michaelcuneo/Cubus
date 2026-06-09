using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Player
{
  public sealed class DemoSmoothDensityEditInput : MonoBehaviour
  {
    [SerializeField] private Camera viewCamera;
    [SerializeField] private SmoothDensityEditTool editTool;

    private void Awake()
    {
      if (viewCamera == null)
      {
        viewCamera = GetComponentInChildren<Camera>();
      }

      if (editTool == null)
      {
        editTool = FindAnyObjectByType<SmoothDensityEditTool>();
      }
    }

    private void Update()
    {
      if (editTool == null || viewCamera == null)
      {
        return;
      }

      Mouse mouse = Mouse.current;

      if (mouse == null)
      {
        return;
      }

      Ray ray = new(
          viewCamera.transform.position,
          viewCamera.transform.forward
      );

      if (mouse.leftButton.wasPressedThisFrame)
      {
        editTool.TryEditFromRay(ray, addDensity: false);
      }

      if (mouse.rightButton.wasPressedThisFrame)
      {
        editTool.TryEditFromRay(ray, addDensity: true);
      }
    }
  }
}