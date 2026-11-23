using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Parte 3D del inventario: bandeja principal + bandeja de desbordamiento (modelo de InventoryStyle.trayModel),
/// modelos de los objetos colocados en sus casillas, resaltado de casillas, objeto arrastrado, luz y cámara.
/// Todo vive bajo un objeto raíz (lejos del nivel, en su propia capa) y la cámara lo pinta en una RenderTexture.
///
/// Espacio de la raíz: X = columnas (derecha), Z = filas (la fila 0 arriba, en +Z), Y = arriba.
/// </summary>
public class Inventory3DView
{
    public class Tray
    {
        public InventoryGrid grid;
        public Transform holder;
        public Vector3 origin;          // esquina arriba-izquierda de la casilla (0,0), sobre el fondo (espacio raíz)
        public float cellW, cellH;      // tamaño de casilla en X y en Z
        public float floorY, topY;
        public Bounds outer;            // caja exterior de la bandeja (espacio raíz)
        public Renderer[,] cells;
        public Transform items;
    }

    /// <summary>Color de fondo del render; la RawImage lo hace transparente.</summary>
    public static readonly Color KeyColor = new Color(1f, 0f, 1f, 1f);
    const float TrayWorldWidth = 0.4f;   // la bandeja se escala a 40 cm de ancho (lejos de la niebla de la escena)

    public Camera Camera { get; private set; }
    public RenderTexture Target { get; private set; }
    public Tray Main { get; private set; }
    public Tray Overflow { get; private set; }
    public Transform Root => root;
    public bool OverflowVisible { get; private set; }

    readonly Transform root;
    readonly InventoryStyle S;
    readonly int layer;
    readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    readonly List<Object> owned = new List<Object>();
    Material highlightMat, noModelMat;
    Transform dragModel;
    Vector3 camPos;
    bool camInit;
    float savedVhs;

    public Inventory3DView(Transform parent, Vector3 position, Inventory inv, InventoryStyle style, int layer)
    {
        S = style;
        this.layer = layer;
        root = NewChild("Inventario 3D", parent);
        root.position = position;
        root.rotation = Quaternion.identity;

        var hs = Shader.Find("PSX/InventoryHighlight");
        highlightMat = new Material(hs != null ? hs : Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
        owned.Add(highlightMat);

        Main = BuildTray("Bandeja", inv.Main, 0f);
        Overflow = BuildTray("Bandeja desbordamiento", inv.Overflow, Main.outer.size.x * (1f + S.trayGap));

        var lt = NewChild("Luz inventario", root);
        var light = lt.gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = S.lightColor;
        light.intensity = S.lightIntensity;
        light.cullingMask = 1 << layer;
        light.shadows = LightShadows.None;
        lt.localRotation = Quaternion.Euler(S.lightRotation);

        var ct = NewChild("Cámara inventario", root);
        Camera = ct.gameObject.AddComponent<Camera>();
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = KeyColor;
        Camera.cullingMask = 1 << layer;
        Camera.allowMSAA = false;
        Camera.allowHDR = false;
        Camera.enabled = false;
        Camera.depth = -50;

        SetOverflowVisible(false);
        UpdateCamera(0f, true);
    }

    // ------------------------------------------------------------------ render

    public void EnsureTarget(int w, int h)
    {
        w = Mathf.Max(16, w); h = Mathf.Max(16, h);
        if (Target != null && Target.width == w && Target.height == h) return;
        if (Target != null) { Target.Release(); Kill(Target); }
        Target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave, name = "Inventario 3D" };
        Target.Create();
        Camera.targetTexture = Target;
        Camera.aspect = (float)w / h;
    }

    /// <summary>Mientras pinta esta cámara, los pases PSX y VHS de pantalla completa no hacen nada.</summary>
    public void HookPostBypass()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    public void UnhookPostBypass()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != Camera) return;
        savedVhs = Shader.GetGlobalFloat("_VHSAmount");
        Shader.SetGlobalFloat("_VHSAmount", 0f);
        Shader.SetGlobalFloat("_PSXPostBypass", 1f);
    }

    void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != Camera) return;
        Shader.SetGlobalFloat("_VHSAmount", savedVhs);
        Shader.SetGlobalFloat("_PSXPostBypass", 0f);
    }

    /// <summary>Encuadra la bandeja principal (y la de desbordamiento si se ve), con transición suave.</summary>
    public void UpdateCamera(float dt, bool instant)
    {
        Bounds b = Main.outer;
        if (OverflowVisible) b.Encapsulate(Overflow.outer);
        Vector3 c = b.center + new Vector3(-S.viewOffset.x * b.size.x, 0f, -S.viewOffset.y * b.size.z);

        float tan = Mathf.Tan(S.cameraFov * 0.5f * Mathf.Deg2Rad);
        float aspect = Camera.aspect > 0.01f ? Camera.aspect : 16f / 9f;
        float halfZ = b.size.z * 0.5f * S.framing, halfX = b.size.x * 0.5f * S.framing;
        float dist = Mathf.Max(halfZ / tan, halfX / (tan * aspect)) + b.size.y;

        var rot = Quaternion.Euler(90f - S.cameraTilt, 0f, 0f);   // mirando hacia abajo, +Z arriba en pantalla
        Vector3 pos = c - rot * Vector3.forward * dist;
        camPos = (instant || !camInit) ? pos : Vector3.Lerp(camPos, pos, 1f - Mathf.Exp(-S.cameraLerp * dt));
        camInit = true;

        Camera.transform.localRotation = rot;
        Camera.transform.localPosition = camPos;
        Camera.fieldOfView = S.cameraFov;
        Camera.nearClipPlane = dist * 0.05f;
        Camera.farClipPlane = dist * 4f;
    }

    public void SetOverflowVisible(bool v)
    {
        OverflowVisible = v;
        Overflow.holder.gameObject.SetActive(v);
        Overflow.items.gameObject.SetActive(v);
    }

    public Vector3 ToViewport(Vector3 rootLocal) => Camera.WorldToViewportPoint(root.TransformPoint(rootLocal));

    public void Dispose()
    {
        UnhookPostBypass();
        if (root != null) Kill(root.gameObject);
        if (Target != null) { Target.Release(); Kill(Target); }
        foreach (var o in owned) if (o != null) Kill(o);
        owned.Clear();
    }

    // ------------------------------------------------------------------ casillas y rayos

    /// <summary>
    /// Punto del fondo de una bandeja bajo el rayo, en coordenadas de cuadrícula continuas (x derecha, y abajo).
    /// margin = casillas de tolerancia alrededor de la bandeja.
    /// </summary>
    public bool HitTray(Ray worldRay, float margin, out Tray tray, out Vector2 g)
    {
        tray = null; g = default;
        foreach (var t in Trays())
        {
            var plane = new Plane(root.up, root.TransformPoint(new Vector3(0f, t.floorY, 0f)));
            if (!plane.Raycast(worldRay, out float d)) continue;
            Vector3 p = root.InverseTransformPoint(worldRay.GetPoint(d));
            float mx = margin * t.cellW, mz = margin * t.cellH;
            if (p.x < t.outer.min.x - mx || p.x > t.outer.max.x + mx || p.z < t.outer.min.z - mz || p.z > t.outer.max.z + mz) continue;
            tray = t;
            g = new Vector2((p.x - t.origin.x) / t.cellW, (t.origin.z - p.z) / t.cellH);
            return true;
        }
        return false;
    }

    public Tray TrayOf(InventoryGrid grid) => grid == Main.grid ? Main : Overflow;

    IEnumerable<Tray> Trays()
    {
        yield return Main;
        if (OverflowVisible) yield return Overflow;
    }

    public void ClearHighlights()
    {
        foreach (var t in new[] { Main, Overflow })
            foreach (var r in t.cells) if (r.enabled) r.enabled = false;
    }

    public void Highlight(Tray t, ItemStack s, int x, int y, bool ok)
    {
        mpb.SetColor("_Color", ok ? S.cellValid : S.cellInvalid);
        for (int ly = 0; ly < s.H; ly++)
            for (int lx = 0; lx < s.W; lx++)
            {
                int cx = x + lx, cy = y + ly;
                if (!s.Covers(lx, ly) || cx < 0 || cy < 0 || cx >= t.grid.width || cy >= t.grid.height) continue;
                var r = t.cells[cx, cy];
                r.enabled = true;
                r.SetPropertyBlock(mpb);
            }
    }

    /// <summary>Esquina de abajo-derecha de la casilla (cx, cy), sobre el fondo (para el contador).</summary>
    public Vector3 CellCorner(Tray t, int cx, int cy) =>
        t.origin + new Vector3((cx + 1) * t.cellW, 0f, -(cy + 1) * t.cellH);

    // ------------------------------------------------------------------ objetos

    public void RefreshItems()
    {
        foreach (var t in new[] { Main, Overflow })
        {
            for (int i = t.items.childCount - 1; i >= 0; i--)
            {
                var c = t.items.GetChild(i).gameObject;
                c.SetActive(false);
                Kill(c);
            }
            foreach (var s in t.grid.stacks)
            {
                var m = MakeModel(s.item, s.rotated, t, t.items);
                m.localPosition = t.origin + new Vector3((s.x + s.W * 0.5f) * t.cellW, 0f, -(s.y + s.H * 0.5f) * t.cellH);
            }
        }
    }

    public void BeginDragModel(ItemStack s, Tray t)
    {
        EndDragModel();
        dragModel = MakeModel(s.item, s.rotated, t, root);
    }

    public void EndDragModel()
    {
        if (dragModel != null) { dragModel.gameObject.SetActive(false); Kill(dragModel.gameObject); }
        dragModel = null;
    }

    /// <summary>Pone el objeto arrastrado bajo el ratón, flotando sobre la bandeja.</summary>
    public void MoveDragModel(Ray worldRay, Tray t)
    {
        if (dragModel == null) return;
        float y = t.floorY + S.dragLift * Mathf.Min(t.cellW, t.cellH);
        var plane = new Plane(root.up, root.TransformPoint(new Vector3(0f, y, 0f)));
        if (plane.Raycast(worldRay, out float d))
        {
            Vector3 p = root.InverseTransformPoint(worldRay.GetPoint(d));
            dragModel.localPosition = new Vector3(p.x, y, p.z);
        }
    }

    /// <summary>
    /// Copia la parte visual del modelo del objeto (sin físicas ni scripts), la tumba (lado fino arriba,
    /// lado largo a lo ancho) y la escala para que quepa en sus casillas. Pivote = centro de su caja, sobre el fondo.
    /// </summary>
    Transform MakeModel(ItemData item, bool rotated, Tray t, Transform parent)
    {
        var itemRoot = NewChild(item.displayName, parent);
        var fit = NewChild("Ajuste", itemRoot);

        var src = item.inventoryModel != null ? item.inventoryModel : item.worldPrefab;
        if (src == null || !CopyRenderers(src, fit))
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(cube.GetComponent<Collider>());
            cube.transform.SetParent(fit, false);
            if (noModelMat == null)
            {
                var sh = Shader.Find("PSX/Lit");
                noModelMat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
                if (noModelMat.HasProperty("_BaseColor")) noModelMat.SetColor("_BaseColor", S.noModelColor);
                if (noModelMat.HasProperty("_UseVertexColor")) noModelMat.SetFloat("_UseVertexColor", 0f);
                owned.Add(noModelMat);
            }
            cube.GetComponent<MeshRenderer>().sharedMaterial = noModelMat;
            cube.transform.localScale = new Vector3(item.width, 0.3f, item.height);
        }
        SetLayer(itemRoot);

        Quaternion baseRot = Quaternion.identity;
        if (!item.manualOrientation)
        {
            var b0 = LocalBounds(fit, fit);
            PickAxes(b0.size, out int thin, out int mid, out int lng);
            Vector3 zAxis = item.width >= item.height ? Axis(mid) : Axis(lng);
            baseRot = Quaternion.Inverse(Quaternion.LookRotation(zAxis, Axis(thin)));
        }
        fit.localRotation = Quaternion.Euler(item.modelRotation) * baseRot;

        var b = LocalBounds(fit, itemRoot);
        // Con el objeto girado, el X local de itemRoot acaba en el eje de las filas (casillas de alto cellH)
        float cw = rotated ? t.cellH : t.cellW, ch = rotated ? t.cellW : t.cellH;
        float tx = item.width * cw * S.itemFill, tz = item.height * ch * S.itemFill;
        float ty = S.itemMaxHeight * Mathf.Min(t.cellW, t.cellH);
        float s = Mathf.Min(tx / Mathf.Max(b.size.x, 1e-5f), Mathf.Min(tz / Mathf.Max(b.size.z, 1e-5f), ty / Mathf.Max(b.size.y, 1e-5f)));
        s *= item.modelScale > 0f ? item.modelScale : 1f;
        fit.localScale = Vector3.one * s;

        Vector3 c = b.center * s;
        float bottom = b.min.y * s;
        fit.localPosition = new Vector3(
            -c.x + item.modelOffset.x * cw,
            -bottom + item.modelLift * Mathf.Min(cw, ch) + 0.002f * Mathf.Min(cw, ch),
            -c.z - item.modelOffset.y * ch);

        itemRoot.localRotation = rotated ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
        return itemRoot;
    }

    // ------------------------------------------------------------------ bandeja

    Tray BuildTray(string name, InventoryGrid grid, float xOffset)
    {
        var t = new Tray { grid = grid };
        t.holder = NewChild(name, root);
        t.holder.localPosition = new Vector3(xOffset, 0f, 0f);

        float outerX, outerZ, topY;
        if (S.trayModel != null)
        {
            var model = Object.Instantiate(S.trayModel, t.holder);
            model.name = "Modelo";
            StripToVisuals(model);
            var mt = model.transform;
            mt.localPosition = Vector3.zero;
            mt.localRotation = Quaternion.identity;

            // Tumbar: lado más fino hacia arriba, lado largo en Z (filas)
            PickAxes(LocalBounds(mt, t.holder).size, out int thin, out int mid, out int lng);
            var q = Quaternion.Inverse(Quaternion.LookRotation(Axis(lng), Axis(thin)));
            if (S.flipTray) q = Quaternion.AngleAxis(180f, Vector3.forward) * q;
            mt.localRotation = q;

            // Escala fija y fondo en y = 0
            var b = LocalBounds(mt, t.holder);
            mt.localScale *= TrayWorldWidth / Mathf.Max(b.size.x, 1e-5f);
            b = LocalBounds(mt, t.holder);
            mt.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = LocalBounds(mt, t.holder);
            outerX = b.size.x; outerZ = b.size.z; topY = b.max.y;

            if (S.trayMaterial != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = S.trayMaterial;
                    r.sharedMaterials = mats;
                }
            SetLayer(mt);
        }
        else
        {
            // Sin modelo: una tabla plana del tamaño de la cuadrícula
            float cell = TrayWorldWidth / (grid.width + 0.5f);
            outerX = cell * (grid.width + 0.5f); outerZ = cell * (grid.height + 0.5f); topY = cell * 0.05f;
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Kill(board.GetComponent<Collider>());
            board.name = "Tabla";
            board.transform.SetParent(t.holder, false);
            board.transform.localScale = new Vector3(outerX, topY, outerZ);
            board.transform.localPosition = new Vector3(0f, -topY * 0.5f, 0f);
            if (S.trayMaterial != null) board.GetComponent<MeshRenderer>().sharedMaterial = S.trayMaterial;
            SetLayer(board.transform);
            topY = 0f;
        }

        float rim = S.trayRim * Mathf.Min(outerX, outerZ);
        float innerX = Mathf.Max(outerX - 2f * rim, 1e-4f), innerZ = Mathf.Max(outerZ - 2f * rim, 1e-4f);
        t.cellW = innerX / grid.width;
        t.cellH = innerZ / grid.height;
        t.floorY = 0f;
        t.topY = topY;
        t.origin = t.holder.localPosition + new Vector3(-innerX * 0.5f, 0f, innerZ * 0.5f);
        t.outer = new Bounds(t.holder.localPosition + new Vector3(0f, topY * 0.5f, 0f), new Vector3(outerX, Mathf.Max(topY, 1e-4f), outerZ));

        // Resaltado: un quad por casilla, apagado hasta que se arrastra algo encima
        t.cells = new Renderer[grid.width, grid.height];
        float lift = 0.004f * Mathf.Min(t.cellW, t.cellH) + 0.0005f;
        for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Kill(q.GetComponent<Collider>());
                q.name = $"Resaltado {x},{y}";
                q.transform.SetParent(t.holder.parent, false);
                q.transform.localPosition = t.origin + new Vector3((x + 0.5f) * t.cellW, lift, -(y + 0.5f) * t.cellH);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(t.cellW * S.cellHighlightSize, t.cellH * S.cellHighlightSize, 1f);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = highlightMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                q.layer = layer;
                q.transform.SetParent(t.holder, true);
                t.cells[x, y] = r;
            }

        t.items = NewChild(name + " objetos", root);
        return t;
    }

    // ------------------------------------------------------------------ utilidades

    Transform NewChild(string name, Transform parent)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    void SetLayer(Transform t)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    static void PickAxes(Vector3 size, out int thin, out int mid, out int lng)
    {
        float[] l = { size.x, size.y, size.z };
        thin = 0; lng = 0;
        for (int k = 1; k < 3; k++) { if (l[k] < l[thin]) thin = k; if (l[k] > l[lng]) lng = k; }
        if (thin == lng) lng = (thin + 1) % 3;
        mid = 3 - thin - lng;
    }

    /// <summary>Caja de todas las mallas bajo 'of', en el espacio local de 'space'.</summary>
    static Bounds LocalBounds(Transform of, Transform space)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in of.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                      : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            var mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = space.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return any ? b : new Bounds(Vector3.zero, Vector3.one * 0.1f);
    }

    /// <summary>Copia solo mallas y materiales de 'src' (prefab o modelo) bajo 'dst'. Devuelve false si no hay mallas.</summary>
    static bool CopyRenderers(GameObject src, Transform dst)
    {
        bool any = false;
        Matrix4x4 toSrc = src.transform.worldToLocalMatrix;
        foreach (var r in src.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                      : r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            var m = toSrc * r.transform.localToWorldMatrix;
            var go = new GameObject(r.name);
            go.transform.SetParent(dst, false);
            go.transform.localPosition = m.GetPosition();
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            any = true;
        }
        return any;
    }

    /// <summary>Quita todo lo que no sea visual (scripts, físicas, animación) de una copia del modelo.</summary>
    static void StripToVisuals(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) Kill(c);
        foreach (var c in go.GetComponentsInChildren<Joint>(true)) Kill(c);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
        foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) Kill(c);
        foreach (var c in go.GetComponentsInChildren<Animator>(true)) Kill(c);
    }

    /// <summary>Destroy en Play, DestroyImmediate en el editor (vista previa).</summary>
    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
