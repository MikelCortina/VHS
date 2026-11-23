using UnityEditor;
using UnityEngine;

/// Sets up Models/Cementerio/Cementerio.fbx on import:
/// - MeshColliders on the walkable / solid parts (floor, tombs, walls, gates, lamps)
/// - Balanceo (pendulum) on every "Ahorcado_NN" pivot
/// - everything static except the swinging bodies
public class CementerioImport : AssetPostprocessor
{
    static readonly string[] Solidos = { "Suelo", "Tumbas", "Muros", "Puertas", "Farolas" };

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.EndsWith("Models/Cementerio/Cementerio.fbx")) return;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(Solidos, t.name) >= 0 && t.GetComponent<MeshFilter>())
            {
                t.gameObject.AddComponent<MeshCollider>();
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)~0);
            }
            else if (t.name == "Escombros")
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)~0);
            }
            else if (t.name.StartsWith("Ahorcado_") && t.name.Length == "Ahorcado_00".Length)
            {
                t.gameObject.AddComponent<Balanceo>();
            }
        }
    }
}
