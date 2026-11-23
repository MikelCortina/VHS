using UnityEngine;

/// <summary>
/// Reproduce a la vez la animación del arma y la de los brazos (exportadas con TsitskiExport),
/// para que vayan sincronizadas. HitscanWeapon llama a PlayShoot() al disparar, SetAim() al apuntar
/// ToggleEquip() al pulsar 1 y PlayReload() al recargar.
/// Estados: Idle (bucle), Attack, Aim, AimAttack, Draw (sacar), Holster (guardar), Reload y, solo en los brazos,
/// FistsIdle (puños en guardia, cuando no hay arma).
/// El apuntado lo mueve este script fotograma a fotograma: mientras se apunta, Aim avanza hasta el final
/// y se queda ahí; al soltar, Aim retrocede (la misma animación al revés) y al llegar al principio pasa a Idle.
/// </summary>
public class FPSViewModelAnimator : MonoBehaviour
{
    public enum EquipState { Equipped, Holstering, Unarmed, Drawing, Reloading }

    public Animator[] animators;
    [Tooltip("Animator de los brazos (los puños solo se ponen aquí)")]
    public Animator armsAnimator;
    [Tooltip("Modelo del arma: se oculta cuando está guardada")]
    public GameObject weaponModel;
    [Tooltip("Empezar la partida con el arma en la mano (si no, con los puños)")]
    public bool startEquipped = false;

    [Header("Estados del Animator")]
    public string idleState = "Idle";
    public string shootState = "Attack";
    public string aimState = "Aim";
    public string aimShootState = "AimAttack";
    public string drawState = "Draw";
    public string holsterState = "Holster";
    public string reloadState = "Reload";
    public string fistsState = "FistsIdle";

    [Header("Velocidad (1 = normal, 2 = el doble de rápido, 0.5 = la mitad)")]
    [Min(0f)] public float idleSpeed = 1f;
    [Min(0f)] public float attackSpeed = 1f;
    [Tooltip("Subir el arma al apuntar")]
    [Min(0f)] public float aimSpeed = 1f;
    [Min(0f)] public float aimAttackSpeed = 1f;
    [Tooltip("Bajar el arma al dejar de apuntar (la animación de apuntar al revés)")]
    [Min(0f)] public float aimOutSpeed = 1f;
    [Tooltip("Sacar el arma")]
    [Min(0f)] public float drawSpeed = 1f;
    [Tooltip("Guardar el arma (si no hay animación Holster, es la de sacar al revés)")]
    [Min(0f)] public float holsterSpeed = 1f;
    [Tooltip("Recargar")]
    [Min(0f)] public float reloadSpeed = 1f;
    [Tooltip("Puños en guardia")]
    [Min(0f)] public float fistsSpeed = 1f;

    [Header("Puños")]
    [Tooltip("Segundos de fundido entre las manos fuera de pantalla y los puños (y al revés)")]
    [Min(0f)] public float fistsBlendTime = 0.25f;

    public EquipState State { get; private set; } = EquipState.Equipped;
    /// <summary>true solo con el arma en la mano (ni guardada, ni sacándola, ni guardándola).</summary>
    public bool CanUseWeapon => State == EquipState.Equipped;
    public bool IsAiming { get; private set; }
    public bool IsReloading => State == EquipState.Reloading;
    /// <summary>Se llama al terminar la animación de recarga (HitscanWeapon rellena el cargador ahí).</summary>
    public event System.Action ReloadFinished;
    /// <summary>Cuánto está subida el arma: 0 = abajo (Idle), 1 = apuntando del todo.</summary>
    public float AimAmount => aimT;

    float aimT;            // posición dentro de la animación Aim (0..1)
    bool driving;          // true mientras el script mueve Aim a mano
    float aimLength = -1f; // duración del clip Aim (segundos)
    int playFrame = -1;    // el Animator no cambia de estado hasta su propia actualización: ese frame no se toca

    Animator Arms => armsAnimator ? armsAnimator : (animators != null && animators.Length > 0 ? animators[0] : null);
    bool Has(string state) => Arms && Arms.HasState(0, Animator.StringToHash(state));
    /// <summary>true si estos brazos tienen la animación de puños (se usan cuando no llevas ningún arma).</summary>
    public bool HasFists => Has(fistsState);

    bool initialized;

    void Start()
    {
        if (initialized) return;   // WeaponSwitcher ya lo puso a sacar el arma antes de este Start
        initialized = true;
        if (startEquipped) { State = EquipState.Equipped; SetWeaponVisible(true); }
        else GoUnarmed(0f);
    }

    /// <summary>Sin arma: puños si estos brazos los tienen. Lo usa WeaponSwitcher al volver a activar estos brazos
    /// (el Animator vuelve a su estado por defecto al reactivarse).</summary>
    public void ShowUnarmed()
    {
        initialized = true;
        GoUnarmed(0f);
    }

    // ───────── Sacar / guardar ─────────

    public void ToggleEquip()
    {
        if (State == EquipState.Equipped) Holster();
        else if (State == EquipState.Unarmed) Draw();
        // mientras se saca o se guarda, se ignora
    }

    public void Holster()
    {
        if (State != EquipState.Equipped) return;
        IsAiming = false; driving = false; aimT = 0f;
        State = EquipState.Holstering;
        if (Has(holsterState)) { holsterReversed = false; Play(holsterState, 0f, holsterSpeed); }
        else if (Has(drawState)) { holsterReversed = true; holsterT = 1f; }   // sin Holster: sacar al revés
        else GoUnarmed(fistsBlendTime);
    }

    bool holsterReversed;  // guardar = animación de sacar al revés
    float holsterT;

    public void Draw()
    {
        if (!initialized) { initialized = true; State = EquipState.Unarmed; }   // activado justo ahora por WeaponSwitcher
        if (State != EquipState.Unarmed) return;
        SetWeaponVisible(true);
        if (!Has(drawState)) { State = EquipState.Equipped; Play(idleState, 0f, idleSpeed); return; }
        State = EquipState.Drawing;
        playFrame = Time.frameCount;
        foreach (var a in animators)
        {
            if (!a) continue;
            // los brazos funden desde los puños; el arma (aún fuera de pantalla) empieza directamente.
            // Sin puños (brazos recién activados) no hay nada de lo que fundir: empieza directamente también.
            if (a == Arms && Has(fistsState)) a.CrossFadeInFixedTime(drawState, fistsBlendTime * 0.6f, 0);
            else a.Play(drawState, 0, 0f);
            a.speed = drawSpeed;
        }
    }

    void GoUnarmed(float blend)
    {
        State = EquipState.Unarmed;
        IsAiming = false; driving = false; aimT = 0f;
        SetWeaponVisible(false);
        playFrame = Time.frameCount;
        if (Arms && Has(fistsState))
        {
            if (blend > 0f) Arms.CrossFadeInFixedTime(fistsState, blend, 0);
            else Arms.Play(fistsState, 0, 0f);
            Arms.speed = fistsSpeed;
        }
    }

    void SetWeaponVisible(bool visible)
    {
        if (!weaponModel) return;
        foreach (var r in weaponModel.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    // ───────── Disparo / apuntado ─────────

    public void PlayShoot()
    {
        if (State != EquipState.Equipped) return;
        driving = false;
        if (IsAiming && Has(aimShootState)) Play(aimShootState, 0f, aimAttackSpeed);
        else { aimT = 0f; Play(shootState, 0f, attackSpeed); }
    }

    public void PlayIdle()
    {
        if (State != EquipState.Equipped) return;
        driving = false; aimT = 0f;
        Play(idleState, 0f, idleSpeed);
    }

    public void SetAim(bool aim) => IsAiming = aim && State == EquipState.Equipped;

    /// <summary>Empieza la recarga. Devuelve false si no se puede (sin arma en la mano o sin animación Reload).</summary>
    public bool PlayReload()
    {
        if (State != EquipState.Equipped || !Has(reloadState)) return false;
        IsAiming = false; driving = false; aimT = 0f;
        State = EquipState.Reloading;
        Play(reloadState, 0f, reloadSpeed);
        return true;
    }

    void Play(string state, float normalizedTime, float speed)
    {
        playFrame = Time.frameCount;
        foreach (var a in animators)
            if (a) { a.Play(state, 0, normalizedTime); a.speed = speed; }
    }

    float AimLength()
    {
        if (aimLength <= 0f) aimLength = ClipLength(aimState);
        return aimLength;
    }

    float ClipLength(string clipName)
    {
        var ctrl = Arms ? Arms.runtimeAnimatorController : null;
        if (ctrl)
            foreach (var c in ctrl.animationClips)
                if (c && c.name == clipName) return Mathf.Max(c.length, 0.01f);
        return 1f;
    }

    static AnimatorStateInfo Info(Animator a) =>
        a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);

    void Update()
    {
        if (!Arms || !Arms.isActiveAndEnabled || Time.frameCount == playFrame) return;
        var st = Info(Arms);

        switch (State)
        {
            case EquipState.Holstering:
                if (holsterReversed)
                {
                    holsterT -= Time.deltaTime * holsterSpeed / ClipLength(drawState);
                    if (holsterT <= 0f) GoUnarmed(fistsBlendTime);
                    else Play(drawState, Mathf.Min(holsterT, 0.999f), 0f);
                }
                else if (st.IsName(holsterState) && st.normalizedTime >= 1f) GoUnarmed(fistsBlendTime);
                return;
            case EquipState.Drawing:
                if (st.IsName(drawState) && st.normalizedTime >= 1f) { State = EquipState.Equipped; Play(idleState, 0f, idleSpeed); }
                return;
            case EquipState.Unarmed:
                Arms.speed = fistsSpeed;
                return;
            case EquipState.Reloading:
                if (st.IsName(reloadState) && st.normalizedTime >= 1f)
                {
                    State = EquipState.Equipped;
                    Play(idleState, 0f, idleSpeed);
                    ReloadFinished?.Invoke();
                }
                return;
        }

        if (!Has(aimState)) return;

        // ¿Disparando? Se deja terminar y luego se sigue con el apuntado
        if (!driving && st.IsName(aimShootState))
        {
            if (st.normalizedTime < 1f) return;
            aimT = 1f; driving = true;            // tras disparar apuntando: arma arriba (o bajando si se soltó)
        }
        else if (!driving && st.IsName(shootState))
        {
            return;                               // al acabar, el controller vuelve a Idle
        }

        // Empezar a subir desde Idle
        if (!driving)
        {
            if (!IsAiming) return;                // Idle normal
            aimT = 0f; driving = true;
        }

        // Mover el arma por la animación Aim: hacia delante al apuntar, hacia atrás al soltar
        float len = AimLength();
        aimT = IsAiming
            ? Mathf.Min(1f, aimT + Time.deltaTime * aimSpeed / len)
            : Mathf.Max(0f, aimT - Time.deltaTime * aimOutSpeed / len);

        if (!IsAiming && aimT <= 0f)
        {
            driving = false;
            Play(idleState, 0f, idleSpeed);
            return;
        }
        // speed 0: el Animator solo pinta la pose del tiempo que le damos
        Play(aimState, Mathf.Min(aimT, 0.999f), 0f);
    }
}
