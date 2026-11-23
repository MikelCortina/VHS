using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PSX > Inventario > Montar inventario en la escena abierta
///   1. Crea (si no existen) los objetos de ejemplo en Assets/Items: Pistola 2x1, Balas 1x1 (pila 50), Maletín 3x2.
///   2. Crea sus prefabs de mundo (con collider, Rigidbody e ItemPickup) y sus iconos.
///   3. Añade Inventory + PickupInteractor + InventoryUI al jugador, le asigna Assets/Items/Estilo_Inventario.asset
///      (lo crea si no existe) y enlaza las balas al arma.
///   4. Inventario 3D: asigna al estilo la bandeja Assets/Models/Inventario/Inventario.fbx y un material,
///      pone las cuadrículas a 6x6 (las de la bandeja) y crea la capa "Inventario3D" (la cámara del jugador no la ve).
///   5. Pone objetos de prueba delante del jugador (grupo "Objetos de prueba").
///   Nunca sobrescribe assets existentes. La escena NO se guarda: Ctrl+S.
/// PSX > Inventario > Regenerar iconos: vuelve a renderizar los iconos de todos los ItemData de Assets/Items.
/// </summary>
public static class InventorySetup
{
    const string Dir = "Assets/Items";
    const string PrefabDir = Dir + "/Prefabs";
    const string IconDir = Dir + "/Iconos";
    const string PistolModel = "Assets/Weapons/Pistol/Pistol.fbx";
    const int IconPxPerCell = 128;

    [MenuItem("PSX/Inventario/Montar inventario en la escena abierta")]
    static void Setup()
    {
        EnsureFolder(Dir); EnsureFolder(PrefabDir); EnsureFolder(IconDir);

        var pistola = GetOrCreateItem("Item_Pistola", "Pistola", 2, 1, 1);
        var balas = GetOrCreateItem("Item_Balas", "Balas 9mm", 1, 1, 50);
        var maletin = GetOrCreateItem("Item_Maletin", "Maletín", 3, 2, 1);

        if (pistola.worldPrefab == null) pistola.worldPrefab = MakePistolPrefab(pistola);
        if (balas.worldPrefab == null) balas.worldPrefab = MakeBoxPrefab(balas, "Pickup_Balas", new Vector3(0.12f, 0.07f, 0.08f), new Color(0.75f, 0.16f, 0.13f), 0.3f);
        if (maletin.worldPrefab == null) maletin.worldPrefab = MakeBoxPrefab(maletin, "Pickup_Maletin", new Vector3(0.45f, 0.32f, 0.12f), new Color(0.35f, 0.22f, 0.12f), 2f);

        foreach (var it in new[] { pistola, balas, maletin })
        {
            if (it.icon == null) it.icon = RenderIcon(it);
            EditorUtility.SetDirty(it);
        }
        AssetDatabase.SaveAssets();

        SetupPlayer(balas, pistola, maletin);
    }

    [MenuItem("PSX/Inventario/Regenerar iconos")]
    static void RegenerateIcons()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData", new[] { Dir }))
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (it == null || it.worldPrefab == null) continue;
            it.icon = RenderIcon(it);
            EditorUtility.SetDirty(it);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("InventorySetup: iconos regenerados en " + IconDir);
    }

    // ------------------------------------------------------------------ jugador y escena

    static void SetupPlayer(ItemData balas, ItemData pistola, ItemData maletin)
    {
        var fps = Object.FindAnyObjectByType<FPSController>();
        if (fps == null) { Debug.LogError("InventorySetup: no hay FPSController en la escena abierta."); return; }
        var player = fps.gameObject;

        var inv = player.GetComponent<Inventory>();
        if (inv == null) inv = Undo.AddComponent<Inventory>(player);
        var pick = player.GetComponent<PickupInteractor>();
        if (pick == null) pick = Undo.AddComponent<PickupInteractor>(player);
        var ui = player.GetComponent<InventoryUI>();
        if (ui == null) ui = Undo.AddComponent<InventoryUI>(player);

        var cam = fps.cameraPivot != null ? fps.cameraPivot.GetComponentInChildren<Camera>() : null;
        if (cam == null) cam = player.GetComponentInChildren<Camera>();
        Undo.RecordObjects(new Object[] { inv, pick, ui }, "Montar inventario");
        if (cam != null) { pick.cam = cam; inv.dropOrigin = cam.transform; }
        ui.controller = fps;
        ui.interactor = pick;
        if (ui.style == null) ui.style = GetOrCreateStyle();
        SetupTray(ui.style);

        // La bandeja de Inventario.fbx tiene 6 x 6 casillas
        if (inv.width == 8 && inv.height == 5) { inv.width = 6; inv.height = 6; }
        if (inv.overflowWidth == 6 && inv.overflowHeight == 8) { inv.overflowWidth = 6; inv.overflowHeight = 6; }

        // Capa propia para el inventario 3D, que la cámara del jugador no ve
        int layer = EnsureLayer(InventoryLayerName, 31);
        if (cam != null && layer >= 0)
        {
            Undo.RecordObject(cam, "Capa del inventario");
            cam.cullingMask &= ~(1 << layer);
        }

        foreach (var w in player.GetComponentsInChildren<HitscanWeapon>(true))
        {
            Undo.RecordObject(w, "Munición del inventario");
            if (w.ammoItem == null) w.ammoItem = balas;
        }

        // Objetos de prueba delante del jugador
        if (GameObject.Find("Objetos de prueba") == null)
        {
            var group = new GameObject("Objetos de prueba");
            Undo.RegisterCreatedObjectUndo(group, "Objetos de prueba");
            Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 origin = player.transform.position + fwd * 2.5f;

            int i = 0;
            void Put(ItemData item, int count, int row)
            {
                Vector3 p = origin + right * ((i % 6) - 2.5f) * 0.5f + fwd * row * 0.7f;
                float y = p.y;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                    y = hit.point.y;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(item.worldPrefab, group.transform);
                go.transform.position = new Vector3(p.x, y + 0.15f, p.z);
                go.transform.rotation = Quaternion.LookRotation(fwd);
                var ip = go.GetComponent<ItemPickup>();
                ip.count = count;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ip);
                i++;
            }
            i = 0; Put(pistola, 1, 0); Put(pistola, 1, 0); Put(balas, 40, 0); Put(balas, 30, 0); Put(balas, 50, 0); Put(balas, 45, 0);
            i = 0; for (int k = 0; k < 6; k++) Put(maletin, 1, 1);
        }

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log("InventorySetup: inventario montado en '" + player.name + "'. Guarda la escena (Ctrl+S). " +
                  "Tab = inventario, F = recoger.", player);
    }

    // ------------------------------------------------------------------ assets

    const string InventoryLayerName = "Inventario3D";
    const string TrayModelPath = "Assets/Models/Inventario/Inventario.fbx";

    /// <summary>Bandeja 3D del estilo: Inventario.fbx + material PSX gris oscuro (solo si están vacíos).</summary>
    static void SetupTray(InventoryStyle st)
    {
        Undo.RecordObject(st, "Bandeja del inventario");
        if (st.trayModel == null)
        {
            st.trayModel = AssetDatabase.LoadAssetAtPath<GameObject>(TrayModelPath);
            if (st.trayModel == null) Debug.LogWarning("InventorySetup: no encuentro " + TrayModelPath + "; se usará una tabla plana.");
        }
        if (st.trayMaterial == null)
        {
            string path = $"{Dir}/M_Bandeja.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var sh = Shader.Find("PSX/Lit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(sh);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.22f, 0.22f, 0.24f));
                if (mat.HasProperty("_UseVertexColor")) mat.SetFloat("_UseVertexColor", 0f);
                AssetDatabase.CreateAsset(mat, path);
            }
            st.trayMaterial = mat;
        }
        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Devuelve la capa con ese nombre; si no existe, se lo pone a 'preferred' (si está libre).</summary>
    static int EnsureLayer(string name, int preferred)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = preferred; i >= 8; i--)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = name;
            tm.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"InventorySetup: capa {i} = '{name}'.");
            return i;
        }
        Debug.LogWarning("InventorySetup: no hay capas libres para el inventario 3D; se usará la 31.");
        return -1;
    }

    static InventoryStyle GetOrCreateStyle()
    {
        string path = $"{Dir}/Estilo_Inventario.asset";
        var st = AssetDatabase.LoadAssetAtPath<InventoryStyle>(path);
        if (st != null) return st;
        st = ScriptableObject.CreateInstance<InventoryStyle>();
        AssetDatabase.CreateAsset(st, path);
        AssetDatabase.SaveAssets();
        return st;
    }

    static ItemData GetOrCreateItem(string file, string displayName, int w, int h, int maxStack)
    {
        string path = $"{Dir}/{file}.asset";
        var it = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (it != null) return it;
        it = ScriptableObject.CreateInstance<ItemData>();
        it.displayName = displayName; it.width = w; it.height = h; it.maxStack = maxStack;
        AssetDatabase.CreateAsset(it, path);
        return it;
    }

    static GameObject MakePistolPrefab(ItemData item)
    {
        string path = $"{PrefabDir}/Pickup_Pistola.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PistolModel);
        if (model == null)
        {
            Debug.LogWarning("InventorySetup: no encuentro " + PistolModel + "; la pistola será una caja.");
            return MakeBoxPrefab(item, "Pickup_Pistola", new Vector3(0.2f, 0.13f, 0.04f), new Color(0.2f, 0.2f, 0.22f), 1f);
        }

        var root = new GameObject("Pickup_Pistola");
        var m = (GameObject)Object.Instantiate(model, root.transform);
        m.name = "Modelo";
        foreach (var a in m.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        // Escala: lado más largo = 22 cm, centrado en el origen
        var b = Bounds(m);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) m.transform.localScale *= 0.22f / longest;
        b = Bounds(m);
        m.transform.position -= b.center;
        b = Bounds(m);

        var box = root.AddComponent<BoxCollider>();
        box.center = b.center; box.size = b.size;
        root.AddComponent<Rigidbody>().mass = 1f;
        var p = root.AddComponent<ItemPickup>(); p.item = item; p.count = 1;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject MakeBoxPrefab(ItemData item, string name, Vector3 size, Color color, float mass)
    {
        string path = $"{PrefabDir}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        string matPath = $"{PrefabDir}/M_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_UseVertexColor")) mat.SetFloat("_UseVertexColor", 0f);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = name;
        root.transform.localScale = size;
        root.GetComponent<MeshRenderer>().sharedMaterial = mat;
        root.AddComponent<Rigidbody>().mass = mass;
        var p = root.AddComponent<ItemPickup>(); p.item = item; p.count = 1;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ------------------------------------------------------------------ iconos

    /// <summary>
    /// Renderiza el prefab de mundo de lado (eje más fino hacia la cámara, eje más largo en horizontal)
    /// con fondo transparente. Se hace dos veces (fondo negro y blanco) para sacar el alfa.
    /// </summary>
    static Sprite RenderIcon(ItemData item)
    {
        if (item.worldPrefab == null) return null;
        int w = item.width * IconPxPerCell, h = item.height * IconPxPerCell;

        var pru = new PreviewRenderUtility();
        try
        {
            var go = pru.InstantiatePrefabInScene(item.worldPrefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = item.worldPrefab.transform.localScale;

            // Orientar: eje más fino -> Z (hacia la cámara), mediano -> Y, largo -> X.
            var b = Bounds(go);
            Vector3 s = b.size;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] len = { s.x, s.y, s.z };
            int thin = 0, longI = 0;
            for (int k = 1; k < 3; k++) { if (len[k] < len[thin]) thin = k; if (len[k] > len[longI]) longI = k; }
            int mid = 3 - thin - longI;
            if (thin == longI) mid = (thin + 1) % 3;
            // Si el icono es más alto que ancho, el eje largo va en vertical
            if (item.height > item.width) { int t = mid; mid = longI; longI = t; }
            go.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(axes[thin], axes[mid]));

            b = Bounds(go);
            go.transform.position -= b.center;

            var cam = pru.camera;
            cam.orthographic = true;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 20f;
            float aspect = (float)w / h;
            b = Bounds(go);
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.08f;

            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(30f, -30f, 0f);
            pru.lights[1].intensity = 0.6f;
            pru.lights[1].transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            pru.ambientColor = new Color(0.35f, 0.35f, 0.38f);

            var black = Shot(pru, w, h, Color.black);
            var white = Shot(pru, w, h, Color.white);

            var outTex = new Texture2D(black.width, black.height, TextureFormat.RGBA32, false);
            var pb = black.GetPixels(); var pw = white.GetPixels();
            var po = new Color[pb.Length];
            for (int i = 0; i < pb.Length; i++)
            {
                float a = 1f - Mathf.Clamp01(((pw[i].r - pb[i].r) + (pw[i].g - pb[i].g) + (pw[i].b - pb[i].b)) / 3f);
                po[i] = a > 0.004f ? new Color(pb[i].r / a, pb[i].g / a, pb[i].b / a, a) : Color.clear;
            }
            outTex.SetPixels(po); outTex.Apply();

            string path = $"{IconDir}/Icono_{item.name}.png";
            File.WriteAllBytes(path, outTex.EncodeToPNG());
            Object.DestroyImmediate(black); Object.DestroyImmediate(white); Object.DestroyImmediate(outTex);

            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;   // en Multiple no se crea ningún Sprite
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"InventorySetup: no se pudo crear el icono de {item.name} ({e.Message}). Se mostrará su nombre.");
            return null;
        }
        finally { pru.Cleanup(); }
    }

    static Texture2D Shot(PreviewRenderUtility pru, int w, int h, Color bg)
    {
        pru.camera.clearFlags = CameraClearFlags.SolidColor;
        pru.camera.backgroundColor = bg;
        pru.BeginStaticPreview(new Rect(0, 0, w, h));
        pru.Render(true, false);
        return pru.EndStaticPreview();
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
