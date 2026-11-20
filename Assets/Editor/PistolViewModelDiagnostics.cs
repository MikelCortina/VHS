using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PSX > Diagnóstico brazos: muestrea cada animación de "Arms Pistol.fbx" sobre una copia temporal de los brazos
/// y escribe en la consola qué huesos encuentra y dónde quedan las manos. No cambia la escena.
/// </summary>
public static class PistolViewModelDiagnostics
{
    [MenuItem("PSX/Diagnóstico brazos")]
    static void Run()
    {
        const string fbx = "Assets/Weapons/Pistol/Arms Pistol.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var all = go.GetComponentsInChildren<Transform>(true);
            string Path(Transform t) => t == go.transform ? "" : AnimationUtility.CalculateTransformPath(t, go.transform);
            var paths = new System.Collections.Generic.HashSet<string>(all.Select(Path));
            var handR = all.FirstOrDefault(t => t.name == "hand.R" || t.name == "DEF-hand.R" || t.name.EndsWith("hand.R"));
            var finger = all.FirstOrDefault(t => t.name.Contains("f_index.02.R") || t.name.Contains("index") && t.name.EndsWith(".R"));
            Debug.Log($"[Diag] brazos: {all.Length} transforms, mano={(handR ? Path(handR) : "NO")} dedo={(finger ? Path(finger) : "NO")}");

            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            {
                var b = AnimationUtility.GetCurveBindings(clip);
                int missing = b.Count(x => !paths.Contains(x.path));
                clip.SampleAnimation(go, clip.length);
                string hand = handR ? handR.position.ToString("F3") : "-";
                string fin = finger ? finger.localEulerAngles.ToString("F0") : "-";
                Debug.Log($"[Diag] {clip.name}: largo={clip.length:F3}s curvas={b.Length} sin destino={missing} mano.R(final)={hand} dedo={fin}"
                          + (missing > 0 ? " ej: " + b.First(x => !paths.Contains(x.path)).path : ""));
            }
        }
        finally { Object.DestroyImmediate(go); }
    }
}
