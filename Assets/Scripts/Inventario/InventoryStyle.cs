using System;
using UnityEngine;

/// <summary>
/// Aspecto del inventario 3D: bandeja, cámara, luz, modelos, resaltado, textos.
/// Crear: clic derecho en Project > Create > PSX > Estilo de inventario, y asignarlo en InventoryUI.
/// Se puede editar en Play y en la ventana PSX > Inventario > Vista previa: se redibuja al momento.
/// </summary>
[CreateAssetMenu(menuName = "PSX/Estilo de inventario", fileName = "Estilo_Inventario")]
public class InventoryStyle : ScriptableObject
{
    [Header("Bandeja")]
    [Tooltip("Modelo de la bandeja (Inventario.fbx). Se tumba solo: su lado más fino queda hacia arriba")]
    public GameObject trayModel;
    [Tooltip("Material de la bandeja (vacío = el que traiga el modelo)")]
    public Material trayMaterial;
    [Tooltip("Ancho del borde respecto al lado corto de la bandeja (Inventario.fbx: 0.2 / 4.4)")]
    [Range(0f, 0.3f)] public float trayRim = 0.0455f;
    [Tooltip("Dale la vuelta si la bandeja sale boca abajo")]
    public bool flipTray;
    [Tooltip("Separación entre la bandeja principal y la de desbordamiento (en anchos de bandeja)")]
    public float trayGap = 0.12f;

    [Header("Cámara")]
    [Tooltip("Inclinación: 0 = vista cenital, más = más perspectiva")]
    [Range(0f, 60f)] public float cameraTilt = 18f;
    [Range(5f, 90f)] public float cameraFov = 30f;
    [Tooltip("Margen alrededor de las bandejas (1 = justas)")]
    [Range(0.8f, 2f)] public float framing = 1.15f;
    [Tooltip("Desplazamiento del encuadre (en tamaños de bandeja). Y positivo sube la bandeja en pantalla")]
    public Vector2 viewOffset = new Vector2(0f, -0.05f);
    [Tooltip("Resolución del render del inventario respecto a la pantalla (menos = más pixelado)")]
    [Range(0.1f, 1f)] public float renderScale = 1f;
    [Tooltip("Velocidad del reencuadre al aparecer el desbordamiento")]
    public float cameraLerp = 8f;

    [Header("Luz del inventario")]
    public Color lightColor = new Color(1f, 0.96f, 0.9f);
    [Range(0f, 4f)] public float lightIntensity = 1.2f;
    public Vector3 lightRotation = new Vector3(55f, -30f, 0f);

    [Header("Objetos")]
    [Tooltip("Cuánto ocupan los modelos de sus casillas (1 = justo)")]
    [Range(0.3f, 1.2f)] public float itemFill = 0.88f;
    [Tooltip("Altura máxima de un modelo, en casillas")]
    [Range(0.1f, 3f)] public float itemMaxHeight = 0.6f;
    [Tooltip("Altura a la que flota lo que arrastras, en casillas")]
    [Range(0f, 3f)] public float dragLift = 0.6f;
    [Tooltip("Color de la caja que sustituye a un objeto sin modelo")]
    public Color noModelColor = new Color(0.5f, 0.5f, 0.55f);

    [Header("Resaltado de casillas")]
    [Tooltip("Donde se puede soltar")]
    public Color cellValid = new Color(0.25f, 0.85f, 0.3f, 0.55f);
    [Tooltip("Donde NO se puede soltar")]
    public Color cellInvalid = new Color(0.9f, 0.2f, 0.2f, 0.55f);
    [Tooltip("Tamaño del resaltado dentro de la casilla")]
    [Range(0.3f, 1f)] public float cellHighlightSize = 0.9f;

    [Header("Fondo de pantalla al abrir")]
    public Color screenDim = new Color(0f, 0f, 0f, 0.6f);
    public Sprite screenSprite;

    [Header("Fuente y textos")]
    public Vector2 referenceResolution = new Vector2(1920, 1080);
    [Tooltip("Vacío = la fuente por defecto de Unity")]
    public Font font;
    public Color textColor = Color.white;
    public Color titleColor = Color.white;
    [Min(1)] public int titleSize = 20;
    public Color countColor = Color.white;
    public bool countOutline = true;
    public Color countOutlineColor = Color.black;
    [Min(1)] public int countSize = 18;
    [Min(1)] public int helpSize = 16;

    [Header("Aviso de desbordamiento")]
    public Color warningBackground = new Color(0.85f, 0.3f, 0.35f, 1f);
    public Sprite warningSprite;
    public Color warningTextColor = Color.black;
    [Min(1)] public int warningSize = 18;
    [Min(10)] public float warningHeight = 34f;
    [Min(50)] public float warningWidth = 460f;

    [Header("Texto de recoger (bajo la mira)")]
    public Color promptColor = Color.white;
    [Min(1)] public int promptSize = 22;
    public bool promptOutline = true;
    [Tooltip("Desplazamiento respecto al centro de la pantalla")]
    public Vector2 promptOffset = new Vector2(0f, -40f);

    [Header("Textos ({0}, {1} = teclas o nombre)")]
    public string mainTitle = "INVENTARIO";
    public string overflowTitle = "DESBORDAMIENTO";
    public string warningText = "AVISO: HAY OBJETOS EN EL DESBORDAMIENTO";
    [Tooltip("{0} = tecla de abrir/cerrar")]
    public string helpOverflow = "PULSA [{0}] PARA CERRAR Y TIRAR LOS OBJETOS DEL DESBORDAMIENTO";
    [Tooltip("{0} = tecla de girar, {1} = tecla de abrir/cerrar")]
    public string helpNormal = "Arrastrar: mover/apilar   [{0}]: girar   Shift+clic: dividir   Soltar fuera: tirar   [{1}]: cerrar";
    [Tooltip("{0} = tecla de recoger, {1} = nombre del objeto")]
    public string promptFormat = "[{0}]  {1}";

    /// <summary>Se lanza al cambiar algo en el Inspector (la UI se reconstruye).</summary>
    public static event Action<InventoryStyle> Edited;

    void OnValidate() => Edited?.Invoke(this);
}
