using UnityEngine;

/// <summary>
/// Objeto en el mundo que se puede recoger (mirándolo y pulsando F, ver PickupInteractor).
/// Necesita un collider (no trigger) en este objeto o en sus hijos.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    public ItemData item;
    [Min(1)] public int count = 1;

    public string Label
    {
        get
        {
            string n = item != null ? item.displayName : name;
            return count > 1 ? $"{n} ({count})" : n;
        }
    }

    /// <summary>Crea el objeto en el mundo (usa item.worldPrefab o, si no hay, una caja).</summary>
    public static ItemPickup Spawn(ItemData item, int count, Vector3 position, Quaternion rotation)
    {
        GameObject go;
        if (item.worldPrefab != null)
            go = Instantiate(item.worldPrefab, position, rotation);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = new Vector3(0.1f * item.width, 0.08f, 0.1f * item.height);
            go.AddComponent<Rigidbody>().mass = 0.5f;
        }
        go.name = item.displayName;

        var p = go.GetComponent<ItemPickup>();
        if (p == null) p = go.AddComponent<ItemPickup>();
        p.item = item;
        p.count = count;
        return p;
    }
}
