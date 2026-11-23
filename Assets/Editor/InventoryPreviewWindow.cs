using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PSX > Inventario > Vista previa en tiempo real
/// Ventana que enseña el inventario tal y como sale en el juego (es la misma UI de InventoryUI),
/// rellenado con todos los ItemData del proyecto. Se redibuja sola al cambiar el estilo, un objeto
/// (icono, forma, tamaño...) o el tamaño de la ventana. Se construye en una escena de previsualización
/// aparte: no toca la escena abierta.
/// </summary>
public class InventoryPreviewWindow : EditorWindow
{
    enum Mode { Abierto, ConDesbordamiento, TextoDeRecoger }

    InventoryStyle style;
    Mode mode = Mode.Abierto;
    bool simulateDrag = true;
    Color background = new Color(0.18f, 0.2f, 0.22f);
    bool followSelection = true;

    Scene scene;
    Camera cam;
    GameObject root;
    InventoryUI ui;
    RenderTexture rt;
    bool dirty = true;
    Vector2Int builtSize;
    InventoryStyle tempStyle;

    [MenuItem("PSX/Inventario/Vista previa en tiempo real")]
    static void Open()
    {
        var w = GetWindow<InventoryPreviewWindow>("Vista previa inventario");
        w.minSize = new Vector2(420, 280);
    }

    void OnEnable()
    {
        scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("Cámara vista previa") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(camGo, scene);
        cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.cameraType = CameraType.Preview;     // los pases PSX/VHS no se aplican a cámaras de preview
        cam.enabled = false;
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f;
        int invLayer = LayerMask.NameToLayer("Inventario3D");
        cam.cullingMask = ~(1 << (invLayer >= 0 ? invLayer : 31));   // la bandeja 3D la pinta su propia cámara

        if (style == null) style = FindSceneStyle();

        InventoryStyle.Edited += OnSomethingEdited;
        ItemData.Edited += OnSomethingEdited;
        Undo.undoRedoPerformed += MarkDirty;
        EditorApplication.projectChanged += MarkDirty;
        dirty = true;
    }

    void OnDisable()
    {
        InventoryStyle.Edited -= OnSomethingEdited;
        ItemData.Edited -= OnSomethingEdited;
        Undo.undoRedoPerformed -= MarkDirty;
        EditorApplication.projectChanged -= MarkDirty;

        DestroyPreview();
        if (rt != null) { rt.Release(); DestroyImmediate(rt); }
        if (tempStyle != null) DestroyImmediate(tempStyle);
        EditorSceneManager.ClosePreviewScene(scene);
    }

    void OnSomethingEdited(Object _) => MarkDirty();
    void MarkDirty() { dirty = true; Repaint(); }

    void OnSelectionChange()
    {
        if (followSelection && Selection.activeObject is InventoryStyle s && s != style) { style = s; MarkDirty(); }
    }

    void OnGUI()
    {
        // ---- Barra de opciones ----
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            style = (InventoryStyle)EditorGUILayout.ObjectField(style, typeof(InventoryStyle), false, GUILayout.Width(200));
            mode = (Mode)EditorGUILayout.EnumPopup(mode, EditorStyles.toolbarPopup, GUILayout.Width(150));
            using (new EditorGUI.DisabledScope(mode == Mode.TextoDeRecoger))
                simulateDrag = GUILayout.Toggle(simulateDrag, "Simular arrastre", EditorStyles.toolbarButton);
            followSelection = GUILayout.Toggle(followSelection, new GUIContent("Seguir selección", "Usar el estilo que selecciones en Project"), EditorStyles.toolbarButton);
            background = EditorGUILayout.ColorField(GUIContent.none, background, false, false, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck()) dirty = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refrescar", EditorStyles.toolbarButton)) dirty = true;
        }
        if (style == null)
            EditorGUILayout.HelpBox("Sin estilo asignado: se ve el estilo por defecto. Arrastra un Estilo de inventario arriba.", MessageType.Info);

        // ---- Zona de dibujo ----
        Rect area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (area.width < 10 || area.height < 10) return;

        float ppp = EditorGUIUtility.pixelsPerPoint;
        var size = new Vector2Int(Mathf.RoundToInt(area.width * ppp), Mathf.RoundToInt(area.height * ppp));
        if (size != builtSize) dirty = true;

        if (Event.current.type != EventType.Repaint) return;

        if (dirty)
        {
            dirty = false;
            builtSize = size;
            Rebuild(size);
        }

        if (rt == null || root == null) return;
        cam.backgroundColor = background;
        if (ui != null) ui.RenderEditorPreview();   // bandeja y modelos 3D -> su RenderTexture
        Canvas.ForceUpdateCanvases();
        cam.Render();                                // fondo + canvas (render 3D recortado + textos)
        GUI.DrawTexture(area, rt, ScaleMode.StretchToFill, false);
    }

    void Rebuild(Vector2Int size)
    {
        DestroyPreview();

        if (rt == null || rt.width != size.x || rt.height != size.y)
        {
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
            rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
        }
        cam.targetTexture = rt;
        cam.aspect = (float)size.x / size.y;

        // Inventario de ejemplo con el mismo tamaño que el del jugador de la escena (si hay)
        var sceneInv = Object.FindAnyObjectByType<Inventory>();
        root = new GameObject("Inventario vista previa") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        var inv = root.AddComponent<Inventory>();
        if (sceneInv != null)
        {
            inv.width = sceneInv.width; inv.height = sceneInv.height;
            inv.overflowWidth = sceneInv.overflowWidth; inv.overflowHeight = sceneInv.overflowHeight;
        }
        inv.InitGrids();
        string promptLabel = FillWithSamples(inv, mode == Mode.ConDesbordamiento);

        ui = root.AddComponent<InventoryUI>();
        ui.enabled = false;   // que no haga Start/Update (en Play cogería el ratón y el Tab del juego)
        var st = style;
        if (st == null)
        {
            if (tempStyle == null) tempStyle = CreateInstance<InventoryStyle>();
            tempStyle.hideFlags = HideFlags.HideAndDontSave;
            st = tempStyle;
        }
        try
        {
            ui.BuildEditorPreview(st, cam, size, mode == Mode.TextoDeRecoger, promptLabel,
                                  simulateDrag && mode != Mode.TextoDeRecoger);
        }
        catch (System.Exception e) { Debug.LogException(e); }

        // Todo lo creado debe vivir en la escena de vista previa y no guardarse
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.HideAndDontSave;
    }

    void DestroyPreview()
    {
        if (ui != null) ui.CleanupEditorPreview();
        if (root != null) DestroyImmediate(root);
        root = null; ui = null;
    }

    /// <summary>Mete un poco de todo. Con desbordamiento, sigue metiendo hasta que algo no quepa.</summary>
    static string FillWithSamples(Inventory inv, bool overflow)
    {
        var guids = AssetDatabase.FindAssets("t:ItemData");
        if (guids.Length == 0) return "Objeto";
        var items = new ItemData[guids.Length];
        for (int i = 0; i < guids.Length; i++) items[i] = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guids[i]));

        foreach (var it in items)
        {
            if (it == null) continue;
            if (it.maxStack > 1) inv.Add(it, it.maxStack + it.maxStack / 2 + 3);   // una pila llena y otra a medias
            else inv.Add(it, 1);
        }
        if (overflow)
            for (int guard = 0; guard < 200 && inv.Overflow.IsEmpty; guard++)
                foreach (var it in items) if (it != null) inv.Add(it, Mathf.Max(1, it.maxStack));

        return items[0] != null ? items[0].displayName : "Objeto";
    }

    static InventoryStyle FindSceneStyle()
    {
        var ui = Object.FindAnyObjectByType<InventoryUI>();
        if (ui != null && ui.style != null) return ui.style;
        var g = AssetDatabase.FindAssets("t:InventoryStyle");
        return g.Length > 0 ? AssetDatabase.LoadAssetAtPath<InventoryStyle>(AssetDatabase.GUIDToAssetPath(g[0])) : null;
    }
}
