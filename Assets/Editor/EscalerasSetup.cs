using UnityEditor;
using UnityEngine;

/// <summary>
/// PSX > Escaleras > Hacer escalable la selección: añade el componente Escalera a cada objeto seleccionado
/// (el objeto raíz de la escalera, con sus modelos como hijos) y ajusta su zona de agarre.
/// Si lo vuelves a usar sobre una escalera que ya lo tiene, solo recalcula la zona (p. ej. tras moverle modelos).
/// </summary>
public static class EscalerasSetup
{
    [MenuItem("PSX/Escaleras/Hacer escalable la selección")]
    static void HacerEscalable()
    {
        int n = 0;
        foreach (var go in Selection.gameObjects)
        {
            var e = go.GetComponent<Escalera>();
            if (e == null) e = Undo.AddComponent<Escalera>(go);
            var box = go.GetComponent<BoxCollider>();
            if (box != null) Undo.RecordObject(box, "Ajustar escalera");
            e.Ajustar();
            EditorUtility.SetDirty(go);
            n++;
        }
        Debug.Log($"[Escaleras] {n} escalera(s) lista(s). Guarda la escena para conservarlo.");
    }

    [MenuItem("PSX/Escaleras/Hacer escalable la selección", true)]
    static bool Validar() => Selection.gameObjects.Length > 0;
}
