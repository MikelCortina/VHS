using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menú PSX > Crear escena de pruebas (plano vacío).
/// Copia PSX_Test.unity a PSX_Pruebas.unity (así conserva niebla, luz ambiente,
/// jugador, cámara, arma y VHS) y borra todo el nivel: solo deja el jugador,
/// las luces direccionales y un suelo plano de 100x100 m con M_Suelo.
/// Nunca sobrescribe: cada ejecución crea una escena nueva (PSX_Pruebas, PSX_Pruebas 1...).
/// </summary>
public static class PSXTestPlaneScene
{
    const string SourcePath = "Assets/Scenes/PSX_Test.unity";
    const string TargetPath = "Assets/Scenes/PSX_Pruebas.unity";
    const string FloorMatPath = "Assets/Materials/M_Suelo.mat";

    [MenuItem("PSX/Crear escena de pruebas (plano vacío)")]
    static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(SourcePath)) { Debug.LogError("PSXTestPlaneScene: no existe " + SourcePath); return; }
        // Nunca borra ni sobrescribe: si PSX_Pruebas ya existe crea "PSX_Pruebas 1", etc.
        string targetPath = AssetDatabase.GenerateUniqueAssetPath(TargetPath);
        if (!EditorUtility.DisplayDialog("PSX",
                "Se creará una escena NUEVA en:\n" + targetPath + "\n\nPSX_Test no se modifica.",
                "Crear", "Cancelar"))
            return;
        if (!AssetDatabase.CopyAsset(SourcePath, targetPath)) { Debug.LogError("PSXTestPlaneScene: no se pudo copiar la escena"); return; }

        var scene = EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);

        // ---- Quitar el nivel: dejar solo jugador y luces direccionales ----
        GameObject player = null;
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<FPSController>(true) != null) { player = root; continue; }
            if (IsDirectionalLightRig(root)) continue;
            Object.DestroyImmediate(root);
            removed++;
        }

        // ---- Suelo ----
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Suelo";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(10f, 1f, 10f);   // 100 x 100 m (M_Suelo tiene tiling 10x10)
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMatPath);
        if (mat != null) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        else Debug.LogWarning("PSXTestPlaneScene: no encuentro " + FloorMatPath + "; el suelo usa el material por defecto.");

        // ---- Colocar al jugador en el centro, de pie sobre el suelo ----
        if (player != null)
        {
            var fps = player.GetComponentInChildren<FPSController>(true);
            var cc = fps.GetComponent<CharacterController>();
            float y = cc != null ? cc.height * 0.5f - cc.center.y + 0.05f : 1f;
            var t = fps.transform;
            Vector3 delta = new Vector3(0f, y, 0f) - t.position;
            player.transform.position += delta;
        }
        else Debug.LogWarning("PSXTestPlaneScene: PSX_Test no tiene jugador con FPSController.");

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"PSXTestPlaneScene: creada {targetPath} ({removed} objetos del nivel quitados).");
    }

    // Raíz que solo contiene luces direccionales (p. ej. "Luz direccional").
    static bool IsDirectionalLightRig(GameObject root)
    {
        var lights = root.GetComponentsInChildren<Light>(true);
        if (lights.Length == 0) return false;
        foreach (var l in lights) if (l.type != LightType.Directional) return false;
        return root.GetComponentsInChildren<Renderer>(true).Length == 0;
    }
}
