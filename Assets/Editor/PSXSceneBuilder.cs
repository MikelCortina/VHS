using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Monta una escena de prueba con la parte jugable del kit PSX:
/// jugador (FPSController + Health), cámara, arma (HitscanWeapon),
/// enemigos (solo cuerpo + Health, sin lógica) y un nivel sencillo con texturas de 64x64.
/// Menú: PSX > Crear escena de prueba. Se puede volver a ejecutar cuando quieras.
/// </summary>
public static class PSXSceneBuilder
{
    const string ScenePath = "Assets/Scenes/PSX_Test.unity";
    const string TexDir = "Assets/Textures";
    const string MatDir = "Assets/Materials";
    const string PrefabDir = "Assets/Prefabs";

    static readonly Color FogColor = new Color(0.12f, 0.10f, 0.14f);

    [MenuItem("PSX/Crear escena de prueba")]
    static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("PSX", "Ya existe PSX_Test.unity. ¿Reemplazarla?", "Sí", "No"))
            return;
        Build();
    }

    public static void Build()
    {
        if (Shader.Find("PSX/Lit") == null) { Debug.LogError("PSXSceneBuilder: falta el shader PSX/Lit"); return; }
        EnsureFolder(TexDir); EnsureFolder(MatDir); EnsureFolder(PrefabDir);

        // ---------------- Texturas (64x64, 15 bits de color como en PS1) ----------------
        var floorTex = MakeTexture("T_Suelo", 11, (x, y, r) =>
        {
            if (x % 16 == 0 || y % 16 == 0) return new Color(0.16f, 0.14f, 0.12f);
            float tile = ((x / 16) * 73 + (y / 16) * 151) % 7 / 7f * 0.12f;
            float n = (float)r.NextDouble() * 0.08f;
            return new Color(0.36f + tile + n, 0.33f + tile + n, 0.29f + tile + n);
        });
        var brickTex = MakeTexture("T_Ladrillo", 22, (x, y, r) =>
        {
            int row = y / 8;
            int bx = (x + (row % 2) * 8) % 16;
            if (y % 8 == 0 || bx == 0) return new Color(0.2f, 0.17f, 0.15f);
            float v = ((x + (row % 2) * 8) / 16 * 37 + row * 59) % 5 / 5f * 0.1f;
            float n = (float)r.NextDouble() * 0.07f;
            return new Color(0.45f + v + n, 0.22f + v * 0.5f + n, 0.16f + n);
        });
        var crateTex = MakeTexture("T_Caja", 33, (x, y, r) =>
        {
            bool frame = x < 4 || x > 59 || y < 4 || y > 59 || Mathf.Abs(x - y) < 3;
            float n = (float)r.NextDouble() * 0.06f;
            if (frame) return new Color(0.35f + n, 0.22f + n, 0.1f);
            if (x % 14 == 0) return new Color(0.25f, 0.16f, 0.08f);
            float grain = Mathf.Sin(y * 0.9f + x * 0.15f) * 0.04f;
            return new Color(0.55f + grain + n, 0.38f + grain + n, 0.2f + n);
        });
        var skinTex = MakeTexture("T_Enemigo", 44, (x, y, r) =>
        {
            float n = (float)r.NextDouble();
            if (n > 0.93f) return new Color(0.12f, 0.18f, 0.1f);
            return new Color(0.28f + n * 0.08f, 0.42f + n * 0.08f, 0.22f + n * 0.05f);
        });
        var metalTex = MakeTexture("T_Metal", 55, (x, y, r) =>
        {
            float n = (float)r.NextDouble() * 0.06f;
            float band = (y % 16 < 2) ? -0.1f : 0f;
            return new Color(0.33f + n + band, 0.33f + n + band, 0.37f + n + band);
        });

        // ---------------- Materiales PSX/Lit ----------------
        var mFloor = MakeMat("M_Suelo", floorTex, new Vector2(10, 10));
        var mWall = MakeMat("M_Muro", brickTex, new Vector2(10, 1));
        var mPillar = MakeMat("M_Columna", brickTex, new Vector2(1, 3));
        var mCrate = MakeMat("M_Caja", crateTex, Vector2.one);
        var mEnemy = MakeMat("M_Enemigo", skinTex, Vector2.one);
        var mEyes = MakeMat("M_Ojos", Texture2D.whiteTexture, Vector2.one, new Color(1f, 0.15f, 0.1f));
        var mGun = MakeMat("M_Arma", metalTex, Vector2.one);

        // ---------------- Escena ----------------
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------------- Prefabs ----------------
        var enemyGO = Prim(PrimitiveType.Capsule, "Enemigo", Vector3.zero, Vector3.one, mEnemy, null, true);
        Prim(PrimitiveType.Cube, "Ojos", new Vector3(0, 0.55f, 0.42f), new Vector3(0.6f, 0.12f, 0.2f), mEyes, enemyGO.transform, false);
        enemyGO.AddComponent<Health>().maxHealth = 50f;
        var enemyPrefab = PrefabUtility.SaveAsPrefabAsset(enemyGO, PrefabDir + "/Enemigo.prefab");
        Object.DestroyImmediate(enemyGO);

        // Luz ambiente + niebla (la niebla esconde el poco alcance de dibujado)
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.26f);
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(RenderSettings.ambientLight);
        RenderSettings.ambientProbe = sh;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = 6f;
        RenderSettings.fogEndDistance = 32f;

        var sun = new GameObject("Luz direccional").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.75f, 0.7f, 0.85f);
        sun.intensity = 0.6f;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Nivel
        var level = new GameObject("Nivel").transform;
        Prim(PrimitiveType.Plane, "Suelo", Vector3.zero, new Vector3(4, 1, 4), mFloor, level);
        Prim(PrimitiveType.Cube, "Muro N", new Vector3(0, 2, 20), new Vector3(40, 4, 1), mWall, level);
        Prim(PrimitiveType.Cube, "Muro S", new Vector3(0, 2, -20), new Vector3(40, 4, 1), mWall, level);
        Prim(PrimitiveType.Cube, "Muro E", new Vector3(20, 2, 0), new Vector3(1, 4, 40), mWall, level);
        Prim(PrimitiveType.Cube, "Muro O", new Vector3(-20, 2, 0), new Vector3(1, 4, 40), mWall, level);
        foreach (var p in new[] { new Vector3(-6, 2, -6), new Vector3(6, 2, -6), new Vector3(-6, 2, 6), new Vector3(6, 2, 6) })
            Prim(PrimitiveType.Cube, "Columna", p, new Vector3(1.5f, 4, 1.5f), mPillar, level);
        foreach (var p in new[] { new Vector3(-3, 0.75f, -9), new Vector3(4, 0.75f, 2), new Vector3(4.2f, 2.25f, 2.1f),
                                  new Vector3(-11, 0.75f, 9), new Vector3(11, 0.75f, -11) })
            Prim(PrimitiveType.Cube, "Caja", p, Vector3.one * 1.5f, mCrate, level);

        // Antorchas: luces puntuales (iluminan por vértice -> se nota lo poco subdividido que está todo)
        foreach (var p in new[] { new Vector3(-6, 2.5f, -4.5f), new Vector3(6, 2.5f, 4.5f), new Vector3(0, 2.5f, 12) })
        {
            var l = new GameObject("Antorcha").AddComponent<Light>();
            l.transform.SetParent(level, false);
            l.transform.position = p;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.3f);
            l.range = 9f;
            l.intensity = 3f;
            l.shadows = LightShadows.None;
        }

        // Jugador
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0, 0, -15);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
        player.AddComponent<Health>().maxHealth = 100f;
        var fps = player.AddComponent<FPSController>();

        var camGO = new GameObject("Camera");
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(player.transform, false);
        camGO.transform.localPosition = new Vector3(0, 1.6f, 0);
        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 40f;
        cam.fieldOfView = 70f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = FogColor;
        camGO.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        fps.cameraPivot = camGO.transform;

        // Arma (hija de la cámara)
        var weaponGO = new GameObject("Weapon");
        weaponGO.transform.SetParent(camGO.transform, false);
        weaponGO.transform.localPosition = new Vector3(0.22f, -0.22f, 0.45f);
        Prim(PrimitiveType.Cube, "Cuerpo", Vector3.zero, new Vector3(0.08f, 0.1f, 0.28f), mGun, weaponGO.transform, false);
        Prim(PrimitiveType.Cube, "Cañón", new Vector3(0, 0.02f, 0.2f), new Vector3(0.045f, 0.045f, 0.2f), mGun, weaponGO.transform, false);
        Prim(PrimitiveType.Cube, "Empuñadura", new Vector3(0, -0.1f, -0.08f), new Vector3(0.06f, 0.14f, 0.07f), mGun, weaponGO.transform, false);
        var src = weaponGO.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        var weapon = weaponGO.AddComponent<HitscanWeapon>();
        weapon.audioSource = src;

        // Enemigos (sin lógica: solo cuerpo y Health)
        var enemies = new GameObject("Enemigos").transform;
        foreach (var p in new[] { new Vector3(-8, 1, 6), new Vector3(8, 1, 9), new Vector3(0, 1, 15), new Vector3(-13, 1, -3) })
        {
            var e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, scene);
            e.transform.SetParent(enemies, false);
            e.transform.position = p;
            e.transform.rotation = Quaternion.LookRotation(player.transform.position - p);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.SaveAssets();
        Debug.Log("PSXSceneBuilder: escena creada en " + ScenePath);
    }

    // ------------------------------------------------------------------
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    static float Q(float c) => Mathf.Round(Mathf.Clamp01(c) * 31f) / 31f; // 5 bits por canal

    static Texture2D MakeTexture(string name, int seed, System.Func<int, int, System.Random, Color> pixel)
    {
        string path = $"{TexDir}/{name}.png";
        var rng = new System.Random(seed);
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var c = pixel(x, y, rng);
                tex.SetPixel(x, y, new Color(Q(c.r), Q(c.g), Q(c.b), 1f));
            }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material MakeMat(string name, Texture tex, Vector2 tiling, Color? color = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("PSX/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetColor("_BaseColor", color ?? Color.white);
        mat.SetFloat("_UseVertexColor", 0f); // las primitivas de Unity no traen color de vértice
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat,
                           Transform parent = null, bool keepCollider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
}
