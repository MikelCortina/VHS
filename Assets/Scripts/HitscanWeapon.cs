using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arma de disparo instantáneo (hitscan) como la pistola de Doom o MediEvil.
/// Ponla como hija de la cámara, con el modelo del arma debajo.
/// </summary>
public class HitscanWeapon : MonoBehaviour
{
    [Header("Disparo")]
    public float damage = 25f;
    public float range = 100f;
    public float fireRate = 4f;          // disparos por segundo
    public bool automatic = false;
    public LayerMask hitMask = ~0;

    [Header("Fogonazo (lo crea PSX > Montar pistola animada)")]
    [Tooltip("Partículas en la boca del cañón")]
    public ParticleSystem muzzleParticles;
    [Tooltip("Luz que se enciende un instante al disparar")]
    public Light muzzleLight;
    [Tooltip("Segundos que dura encendida la luz")]
    [Min(0f)] public float muzzleLightTime = 0.05f;

    [Header("Sonido")]
    public AudioSource audioSource;
    public AudioClip shootClip;
    [Range(0f, 1f)] public float shootVolume = 1f;
    [Tooltip("Cambio de tono aleatorio en cada disparo (0.06 = más o menos un 6%), para que no suenen todos iguales")]
    [Range(0f, 0.3f)] public float shootPitchVariation = 0.06f;

    [Header("Animación (brazos + arma)")]
    public FPSViewModelAnimator viewModel; // opcional: reproduce "Attack" al disparar
    [Tooltip("Tecla para sacar y guardar el arma (pistola 1, AK47 3)")]
    public Key equipKey = Key.Digit1;

    [Header("Munición")]
    [Tooltip("Balas por cargador")]
    [Min(1)] public int magazineSize = 12;
    [Tooltip("Tecla para recargar")]
    public Key reloadKey = Key.R;
    [Tooltip("Recargar solo al intentar disparar con el cargador vacío")]
    public bool autoReload = true;
    [Tooltip("Dejar recargar aunque el cargador esté lleno")]
    public bool reloadWhenFull = true;
    [Tooltip("Munición del inventario que gasta al recargar. Vacío = munición infinita")]
    public ItemData ammoItem;
    /// <summary>Balas que quedan en el cargador.</summary>
    public int Ammo { get; private set; }
    Inventory inventory;
    WeaponSwitcher switcher;
    bool UsesInventoryAmmo => ammoItem != null && inventory != null;

    [Header("Apuntar (clic derecho)")]
    public bool canAim = true;
    [Tooltip("FOV al apuntar respecto al normal (0.8 = 20% de zoom, 1 = sin zoom)")]
    [Range(0.3f, 1f)] public float aimFovMultiplier = 0.8f;
    public float aimZoomSpeed = 12f;

    [Header("Retroceso visual")]
    public float recoilKick = 0.08f;
    public float recoilReturn = 12f;

    Camera cam;
    float nextFireTime;
    Vector3 restLocalPos;
    float baseFov;
    float lightOffTime;

    public bool IsAiming { get; private set; }

    void Awake()
    {
        cam = GetComponentInParent<Camera>();
        if (cam) baseFov = cam.fieldOfView;
        restLocalPos = transform.localPosition;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
        if (!audioSource && shootClip) { audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0f; }
        if (muzzleLight) muzzleLight.enabled = false;
        Ammo = magazineSize;
        inventory = GetComponentInParent<Inventory>();
        switcher = GetComponentInParent<WeaponSwitcher>(true);
    }

    void OnEnable() { if (viewModel) viewModel.ReloadFinished += Refill; }
    void OnDisable() { if (viewModel) viewModel.ReloadFinished -= Refill; }

    void Refill()
    {
        if (UsesInventoryAmmo) Ammo += inventory.Take(ammoItem, magazineSize - Ammo);   // solo lo que haya
        else Ammo = magazineSize;
    }

    void Reload()
    {
        if (Ammo >= magazineSize && !reloadWhenFull) return;   // cargador lleno: nada que hacer
        if (UsesInventoryAmmo && inventory.Count(ammoItem) == 0) return;   // sin balas en el inventario
        if (viewModel && viewModel.IsReloading) return;
        // con animación: el cargador se rellena al terminarla (ReloadFinished); sin ella, al momento
        if (!viewModel || !viewModel.PlayReload()) Refill();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // Sacar / guardar el arma
        var kb = Keyboard.current;
        // (con varias armas lo hace WeaponSwitcher, que mira el equipKey de cada una)
        if (viewModel && !switcher && kb != null && locked && kb[equipKey].wasPressedThisFrame) viewModel.ToggleEquip();
        bool inHand = !viewModel || viewModel.CanUseWeapon;   // guardada, a medio sacar o recargando: ni dispara ni apunta

        // Recargar
        if (kb != null && locked && inHand && kb[reloadKey].wasPressedThisFrame) { Reload(); inHand = !viewModel || viewModel.CanUseWeapon; }

        // Apuntar mientras se mantiene el clic derecho
        IsAiming = canAim && inHand && mouse.rightButton.isPressed && locked;
        if (viewModel) viewModel.SetAim(IsAiming);
        if (cam && baseFov > 0f)
        {
            float targetFov = IsAiming ? baseFov * aimFovMultiplier : baseFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * aimZoomSpeed);
        }

        bool trigger = automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame;
        if (trigger && inHand && Time.time >= nextFireTime && locked)
        {
            if (Ammo <= 0) { if (autoReload) Reload(); }
            else
            {
                nextFireTime = Time.time + 1f / fireRate;
                Ammo--;
                Shoot();
            }
        }

        if (muzzleLight && muzzleLight.enabled && Time.time >= lightOffTime) muzzleLight.enabled = false;

        // El arma vuelve poco a poco a su sitio
        transform.localPosition = Vector3.Lerp(transform.localPosition, restLocalPos, Time.deltaTime * recoilReturn);
    }

    void Shoot()
    {
        // Rayo desde el centro de la pantalla
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            // Personaje con ragdoll: daño por zona (cabeza, pecho...) y caída física al morir
            var ragdoll = hit.collider.GetComponentInParent<RagdollCharacter>();
            if (ragdoll) ragdoll.OnBulletHit(hit, ray.direction, damage);
            else
            {
                var health = hit.collider.GetComponentInParent<Health>();
                if (health) health.TakeDamage(damage);

                if (hit.rigidbody) hit.rigidbody.AddForceAtPosition(ray.direction * 5f, hit.point, ForceMode.Impulse);
            }
        }

        if (viewModel) viewModel.PlayShoot();
        transform.localPosition = restLocalPos - Vector3.forward * recoilKick;
        if (audioSource && shootClip)
        {
            audioSource.pitch = 1f + Random.Range(-shootPitchVariation, shootPitchVariation);
            audioSource.PlayOneShot(shootClip, shootVolume);
        }

        // Fogonazo: destello de partículas + luz un instante
        if (muzzleParticles)
        {
            muzzleParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleParticles.Play(true);
        }
        if (muzzleLight)
        {
            muzzleLight.enabled = true;
            lightOffTime = Time.time + muzzleLightTime;
        }
    }
}
