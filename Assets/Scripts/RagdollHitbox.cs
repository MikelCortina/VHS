using UnityEngine;

/// <summary>
/// Va en cada hueso del ragdoll que tiene collider. Indica cuánto daño hace
/// una bala en esa parte del cuerpo (cabeza x4, piernas x0.75...).
/// Lo crea el menú PSX > Personajes > Montar policías con ragdoll.
/// </summary>
[DisallowMultipleComponent]
public class RagdollHitbox : MonoBehaviour
{
    [Tooltip("Multiplicador de daño de esta parte del cuerpo")]
    public float damageMultiplier = 1f;

    [Tooltip("Cuánto se sacude esta parte al recibir un disparo mientras está vivo (grados)")]
    public float flinchAngle = 12f;

    public RagdollCharacter Owner { get; internal set; }
    public Rigidbody Body { get; internal set; }
}
