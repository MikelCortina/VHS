using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mira un ItemPickup desde el centro de la pantalla y pulsa F para meterlo en el inventario.
/// Si no cabe todo, lo que sobra se queda en el suelo.
/// </summary>
[RequireComponent(typeof(Inventory))]
public class PickupInteractor : MonoBehaviour
{
    [Tooltip("Cámara del jugador. Vacío = Camera.main")]
    public Camera cam;
    public Key pickupKey = Key.F;
    [Tooltip("Distancia máxima para recoger (metros)")]
    public float range = 2.5f;
    public LayerMask mask = ~0;

    /// <summary>Objeto que se está mirando ahora (para el texto "[F] Nombre").</summary>
    public ItemPickup Target { get; private set; }

    Inventory inventory;

    void Awake() => inventory = GetComponent<Inventory>();

    void Update()
    {
        Target = null;
        if (inventory.IsOpen) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
        {
            var p = hit.collider.GetComponentInParent<ItemPickup>();
            if (p != null && p.item != null) Target = p;
        }

        var kb = Keyboard.current;
        if (Target != null && kb != null && kb[pickupKey].wasPressedThisFrame)
        {
            int left = inventory.Add(Target.item, Target.count);
            if (left <= 0) Destroy(Target.gameObject);
            else Target.count = left;
        }
    }
}
