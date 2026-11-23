using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector de ItemData: cuadrícula para pintar qué casillas ocupa el objeto, con el icono por encima.
///  Clic ............ pinta o borra una casilla
///  Clic y arrastrar  pinta/borra varias (según lo que hiciera el primer clic)
///  Botones ......... llenar, vaciar, invertir, sacar la forma del icono (por transparencia)
///  "Ver girado" .... vista previa de cómo queda girado en el inventario (también se puede pintar)
/// Mover el icono (herramienta "Mover icono", o clic DERECHO y arrastrar con cualquier herramienta):
///  arrastrar = mover, rueda = escalar, Shift = ajustar a cuartos de casilla.
/// </summary>
[CustomEditor(typeof(ItemData))]
public class ItemDataEditor : Editor
{
    static readonly Color FilledColor = new Color(0.22f, 0.62f, 0.32f, 1f);
    static readonly Color EmptyColor = new Color(0.12f, 0.12f, 0.14f, 1f);
    static readonly Color LineColor = new Color(0.75f, 0.75f, 0.8f, 0.9f);
    static readonly Color FrameColor = new Color(0.35f, 1f, 0.45f, 1f);

    static float iconAlpha = 0.75f;
    static bool showRotated;
    static int tool;                 // 0 = pintar casillas, 1 = mover icono
    static readonly GUIContent[] ToolNames = { new GUIContent("Pintar casillas"), new GUIContent("Mover icono") };

    bool paintValue;
    bool movingIcon;
    Vector2 moveStartMouse, moveStartOffset;

    public override void OnInspectorGUI()
    {
        var item = (ItemData)target;

        // Campos normales, con el icono aparte para aceptar cualquier imagen
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "icon", "iconOffset", "iconScale");
        serializedObject.ApplyModifiedProperties();
        DrawIconField(item);

        serializedObject.Update();
        var shape = serializedObject.FindProperty("shape");
        if (shape == null) return;
        if (shape.arraySize != item.width * item.height)
        {
            Undo.RecordObject(item, "Ajustar forma");
            item.EnsureShape();
            EditorUtility.SetDirty(item);
            serializedObject.Update();
            shape = serializedObject.FindProperty("shape");
        }

        int w = item.width, h = item.height;
        int filled = 0;
        for (int i = 0; i < shape.arraySize; i++) if (shape.GetArrayElementAtIndex(i).boolValue) filled++;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Forma en el inventario", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Pinta las casillas que ocupa ({filled} de {w * h}). Clic para pintar/borrar, arrastra para varias. " +
            "Si no hay ninguna pintada, ocupa el rectángulo entero.", MessageType.None);

        // ---- Botones ----
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Llenar")) SetAll(shape, _ => true);
            if (GUILayout.Button("Vaciar")) SetAll(shape, _ => false);
            if (GUILayout.Button("Invertir")) SetAll(shape, v => !v);
            using (new EditorGUI.DisabledScope(item.icon == null))
                if (GUILayout.Button(new GUIContent("Desde icono", "Marca las casillas donde el icono no es transparente")))
                    FromIcon(item, shape);
        }
        iconAlpha = EditorGUILayout.Slider("Opacidad del icono", iconAlpha, 0f, 1f);
        showRotated = EditorGUILayout.Toggle(new GUIContent("Ver girado", "Cómo queda al girarlo con R en el inventario"), showRotated);

        // ---- Colocación del icono ----
        var offsetProp = serializedObject.FindProperty("iconOffset");
        var scaleProp = serializedObject.FindProperty("iconScale");
        if (scaleProp.floatValue <= 0f) scaleProp.floatValue = 1f;   // assets antiguos
        tool = GUILayout.Toolbar(tool, ToolNames);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(offsetProp, new GUIContent("Desplazamiento", "En casillas: x a la derecha, y hacia abajo (sin girar)"));
            if (GUILayout.Button(new GUIContent("Centrar", "Desplazamiento 0 y escala 1"), GUILayout.Width(60)))
            { offsetProp.vector2Value = Vector2.zero; scaleProp.floatValue = 1f; }
        }
        scaleProp.floatValue = EditorGUILayout.Slider(new GUIContent("Escala del icono"), scaleProp.floatValue, 0.2f, 3f);
        if (tool == 1 || item.icon == null)
            EditorGUILayout.HelpBox(item.icon == null ? "Asigna un icono para poder moverlo."
                : "Arrastra el icono para moverlo · rueda: tamaño · Shift: ajustar a 1/4 de casilla. " +
                  "(El clic derecho arrastrando también lo mueve en el modo Pintar)", MessageType.None);

        // ---- Cuadrícula ----
        int gw = showRotated ? h : w, gh = showRotated ? w : h;   // tamaño mostrado
        float avail = EditorGUIUtility.currentViewWidth - 40f;
        float cs = Mathf.Clamp(Mathf.Min(avail / gw, 320f / gh), 16f, 72f);
        Rect area = GUILayoutUtility.GetRect(gw * cs, gh * cs + 8f);
        area = new Rect(area.x + (area.width - gw * cs) * 0.5f, area.y + 4f, gw * cs, gh * cs);

        // Índice en el array (forma SIN girar) de la casilla mostrada (dx, dy)
        int Index(int dx, int dy) => showRotated ? dx * w + (w - 1 - dy) : dy * w + dx;
        Rect CellRect(int dx, int dy) => new Rect(area.x + dx * cs, area.y + dy * cs, cs, cs);

        if (Event.current.type == EventType.Repaint)
        {
            // 1. Casillas
            for (int dy = 0; dy < gh; dy++)
                for (int dx = 0; dx < gw; dx++)
                {
                    bool on = shape.GetArrayElementAtIndex(Index(dx, dy)).boolValue;
                    EditorGUI.DrawRect(Shrink(CellRect(dx, dy), 1f), on ? FilledColor : EmptyColor);
                }

            // 2. Icono por encima
            if (item.icon != null && iconAlpha > 0f)
            {
                var tex = item.icon.texture;
                var tr = item.icon.textureRect;
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                var old = GUI.color; var oldM = GUI.matrix;
                GUI.color = new Color(1f, 1f, 1f, iconAlpha);
                // tamaño y desplazamiento SIN girar; el giro de la matriz los gira igual que en el juego
                float sc = scaleProp.floatValue;
                Vector2 off = offsetProp.vector2Value * cs;
                Rect iconRect = new Rect(0, 0, w * cs * sc, h * cs * sc) { center = area.center + off };
                if (showRotated) GUIUtility.RotateAroundPivot(-90f, area.center);          // antihorario, como en el juego
                GUI.DrawTextureWithTexCoords(FitAspect(iconRect, tr.width / tr.height), tex, uv, true);
                GUI.matrix = oldM; GUI.color = old;
            }

            // 3. Rejilla y marco de las casillas ocupadas (encima del icono para verlas siempre)
            for (int dy = 0; dy < gh; dy++)
                for (int dx = 0; dx < gw; dx++)
                {
                    var r = CellRect(dx, dy);
                    bool on = shape.GetArrayElementAtIndex(Index(dx, dy)).boolValue;
                    Frame(r, 1f, LineColor);
                    if (on) Frame(Shrink(r, 2f), 2f, FrameColor);
                }
        }

        // ---- Ratón: pintar casillas o mover el icono ----
        int id = GUIUtility.GetControlID(FocusType.Passive, area);
        var e = Event.current;
        if (item.icon != null && area.Contains(e.mousePosition))
            EditorGUIUtility.AddCursorRect(area, tool == 1 ? MouseCursor.Pan : MouseCursor.Arrow);

        // Pasa un movimiento en pantalla (y hacia abajo) a casillas del objeto SIN girar
        Vector2 ToItemSpace(Vector2 d) => showRotated ? new Vector2(-d.y, d.x) : d;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (item.icon != null && area.Contains(e.mousePosition) && (e.button == 1 || (e.button == 0 && tool == 1)))
                {
                    movingIcon = true;
                    moveStartMouse = e.mousePosition;
                    moveStartOffset = offsetProp.vector2Value;
                    GUIUtility.hotControl = id;
                    e.Use();
                }
                else if (e.button == 0 && tool == 0 && area.Contains(e.mousePosition))
                {
                    var p = shape.GetArrayElementAtIndex(CellIndexAt(e.mousePosition));
                    paintValue = !p.boolValue;
                    p.boolValue = paintValue;
                    GUIUtility.hotControl = id;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    if (movingIcon)
                    {
                        Vector2 o = moveStartOffset + ToItemSpace(e.mousePosition - moveStartMouse) / cs;
                        if (e.shift) o = new Vector2(Mathf.Round(o.x * 4f) / 4f, Mathf.Round(o.y * 4f) / 4f);
                        offsetProp.vector2Value = o;
                    }
                    else if (area.Contains(e.mousePosition))
                        shape.GetArrayElementAtIndex(CellIndexAt(e.mousePosition)).boolValue = paintValue;
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; movingIcon = false; e.Use(); }
                break;
            case EventType.ContextClick:
                if (area.Contains(e.mousePosition)) e.Use();   // el clic derecho mueve el icono, sin menú
                break;
            case EventType.ScrollWheel:
                if (tool == 1 && item.icon != null && area.Contains(e.mousePosition))
                {
                    float s = scaleProp.floatValue * (e.delta.y > 0 ? 1f / 1.05f : 1.05f);
                    if (e.shift) s = Mathf.Round(s * 20f) / 20f;
                    scaleProp.floatValue = Mathf.Clamp(s, 0.2f, 3f);
                    e.Use();
                }
                break;
        }

        int CellIndexAt(Vector2 mp)
        {
            int dx = Mathf.Clamp(Mathf.FloorToInt((mp.x - area.x) / cs), 0, gw - 1);
            int dy = Mathf.Clamp(Mathf.FloorToInt((mp.y - area.y) / cs), 0, gh - 1);
            return Index(dx, dy);
        }

        if (serializedObject.ApplyModifiedProperties()) Repaint();
        EditorGUILayout.Space(6);
    }

    /// <summary>
    /// Campo Icono que acepta un Sprite o cualquier imagen (Texture2D). Si la imagen no está importada
    /// como Sprite (o está en modo Multiple sin cortar), la cambia a Sprite / Single y usa ese sprite.
    /// </summary>
    static void DrawIconField(ItemData item)
    {
        EditorGUI.BeginChangeCheck();
        var obj = EditorGUILayout.ObjectField(
            new GUIContent("Icon", "Arrastra cualquier imagen: si no es Sprite, se convierte sola"),
            item.icon, typeof(Object), false);
        if (!EditorGUI.EndChangeCheck()) return;

        Sprite sprite = null;
        if (obj is Sprite s) sprite = s;
        else if (obj is Texture2D tex) sprite = SpriteFromTexture(tex);
        else if (obj != null) { Debug.LogWarning("ItemData: el icono tiene que ser una imagen o un Sprite."); return; }

        Undo.RecordObject(item, "Cambiar icono");
        item.icon = sprite;
        EditorUtility.SetDirty(item);
    }

    static Sprite SpriteFromTexture(Texture2D tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return null;

        Sprite First() { foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path)) if (a is Sprite sp) return sp; return null; }

        var existing = First();
        if (existing != null) return existing;            // ya tenía sprites (p. ej. una hoja cortada)

        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            Debug.Log($"ItemData: '{path}' importada como Sprite (Single) para usarla de icono.");
        }
        return First();
    }

    static void SetAll(SerializedProperty shape, System.Func<bool, bool> f)
    {
        for (int i = 0; i < shape.arraySize; i++)
        {
            var p = shape.GetArrayElementAtIndex(i);
            p.boolValue = f(p.boolValue);
        }
    }

    /// <summary>Casilla ocupada si al menos un 15% de sus píxeles del icono son opacos.</summary>
    static void FromIcon(ItemData item, SerializedProperty shape)
    {
        var sprite = item.icon;
        string path = AssetDatabase.GetAssetPath(sprite.texture);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Debug.LogWarning("ItemDataEditor: no encuentro el archivo del icono."); return; }

        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { DestroyImmediate(tex); return; }   // lee el PNG aunque no sea Read/Write

        // Si la textura importada es más pequeña/grande que el archivo, escalar el rect del sprite
        float sx = (float)tex.width / sprite.texture.width, sy = (float)tex.height / sprite.texture.height;
        Rect tr = sprite.textureRect;
        int x0 = Mathf.RoundToInt(tr.x * sx), y0 = Mathf.RoundToInt(tr.y * sy);
        int tw = Mathf.RoundToInt(tr.width * sx), th = Mathf.RoundToInt(tr.height * sy);
        var px = tex.GetPixels32();

        int w = item.width, h = item.height;
        for (int cy = 0; cy < h; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                // fila 0 de la forma = arriba; en la textura y=0 es abajo
                int px0 = x0 + cx * tw / w, px1 = x0 + (cx + 1) * tw / w;
                int py1 = y0 + th - cy * th / h, py0 = y0 + th - (cy + 1) * th / h;
                int total = 0, solid = 0;
                for (int y = py0; y < py1; y++)
                    for (int x = px0; x < px1; x++)
                    {
                        total++;
                        if (px[y * tex.width + x].a > 128) solid++;
                    }
                shape.GetArrayElementAtIndex(cy * w + cx).boolValue = total > 0 && solid >= total * 0.15f;
            }
        DestroyImmediate(tex);
    }

    static Rect Shrink(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

    static Rect FitAspect(Rect r, float aspect)
    {
        if (r.width / r.height > aspect) { float nw = r.height * aspect; return new Rect(r.center.x - nw * 0.5f, r.y, nw, r.height); }
        float nh = r.width / aspect; return new Rect(r.x, r.center.y - nh * 0.5f, r.width, nh);
    }

    static void Frame(Rect r, float t, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}
