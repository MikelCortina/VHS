using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Personaje con ragdoll que recibe balas.
///
/// Vivo: todos los Rigidbody del esqueleto están en modo cinemático (siguen la pose o la
/// animación) y sus colliders hacen de "hitbox". Cada bala resta vida (con multiplicador
/// por zona) y da una pequeña sacudida a la parte alcanzada.
/// Muerto: se apaga el Animator y los Rigidbody pasan a ser físicos. Se conserva la
/// velocidad que llevaba cada hueso y se aplica el impulso de la bala en el punto de
/// impacto, así cae de forma creíble (se dobla por donde le han dado).
///
/// Lo monta el menú PSX > Personajes > Montar policías con ragdoll.
/// </summary>
[RequireComponent(typeof(Health))]
public class RagdollCharacter : MonoBehaviour
{
    [Header("Referencias (se rellenan solas)")]
    public Animator animator;
    public Rigidbody hips;

    [Header("Impacto de bala")]
    [Tooltip("Impulso (N·s) que la bala aplica a la parte del cuerpo alcanzada al morir")]
    public float bulletImpulse = 9f;
    [Tooltip("Velocidad (m/s) que se suma a todo el cuerpo en la dirección del disparo al morir")]
    public float bodyPush = 0.6f;
    [Tooltip("Velocidad hacia abajo (m/s) al morir: las piernas ceden en vez de caer como un tablón")]
    public float collapseSpeed = 1.6f;
    [Tooltip("Rotación aleatoria (rad/s) al morir para que no caigan todos igual")]
    public float randomSpin = 0.6f;
    [Tooltip("Al morir las rodillas ceden hacia delante (m/s): se desploma en vez de caer recto")]
    public float kneeBuckle = 2f;
    public Rigidbody[] thighs;
    public Rigidbody[] shins;

    [Header("Sacudida al recibir balas (vivo)")]
    public bool flinch = true;
    [Tooltip("Segundos que tarda en recuperarse de la sacudida")]
    public float flinchRecover = 0.25f;

    [Header("Rendimiento")]
    [Tooltip("Cuando el cuerpo lleva un rato quieto se congela (deja de gastar física)")]
    public bool freezeWhenSettled = true;
    public float settleSpeed = 0.05f;
    public float settleTime = 2f;

    public bool IsRagdoll { get; private set; }

    Health health;
    readonly List<Rigidbody> bodies = new();
    readonly Dictionary<Rigidbody, Vector3> lastPos = new();
    readonly Dictionary<Rigidbody, Vector3> velocity = new();

    // sacudida
    readonly Dictionary<Transform, Quaternion> flinchOffset = new();
    readonly Dictionary<Transform, Quaternion> restLocal = new();

    // último impacto
    Rigidbody pendingBody;
    Vector3 pendingPoint, pendingDir;
    bool hasPendingHit;
    float stillTimer;

    void Awake()
    {
        health = GetComponent<Health>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            bodies.Add(rb);
            restLocal[rb.transform] = rb.transform.localRotation;
            var hb = rb.GetComponent<RagdollHitbox>();
            if (!hb) hb = rb.gameObject.AddComponent<RagdollHitbox>();
            hb.Owner = this;
            hb.Body = rb;
        }
        if (!hips && bodies.Count > 0) hips = bodies[0];

        IgnoreTouchingParts();
        SetRagdoll(false);
        health.onDeath.AddListener(EnableRagdoll);
    }

    /// <summary>
    /// Partes del cuerpo que ya se tocan en reposo (muslos entre sí, antebrazos con la cadera...)
    /// no deben chocar entre ellas: PhysX las separaría de golpe y el cuerpo saldría disparado.
    /// </summary>
    void IgnoreTouchingParts()
    {
        var cols = new List<Collider>();
        foreach (var rb in bodies)
            foreach (var c in rb.GetComponentsInChildren<Collider>())
                if (c.attachedRigidbody == rb) cols.Add(c);

        const float margin = 0.02f;   // también las que están casi tocándose
        for (int i = 0; i < cols.Count; i++)
            for (int j = i + 1; j < cols.Count; j++)
            {
                Collider a = cols[i], b = cols[j];
                if (a.attachedRigidbody == b.attachedRigidbody) continue;
                if (!a.bounds.Intersects(new Bounds(b.bounds.center, b.bounds.size + Vector3.one * margin * 2f))) continue;
                bool touching = Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                                           b, b.transform.position, b.transform.rotation, out _, out _);
                if (!touching)
                {
                    // ¿casi? prueba desplazando un poco una contra otra
                    Vector3 toB = (b.bounds.center - a.bounds.center).normalized * margin;
                    touching = Physics.ComputePenetration(a, a.transform.position + toB, a.transform.rotation,
                                                          b, b.transform.position, b.transform.rotation, out _, out _);
                }
                if (touching) Physics.IgnoreCollision(a, b, true);
            }
    }

    void OnDestroy()
    {
        if (health) health.onDeath.RemoveListener(EnableRagdoll);
    }

    /// <summary>
    /// Llama a esto desde el arma. Aplica el daño (con el multiplicador de la zona)
    /// y, si muere, el impulso de la bala.
    /// </summary>
    public void OnBulletHit(RaycastHit hit, Vector3 direction, float damage)
    {
        var hb = hit.collider.GetComponentInParent<RagdollHitbox>();
        var body = hb ? hb.Body : hit.rigidbody;
        float mult = hb ? hb.damageMultiplier : 1f;

        pendingBody = body;
        pendingPoint = hit.point;
        pendingDir = direction.normalized;
        hasPendingHit = true;

        if (IsRagdoll)
        {
            // ya está en el suelo: la bala solo empuja el cuerpo (y lo descongela si hacía falta)
            if (body && body.isKinematic) { foreach (var rb in bodies) rb.isKinematic = false; stillTimer = 0f; }
            if (body) body.AddForceAtPosition(pendingDir * bulletImpulse * 0.5f, hit.point, ForceMode.Impulse);
            return;
        }

        health.TakeDamage(damage * mult);           // si muere, Health llama a EnableRagdoll

        if (!IsRagdoll && flinch && hb && body) AddFlinch(body.transform, pendingDir, hb.flinchAngle);
    }

    /// <summary>Pasa a ragdoll (también lo llama Health.onDeath).</summary>
    public void EnableRagdoll()
    {
        if (IsRagdoll) return;
        flinchOffset.Clear();
        SetRagdoll(true);

        foreach (var rb in bodies)
        {
            // conserva el movimiento que traía (animación, sacudida...)
            velocity.TryGetValue(rb, out var v);
            // el tronco cae, los pies se quedan en el suelo: las piernas tienen que doblarse
            float down = System.Array.IndexOf(shins ?? new Rigidbody[0], rb) >= 0 ? 0f
                       : System.Array.IndexOf(thighs ?? new Rigidbody[0], rb) >= 0 ? collapseSpeed * 0.5f
                       : collapseSpeed;
            rb.linearVelocity = v + pendingDir * bodyPush + Vector3.down * down;
            rb.angularVelocity = Random.insideUnitSphere * randomSpin;
        }

        // rodillas hacia delante y pies hacia atrás: las piernas se doblan
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (thighs != null) foreach (var rb in thighs) if (rb) rb.linearVelocity += fwd * kneeBuckle * 0.5f;
        if (shins != null) foreach (var rb in shins) if (rb) rb.linearVelocity -= fwd * kneeBuckle;

        if (hasPendingHit && pendingBody)
            pendingBody.AddForceAtPosition(pendingDir * bulletImpulse, pendingPoint, ForceMode.Impulse);
        hasPendingHit = false;
        stillTimer = 0f;
    }

    /// <summary>Vuelve a dejarlo cinemático (p. ej. para reutilizarlo).</summary>
    public void DisableRagdoll() => SetRagdoll(false);

    void SetRagdoll(bool on)
    {
        IsRagdoll = on;
        if (animator) animator.enabled = !on;
        foreach (var rb in bodies)
        {
            rb.isKinematic = !on;
            rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (on) rb.WakeUp();
        }
        // otros colliders del personaje (cápsula de movimiento...) estorbarían al caer
        foreach (var c in GetComponents<Collider>()) c.enabled = !on;
        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = !on;
    }

    void AddFlinch(Transform bone, Vector3 dir, float angle)
    {
        // gira el hueso alcanzado (y un poco su padre) como si la bala lo empujara
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.Cross(bone.forward, dir);
        axis.Normalize();
        Push(bone, axis, angle);
        var parentBody = bone.parent ? bone.parent.GetComponentInParent<Rigidbody>() : null;
        if (parentBody && parentBody.transform.IsChildOf(transform)) Push(parentBody.transform, axis, angle * 0.4f);
    }

    void Push(Transform t, Vector3 worldAxis, float angle)
    {
        Vector3 localAxis = t.parent ? t.parent.InverseTransformDirection(worldAxis) : worldAxis;
        var q = Quaternion.AngleAxis(angle, localAxis);
        flinchOffset[t] = flinchOffset.TryGetValue(t, out var cur) ? q * cur : q;
    }

    void LateUpdate()
    {
        if (IsRagdoll || flinchOffset.Count == 0) return;
        bool animated = animator && animator.enabled && animator.runtimeAnimatorController;
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, flinchRecover));
        var keys = new List<Transform>(flinchOffset.Keys);
        foreach (var t in keys)
        {
            var off = Quaternion.Slerp(flinchOffset[t], Quaternion.identity, k);
            flinchOffset[t] = off;
            Quaternion baseRot = animated ? t.localRotation : restLocal[t];
            t.localRotation = off * baseRot;
            if (Quaternion.Angle(off, Quaternion.identity) < 0.1f)
            {
                if (!animated) t.localRotation = restLocal[t];
                flinchOffset.Remove(t);
            }
        }
    }

    void FixedUpdate()
    {
        if (!IsRagdoll)
        {
            // velocidad de cada hueso mientras es cinemático, para heredarla al morir
            float dt = Time.fixedDeltaTime;
            foreach (var rb in bodies)
            {
                var p = rb.transform.position;
                velocity[rb] = lastPos.TryGetValue(rb, out var lp) ? (p - lp) / dt : Vector3.zero;
                lastPos[rb] = p;
            }
            return;
        }

        if (!freezeWhenSettled || !hips || hips.isKinematic) return;
        bool still = true;
        foreach (var rb in bodies)
            if (rb.linearVelocity.sqrMagnitude > settleSpeed * settleSpeed) { still = false; break; }
        stillTimer = still ? stillTimer + Time.fixedDeltaTime : 0f;
        if (stillTimer > settleTime)
            foreach (var rb in bodies) rb.isKinematic = true;   // queda tirado tal cual
    }
}
