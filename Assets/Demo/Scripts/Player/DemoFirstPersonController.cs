using UnityEngine;
using UnityEngine.InputSystem;

namespace Assets.Demo.Scripts.Player
{
  [RequireComponent(typeof(CharacterController))]
  public sealed class DemoFirstPersonController : MonoBehaviour
  {
    [Header("Look")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 0.02f;
    [SerializeField] private float minPitch = -85.0f;
    [SerializeField] private float maxPitch = 85.0f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 6.0f;
    [SerializeField] private float sprintSpeed = 10.0f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -24.0f;

    [Header("Crosshair")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private float crosshairSize = 8.0f;
    [SerializeField] private float crosshairThickness = 2.0f;
    [SerializeField] private Color crosshairColor = new(1.0f, 1.0f, 1.0f, 0.9f);

    private CharacterController characterController;

    private float pitch;
    private float verticalVelocity;

    private void Awake()
    {
      characterController = GetComponent<CharacterController>();

      if (playerCamera == null)
      {
        playerCamera = GetComponentInChildren<Camera>();
      }
    }

    private void Start()
    {
      LockCursor();
    }

    private void Update()
    {
      HandleCursor();
      HandleLook();
      HandleMovement();
    }

    private void HandleCursor()
    {
      Keyboard keyboard = Keyboard.current;
      Mouse mouse = Mouse.current;

      if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
      {
        UnlockCursor();
      }

      if (
          mouse != null &&
          (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
      )
      {
        LockCursor();
      }
    }

    private void HandleLook()
    {
      Mouse mouse = Mouse.current;

      if (
          mouse == null ||
          Cursor.lockState != CursorLockMode.Locked ||
          playerCamera == null
      )
      {
        return;
      }

      Vector2 delta = mouse.delta.ReadValue();

      float mouseX = delta.x * mouseSensitivity;
      float mouseY = -delta.y * mouseSensitivity;

      transform.Rotate(Vector3.up * mouseX);

      pitch -= mouseY;
      pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

      playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0.0f, 0.0f);
    }

    private void HandleMovement()
    {
      Keyboard keyboard = Keyboard.current;

      if (keyboard == null)
      {
        return;
      }

      bool grounded = characterController.isGrounded;

      if (grounded && verticalVelocity < 0.0f)
      {
        verticalVelocity = -2.0f;
      }

      Vector2 input = Vector2.zero;

      if (keyboard.aKey.isPressed)
      {
        input.x -= 1.0f;
      }

      if (keyboard.dKey.isPressed)
      {
        input.x += 1.0f;
      }

      if (keyboard.sKey.isPressed)
      {
        input.y -= 1.0f;
      }

      if (keyboard.wKey.isPressed)
      {
        input.y += 1.0f;
      }

      if (input.sqrMagnitude > 1.0f)
      {
        input.Normalize();
      }

      Vector3 move =
          transform.right * input.x +
          transform.forward * input.y;

      float speed = keyboard.leftShiftKey.isPressed
          ? sprintSpeed
          : walkSpeed;

      if (grounded && keyboard.spaceKey.wasPressedThisFrame)
      {
        verticalVelocity = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
      }

      verticalVelocity += gravity * Time.deltaTime;

      Vector3 velocity = move * speed;
      velocity.y = verticalVelocity;

      characterController.Move(velocity * Time.deltaTime);

      if (characterController.isGrounded && verticalVelocity < 0.0f)
      {
        verticalVelocity = -2.0f;
      }
    }

    private static void LockCursor()
    {
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }

    private void OnGUI()
    {
      if (!showCrosshair || Cursor.lockState != CursorLockMode.Locked)
      {
        return;
      }

      if (Event.current.type != EventType.Repaint)
      {
        return;
      }

      float cx;
      float cy;

      if (playerCamera != null)
      {
        Rect cameraRect = playerCamera.pixelRect;
        cx = cameraRect.x + cameraRect.width * 0.5f;
        cy = Screen.height - (cameraRect.y + cameraRect.height * 0.5f);
      }
      else
      {
        cx = Screen.width * 0.5f;
        cy = Screen.height * 0.5f;
      }

      cx = Mathf.Round(cx);
      cy = Mathf.Round(cy);
      float halfSize = Mathf.Max(1.0f, crosshairSize * 0.5f);
      float thickness = Mathf.Max(1.0f, crosshairThickness);

      Color previousColor = GUI.color;
      GUI.color = crosshairColor;

      GUI.DrawTexture(
          new Rect(cx - halfSize, cy - thickness * 0.5f, crosshairSize, thickness),
          Texture2D.whiteTexture
      );

      GUI.DrawTexture(
          new Rect(cx - thickness * 0.5f, cy - halfSize, thickness, crosshairSize),
          Texture2D.whiteTexture
      );

      GUI.color = previousColor;
    }
  }
}