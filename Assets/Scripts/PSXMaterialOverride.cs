using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cambia propiedades del material PSX/Lit SOLO en este objeto, usando
/// MaterialPropertyBlocks. No crea copias del material ni modifica el asset.
///
/// Admite varios huecos de material por objeto: añade una entrada en
/// "Slots" por cada hueco que quieras cambiar (p. ej. 0 = carcasa de la tele,
/// 1 = pantalla). Un índice de -1 afecta a todos los huecos.
/// Si dos entradas tocan el mismo hueco y la misma propiedad, gana la de más abajo.
///
/// Ojo: el Alpha Test (_ALPHATEST_ON) es una keyword del shader y NO se puede
/// cambiar con un property block; para eso hace falta otro material.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class PSXMaterialOverride : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        [Tooltip("Nombre para reconocer la entrada en el Inspector (p. ej. Carcasa, Pantalla).")]
        public string name = "Material";

        [Tooltip("-1 = todos los materiales del Renderer. 0, 1, 2... = ese hueco de material.")]
        public int materialIndex = 0;

        [Header("Color (_BaseColor)")]
        public bool overrideColor = true;
        public Color color = Color.white;

        [Header("Textura (_BaseMap)")]
        public bool overrideTexture;
        public Texture texture;

        [Header("Tiling y offset (_BaseMap_ST)")]
        public bool overrideTiling;
        public Vector2 tiling = Vector2.one;
        public Vector2 offset = Vector2.zero;

        [Header("PSX")]
        public bool overrideSnap;
        [Range(0, 1)] public float snapStrength = 1f;

        public bool overrideAffine;
        [Range(0, 1)] public float affineStrength = 1f;

        public bool overrideVertexColor;
        public bool useVertexColor = true;

        // Unity crea a ceros las entradas nuevas de una lista; esto nos permite
        // detectarlas y darles valores con sentido (color blanco, tiling 1...).
        [SerializeField, HideInInspector] internal bool initialized;

        public Slot() => initialized = true;

        internal void ResetDefaults(int index)
        {
            name = "Material " + index;
            materialIndex = index;
            overrideColor = true;
            color = Color.white;
            overrideTexture = false;
            texture = null;
            overrideTiling = false;
            tiling = Vector2.one;
            offset = Vector2.zero;
            overrideSnap = false;
            snapStrength = 1f;
            overrideAffine = false;
            affineStrength = 1f;
            overrideVertexColor = false;
            useVertexColor = true;
            initialized = true;
        }

        internal bool Affects(int index) => materialIndex < 0 || materialIndex == index;

        internal void WriteTo(MaterialPropertyBlock block)
        {
            if (overrideColor) block.SetColor(BaseColorId, color);
            if (overrideTexture && texture != null) block.SetTexture(BaseMapId, texture);
            if (overrideTiling) block.SetVector(BaseMapStId, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            if (overrideSnap) block.SetFloat(SnapStrengthId, snapStrength);
            if (overrideAffine) block.SetFloat(AffineStrengthId, affineStrength);
            if (overrideVertexColor) block.SetFloat(UseVertexColorId, useVertexColor ? 1f : 0f);
        }
    }

    [Tooltip("Una entrada por cada hueco de material que quieras cambiar.")]
    public List<Slot> slots = new List<Slot>();

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    static readonly int SnapStrengthId = Shader.PropertyToID("_SnapStrength");
    static readonly int AffineStrengthId = Shader.PropertyToID("_AffineStrength");
    static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");

    static MaterialPropertyBlock block;
    Renderer rend;

    // Al añadir el componente: una entrada por cada material que ya tenga el objeto.
    void Reset()
    {
        slots.Clear();
        if (!TryGetRenderer()) return;
        for (int i = 0; i < rend.sharedMaterials.Length; i++)
        {
            var s = new Slot();
            s.ResetDefaults(i);
            var mat = rend.sharedMaterials[i];
            if (mat != null) s.name = mat.name;
            slots.Add(s);
        }
        Apply();
    }

    void OnEnable() => Apply();
    void OnDisable() => Clear();

    void OnValidate()
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i] != null && !slots[i].initialized)
                slots[i].ResetDefaults(Mathf.Min(i, MaterialCount() - 1));
        Apply();
    }

    /// <summary>Atajo para código de juego, p. ej. un parpadeo al recibir daño.
    /// materialIndex -1 = todos los huecos.</summary>
    public void SetColor(Color c, int materialIndex = -1)
    {
        var slot = slots.Find(s => s.materialIndex == materialIndex);
        if (slot == null)
        {
            slot = new Slot();
            slot.ResetDefaults(materialIndex);
            slot.name = "Código";
            slots.Add(slot);
        }
        slot.color = c;
        slot.overrideColor = true;
        Apply();
    }

    /// <summary>Vuelve a aplicar los valores. Llámalo si cambias campos desde código.</summary>
    public void Apply()
    {
        if (!enabled || !TryGetRenderer()) return;
        block ??= new MaterialPropertyBlock();

        int count = rend.sharedMaterials.Length;
        for (int i = 0; i < count; i++)
        {
            block.Clear();
            foreach (var slot in slots)
                if (slot != null && slot.Affects(i))
                    slot.WriteTo(block);

            // Sin cambios en este hueco, quitamos el bloque para que siga
            // siendo compatible con el SRP Batcher.
            rend.SetPropertyBlock(block.isEmpty ? null : block, i);
        }
    }

    /// <summary>Quita todos los cambios: el objeto vuelve a verse con sus materiales.</summary>
    public void Clear()
    {
        if (!TryGetRenderer()) return;
        for (int i = 0; i < rend.sharedMaterials.Length; i++)
            rend.SetPropertyBlock(null, i);
    }

    int MaterialCount() => TryGetRenderer() ? Mathf.Max(1, rend.sharedMaterials.Length) : 1;

    bool TryGetRenderer()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        return rend != null;
    }
}
