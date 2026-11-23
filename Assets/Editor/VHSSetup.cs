using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menú PSX > Montar efecto VHS (tecla C):
///  1. Crea Assets/Materials/VHS_PostProcess.mat con el shader PSX/VHS.
///  2. Añade un Full Screen Pass "VHS" al final de PC_Renderer (después del PSX PostProcess).
///  3. Añade VHSToggle al jugador (FPSController) de la escena abierta. La escena NO se guarda: Ctrl+S.
/// Se puede ejecutar varias veces sin duplicar nada.
/// </summary>
public static class VHSSetup
{
    const string MatPath = "Assets/Materials/VHS_PostProcess.mat";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string FeatureName = "VHS";

    [MenuItem("PSX/Montar efecto VHS (tecla C)")]
    public static void Setup()
    {
        var shader = Shader.Find("PSX/VHS");
        if (shader == null) { Debug.LogError("VHSSetup: no encuentro el shader PSX/VHS"); return; }

        // ---- 1. Material ----
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "VHS_PostProcess" };
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        else if (mat.shader != shader) mat.shader = shader;

        // ---- 2. Renderer feature ----
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (data == null) { Debug.LogError("VHSSetup: no encuentro " + RendererPath); return; }

        FullScreenPassRendererFeature feature = null;
        foreach (var f in data.rendererFeatures)
            if (f is FullScreenPassRendererFeature fs && f.name == FeatureName) feature = fs;

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = FeatureName;
            feature.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.SaveAssetIfDirty(data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        feature.fetchColorBuffer = true;
        feature.requirements = ScriptableRenderPassInput.Color;
        feature.passMaterial = mat;
        feature.passIndex = 0;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        data.SetDirty();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // ---- 3. Script en el jugador ----
        var player = Object.FindAnyObjectByType<FPSController>();
        GameObject target = player != null ? player.gameObject
                          : (Camera.main != null ? Camera.main.gameObject : null);
        if (target == null)
        {
            Debug.LogWarning("VHSSetup: no hay FPSController ni Main Camera en la escena; añade VHSToggle a mano.");
        }
        else if (target.GetComponent<VHSToggle>() == null)
        {
            Undo.AddComponent<VHSToggle>(target);
            EditorSceneManager.MarkSceneDirty(target.scene);
            Debug.Log($"VHSSetup: VHSToggle añadido a '{target.name}'. Guarda la escena (Ctrl+S).", target);
        }

        Selection.activeObject = mat;
        Debug.Log("VHSSetup: listo. Dale a Play y pulsa C. Ajustes del efecto en " + MatPath, mat);
    }
}
