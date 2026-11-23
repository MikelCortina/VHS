using UnityEngine;

/// Keeps a straight rope mesh stretched between its own pivot (fixed end, e.g. the lamp)
/// and a target that moves (e.g. the "Cuerda_Ancla" empty under the neck bone).
/// Unity version of Blender's "Stretch To" constraint used on Cuerda_Tramo.
/// Works with whatever axis the mesh was exported along: it measures it on Start.
[ExecuteAlways]
public class RopeStretch : MonoBehaviour
{
    [Tooltip("Moving end of the rope (Cuerda_Ancla).")]
    public Transform target;

    Vector3 restDirWorld;
    Quaternion restRotation;
    Vector3 restScale;
    float restLength;
    int axis;   // local axis the rope runs along (0 = X, 1 = Y, 2 = Z)

    void OnEnable()
    {
        if (target == null) return;
        Vector3 d = target.position - transform.position;
        restLength = d.magnitude;
        restDirWorld = d / Mathf.Max(restLength, 1e-5f);
        restRotation = transform.rotation;
        restScale = transform.localScale;
        Vector3 local = Quaternion.Inverse(transform.rotation) * restDirWorld;
        local = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        axis = local.x > local.y ? (local.x > local.z ? 0 : 2) : (local.y > local.z ? 1 : 2);
    }

    void LateUpdate()
    {
        if (target == null || restLength <= 0f) return;
        Vector3 d = target.position - transform.position;
        float len = d.magnitude;
        if (len < 1e-5f) return;
        transform.rotation = Quaternion.FromToRotation(restDirWorld, d / len) * restRotation;
        Vector3 s = restScale;
        s[axis] = restScale[axis] * len / restLength;
        transform.localScale = s;
    }
}
