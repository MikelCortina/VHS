using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Inventario 3D: la bandeja (InventoryStyle.trayModel) y los modelos de los objetos se pintan con su propia cámara
/// (Inventory3DView) y se muestran a pantalla completa; los textos son UI normal colocada encima.
///  Tab ............ abrir / cerrar (al cerrar se tira lo que haya en el desbordamiento)
///  Clic y arrastrar mover un objeto; soltarlo sobre otro igual lo apila
///  R .............. girar el objeto que arrastras
///  Shift + clic ... dividir una pila por la mitad
///  Soltar fuera ... tirar el objeto al suelo
/// También muestra el texto "[F] Nombre" al mirar un objeto recogible.
/// </summary>
[RequireComponent(typeof(Inventory))]
public class InventoryUI : MonoBehaviour
{
    [Tooltip("Aspecto del inventario. Vacío = estilo por defecto. Se puede editar en Play")]
    public InventoryStyle style;
    public Key toggleKey = Key.Tab;
    public Key rotateKey = Key.R;
    [Tooltip("Para bloquear mirar/moverse mientras está abierto. Vacío = se busca en el jugador")]
    public FPSController controller;
    public PickupInteractor interactor;

    const string LayerName = "Inventario3D";
    static readonly Vector3 ViewPosition = new Vector3(0f, -5000f, 0f);   // lejos del nivel

    Inventory inv;
    InventoryStyle S;
    InventoryStyle defaultStyle;
    Font font;
    Material keyMaterial;
    GameObject canvasGo;
    RectTransform canvasRect, window, labels;
    RawImage view3D;
    Inventory3DView view;
    GameObject warningBox;
    Text helpText, promptText, mainTitle, overflowTitle;
    readonly List<(ItemStack s, Text t)> counts = new List<(ItemStack, Text)>();
    bool built, rebuildPending, refreshPending;
    Vector2 previewCanvasSize;      // solo en la vista previa del editor
    bool editorPreview;             // construido por la ventana de vista previa (escena aparte)

    // Arrastre
    ItemStack drag;
    InventoryGrid dragFrom;
    int fromX, fromY;
    bool fromRotated;
    ItemStack splitSource;          // si se arrastra media pila, la otra mitad sigue aquí

    void Awake()
    {
        inv = GetComponent<Inventory>();
        if (controller == null) controller = GetComponent<FPSController>();
        if (interactor == null) interactor = GetComponent<PickupInteractor>();
    }

    void OnEnable()
    {
        InventoryStyle.Edited += OnStyleEdited;
        ItemData.Edited += OnItemEdited;
    }

    void OnDisable()
    {
        InventoryStyle.Edited -= OnStyleEdited;
        ItemData.Edited -= OnItemEdited;
    }

    void Start()
    {
        BuildUI();
        inv.Changed += Refresh;
        inv.OverflowReceived += () => { if (!inv.IsOpen) SetOpen(true); };
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (inv != null) inv.Changed -= Refresh;
        DestroyBuilt();
        if (defaultStyle != null) Destroy(defaultStyle);
    }

    // Cambios en Play: estilo (reconstruye todo) u objetos (rehace los modelos)
    void OnValidate() { if (built && Application.isPlaying && S != CurrentStyle()) rebuildPending = true; }
    void OnStyleEdited(InventoryStyle st) { if (built && st == S) rebuildPending = true; }
    void OnItemEdited(ItemData _) { if (built) refreshPending = true; }

    InventoryStyle CurrentStyle()
    {
        if (style != null) return style;
        if (defaultStyle == null) defaultStyle = ScriptableObject.CreateInstance<InventoryStyle>();
        return defaultStyle;
    }

    static int InventoryLayer()
    {
        int l = LayerMask.NameToLayer(LayerName);
        return l >= 0 ? l : 31;
    }

    // ------------------------------------------------------------------ bucle

    void Update()
    {
        if (rebuildPending) { rebuildPending = false; refreshPending = false; Rebuild(); }
        if (refreshPending) { refreshPending = false; Refresh(); }

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (kb[toggleKey].wasPressedThisFrame) SetOpen(!inv.IsOpen);

        // Texto de recoger
        var t = interactor != null ? interactor.Target : null;
        promptText.text = (!inv.IsOpen && t != null) ? SafeFormat(S.promptFormat, interactor.pickupKey, t.Label) : "";

        if (!inv.IsOpen) return;

        view.EnsureTarget(Mathf.RoundToInt(Screen.width * S.renderScale), Mathf.RoundToInt(Screen.height * S.renderScale));
        if (view3D.texture != view.Target) view3D.texture = view.Target;   // la pantalla ha cambiado de tamaño
        view.UpdateCamera(Time.unscaledDeltaTime, false);

        Vector2 mp = mouse.position.ReadValue();
        Ray ray = view.Camera.ViewportPointToRay(new Vector3(mp.x / Screen.width, mp.y / Screen.height, 0f));

        if (drag == null)
        {
            if (mouse.leftButton.wasPressedThisFrame) BeginDrag(ray, kb.shiftKey.isPressed);
        }
        else
        {
            if (kb[rotateKey].wasPressedThisFrame && drag.CanRotate)
            {
                drag.rotated = !drag.rotated;
                view.BeginDragModel(drag, view.TrayOf(dragFrom));
            }
            view.MoveDragModel(ray, view.HitTray(ray, 1f, out var ht, out _) ? ht : view.TrayOf(dragFrom));
            if (mouse.leftButton.wasReleasedThisFrame) EndDrag(ray);
        }

        UpdateHighlight(ray);
        UpdateOverflowVisibility();
        PlaceLabels(new Vector2(canvasRect.rect.width, canvasRect.rect.height));
    }

    void SetOpen(bool open)
    {
        if (!open && drag != null) ReturnDrag();
        inv.IsOpen = open;
        window.gameObject.SetActive(open);
        view.Camera.enabled = open;
        if (open)
        {
            view.EnsureTarget(Mathf.RoundToInt(Screen.width * S.renderScale), Mathf.RoundToInt(Screen.height * S.renderScale));
            view3D.texture = view.Target;
            view.UpdateCamera(0f, true);
        }
        if (controller != null) controller.inputEnabled = !open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        if (!open && !inv.Overflow.IsEmpty) inv.DropAllOverflow();
        Refresh();
    }

    /// <summary>Reconstruye todo con el estilo actual sin cerrar el inventario ni soltar lo arrastrado.</summary>
    void Rebuild()
    {
        bool wasOpen = inv.IsOpen;
        DestroyBuilt();
        BuildUI();
        window.gameObject.SetActive(wasOpen);
        view.Camera.enabled = wasOpen;
        if (wasOpen) { view.EnsureTarget(Mathf.RoundToInt(Screen.width * S.renderScale), Mathf.RoundToInt(Screen.height * S.renderScale)); view3D.texture = view.Target; }
        if (drag != null) view.BeginDragModel(drag, view.TrayOf(dragFrom));
        Refresh();
    }

    void DestroyBuilt()
    {
        if (view != null) { view.Dispose(); view = null; }
        if (canvasGo != null) { if (Application.isPlaying) Destroy(canvasGo); else DestroyImmediate(canvasGo); }
        if (keyMaterial != null) { if (Application.isPlaying) Destroy(keyMaterial); else DestroyImmediate(keyMaterial); }
        counts.Clear();
        built = false;
    }

    // ------------------------------------------------------------------ arrastrar

    void BeginDrag(Ray ray, bool split)
    {
        if (!view.HitTray(ray, 0f, out var tray, out var g)) return;
        var s = tray.grid.At(Mathf.FloorToInt(g.x), Mathf.FloorToInt(g.y));
        if (s == null) return;

        dragFrom = tray.grid; fromX = s.x; fromY = s.y; fromRotated = s.rotated;
        if (split && s.count > 1)
        {
            int half = s.count / 2;
            s.count -= half;
            drag = new ItemStack(s.item, half) { rotated = s.rotated };
            splitSource = s;
        }
        else
        {
            tray.grid.Remove(s);
            drag = s;
            splitSource = null;
        }
        view.BeginDragModel(drag, tray);
        view.MoveDragModel(ray, tray);
        Refresh();
    }

    void EndDrag(Ray ray)
    {
        if (view.HitTray(ray, 0f, out var tray, out var g))
        {
            var grid = tray.grid;
            TopLeft(g, out int x, out int y);
            var over = grid.InBounds(drag, x, y, drag.rotated) ? grid.Overlapping(drag, x, y, drag.rotated) : null;
            if (over != null && over.Count == 0)
            {
                grid.Place(drag, x, y, drag.rotated);
                FinishDrag();
                return;
            }
            if (over != null && over.Count == 1 && over[0].item == drag.item && over[0].SpaceLeft > 0)
            {
                int n = Mathf.Min(drag.count, over[0].SpaceLeft);     // apilar
                over[0].count += n; drag.count -= n;
                if (drag.count <= 0) { FinishDrag(); return; }
            }
            ReturnDrag();
        }
        else if (view.HitTray(ray, 0.5f, out _, out _)) ReturnDrag();   // justo al borde: no tirar sin querer
        else
        {
            var d = drag;            // fuera de las bandejas: tirarlo
            FinishDrag();
            inv.Drop(d);
        }
    }

    /// <summary>Devuelve lo que se arrastra a donde estaba (o al primer hueco, o al suelo).</summary>
    void ReturnDrag()
    {
        if (splitSource != null) splitSource.count += drag.count;
        else if (!dragFrom.Place(drag, fromX, fromY, fromRotated)
                 && !inv.Main.TryAutoPlace(drag) && !inv.Overflow.TryAutoPlace(drag))
        {
            var d = drag; FinishDrag(); inv.Drop(d); return;
        }
        FinishDrag();
    }

    void FinishDrag()
    {
        drag = null; splitSource = null; dragFrom = null;
        view.EndDragModel();
        inv.NotifyChanged();
    }

    void TopLeft(Vector2 g, out int x, out int y)
    {
        x = Mathf.RoundToInt(g.x - drag.W * 0.5f);
        y = Mathf.RoundToInt(g.y - drag.H * 0.5f);
    }

    void UpdateHighlight(Ray ray)
    {
        view.ClearHighlights();
        if (drag == null || !view.HitTray(ray, 0f, out var tray, out var g)) return;
        TopLeft(g, out int x, out int y);
        bool ok = false;
        if (tray.grid.InBounds(drag, x, y, drag.rotated))
        {
            var over = tray.grid.Overlapping(drag, x, y, drag.rotated);
            ok = over.Count == 0 || (over.Count == 1 && over[0].item == drag.item && over[0].SpaceLeft > 0);
        }
        view.Highlight(tray, drag, x, y, ok);
    }

    void UpdateOverflowVisibility()
    {
        bool has = !inv.Overflow.IsEmpty;
        bool show = has || (drag != null && dragFrom == inv.Overflow);
        if (view.OverflowVisible != show) view.SetOverflowVisible(show);
        if (overflowTitle.gameObject.activeSelf != show) overflowTitle.gameObject.SetActive(show);
        if (warningBox.activeSelf != has) warningBox.SetActive(has);
        helpText.text = has ? SafeFormat(S.helpOverflow, toggleKey, null)
                            : SafeFormat(S.helpNormal, rotateKey, toggleKey);
    }

    /// <summary>string.Format que no rompe el juego si el texto del estilo tiene llaves mal puestas.</summary>
    static string SafeFormat(string fmt, object a, object b)
    {
        if (string.IsNullOrEmpty(fmt)) return "";
        try { return string.Format(fmt, a, b); }
        catch (System.FormatException) { return fmt; }
    }

    // ------------------------------------------------------------------ dibujo

    void Refresh()
    {
        if (!built) return;
        view.RefreshItems();

        foreach (var c in counts) if (c.t != null) Destroy(c.t.gameObject);
        counts.Clear();
        foreach (var grid in new[] { inv.Main, inv.Overflow })
            foreach (var s in grid.stacks)
            {
                if (s.item.maxStack <= 1) continue;
                var txt = AddText(NewRect("Cantidad", labels), s.count.ToString(), S.countSize, TextAnchor.LowerRight, S.countColor);
                var r = txt.rectTransform;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.pivot = new Vector2(1f, 0f);
                r.sizeDelta = new Vector2(120f, S.countSize * 1.4f);
                if (S.countOutline) txt.gameObject.AddComponent<Outline>().effectColor = S.countOutlineColor;
                counts.Add((s, txt));
            }
        UpdateOverflowVisibility();
    }

    /// <summary>Coloca títulos, aviso, ayuda y contadores sobre la bandeja (proyectando con la cámara 3D).</summary>
    void PlaceLabels(Vector2 canvasSize)
    {
        Vector2 ToCanvas(Vector3 rootLocal) { var v = view.ToViewport(rootLocal); return new Vector2(v.x * canvasSize.x, v.y * canvasSize.y); }

        var m = view.Main;
        mainTitle.rectTransform.anchoredPosition = ToCanvas(new Vector3(m.outer.min.x, m.topY, m.outer.max.z)) + new Vector2(0f, 6f);
        if (overflowTitle.gameObject.activeSelf)
        {
            var o = view.Overflow;
            overflowTitle.rectTransform.anchoredPosition = ToCanvas(new Vector3(o.outer.min.x, o.topY, o.outer.max.z)) + new Vector2(0f, 6f);
        }
        Vector2 bottom = ToCanvas(new Vector3(m.outer.center.x, 0f, m.outer.min.z));
        ((RectTransform)warningBox.transform).anchoredPosition = bottom - new Vector2(0f, 14f);
        helpText.rectTransform.anchoredPosition = bottom - new Vector2(0f, 22f + (warningBox.activeSelf ? S.warningHeight : 0f));

        foreach (var (s, t) in counts)
        {
            if (t == null) continue;
            var tray = view.TrayOf(inv.Main.stacks.Contains(s) ? inv.Main : inv.Overflow);
            int lastX = 0, lastY = 0;
            for (int ly = 0; ly < s.H; ly++)
                for (int lx = 0; lx < s.W; lx++)
                    if (s.Covers(lx, ly) && (ly > lastY || (ly == lastY && lx >= lastX))) { lastX = lx; lastY = ly; }
            t.rectTransform.anchoredPosition = ToCanvas(view.CellCorner(tray, s.x + lastX, s.y + lastY)) + new Vector2(-3f, 3f);
            t.gameObject.SetActive(tray == view.Main || view.OverflowVisible);
        }
    }

    void BuildUI()
    {
        S = CurrentStyle();
        font = S.font != null ? S.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Parte 3D (lejos del nivel, en su capa). En la vista previa del editor cuelga de este objeto.
        bool preview = editorPreview;
        view = new Inventory3DView(preview ? transform : null, preview ? transform.position : ViewPosition, inv, S, InventoryLayer());
        if (!preview) view.HookPostBypass();

        canvasGo = new GameObject("Inventario UI", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = S.referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)canvasGo.transform;

        // Texto de recoger (siempre activo, bajo la mira)
        promptText = AddText(NewRect("Recoger", canvasRect), "", S.promptSize, TextAnchor.UpperCenter, S.promptColor);
        promptText.rectTransform.anchorMin = promptText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        promptText.rectTransform.sizeDelta = new Vector2(800, S.promptSize * 2f);
        promptText.rectTransform.anchoredPosition = S.promptOffset;
        if (S.promptOutline) promptText.gameObject.AddComponent<Outline>().effectColor = Color.black;

        // Ventana: fondo oscuro + render 3D (con el fondo recortado) + textos
        window = NewRect("Ventana", canvasRect);
        Stretch(window);
        AddImage(window, S.screenSprite, S.screenDim);

        var vr = NewRect("Render 3D", window);
        Stretch(vr);
        view3D = vr.gameObject.AddComponent<RawImage>();
        view3D.raycastTarget = false;
        var ks = Shader.Find("PSX/UIChromaKey");
        if (ks != null)
        {
            keyMaterial = new Material(ks) { hideFlags = HideFlags.DontSave };
            keyMaterial.SetColor("_KeyColor", Inventory3DView.KeyColor);
            view3D.material = keyMaterial;
        }

        labels = NewRect("Textos", window);
        Stretch(labels);

        mainTitle = MakeLabel(S.mainTitle, S.titleSize, S.titleColor, TextAnchor.LowerLeft, new Vector2(0f, 0f), 600f);
        overflowTitle = MakeLabel(S.overflowTitle, S.titleSize, S.titleColor, TextAnchor.LowerLeft, new Vector2(0f, 0f), 600f);

        var wb = NewRect("Aviso", labels);
        wb.anchorMin = wb.anchorMax = Vector2.zero;
        wb.pivot = new Vector2(0.5f, 1f);
        wb.sizeDelta = new Vector2(S.warningWidth, S.warningHeight);
        AddImage(wb, S.warningSprite, S.warningBackground);
        var wt = AddText(NewRect("Texto", wb), S.warningText, S.warningSize, TextAnchor.MiddleCenter, S.warningTextColor);
        Stretch(wt.rectTransform);
        warningBox = wb.gameObject;

        helpText = MakeLabel("", S.helpSize, S.textColor, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), 1100f);
        built = true;
    }

    Text MakeLabel(string text, int size, Color color, TextAnchor anchor, Vector2 pivot, float width)
    {
        var t = AddText(NewRect("Texto", labels), text, size, anchor, color);
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = Vector2.zero;
        r.pivot = pivot;
        r.sizeDelta = new Vector2(width, size * 1.6f);
        return t;
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------ vista previa del editor

    /// <summary>
    /// Solo para PSX > Inventario > Vista previa: construye el inventario real (3D + textos) con este estilo.
    /// La cámara 3D pinta en su RenderTexture (RenderEditorPreview) y 'cam' pinta el canvas encima.
    /// </summary>
    public void BuildEditorPreview(InventoryStyle st, Camera cam, Vector2 pixelSize,
                                   bool promptOnly, string promptLabel, bool simulateDrag)
    {
        inv = GetComponent<Inventory>();
        style = st;
        editorPreview = true;
        if (simulateDrag && inv.Main.stacks.Count > 0)
        {
            drag = inv.Main.stacks[0];
            inv.Main.Remove(drag);
            dragFrom = inv.Main;
        }

        BuildUI();
        view.Camera.scene = gameObject.scene;
        view.Camera.cameraType = CameraType.Preview;   // los pases PSX/VHS no se aplican
        view.EnsureTarget(Mathf.RoundToInt(pixelSize.x * S.renderScale), Mathf.RoundToInt(pixelSize.y * S.renderScale));
        view3D.texture = view.Target;

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        // El CanvasScaler no se actualiza fuera de Play: misma cuenta que "Scale With Screen Size" (match 0.5)
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        float lw = Mathf.Log(pixelSize.x / S.referenceResolution.x, 2f), lh = Mathf.Log(pixelSize.y / S.referenceResolution.y, 2f);
        scaler.scaleFactor = Mathf.Pow(2f, Mathf.Lerp(lw, lh, 0.5f));
        canvas.scaleFactor = scaler.scaleFactor;
        previewCanvasSize = pixelSize / scaler.scaleFactor;

        window.gameObject.SetActive(!promptOnly);
        promptText.text = promptOnly ? SafeFormat(S.promptFormat, Key.F, promptLabel) : "";

        view.RefreshItems();
        Refresh();
        UpdateOverflowVisibility();
        view.UpdateCamera(0f, true);

        if (drag != null)
        {
            // Hueco libre para enseñar el resaltado verde y el objeto flotando encima
            var tray = view.Main;
            for (int y = 0; y < inv.Main.height; y++)
                for (int x = 0; x < inv.Main.width; x++)
                {
                    if (!inv.Main.IsFree(drag, x, y, drag.rotated)) continue;
                    view.Highlight(tray, drag, x, y, true);
                    view.BeginDragModel(drag, tray);
                    var center = tray.origin + new Vector3((x + drag.W * 0.5f + 0.3f) * tray.cellW, 0f, -(y + drag.H * 0.5f + 0.3f) * tray.cellH);
                    var world = view.Root.TransformPoint(center);
                    view.MoveDragModel(new Ray(view.Camera.transform.position, world - view.Camera.transform.position), tray);
                    goto placed;
                }
            placed:;
        }
        PlaceLabels(previewCanvasSize);
    }

    /// <summary>Pinta la parte 3D de la vista previa (llamar antes de pintar el canvas).</summary>
    public void RenderEditorPreview()
    {
        if (view != null && view.Camera != null) view.Camera.Render();
    }

    /// <summary>Libera lo que la vista previa crea fuera de la jerarquía.</summary>
    public void CleanupEditorPreview()
    {
        DestroyBuilt();
        if (defaultStyle != null) DestroyImmediate(defaultStyle);
    }
#endif

    // ------------------------------------------------------------------ utilidades UI

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    /// <summary>Image con sprite opcional; si el sprite tiene bordes (9-slice) se estira por partes.</summary>
    static Image AddImage(RectTransform r, Sprite sprite, Color color)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return img;
    }

    Text AddText(RectTransform r, string text, int size, TextAnchor anchor, Color color)
    {
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
}
