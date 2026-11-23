using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// PSX > Montar pistola animada
/// Monta en la escena abierta los brazos + pistola exportados de Blender con TsitskiExport
/// ("Arms Pistol.fbx" y "Pistol.fbx" en Assets/Weapons/Pistol) dentro del arma que tiene HitscanWeapon:
///  - crea un Animator Controller por modelo (Idle en bucle, Attack -> Idle),
///  - coloca los brazos para que su hueso "camera" coincida con la cámara del jugador,
///  - pone materiales PSX/Lit,
///  - y conecta HitscanWeapon para que reproduzca "Attack" en los dos al disparar.
/// Se puede volver a ejecutar tras reexportar: rehace el ViewModel y le pasa del anterior los materiales,
/// scripts y objetos que le hayas añadido (ver Transfer). Ctrl+Z lo deshace.
/// </summary>
public static class PistolViewModelSetup
{
    const string Dir = "Assets/Weapons/Pistol/";   // pistola, brazos (M_Brazos, arms_01) y fogonazo (FX) compartidos

    /// <summary>Qué arma montar: sus FBX, materiales, objeto en la escena, tecla y sonido.</summary>
    class WeaponDef
    {
        public string Label, Dir, GunFbx, ArmsFbx, MatPrefix, TexPattern, ObjectName, Sound;
        public UnityEngine.InputSystem.Key Key;
        public System.Action<HitscanWeapon> Defaults;   // valores iniciales al crear su HitscanWeapon
    }

    static readonly WeaponDef PistolDef = new WeaponDef
    {
        Label = "Pistola", Dir = Dir, GunFbx = "Pistol.fbx", ArmsFbx = "Arms Pistol.fbx",
        MatPrefix = "Piezas/M_Pistola_", ObjectName = null,   // el HitscanWeapon que ya había en la escena
        Sound = "Assets/Audio/Weapons/Disparo_Pistola_Crudo.wav", Key = UnityEngine.InputSystem.Key.Digit1,
    };

    static readonly WeaponDef AK47Def = new WeaponDef
    {
        Label = "AK47", Dir = "Assets/Weapons/AK47/", GunFbx = "AK47.fbx", ArmsFbx = "Arms AK47.fbx",
        MatPrefix = "Piezas/M_AK47_", TexPattern = "Texturas/T_AK47_{0}.png", ObjectName = "Weapon_AK47",
        Sound = "Assets/Audio/Weapons/Disparo_AK47_Crudo.wav", Key = UnityEngine.InputSystem.Key.Digit3,
        Defaults = w =>
        {
            w.automatic = true;          // ráfaga manteniendo el clic
            w.fireRate = 10f;            // ~600 disparos/min, como un AKM
            w.damage = 30f;
            w.range = 150f;
            w.magazineSize = 30;
            w.shootPitchVariation = 0.04f;
            w.aimFovMultiplier = 0.75f;
        },
    };

    static readonly WeaponDef[] AllDefs = { PistolDef, AK47Def };

    [MenuItem("PSX/Montar pistola animada")]
    public static void Setup() => Setup(PistolDef);

    [MenuItem("PSX/Montar AK47")]
    public static void SetupAK47() => Setup(AK47Def);

    /// <summary>El HitscanWeapon de esta arma en la escena abierta; el del AK47 se crea junto a la pistola si falta.</summary>
    static HitscanWeapon FindWeapon(WeaponDef def)
    {
        var all = Object.FindObjectsByType<HitscanWeapon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var named = AllDefs.Where(d => d.ObjectName != null).Select(d => d.ObjectName).ToList();
        var pistol = all.FirstOrDefault(w => !named.Contains(w.name));
        if (def.ObjectName == null) return pistol;
        var w0 = all.FirstOrDefault(w => w.name == def.ObjectName);
        if (w0) return w0;
        if (!pistol) return null;

        var go = new GameObject(def.ObjectName);
        Undo.RegisterCreatedObjectUndo(go, def.Label);
        go.transform.SetParent(pistol.transform.parent, false);
        var w1 = Undo.AddComponent<HitscanWeapon>(go);
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(pistol), w1);   // mismos ajustes (capas, munición…)
        w1.viewModel = null; w1.muzzleParticles = null; w1.muzzleLight = null;
        w1.audioSource = null; w1.shootClip = null;
        def.Defaults?.Invoke(w1);
        return w1;
    }

    static void Setup(WeaponDef def)
    {
        string Dir = def.Dir;   // (oculta la constante: FBX, controllers y materiales de esta arma)
        string tag = "Montar " + def.Label;
        var weapon = FindWeapon(def);
        if (!weapon) { Debug.LogError(tag + ": no hay ningún HitscanWeapon en la escena abierta."); return; }
        var cam = weapon.GetComponentInParent<Camera>(true);
        if (!cam) { Debug.LogError(tag + ": el HitscanWeapon no está dentro de una cámara."); return; }

        var armsModel = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + def.ArmsFbx);
        var gunModel = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + def.GunFbx);
        if (!armsModel || !gunModel) { Debug.LogError($"{tag}: faltan {def.ArmsFbx} o {def.GunFbx} en {Dir}"); return; }

        string Ctrl(string fbx) => Dir + System.IO.Path.GetFileNameWithoutExtension(fbx) + ".controller";
        var armsCtrl = BuildController(Ctrl(def.ArmsFbx), Dir + def.ArmsFbx);
        var gunCtrl = BuildController(Ctrl(def.GunFbx), Dir + def.GunFbx);
        if (!armsCtrl || !gunCtrl) return;

        Undo.SetCurrentGroupName(tag);
        int group = Undo.GetCurrentGroup();

        // Rehacer si ya se había montado. El anterior se guarda hasta el final para pasar al nuevo
        // los materiales, scripts y objetos que le hayas puesto (ver Transfer).
        var old = weapon.transform.Find("ViewModel");
        string oldSettings = null;   // velocidades y demás ajustes del FPSViewModelAnimator anterior
        if (old)
        {
            var oldAnim = old.GetComponent<FPSViewModelAnimator>();
            if (oldAnim) oldSettings = EditorJsonUtility.ToJson(oldAnim);
            Undo.RecordObject(old.gameObject, "ViewModel anterior");
            old.name = "ViewModel (anterior)";
        }

        // Ocultar el arma de cubos del kit
        foreach (Transform t in weapon.transform)
        {
            if (t.name == "Cuerpo" || t.name == "Cañón" || t.name == "Empuñadura")
            {
                Undo.RecordObject(t.gameObject, "Ocultar arma de cubos");
                t.gameObject.SetActive(false);
            }
        }

        // El arma pasa a estar en el centro de la cámara: la posición la da la animación
        Undo.RecordObject(weapon.transform, "Weapon");
        weapon.transform.localPosition = Vector3.zero;
        weapon.transform.localRotation = Quaternion.identity;

        var vm = new GameObject("ViewModel");
        Undo.RegisterCreatedObjectUndo(vm, "ViewModel");
        vm.transform.SetParent(weapon.transform, false);

        var arms = (GameObject)PrefabUtility.InstantiatePrefab(armsModel, weapon.gameObject.scene);
        var gun = (GameObject)PrefabUtility.InstantiatePrefab(gunModel, weapon.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(arms, "Brazos");
        Undo.RegisterCreatedObjectUndo(gun, "Pistola");
        arms.transform.SetParent(vm.transform, false);
        gun.transform.SetParent(vm.transform, false);

        // Colocar: el hueso "camera" de los brazos (pose de reposo) = la cámara del jugador
        Vector3 P(Transform t) => vm.transform.InverseTransformPoint(t.position);
        var camBone = Find(arms, "camera");
        var rootBone = Find(arms, "root");
        // Lado derecho = de un hombro a otro. Se usa el inicio de los huesos "shoulder", que no se mueve con la pose
        // (los upper_arm sí: el FBX viene en la pose del Idle y, si en él giras los hombros, la cámara salía torcida)
        var upR = Find(arms, "shoulder.R") ?? Find(arms, "upper_arm.R");
        var upL = Find(arms, "shoulder.L") ?? Find(arms, "upper_arm.L");
        if (!camBone || !rootBone || !upR || !upL) { Debug.LogError(tag + ": no encuentro los huesos camera/root/shoulder.L/R en los brazos."); return; }
        Vector3 pc = P(camBone);
        Vector3 up = (pc - P(rootBone)).normalized;
        Vector3 right = (P(upR) - P(upL)).normalized;
        Vector3 fwd = Vector3.Cross(right, up).normalized;
        up = Vector3.Cross(fwd, right).normalized;
        Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
        foreach (var go in new[] { arms, gun })
        {
            go.transform.localRotation = inv;
            go.transform.localPosition = -(inv * pc);
        }

        // El fogonazo antiguo (solo una luz, sin partículas) se borra. El nuevo, si ya existía, pasa tal cual al arma nueva.
        foreach (var l in weapon.GetComponentsInChildren<Light>(true).Where(l => l.name == "Fogonazo" && !l.GetComponent<ParticleSystem>()).ToArray())
            Undo.DestroyObjectImmediate(l.gameObject);

        // Animators
        var armsAnim = SetupAnimator(arms, armsCtrl);
        var gunAnim = SetupAnimator(gun, gunCtrl);

        // Materiales PSX: uno para los brazos y uno por cada pieza de la pistola
        // (Assets/Weapons/Pistol/Piezas/M_Pistola_<pieza>). La textura que les pongas se conserva al volver a montar.
        // (los brazos comparten material con la pistola; cada arma tiene los suyos: M_Pistola_<pieza>, M_AK47_<pieza>,
        // que al crearse cogen su textura de Texturas/ si la hay)
        var mArms = GetMat(PistolViewModelSetup.Dir + "M_Brazos.mat", Color.white,
                           AssetDatabase.LoadAssetAtPath<Texture2D>(PistolViewModelSetup.Dir + "arms_01.png"));
        foreach (var r in arms.GetComponentsInChildren<Renderer>(true))
            SetMats(r, _ => mArms);
        foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
            SetMats(r, name => string.IsNullOrEmpty(name) ? null : GetMat(Dir + def.MatPrefix + name + ".mat", PieceColor(name), null,
                def.TexPattern == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + string.Format(def.TexPattern, name))));

        // Conectar el disparo
        var vmAnim = Undo.AddComponent<FPSViewModelAnimator>(vm);
        if (oldSettings != null) EditorJsonUtility.FromJsonOverwrite(oldSettings, vmAnim);   // conservar tus ajustes
        vmAnim.animators = new[] { armsAnim, gunAnim };
        vmAnim.armsAnimator = armsAnim;
        vmAnim.weaponModel = gun;
        Undo.RecordObject(weapon, "HitscanWeapon");
        weapon.viewModel = vmAnim;
        weapon.recoilKick = 0.02f;   // el retroceso ya lo hace la animación

        // Sonido del disparo (solo si no le has puesto otro)
        if (!weapon.shootClip)
            weapon.shootClip = AssetDatabase.LoadAssetAtPath<AudioClip>(def.Sound);
        weapon.equipKey = def.Key;
        if (!weapon.audioSource)
        {
            var src = weapon.GetComponent<AudioSource>();
            if (!src) src = Undo.AddComponent<AudioSource>(weapon.gameObject);
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D: suena igual sin importar hacia dónde mires
            weapon.audioSource = src;
        }

        Undo.RecordObject(cam, "Near clip");
        cam.nearClipPlane = 0.02f;   // que no se corten los brazos

        // Pasar al nuevo lo que tenía el anterior y borrarlo
        if (old)
        {
            Transfer(old, vm.transform, vmAnim);
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // Vista previa en el editor: pose del Idle
        var idleArms = Clip(Dir + def.ArmsFbx, "Idle");
        var idleGun = Clip(Dir + def.GunFbx, "Idle");
        if (idleArms) idleArms.SampleAnimation(arms, 0f);
        if (idleGun) idleGun.SampleAnimation(gun, 0f);

        // Fogonazo de partículas + luz en la boca del cañón (solo si no hay ya uno: así conserva tus ajustes)
        if (!weapon.muzzleParticles) CreateMuzzleFlash(weapon, gun, cam);

        SetupSwitcher(cam);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(weapon.gameObject.scene);
        Selection.activeGameObject = vm;
        Debug.Log($"{def.Label} montada (tecla {def.Key}). Dale a Play, sácala con su número y dispara con clic izquierdo. " +
                  "Guarda la escena (Ctrl+S) si te gusta.");
    }

    /// <summary>WeaponSwitcher en la cámara con todas las armas montadas: cada una se saca con su tecla (equipKey) y,
    /// sin arma, se ven los puños de la pistola.</summary>
    static void SetupSwitcher(Camera cam)
    {
        var weapons = cam.GetComponentsInChildren<HitscanWeapon>(true).Where(w => w.viewModel).OrderBy(w => w.equipKey).ToArray();
        var sw = cam.GetComponent<WeaponSwitcher>();
        if (!sw) sw = Undo.AddComponent<WeaponSwitcher>(cam.gameObject);
        Undo.RecordObject(sw, "Cambio de armas");
        sw.weapons = weapons;
        sw.fistsWeapon = FindWeapon(PistolDef);
        foreach (var w in weapons)
            if (!w.gameObject.activeSelf) { Undo.RecordObject(w.gameObject, "Arma"); w.gameObject.SetActive(true); }   // las oculta el propio switcher al jugar
        Debug.Log("Cambio de armas: " + string.Join(", ", weapons.Select(w => $"{w.name} = {w.equipKey}")));
    }

    /// <summary>
    /// Copia del ViewModel anterior al nuevo, buscando cada objeto por su ruta (p. ej. "Pistol/Root"):
    ///  - los materiales de cada Renderer (si la pieza sigue teniendo los mismos huecos de material),
    ///  - los componentes que añadiste a mano (scripts, luces, colliders…) en el ViewModel, los brazos o la pistola,
    ///  - los objetos que creaste dentro (se mueven tal cual al mismo padre en el nuevo),
    /// y actualiza las referencias de la escena que apuntaban al anterior para que apunten al nuevo.
    /// </summary>
    static void Transfer(Transform oldVm, Transform newVm, FPSViewModelAnimator newAnim)
    {
        var map = new Dictionary<Object, Object>();
        bool FromModel(Object o) => PrefabUtility.GetCorrespondingObjectFromSource(o) != null;
        Transform Match(Transform o)
        {
            var p = AnimationUtility.CalculateTransformPath(o, oldVm);
            return p == "" ? newVm : newVm.Find(p);
        }
        var oldTs = oldVm.GetComponentsInChildren<Transform>(true);
        int mats = 0, comps = 0, objs = 0, refs = 0;
        var skipped = new List<string>();

        // 1. Objetos equivalentes, y sus componentes del mismo tipo en el mismo orden
        foreach (var o in oldTs)
        {
            var n = Match(o);
            if (!n) continue;
            map[o] = n; map[o.gameObject] = n.gameObject;
            foreach (var group in o.GetComponents<Component>().Where(c => c && !(c is Transform)).GroupBy(c => c.GetType()))
            {
                var news = n.GetComponents(group.Key);
                int i = 0;
                foreach (var c in group) { if (i < news.Length) map[c] = news[i]; i++; }
            }
        }
        var oldAnim = oldVm.GetComponent<FPSViewModelAnimator>();
        if (oldAnim) map[oldAnim] = newAnim;

        // 2. Materiales
        foreach (var r in oldVm.GetComponentsInChildren<Renderer>(true))
        {
            if (!map.TryGetValue(r, out var nObj) || !(nObj is Renderer nr)) continue;
            var om = r.sharedMaterials; var nm = nr.sharedMaterials;
            if (om.SequenceEqual(nm)) continue;
            if (om.Length != nm.Length) { skipped.Add($"materiales de {r.name} (ahora tiene {nm.Length} huecos, antes {om.Length})"); continue; }
            Undo.RecordObject(nr, "Materiales");
            nr.sharedMaterials = om;
            mats++;
        }

        // 3. Componentes añadidos a mano
        foreach (var o in oldTs)
        {
            bool modelPart = FromModel(o.gameObject);
            if (o != oldVm && !modelPart) continue;            // objeto tuyo: se mueve entero en el paso 4
            if (!map.TryGetValue(o, out var nObj)) { skipped.Add($"componentes de {o.name} (ya no existe en el modelo)"); continue; }
            var nGo = ((Transform)nObj).gameObject;
            foreach (var c in o.GetComponents<Component>())
            {
                if (!c || c is Transform || c is FPSViewModelAnimator) continue;
                if (modelPart && !PrefabUtility.IsAddedComponentOverride(c)) continue;   // viene del FBX
                if (c is Animator) continue;                                              // lo pone el propio montaje
                UnityEditorInternal.ComponentUtility.CopyComponent(c);
                if (UnityEditorInternal.ComponentUtility.PasteComponentAsNew(nGo))
                {
                    map[c] = nGo.GetComponents(c.GetType()).Last();
                    comps++;
                }
                else skipped.Add($"{c.GetType().Name} de {o.name}");
            }
        }

        // 4. Objetos creados a mano (los que cuelgan directamente de algo del modelo o del ViewModel)
        foreach (var o in oldTs)
        {
            if (o == oldVm || FromModel(o.gameObject)) continue;
            var parent = o.parent;
            if (parent != oldVm && !FromModel(parent.gameObject)) continue;   // va dentro de otro objeto tuyo
            var np = Match(parent);
            if (!np) { skipped.Add($"{o.name}: su padre {parent.name} ya no existe, lo cuelgo del ViewModel"); np = newVm; }
            Vector3 lp = o.localPosition, ls = o.localScale; Quaternion lr = o.localRotation;
            Undo.SetTransformParent(o, np, "Mover objeto");
            Undo.RecordObject(o, "Mover objeto");
            o.localPosition = lp; o.localRotation = lr; o.localScale = ls;
            objs++;
        }

        // 5. Referencias que apuntaban al anterior (en los componentes copiados y en el resto de la escena)
        foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c || c is Transform || c.transform.IsChildOf(oldVm)) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                if (!map.TryGetValue(it.objectReferenceValue, out var nv)) continue;
                it.objectReferenceValue = nv; changed = true; refs++;
            }
            if (changed) so.ApplyModifiedProperties();
        }

        Debug.Log($"Montar pistola: conservado del anterior → {mats} renderers con tus materiales, {comps} componentes, " +
                  $"{objs} objetos, {refs} referencias actualizadas.");
        foreach (var s in skipped) Debug.LogWarning("Montar pistola: no he podido conservar " + s);
    }

    static Transform Find(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    static AnimationClip Clip(string fbx, string name) =>
        AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name && !c.name.StartsWith("__preview__"));

    static AnimatorController BuildController(string path, string fbx)
    {
        var idle = Clip(fbx, "Idle");
        var attack = Clip(fbx, "Attack");
        if (!idle || !attack)
        {
            var names = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Select(c => c.name));
            Debug.LogError($"Montar pistola: {fbx} no tiene clips Idle y Attack (tiene: {names}). Reimporta el FBX.");
            return null;
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path)) AssetDatabase.DeleteAsset(path);
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = c.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle");
        sIdle.motion = idle;
        var sAttack = sm.AddState("Attack");
        sAttack.motion = attack;
        sm.defaultState = sIdle;
        BackWhenDone(sAttack, sIdle);

        // Apuntado (opcional). Sin transiciones: FPSViewModelAnimator mueve Aim hacia delante/atrás por script
        // y vuelve de AimAttack al final de Aim.
        var aim = Clip(fbx, "Aim");
        var aimAttack = Clip(fbx, "AimAttack");
        if (aim)
        {
            sm.AddState("Aim").motion = aim;
            if (aimAttack) sm.AddState("AimAttack").motion = aimAttack;
        }
        // Sacar/guardar, recargar y puños (solo brazos): también los mueve FPSViewModelAnimator, sin transiciones
        var found = new System.Collections.Generic.List<string> { "Idle", "Attack" };
        if (aim) found.Add("Aim");
        if (aimAttack) found.Add("AimAttack");
        foreach (var n in new[] { "Draw", "Holster", "Reload", "FistsIdle", "FistsPunch" })
        {
            var clip = Clip(fbx, n);
            if (!clip) continue;
            sm.AddState(n).motion = clip;
            found.Add(n);
        }
        Debug.Log($"Montar pistola: {System.IO.Path.GetFileName(fbx)} → {string.Join(", ", found)}");
        AssetDatabase.SaveAssets();
        return c;
    }

    static AnimatorStateTransition BackWhenDone(AnimatorState from, AnimatorState to)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.hasFixedDuration = true;
        t.duration = 0f;
        return t;
    }

    static Animator SetupAnimator(GameObject go, RuntimeAnimatorController ctrl)
    {
        var an = go.GetComponent<Animator>();
        if (!an) an = Undo.AddComponent<Animator>(go);
        Undo.RecordObject(an, "Animator");
        an.runtimeAnimatorController = ctrl;
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Undo.RecordObject(smr, "Renderer");
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
        }
        return an;
    }

    // Color inicial de cada pieza (el del modelo original). Solo se usa al crear el material.
    static Color PieceColor(string piece)
    {
        if (piece.Contains("Ventana")) return new Color(0.90f, 0.90f, 0.90f);
        if (piece.Contains("Armazon") || piece.Contains("Cargador") || piece.Contains("Guardamonte") || piece.Contains("Gatillo") || piece.Contains("Mira"))
            return new Color(0.24f, 0.24f, 0.24f);
        return new Color(0.40f, 0.40f, 0.40f);
    }

    // Crea el material si no existe. Si ya existe NO lo toca (para no borrar la textura/color que hayas puesto),
    // salvo que se pase una textura fija (los brazos).
    // ───────── Fogonazo ─────────

    /// <summary>
    /// "Fogonazo" colgado del hueso Root de la pistola, en la boca del cañón: un ParticleSystem que suelta 2 destellos
    /// de estrella pixelada (aditivos, ~1/20 s) con una Point Light hija ("Luz") que HitscanWeapon enciende al disparar.
    /// </summary>
    static void CreateMuzzleFlash(HitscanWeapon weapon, GameObject gun, Camera cam)
    {
        var root = Find(gun, "Root");
        if (!root) { Debug.LogWarning("Montar pistola: no encuentro el hueso Root de la pistola, no pongo el fogonazo."); return; }
        Vector3 fwd = cam.transform.forward;

        var go = new GameObject("Fogonazo");
        Undo.RegisterCreatedObjectUndo(go, "Fogonazo");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(FrontPoint(gun, fwd) + fwd * 0.015f, Quaternion.LookRotation(fwd, cam.transform.up));

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.25f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // va pegado al cañón
        main.scalingMode = ParticleSystemScalingMode.Shape;            // el tamaño no depende de la escala del FBX
        main.maxParticles = 8;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
        var shape = ps.shape;
        shape.enabled = false;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = FlashMaterial();
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;

        var lgo = new GameObject("Luz");
        Undo.RegisterCreatedObjectUndo(lgo, "Luz del fogonazo");
        lgo.transform.SetParent(go.transform, false);
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.35f);
        light.range = 4f;
        light.intensity = 3f;
        light.shadows = LightShadows.None;
        light.enabled = false;   // solo se enciende al disparar

        Undo.RecordObject(weapon, "Fogonazo");
        weapon.muzzleParticles = ps;
        weapon.muzzleLight = light;
    }

    // Material aditivo de partículas con una estrella pixelada de 16x16 (se crean una vez en Weapons/Pistol/FX)
    static Material FlashMaterial()
    {
        const string matPath = Dir + "FX/M_Fogonazo.mat", texPath = Dir + "FX/T_Fogonazo.png";
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m) return m;
        if (!AssetDatabase.IsValidFolder(Dir + "FX")) AssetDatabase.CreateFolder(Dir.TrimEnd('/'), "FX");

        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(texPath))
        {
            const int N = 16; float c = (N - 1) / 2f;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c), r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = 1f - r / 4.5f;
                    float cross = Mathf.Max((1f - dy / 1.2f) * (1f - dx / 8f), (1f - dx / 1.2f) * (1f - dy / 8f));
                    float diag = Mathf.Max(1f - Mathf.Abs(dx - dy) / 1.2f, 0f) * (1f - r / 5.5f) * 0.7f;
                    float v = Mathf.Max(core, Mathf.Max(cross, diag));
                    // pocos tonos, estilo PSX: blanco, amarillo, naranja
                    Color col = v > 0.75f ? new Color(1f, 1f, 0.9f, 1f)
                              : v > 0.45f ? new Color(1f, 0.85f, 0.3f, 1f)
                              : v > 0.15f ? new Color(1f, 0.45f, 0.1f, 1f)
                              : new Color(0f, 0f, 0f, 0f);
                    t.SetPixel(x, y, col);
                }
            System.IO.File.WriteAllBytes(texPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath);   // PistolViewModelImport la pone en Point, sin mipmaps
        }

        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!sh) { Debug.LogWarning("Montar pistola: no encuentro el shader de partículas de URP."); return null; }
        m = new Material(sh);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        m.SetColor("_BaseColor", Color.white);
        // transparente aditivo
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, matPath);
        return m;
    }

    // Centro de la cara más adelantada de la pistola (la boca del cañón)
    static Vector3 FrontPoint(GameObject gun, Vector3 fwd)
    {
        var pts = new List<Vector3>();
        foreach (var smr in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = new Mesh();
            smr.BakeMesh(m, true);   // ya incluye la escala: solo falta posición y rotación
            var tr = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (var v in m.vertices) pts.Add(tr.MultiplyPoint3x4(v));
            Object.DestroyImmediate(m);
        }
        if (pts.Count == 0) return gun.transform.position;
        float max = pts.Max(p => Vector3.Dot(p, fwd));
        var front = pts.Where(p => Vector3.Dot(p, fwd) > max - 0.004f).ToList();
        return front.Aggregate(Vector3.zero, (a, b) => a + b) / front.Count;
    }

    // path = ruta completa del .mat. initialTex: textura que se le pone solo al crearlo (con color blanco).
    static Material GetMat(string path, Color color, Texture forcedTex = null, Texture initialTex = null)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            var sh = Shader.Find("PSX/Lit");
            if (!sh) { Debug.LogWarning("Montar arma: no encuentro el shader PSX/Lit, uso el material del FBX."); return null; }
            var folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
            m = new Material(sh);
            if (initialTex && m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", initialTex); color = Color.white; }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
        }
        if (forcedTex && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != forcedTex)
        {
            m.SetTexture("_BaseMap", forcedTex);
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    static void SetMats(Renderer r, System.Func<string, Material> pick)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            var m = pick(mats[i] ? mats[i].name : "");
            if (m) mats[i] = m;
        }
        Undo.RecordObject(r, "Materiales");
        r.sharedMaterials = mats;
    }
}
