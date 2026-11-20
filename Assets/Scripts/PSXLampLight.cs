using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hace que una lámpara (un objeto con el material M_Lampara) ilumine la escena
/// siguiendo su forma: crea luces puntuales hijas repartidas a lo largo de su
/// lado más largo. Una bombilla usa una luz; un fluorescente largo, varias.
///
/// Así se hacía en la época: no había luces con forma, se ponían varias luces
/// puntuales a lo largo del objeto y la luz por vértice hacía el resto.
///
/// Uso: añade el componente a la lámpara. Las luces se crean como hijas
/// ("PSX Luz 0", "PSX Luz 1"...) y se guardan en la escena. Si cambias algo en
/// el Inspector se recolocan solas; también tienes "Recolocar luces" en el menú
/// del componente (los tres puntos).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class PSXLampLight : MonoBehaviour
{
    [Tooltip("Hueco del material emisivo en este Renderer (el que usa M_Lampara).")]
    public int materialIndex = 0;

    [Header("Luz")]
    [Tooltip("Usa el tono del 'Color de emision' del material.")]
    public bool colorFromMaterial = true;
    public Color color = new Color(1f, 0.85f, 0.6f);
    [Tooltip("Intensidad de CADA luz.")]
    public float intensity = 2f;
    [Tooltip("Alcance de cada luz, en metros.")]
    public float range = 6f;

    [Header("Forma")]
    [Tooltip("Metros entre luces a lo largo del lado más largo de la lámpara.")]
    public float spacing = 1.5f;
    [Tooltip("Máximo de luces por lámpara. Cada una cuesta: URP solo deja unas pocas por objeto.")]
    [Range(1, 4)] public int maxLights = 2;
    [Tooltip("Desplaza las luces (ejes locales). P. ej. Y negativa en una lámpara de techo para que no queden dentro de la pantalla de la lámpara.")]
    public Vector3 localOffset = Vector3.zero;

    [Header("Parpadeo (solo en Play)")]
    [Range(0, 1)] public float flicker = 0f;
    public float flickerSpeed = 8f;
    [Tooltip("Hace parpadear también el brillo de la lámpara, no solo su luz.")]
    public bool flickerEmission = true;

    const string LightPrefix = "PSX Luz ";
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static MaterialPropertyBlock block;

    readonly List<Light> lights = new List<Light>();
    Renderer rend;
    float seed;

    void Reset() => ScheduleRebuild();

    void OnValidate() => ScheduleRebuild();

    void OnEnable()
    {
        seed = Random.value * 100f;
        CollectLights();
        if (lights.Count == 0) ScheduleRebuild();
        SetLightsEnabled(true);
    }

    void OnDisable() => SetLightsEnabled(false);

    void Update()
    {
        if (!Application.isPlaying || flicker <= 0f || !TryGetRenderer()) return;

        // Ruido suave + algún bajón brusco de vez en cuando
        float n = Mathf.PerlinNoise(seed, Time.time * flickerSpeed);
        float k = 1f - flicker * n * n * 1.5f;
        k = Mathf.Clamp01(k);

        foreach (var l in lights)
            if (l) l.intensity = intensity * k;

        if (flickerEmission)
        {
            var mat = EmissiveMaterial();
            if (mat != null && mat.HasProperty(EmissionColorId))
            {
                block ??= new MaterialPropertyBlock();
                rend.GetPropertyBlock(block, materialIndex); // respeta lo que ponga PSXMaterialOverride
                block.SetColor(EmissionColorId, mat.GetColor(EmissionColorId) * k);
                rend.SetPropertyBlock(block, materialIndex);
            }
        }
    }

    [ContextMenu("Recolocar luces")]
    public void Rebuild()
    {
        if (this == null || !TryGetRenderer()) return;
        CollectLights();

        // Forma: caja de la malla en espacio local y su eje más largo
        Bounds b = rend.localBounds;
        Vector3 size = b.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        float length = size[axis] * Mathf.Abs(transform.lossyScale[axis]);
        int count = Mathf.Clamp(Mathf.RoundToInt(length / Mathf.Max(0.05f, spacing)), 1, maxLights);

        // Crear o borrar luces hasta tener las justas
        while (lights.Count < count)
        {
            var go = new GameObject(LightPrefix + lights.Count);
            go.transform.SetParent(transform, false);
            lights.Add(go.AddComponent<Light>());
        }
        while (lights.Count > count)
        {
            var last = lights[lights.Count - 1];
            lights.RemoveAt(lights.Count - 1);
            if (last) DestroySafe(last.gameObject);
        }

        Color c = LightColor();
        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            Vector3 p = b.center;
            p[axis] = b.min[axis] + size[axis] * t;

            var l = lights[i];
            l.name = LightPrefix + i;
            l.transform.localPosition = p + localOffset;
            l.transform.localRotation = Quaternion.identity;
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None; // PSX/Lit no recibe sombras
            l.enabled = isActiveAndEnabled;
        }
    }

    // ------------------------------------------------------------------
    void ScheduleRebuild()
    {
#if UNITY_EDITOR
        // Crear o borrar objetos dentro de OnValidate no está permitido:
        // lo dejamos para justo después.
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#else
        Rebuild();
#endif
    }

    void CollectLights()
    {
        lights.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith(LightPrefix) && child.TryGetComponent(out Light l))
                lights.Add(l);
        }
    }

    void SetLightsEnabled(bool on)
    {
        foreach (var l in lights)
            if (l) l.enabled = on;
    }

    Color LightColor()
    {
        var mat = EmissiveMaterial();
        if (colorFromMaterial && mat != null && mat.HasProperty(EmissionColorId))
        {
            Color e = mat.GetColor(EmissionColorId);
            float max = e.maxColorComponent;
            if (max > 0.0001f) return new Color(e.r / max, e.g / max, e.b / max, 1f);
        }
        return color;
    }

    Material EmissiveMaterial()
    {
        if (!TryGetRenderer()) return null;
        var mats = rend.sharedMaterials;
        return materialIndex >= 0 && materialIndex < mats.Length ? mats[materialIndex] : null;
    }

    bool TryGetRenderer()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        return rend != null;
    }

    static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
