using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Escalera de mano trepable. Ponlo en el objeto raíz de cualquier escalera (con sus modelos como hijos)
/// o usa el menú "PSX > Escaleras > Hacer escalable la selección".
/// Crea/ajusta sola una zona de agarre (BoxCollider trigger) que envuelve los modelos de la escalera,
/// un poco más gruesa para que el jugador la alcance desde delante. El FPSController la detecta y trepa:
///   W / S = subir / bajar · Espacio = soltarse · al llegar arriba con W se sube al borde.
/// La dirección "hacia arriba" es el eje Y del objeto, así que también vale para escaleras inclinadas.
/// </summary>
[DisallowMultipleComponent]
public class Escalera : MonoBehaviour
{
    [Tooltip("Grosor extra de la zona de agarre a cada lado de la escalera (m).")]
    public float margenFondo = 0.45f;
    [Tooltip("Margen extra a los lados (m).")]
    public float margenLados = 0.1f;
    [Tooltip("La zona sobresale esto por arriba para poder subir al borde (m).")]
    public float margenArriba = 0.6f;
    [Tooltip("Velocidad al trepar (m/s).")]
    public float velocidad = 3f;
    [Tooltip("Recalcula la zona a partir de los modelos al empezar (desmárcalo si la ajustas a mano).")]
    public bool ajustarAlIniciar = true;

    public static readonly List<Escalera> Todas = new List<Escalera>();

    BoxCollider zona;
    public BoxCollider Zona => zona != null ? zona : (zona = GetComponent<BoxCollider>());

    /// <summary>Altura (mundo) de la parte de arriba de la escalera, sin el margen.</summary>
    public float Arriba => Zona.bounds.max.y - margenArriba;

    void Awake()
    {
        if (ajustarAlIniciar || Zona == null) Ajustar();
    }

    void OnEnable() { if (!Todas.Contains(this)) Todas.Add(this); }
    void OnDisable() { Todas.Remove(this); }

    void Reset() { Ajustar(); }

    /// <summary>Ajusta el BoxCollider trigger a los renderers hijos (en el espacio local de este objeto).</summary>
    public void Ajustar()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        zona = box;

        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Matrix4x4 w2l = transform.worldToLocalMatrix;
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        foreach (var r in rends)
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 l = w2l.MultiplyPoint3x4(c);
                min = Vector3.Min(min, l); max = Vector3.Max(max, l);
            }
        }
        Vector3 size = max - min;
        // el eje horizontal más fino es el "fondo" (hacia la pared); el otro, el ancho
        bool fondoEsX = size.x < size.z;
        Vector3 pad = fondoEsX ? new Vector3(margenFondo, 0, margenLados) : new Vector3(margenLados, 0, margenFondo);
        Vector3 s = transform.lossyScale;
        pad = new Vector3(pad.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), pad.y, pad.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
        float top = margenArriba / Mathf.Max(1e-4f, Mathf.Abs(s.y));
        min -= pad; max += pad; max.y += top;
        box.center = (min + max) * 0.5f;
        box.size = max - min;
    }

    /// <summary>¿Está el punto (mundo) dentro de la zona de agarre?</summary>
    public bool Contiene(Vector3 p)
    {
        return Zona != null && (Zona.ClosestPoint(p) - p).sqrMagnitude < 1e-6f;
    }

    /// <summary>Dirección horizontal (mundo) desde el punto hacia la escalera, a lo largo de su fondo.</summary>
    public Vector3 HaciaEscalera(Vector3 p)
    {
        Vector3 d = Zona.bounds.center - p; d.y = 0f;
        Vector3 fondo = Zona.size.x < Zona.size.z ? transform.right : transform.forward;
        fondo.y = 0f; fondo.Normalize();
        float k = Vector3.Dot(d, fondo);
        return fondo * Mathf.Sign(k == 0f ? 1f : k);
    }

    public static Escalera En(Vector3 p)
    {
        foreach (var e in Todas)
            if (e != null && e.isActiveAndEnabled && e.Contiene(p)) return e;
        return null;
    }

    void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
