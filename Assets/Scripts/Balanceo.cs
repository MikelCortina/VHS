using UnityEngine;

/// Slow pendulum swing for the hanged bodies of the cemetery.
/// Put it on the pivot (the "Ahorcado_NN" node, whose origin is the top of the rope):
/// the body, noose and straight rope are its children and swing together.
public class Balanceo : MonoBehaviour
{
    [Tooltip("Maximum angle in degrees.")]
    public float amplitud = 4f;
    [Tooltip("Seconds per full swing.")]
    public float periodo = 4.5f;
    [Tooltip("Slow turn around the rope, in degrees.")]
    public float giro = 6f;

    Quaternion baseRot;
    float fase, dirAngle, periodoReal;

    void Start()
    {
        baseRot = transform.localRotation;
        // every body swings a little differently
        var r = new System.Random(transform.position.GetHashCode());
        fase = (float)r.NextDouble() * Mathf.PI * 2f;
        dirAngle = (float)r.NextDouble() * 360f;
        periodoReal = periodo * (0.85f + 0.3f * (float)r.NextDouble());
    }

    void Update()
    {
        float t = Time.time * Mathf.PI * 2f / periodoReal + fase;
        Vector3 eje = Quaternion.Euler(0f, dirAngle, 0f) * Vector3.right;
        Quaternion swing = Quaternion.AngleAxis(Mathf.Sin(t) * amplitud, eje);
        Quaternion twist = Quaternion.AngleAxis(Mathf.Sin(t * 0.37f) * giro, Vector3.up);
        transform.localRotation = baseRot * swing * twist;
    }
}
