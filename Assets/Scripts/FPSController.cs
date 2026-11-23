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

    [Header("Inclinarse (Q izquierda / E derecha)")]
    public Key leanLeftKey = Key.Q;
    public Key leanRightKey = Key.E;
    [Tooltip("Desactivado: mantener pulsado. Activado: pulsar una vez para inclinarse y otra para volver")]
    public bool leanToggle = false;
    [Tooltip("Grados que rueda la cámara al inclinarse")]
    public float leanAngle = 15f;
    [Tooltip("Metros que se desplaza la cabeza hacia el lado")]
    public float leanDistance = 0.4f;
    [Tooltip("Rapidez con la que entra y sale la inclinación")]
    public float leanSpeed = 8f;
    [Tooltip("Capas que bloquean la cabeza al inclinarse (para no asomarse a través de paredes)")]
    public LayerMask leanBlockMask = ~0;

    /// <summary>false = no mira, no se mueve ni se inclina (p. ej. con el inventario abierto). La gravedad sigue.</summary>
    [System.NonSerialized] public bool inputEnabled = true;

    CharacterController controller;
    float lean;                // -1 izquierda .. 0 recto .. 1 derecha (suavizado)
    int leanToggled;            // estado en modo toggle: -1, 0, 1
    Vector3 bobOffset;
    Vector3 velocity;
    float pitch;
    float bobTimer;
    Vector3 cameraBaseLocalPos;
    Escalera escalera;          // escalera a la que está agarrado (null = andando)
    float soltadoHasta;         // tras soltarse con Espacio, no se vuelve a agarrar enseguida

    /// <summary>¿Está trepando por una escalera ahora mismo?</summary>
    public bool Trepando => escalera != null;

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
        Vector2 look = inputEnabled ? mouse.delta.ReadValue() * mouseSensitivity : Vector2.zero;
        transform.Rotate(Vector3.up * look.x);
        pitch = Mathf.Clamp(pitch - look.y, -maxLookAngle, maxLookAngle);
        UpdateLean(kb);

        // ---- Moverse ------------------------------------------------------
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1;
        if (kb.sKey.isPressed) input.y -= 1;
        if (kb.dKey.isPressed) input.x += 1;
        if (kb.aKey.isPressed) input.x -= 1;
        input = inputEnabled ? Vector2.ClampMagnitude(input, 1f) : Vector2.zero;

        float speed = kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        // ---- Escaleras de mano (componente Escalera) -----------------------
        if (Trepar(kb, input))
        {
            bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, Time.deltaTime * 10f);
            ApplyCamera();
            UpdateCursor(kb, mouse);
            return;
        }

        // ---- Gravedad y salto ---------------------------------------------
        if (controller.isGrounded && velocity.y < 0f) velocity.y = -2f;
        if (controller.isGrounded && inputEnabled && kb.spaceKey.wasPressedThisFrame)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        velocity.y += gravity * Time.deltaTime;

        controller.Move((move + velocity) * Time.deltaTime);

        // ---- Head bob -----------------------------------------------------
        if (controller.isGrounded && input.sqrMagnitude > 0.01f)
        {
            bobTimer += Time.deltaTime * bobFrequency * (speed / walkSpeed);
            bobOffset = new Vector3(
                Mathf.Cos(bobTimer * 0.5f) * bobAmount * 0.5f,
                Mathf.Sin(bobTimer) * bobAmount, 0f);
        }
        else
        {
            bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, Time.deltaTime * 10f);
        }

        ApplyCamera();
        UpdateCursor(kb, mouse);
    }

    /// <summary>
    /// Inclinación estilo Rainbow Six: Q izquierda, E derecha (mantener o alternar).
    /// En escaleras no se inclina. Si hay una pared al lado, la cabeza se queda donde choca.
    /// </summary>
    void UpdateLean(Keyboard kb)
    {
        bool left = kb[leanLeftKey].isPressed, right = kb[leanRightKey].isPressed;
        int target;
        if (leanToggle)
        {
            if (kb[leanLeftKey].wasPressedThisFrame)  leanToggled = leanToggled == -1 ? 0 : -1;
            if (kb[leanRightKey].wasPressedThisFrame) leanToggled = leanToggled ==  1 ? 0 :  1;
            target = leanToggled;
        }
        else target = (right ? 1 : 0) - (left ? 1 : 0);    // las dos a la vez = recto

        if (Trepando || !inputEnabled) { target = 0; leanToggled = 0; }

        float goal = target;
        if (target != 0 && leanDistance > 0.001f)
        {
            // ¿Cabe la cabeza en ese lado? Sphere cast desde la posición normal de la cámara.
            Vector3 head = transform.TransformPoint(cameraBaseLocalPos);
            Vector3 dir = transform.right * target;
            const float headRadius = 0.2f;
            if (Physics.SphereCast(head, headRadius, dir, out RaycastHit hit, leanDistance,
                                   leanBlockMask, QueryTriggerInteraction.Ignore))
                goal = target * Mathf.Clamp01((hit.distance - 0.02f) / leanDistance);
        }
        lean = Mathf.MoveTowards(lean, goal, Time.deltaTime * leanSpeed);
    }

    /// <summary>Coloca la cámara: mirada vertical + inclinación + head bob.</summary>
    void ApplyCamera()
    {
        float a = Mathf.Abs(lean);
        Vector3 leanOffset = new Vector3(lean * leanDistance, -a * leanDistance * 0.15f, 0f);
        cameraPivot.localPosition = cameraBaseLocalPos + bobOffset + leanOffset;
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, -lean * leanAngle);
    }

    /// <summary>
    /// Si el jugador está en la zona de una Escalera y empuja hacia ella (o ya estaba agarrado), trepa:
    /// sin gravedad, W/S suben/bajan (mirando muy abajo, W baja), A/D se mueven de lado, Espacio suelta.
    /// Arriba del todo, W le empuja hacia la escalera para subir al borde. Devuelve true si ha trepado este frame.
    /// </summary>
    bool Trepar(Keyboard kb, Vector2 input)
    {
        Vector3 centro = transform.TransformPoint(controller.center);
        Vector3 pies = centro - Vector3.up * (controller.height * 0.5f - controller.radius);
        Escalera e = Escalera.En(centro) ?? Escalera.En(pies);
        if (e == null) { escalera = null; return false; }

        Vector3 hacia = e.HaciaEscalera(centro);
        Vector3 mirar = transform.forward; mirar.y = 0f; mirar.Normalize();
        bool empujando = input.y > 0.1f && Vector3.Dot(mirar, hacia) > 0.3f;
        if (escalera == null && !(empujando || (!controller.isGrounded && input.y != 0f))) return false;
        escalera = e;

        if (kb.spaceKey.wasPressedThisFrame)                       // soltarse: pequeño salto hacia atrás
        {
            escalera = null; soltadoHasta = Time.time + 0.4f;
            velocity = -hacia * 3f + Vector3.up * 2f;
            controller.Move(velocity * Time.deltaTime);
            return true;
        }
        if (Time.time < soltadoHasta) { escalera = null; return false; }

        float sentido = pitch > 50f ? -1f : 1f;                   // mirando muy abajo, W baja
        float vy = input.y * sentido * e.velocidad;
        if (controller.isGrounded && vy < 0f) { escalera = null; return false; }   // abajo del todo: a andar

        Vector3 lado = Vector3.Cross(Vector3.up, hacia);
        Vector3 v = Vector3.up * vy + lado * (Vector3.Dot(transform.right, lado) * input.x * e.velocidad * 0.5f);
        if (pies.y > e.Arriba - 0.2f && vy > 0f) v += hacia * walkSpeed;     // coronar: pasar al borde
        else v += hacia * 0.5f;                                               // pegarse a la escalera
        velocity = Vector3.zero;
        controller.Move(v * Time.deltaTime);
        return true;
    }

    void UpdateCursor(Keyboard kb, Mouse mouse)
    {
        if (!inputEnabled) return;   // el inventario gestiona el cursor mientras está abierto
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
