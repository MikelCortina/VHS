using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PSX > Personajes > Montar policías con ragdoll
///
/// Para cada FBX de Assets/Characters/Police (exportados desde Characters_PSX.blend a 1,80 m):
///  - ajusta la importación (rig Generic, sin materiales del FBX) y la textura (filtro Point),
///  - crea un material PSX/Lit con su textura,
///  - baja los brazos de la pose en T,
///  - crea el ragdoll: Rigidbody + collider + CharacterJoint en cadera, pecho, cabeza,
///    brazos, antebrazos, muslos y piernas (huesos Mixamo),
///  - añade Health + RagdollCharacter y multiplicadores de daño por zona,
///  - guarda el prefab en Assets/Prefabs/Characters y lo coloca delante del jugador.
/// Se puede volver a ejecutar: rehace los prefabs (no duplica los de la escena).
/// </summary>
public static class PoliceRagdollSetup
{
    const string Dir = "Assets/Characters/Police/";
    const string PrefabDir = "Assets/Prefabs/Characters/";
    public static readonly string[] Characters = { "Character_21_Police", "Character_22_Police" };

    [MenuItem("PSX/Personajes/Montar policías con ragdoll")]
    public static void Run()
    {
        var prefabs = BuildPrefabs();
        if (prefabs.Count == 0) return;
        PlaceInScene(prefabs);
        Debug.Log($"Policías con ragdoll: {prefabs.Count} prefabs en {PrefabDir}. Guarda la escena (Ctrl+S).");
    }

    // ------------------------------------------------------------------ prefabs

    public static List<GameObject> BuildPrefabs()
    {
        var result = new List<GameObject>();
        EnsureFolder(PrefabDir.TrimEnd('/'));
        var pm = GetPhysicsMaterial();

        foreach (var name in Characters)
        {
            string fbx = Dir + name + ".fbx";
            if (!ConfigureModel(fbx)) { Debug.LogError($"Ragdoll: no encuentro {fbx}"); continue; }
            var tex = ConfigureTexture(Dir + name + ".png");
            var mat = GetMaterial(name, tex);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = name;
            try
            {
                Build(inst, mat, pm);
                var prefab = PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + name + ".prefab");
                result.Add(prefab);
            }
            catch (System.Exception e) { Debug.LogException(e); }
            finally { Object.DestroyImmediate(inst); }
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    static bool ConfigureModel(string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!mi) return false;
        bool dirty = false;
        void Set<T>(T cur, T want, System.Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(cur, want)) { apply(want); dirty = true; } }
        Set(mi.animationType, ModelImporterAnimationType.Generic, v => mi.animationType = v);
        Set(mi.importAnimation, false, v => mi.importAnimation = v);
        Set(mi.materialImportMode, ModelImporterMaterialImportMode.None, v => mi.materialImportMode = v);
        Set(mi.importCameras, false, v => mi.importCameras = v);
        Set(mi.importLights, false, v => mi.importLights = v);
        Set(mi.globalScale, 1f, v => mi.globalScale = v);
        Set(mi.useFileScale, true, v => mi.useFileScale = v);
        if (dirty) mi.SaveAndReimport();
        return true;
    }

    static Texture2D ConfigureTexture(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti && (ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp))
        {
            ti.filterMode = FilterMode.Point;   // píxel nítido, estilo PSX
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetMaterial(string name, Texture2D tex)
    {
        string path = Dir + "M_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            var sh = Shader.Find("PSX/Lit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Standard");
            m = new Material(sh);
            // el FBX no trae colores de vértice útiles: sin esto el personaje podría salir negro
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            AssetDatabase.CreateAsset(m, path);
        }
        if (tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    static PhysicsMaterial GetPhysicsMaterial()
    {
        string path = Dir + "PM_Ragdoll.physicsMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (!pm)
        {
            // rozamiento alto y sin rebote: el cuerpo se queda donde cae, no patina ni bota
            pm = new PhysicsMaterial("PM_Ragdoll")
            {
                dynamicFriction = 0.7f,
                staticFriction = 0.8f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            AssetDatabase.CreateAsset(pm, path);
        }
        return pm;
    }

    // ------------------------------------------------------------------ ragdoll

    struct Frame { public Vector3 right, up, fwd; }

    class Bones
    {
        readonly Dictionary<string, Transform> map = new();
        public Bones(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int i = n.LastIndexOf(':');
                if (i >= 0) n = n.Substring(i + 1);
                if (!map.ContainsKey(n)) map[n] = t;
            }
        }
        public Transform this[string n] => map.TryGetValue(n, out var t) ? t : throw new System.Exception($"Ragdoll: falta el hueso {n}");
        public Transform Opt(string n) => map.TryGetValue(n, out var t) ? t : null;
    }

    public static void Build(GameObject root, Material mat, PhysicsMaterial pm)
    {
        var b = new Bones(root.transform);
        var t = root.transform;

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (mat) smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;   // los límites siguen al cuerpo al caer
        }

        // Ejes del personaje: arriba = arriba del objeto; delante = hacia donde apuntan los pies
        var f = new Frame { up = t.up };
        Vector3 toes = (b["LeftToeBase"].position - b["LeftFoot"].position) + (b["RightToeBase"].position - b["RightFoot"].position);
        f.fwd = Vector3.ProjectOnPlane(toes, f.up).normalized;
        if (f.fwd.sqrMagnitude < 0.5f) f.fwd = t.forward;
        f.right = Vector3.Cross(f.up, f.fwd);

        float height = Vector3.Dot(b["HeadTop_End"].position - t.position, f.up);
        float k = height / 1.75f;   // escala relativa a una persona normal

        // --- pose de reposo: brazos abajo (venían en T) ---
        foreach (var side in new[] { "Left", "Right" })
        {
            var arm = b[side + "Arm"]; var fore = b[side + "ForeArm"]; var hand = b[side + "Hand"];
            float s = Mathf.Sign(Vector3.Dot(arm.position - b["Hips"].position, f.right));
            Vector3 outward = f.right * s;
            Aim(arm, fore.position, -f.up + outward * 0.18f + f.fwd * 0.02f);
            Aim(fore, hand.position, -f.up + f.fwd * 0.35f + outward * 0.05f);
        }

        // --- cuerpos ---
        var hips = b["Hips"]; var chest = b["Spine1"]; var head = b["Head"];
        Vector3 lUp = b["LeftUpLeg"].position, rUp = b["RightUpLeg"].position;
        float hipHalf = Vector3.Distance(lUp, rUp) * 0.5f;

        // cadera: caja desde un poco por debajo de los muslos hasta Spine1
        {
            float bottom = Vector3.Dot((lUp + rUp) * 0.5f - t.position, f.up) - 0.10f * k;
            float top = Vector3.Dot(chest.position - t.position, f.up);
            Vector3 c = Flat(hips.position, t.position, f) + f.up * ((bottom + top) * 0.5f);
            AddBox(hips, c, new Vector3((hipHalf + 0.09f * k) * 2f, top - bottom, 0.24f * k), f, pm);
        }
        // pecho: caja desde Spine1 hasta los hombros
        {
            float shoulders = Vector3.Distance(b["LeftArm"].position, b["RightArm"].position);
            float bottom = Vector3.Dot(chest.position - t.position, f.up);
            float top = Vector3.Dot(b["Neck"].position - t.position, f.up) + 0.02f * k;
            Vector3 c = Flat(chest.position, t.position, f) + f.up * ((bottom + top) * 0.5f) + f.fwd * 0.01f * k;
            AddBox(chest, c, new Vector3(shoulders * 0.9f, top - bottom, 0.24f * k), f, pm);
        }
        // cabeza: esfera
        {
            var top = b["HeadTop_End"];
            var sc = head.gameObject.AddComponent<SphereCollider>();
            float r = Mathf.Max(0.09f * k, Vector3.Distance(head.position, top.position) * 0.55f);
            sc.center = head.InverseTransformPoint(Vector3.Lerp(head.position, top.position, 0.45f) + f.fwd * 0.02f * k);
            sc.radius = r / MaxScale(head);
            sc.sharedMaterial = pm;
        }
        foreach (var side in new[] { "Left", "Right" })
        {
            var arm = b[side + "Arm"]; var fore = b[side + "ForeArm"]; var hand = b[side + "Hand"];
            AddCapsule(arm, arm.position, fore.position, 0.055f * k, pm);
            // el antebrazo incluye la mano
            AddCapsule(fore, fore.position, hand.position + (hand.position - fore.position) * 0.45f, 0.045f * k, pm);

            var up = b[side + "UpLeg"]; var leg = b[side + "Leg"]; var foot = b[side + "Foot"];
            AddCapsule(up, up.position, leg.position, 0.085f * k, pm);
            AddCapsule(leg, leg.position, foot.position, 0.06f * k, pm);
            // pie: caja (forma parte del cuerpo de la pierna)
            var toeEnd = b.Opt(side + "Toe_End") ?? b[side + "ToeBase"];
            float footY = Vector3.Dot(foot.position - t.position, f.up) + 0.02f * k;
            Vector3 heel = Flat(foot.position, t.position, f) - f.fwd * 0.04f * k;
            Vector3 tip = Flat(toeEnd.position, t.position, f);
            Vector3 fc = (heel + tip) * 0.5f + f.up * (footY * 0.5f);
            AddBox(foot, fc, new Vector3(0.10f * k, footY, Vector3.Distance(heel, tip)), f, pm);
        }

        // --- Rigidbodies (masa total ~70 kg) ---
        var rbHips = AddBody(hips, 11f);
        var rbChest = AddBody(chest, 15f);
        var rbHead = AddBody(head, 5f);

        // --- articulaciones ---
        // CharacterJoint: el giro "twist" alrededor de 'axis'. Convención de PhysX: un giro positivo
        // con Quaternion.AngleAxis(+, axis) da ángulo NEGATIVO de la articulación. FlexAxis devuelve el eje
        // cuyo giro positivo dobla hacia 'bend', así que la flexión va en el límite bajo (negativo).
        Joint(rbChest, rbHips, FlexAxis(f.up, f.fwd), f.fwd, -35, 15, 20, 20);        // inclinarse adelante 35 / atrás 15
        Joint(rbHead, rbChest, FlexAxis(f.up, f.fwd), f.fwd, -45, 30, 25, 35);        // cabeza

        var thighs = new List<Rigidbody>(); var shins = new List<Rigidbody>();
        var hitboxes = new List<(Transform, float, float)>
        {
            (hips, 1f, 8f), (chest, 1f, 10f), (head, 4f, 18f),
        };

        foreach (var side in new[] { "Left", "Right" })
        {
            var arm = b[side + "Arm"]; var fore = b[side + "ForeArm"]; var hand = b[side + "Hand"];
            var up = b[side + "UpLeg"]; var leg = b[side + "Leg"]; var foot = b[side + "Foot"];

            var rbArm = AddBody(arm, 2.5f);
            var rbFore = AddBody(fore, 1.8f);
            var rbUp = AddBody(up, 8.5f);
            var rbLeg = AddBody(leg, 5f);
            thighs.Add(rbUp); shins.Add(rbLeg);

            Vector3 armDir = (fore.position - arm.position).normalized;
            Vector3 foreDir = (hand.position - fore.position).normalized;
            Vector3 thighDir = (leg.position - up.position).normalized;
            Vector3 shinDir = (foot.position - leg.position).normalized;

            // hombro: atrás 45 / adelante 110, abrir-cerrar 40, rotar 40
            Joint(rbArm, rbChest, FlexAxis(armDir, -f.fwd), f.fwd, -45, 110, 40, 40);
            // codo: solo se dobla hacia delante
            Joint(rbFore, rbArm, FlexAxis(foreDir, f.fwd), foreDir, -135, 5, 3, 3);
            // cadera: atrás 25 / adelante 95, abrir 30, rotar 20
            Joint(rbUp, rbHips, FlexAxis(thighDir, -f.fwd), f.fwd, -25, 95, 30, 20);
            // rodilla: solo se dobla hacia atrás
            Joint(rbLeg, rbUp, FlexAxis(shinDir, -f.fwd), shinDir, -135, 2, 3, 3);

            hitboxes.Add((arm, 0.6f, 15f)); hitboxes.Add((fore, 0.6f, 15f));
            hitboxes.Add((up, 0.75f, 8f)); hitboxes.Add((leg, 0.75f, 8f));
        }

        foreach (var (bone, mult, flinch) in hitboxes)
        {
            var hb = bone.gameObject.AddComponent<RagdollHitbox>();
            hb.damageMultiplier = mult;
            hb.flinchAngle = flinch;
        }

        // --- vida y control ---
        var health = root.GetComponent<Health>(); if (!health) health = root.AddComponent<Health>();
        health.maxHealth = 25f;   // un disparo de pistola al cuerpo lo tumba; a brazos/piernas, dos
        var rc = root.GetComponent<RagdollCharacter>(); if (!rc) rc = root.AddComponent<RagdollCharacter>();
        rc.animator = root.GetComponentInChildren<Animator>();
        rc.hips = rbHips;
        rc.thighs = thighs.ToArray();
        rc.shins = shins.ToArray();
    }

    static Vector3 Flat(Vector3 p, Vector3 origin, Frame f) => origin + Vector3.ProjectOnPlane(p - origin, f.up);

    static void Aim(Transform bone, Vector3 childPos, Vector3 targetDir)
    {
        bone.rotation = Quaternion.FromToRotation(childPos - bone.position, targetDir.normalized) * bone.rotation;
    }

    /// <summary>Eje tal que girar +grados alrededor de él lleva 'limbDir' hacia 'bend'.</summary>
    static Vector3 FlexAxis(Vector3 limbDir, Vector3 bend)
    {
        var a = Vector3.Cross(limbDir, bend).normalized;
        if (Vector3.Dot(Quaternion.AngleAxis(30f, a) * limbDir, bend) < Vector3.Dot(limbDir, bend)) a = -a;
        return a;
    }

    static float MaxScale(Transform t) { var s = t.lossyScale; return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)); }

    static int DominantAxis(Vector3 v)
    {
        v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        return v.x >= v.y && v.x >= v.z ? 0 : (v.y >= v.z ? 1 : 2);
    }

    static void AddBox(Transform bone, Vector3 worldCenter, Vector3 sizeRUF, Frame f, PhysicsMaterial pm)
    {
        var bc = bone.gameObject.AddComponent<BoxCollider>();
        bc.center = bone.InverseTransformPoint(worldCenter);
        Vector3[] axes = { f.right, f.up, f.fwd };
        float[] sizes = { sizeRUF.x, sizeRUF.y, sizeRUF.z };
        var size = Vector3.zero;
        var used = new bool[3];
        for (int i = 0; i < 3; i++)
        {
            var l = bone.InverseTransformDirection(axes[i]);
            int a = DominantAxis(l);
            if (used[a]) a = Enumerable.Range(0, 3).First(j => !used[j]);
            used[a] = true;
            size[a] = Mathf.Abs(sizes[i]) / Mathf.Abs(bone.lossyScale[a]);
        }
        bc.size = size;
        bc.sharedMaterial = pm;
    }

    static void AddCapsule(Transform bone, Vector3 start, Vector3 end, float radius, PhysicsMaterial pm)
    {
        var cc = bone.gameObject.AddComponent<CapsuleCollider>();
        int a = DominantAxis(bone.InverseTransformDirection(end - start));
        float sc = Mathf.Abs(bone.lossyScale[a]);
        cc.direction = a;
        cc.center = bone.InverseTransformPoint((start + end) * 0.5f);
        cc.height = (Vector3.Distance(start, end) + radius) / sc;   // se solapa un poco en las juntas
        cc.radius = radius / MaxScale(bone);
        cc.sharedMaterial = pm;
    }

    static Rigidbody AddBody(Transform bone, float mass)
    {
        var rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.8f;                // las articulaciones no giran como bisagras locas
        rb.isKinematic = true;                   // vivo: lo mueve la animación/pose
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.solverIterations = 20;
        rb.solverVelocityIterations = 10;
        rb.maxDepenetrationVelocity = 3f;        // evita "explosiones" si algo se solapa
        return rb;
    }

    static void Joint(Rigidbody body, Rigidbody parent, Vector3 worldTwist, Vector3 worldSwing,
                      float lowTwist, float highTwist, float swing1, float swing2)
    {
        var j = body.gameObject.AddComponent<CharacterJoint>();
        j.connectedBody = parent;
        j.anchor = Vector3.zero;
        j.autoConfigureConnectedAnchor = true;
        worldSwing = Vector3.ProjectOnPlane(worldSwing, worldTwist).normalized;
        j.axis = body.transform.InverseTransformDirection(worldTwist).normalized;
        j.swingAxis = body.transform.InverseTransformDirection(worldSwing).normalized;
        j.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
        j.highTwistLimit = new SoftJointLimit { limit = highTwist };
        j.swing1Limit = new SoftJointLimit { limit = swing1 };
        j.swing2Limit = new SoftJointLimit { limit = swing2 };
        j.enableProjection = true;               // si la física se pasa, recoloca el hueso en vez de estirarlo
        j.projectionDistance = 0.05f;
        j.projectionAngle = 10f;
        j.enablePreprocessing = false;           // más estable en cadenas de ragdoll
        j.enableCollision = false;
    }

    // ------------------------------------------------------------------ escena

    static void PlaceInScene(List<GameObject> prefabs)
    {
        Transform player = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                               .Where(m => m.GetType().Name == "FPSController").Select(p => p.transform).FirstOrDefault();
        if (!player) { var p = GameObject.Find("Player"); if (p) player = p.transform; }
        Vector3 origin = player ? player.position : Vector3.zero;
        Vector3 fwd = player ? Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized : Vector3.forward;
        if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            bool exists = Object.FindObjectsByType<RagdollCharacter>(FindObjectsInactive.Include)
                .Any(r => PrefabUtility.GetCorrespondingObjectFromSource(r.gameObject) == prefab);
            if (exists) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 pos = origin + fwd * 4f + right * ((i - (prefabs.Count - 1) * 0.5f) * 1.6f);
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.GetComponentInParent<RagdollCharacter>())
                pos.y = hit.point.y;
            else if (player) pos.y = origin.y - 1f;
            go.transform.position = pos;

            // mirando al jugador (el modelo mira hacia donde apuntan sus pies)
            var b = new Bones(go.transform);
            Vector3 toes = (b["LeftToeBase"].position - b["LeftFoot"].position) + (b["RightToeBase"].position - b["RightFoot"].position);
            Vector3 modelFwd = Vector3.ProjectOnPlane(toes, Vector3.up).normalized;
            if (modelFwd.sqrMagnitude > 0.5f)
                go.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(modelFwd, -fwd, Vector3.up), Vector3.up) * go.transform.rotation;

            Undo.RegisterCreatedObjectUndo(go, "Colocar policía");
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
