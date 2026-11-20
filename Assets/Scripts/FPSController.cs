using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controlador en primera persona sencillo (estilo shooter de los 90).
/// Montaje:
///   Player (este script + CharacterController)
///     └─ Camera (asígnala en "cameraPivot"), a ~1.6 m de altura
///         └─ Weapon (HitscanWeapon + modelo del arma)
/// Usa el Input System nuevo (por defecto en Unity 6), leyendo el
/// teclado y el ratón directamente para que no haga falta configurar nada.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Referencias")]
    public Transform cameraPivot;

    [Header("Movimiento")]
    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    [Header("Ratón")]
    public float mouseSensitivity = 0.1f;
    public float maxLookAngle = 85f;

    [Header("Balanceo de cámara (head bob)")]
    public float bobFrequency = 10f;
    public float bobAmount = 0.05f;

    CharacterController controller;
    Vector3 velocity;
    float pitch;
    float bobTimer;
    Vector3 cameraBaseLocalPos;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraPivot != null) cameraBaseLocalPos = cameraPivot.localPosition;
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        // ---- Mirar --------------------------------------------------------
        Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
        transform.Rotate(Vector3.up * look.x);
        pitch = Mathf.Clamp(pitch - look.y, -maxLookAngle, maxLookAngle);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // ---- Moverse ------------------------------------------------------
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        float speed = kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        // ---- Gravedad y salto ---------------------------------------------
        if (controller.isGrounded && velocity.y < 0f) velocity.y = -2f;
        if (controller.isGrounded && kb.spaceKey.wasPressedThisFrame)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        velocity.y += gravity * Time.deltaTime;

        controller.Move((move + velocity) * Time.deltaTime);

        // ---- Head bob -----------------------------------------------------
        if (controller.isGrounded && input.sqrMagnitude > 0.01f)
        {
            bobTimer += Time.deltaTime * bobFrequency * (speed / walkSpeed);
            cameraPivot.localPosition = cameraBaseLocalPos + new Vector3(
                Mathf.Cos(bobTimer * 0.5f) * bobAmount * 0.5f,
                Mathf.Sin(bobTimer) * bobAmount, 0f);
        }
        else
        {
            cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, cameraBaseLocalPos, Time.deltaTime * 10f);
        }

        // Escape libera el ratón (útil en el editor)
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
