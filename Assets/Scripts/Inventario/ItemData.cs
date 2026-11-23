using UnityEngine;

/// <summary>
/// Definición de un tipo de objeto del inventario (pistola, caja de balas...).
/// Crear: clic derecho en Project > Create > PSX > Objeto de inventario.
/// La forma (qué casillas ocupa dentro de su ancho x alto) se pinta en el Inspector, con el icono encima.
/// </summary>
[CreateAssetMenu(menuName = "PSX/Objeto de inventario", fileName = "Item_")]
public class ItemData : ScriptableObject
{
    public string displayName = "Objeto";

    [Tooltip("Imagen en la cuadrícula, dibujada para el objeto SIN girar (ancho x alto casillas)")]
    public Sprite icon;

    [Tooltip("Casillas que ocupa a lo ancho (caja que lo contiene)")]
    [Min(1)] public int width = 1;
    [Tooltip("Casillas que ocupa a lo alto (caja que lo contiene)")]
    [Min(1)] public int height = 1;

    [Tooltip("Cuántas unidades caben en una pila (1 = no se apila)")]
    [Min(1)] public int maxStack = 1;

    [Tooltip("Prefab que aparece en el mundo al tirarlo (debe tener collider; ItemPickup se añade si falta)")]
    public GameObject worldPrefab;

    [Header("Modelo 3D en el inventario")]
    [Tooltip("Modelo que se ve en la bandeja. Vacío = el del World Prefab")]
    public GameObject inventoryModel;
    [Tooltip("No orientar solo (tumbado, lado largo a lo ancho): usar el giro original del modelo + Model Rotation")]
    public bool manualOrientation;
    [Tooltip("Giro extra en grados sobre la orientación automática")]
    public Vector3 modelRotation;
    [Tooltip("Desplazamiento en casillas (x a la derecha, y hacia abajo), con el objeto sin girar")]
    public Vector2 modelOffset;
    [Tooltip("Altura extra sobre el fondo de la bandeja, en casillas")]
    public float modelLift;
    [Tooltip("Escala sobre el tamaño automático (el que encaja en sus casillas)")]
    [Min(0.05f)] public float modelScale = 1f;

    [Tooltip("Desplazamiento del icono en casillas (x a la derecha, y hacia abajo), con el objeto sin girar. " +
             "Se ajusta arrastrando el icono en el Inspector (modo Mover icono)")]
    [HideInInspector] public Vector2 iconOffset = Vector2.zero;
    [Tooltip("Escala del icono para este objeto (se multiplica por la del estilo)")]
    [HideInInspector] public float iconScale = 1f;

    // Forma: casillas ocupadas dentro de width x height, por filas (y hacia abajo). Se pinta en el Inspector.
    [SerializeField, HideInInspector] bool[] shape;
    [SerializeField, HideInInspector] int shapeWidth, shapeHeight;

    /// <summary>¿Ocupa la casilla (x, y) del objeto SIN girar? (x a la derecha, y hacia abajo)</summary>
    public bool Occupies(int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return false;
        if (!ShapeValid || !HasAnyCell()) return true;   // sin forma pintada = rectángulo completo
        return shape[y * width + x];
    }

    bool ShapeValid => shape != null && shapeWidth == width && shapeHeight == height && shape.Length == width * height;

    bool HasAnyCell()
    {
        foreach (bool b in shape) if (b) return true;
        return false;
    }

    /// <summary>Ajusta la forma al ancho/alto actual conservando lo pintado; las casillas nuevas empiezan llenas.</summary>
    public void EnsureShape()
    {
        if (ShapeValid) return;
        var n = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool old = true;
                if (shape != null && x < shapeWidth && y < shapeHeight && y * shapeWidth + x < shape.Length)
                    old = shape[y * shapeWidth + x];
                n[y * width + x] = old;
            }
        shape = n; shapeWidth = width; shapeHeight = height;
    }

    /// <summary>Se lanza al cambiar algo en el Inspector (la vista previa del inventario se redibuja).</summary>
    public static event System.Action<ItemData> Edited;

    void OnValidate()
    {
        EnsureShape();
        Edited?.Invoke(this);
    }
}
