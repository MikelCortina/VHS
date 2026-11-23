using System;
using UnityEngine;

/// <summary>
/// Inventario del jugador: cuadrícula principal + cuadrícula de desbordamiento.
/// Al añadir objetos: 1) rellena pilas a medias, 2) crea pilas nuevas (dividiendo por maxStack),
/// 3) lo que no cabe va al desbordamiento. Al cerrar el inventario, el desbordamiento se tira al suelo.
/// Ponlo en el jugador (lo hace PSX > Inventario > Montar inventario en la escena abierta).
/// </summary>
public class Inventory : MonoBehaviour
{
    [Header("Cuadrícula principal")]
    [Min(1)] public int width = 8;
    [Min(1)] public int height = 5;

    [Header("Cuadrícula de desbordamiento")]
    [Min(1)] public int overflowWidth = 6;
    [Min(1)] public int overflowHeight = 8;

    [Header("Tirar objetos")]
    [Tooltip("Desde dónde se tiran los objetos (normalmente la cámara). Vacío = Camera.main")]
    public Transform dropOrigin;
    public float dropDistance = 0.9f;

    public InventoryGrid Main { get; private set; }
    public InventoryGrid Overflow { get; private set; }

    /// <summary>¿Está la ventana del inventario abierta? (lo pone InventoryUI)</summary>
    public bool IsOpen { get; set; }

    /// <summary>Algo ha cambiado (para redibujar la UI o el HUD de munición).</summary>
    public event Action Changed;
    /// <summary>Han entrado objetos en el desbordamiento (la UI se abre sola).</summary>
    public event Action OverflowReceived;

    void Awake() => InitGrids();

    /// <summary>Crea las cuadrículas vacías (lo hace Awake; la vista previa del editor lo llama a mano).</summary>
    public void InitGrids()
    {
        Main = new InventoryGrid(width, height);
        Overflow = new InventoryGrid(overflowWidth, overflowHeight);
    }

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Añade objetos. Devuelve cuántos NO han cabido ni en el desbordamiento (se quedan en el suelo).</summary>
    public int Add(ItemData item, int count)
    {
        if (item == null || count <= 0) return count;

        count = Main.TopUp(item, count);
        count = Main.AddNewStacks(item, count);

        bool overflowed = false;
        if (count > 0)
        {
            int before = count;
            count = Overflow.TopUp(item, count);
            count = Overflow.AddNewStacks(item, count);
            overflowed = count < before;
        }

        Changed?.Invoke();
        if (overflowed) OverflowReceived?.Invoke();
        return count;
    }

    /// <summary>Unidades de ese objeto en la cuadrícula principal.</summary>
    public int Count(ItemData item)
    {
        int n = 0;
        foreach (var s in Main.stacks) if (s.item == item) n += s.count;
        return n;
    }

    /// <summary>Saca hasta 'amount' unidades de la cuadrícula principal (primero de las pilas más pequeñas).</summary>
    public int Take(ItemData item, int amount)
    {
        int taken = 0;
        while (taken < amount)
        {
            ItemStack smallest = null;
            foreach (var s in Main.stacks)
                if (s.item == item && (smallest == null || s.count < smallest.count)) smallest = s;
            if (smallest == null) break;

            int n = Mathf.Min(amount - taken, smallest.count);
            smallest.count -= n; taken += n;
            if (smallest.count <= 0) Main.Remove(smallest);
        }
        if (taken > 0) Changed?.Invoke();
        return taken;
    }

    /// <summary>Tira una pila al mundo delante del jugador (la pila ya no debe estar en ninguna cuadrícula).</summary>
    public void Drop(ItemStack s)
    {
        Transform o = dropOrigin != null ? dropOrigin : (Camera.main != null ? Camera.main.transform : transform);
        Vector3 fwd = o.forward;
        float dist = dropDistance;
        if (Physics.Raycast(o.position, fwd, out RaycastHit hit, dropDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(0.2f, hit.distance - 0.3f);
        Vector3 pos = o.position + fwd * dist + Vector3.down * 0.25f + UnityEngine.Random.insideUnitSphere * 0.1f;
        var rot = Quaternion.Euler(0f, o.eulerAngles.y + UnityEngine.Random.Range(-30f, 30f), 0f);
        var pickup = ItemPickup.Spawn(s.item, s.count, pos, rot);
        var rb = pickup.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = fwd * 1.5f;
        Changed?.Invoke();
    }

    /// <summary>Tira todo lo que haya en el desbordamiento.</summary>
    public void DropAllOverflow()
    {
        var all = Overflow.stacks.ToArray();
        Overflow.stacks.Clear();
        foreach (var s in all) Drop(s);
    }
}
