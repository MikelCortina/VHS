using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cambio entre varias armas (cada una un HitscanWeapon con su ViewModel de brazos + arma, hijos de la cámara).
/// Cada arma se saca con su propia tecla (HitscanWeapon.equipKey: pistola 1, AK47 3).
///  - Sin arma: se ven los puños de "fistsWeapon" (la pistola, cuyos brazos tienen los puños).
///  - Tecla del arma que llevas: la guarda (Hide) y vuelven los puños.
///  - Tecla de otra arma: guarda la actual y, al terminar, saca la nueva (Equip).
/// Solo está activa (GameObject) el arma que se usa, así que solo ella dispara, apunta y recarga.
/// Lo crea y rellena PSX > Montar AK47 / Montar pistola animada.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    [Tooltip("Armas, cada una con su tecla en HitscanWeapon > Equip Key")]
    public HitscanWeapon[] weapons;
    [Tooltip("Arma cuyos brazos tienen los puños: se ve (sin arma) cuando no llevas nada. Vacío = la primera que tenga puños")]
    public HitscanWeapon fistsWeapon;

    /// <summary>Índice del arma en la mano (o sacándose / guardándose); -1 = ninguna.</summary>
    public int Current { get; private set; } = -1;
    public HitscanWeapon CurrentWeapon => Current >= 0 ? weapons[Current] : null;

    const int None = -2;
    int pending = None;   // arma a sacar cuando termine de guardarse la actual (-1 = quedarse sin arma)

    static FPSViewModelAnimator VM(HitscanWeapon w) => w ? w.viewModel : null;

    void Start()
    {
        if (!fistsWeapon)
            foreach (var w in weapons)
                if (VM(w) && VM(w).HasFists) { fistsWeapon = w; break; }

        foreach (var w in weapons)
            if (w) w.gameObject.SetActive(w == fistsWeapon);
        Current = -1;
        // los brazos de los puños empiezan sin arma por su cuenta (FPSViewModelAnimator.startEquipped = false)
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && Cursor.lockState == CursorLockMode.Locked)
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] && kb[weapons[i].equipKey].wasPressedThisFrame) { Request(i); break; }

        // ¿Terminó de guardarse la actual?
        if (pending != None && Current >= 0)
        {
            var vm = VM(weapons[Current]);
            if (!vm || vm.State == FPSViewModelAnimator.EquipState.Unarmed)
            {
                int old = Current;
                Current = -1;
                if (weapons[old] != fistsWeapon) { weapons[old].gameObject.SetActive(false); ShowFists(); }
            }
        }
        if (pending != None && Current == -1)
        {
            int target = pending;
            pending = None;
            if (target >= 0) DrawWeapon(target);
        }
    }

    void Request(int i)
    {
        if (pending != None) return;                                   // ya hay un cambio en marcha
        if (Current >= 0)
        {
            var vm = VM(weapons[Current]);
            if (vm && vm.State != FPSViewModelAnimator.EquipState.Equipped) return;   // sacándola, guardándola o recargando
            pending = i == Current ? -1 : i;
            if (vm) vm.Holster();
            return;
        }
        if (fistsWeapon && VM(fistsWeapon) && VM(fistsWeapon).State != FPSViewModelAnimator.EquipState.Unarmed
            && fistsWeapon.gameObject.activeSelf) return;            // los puños aún están terminando de aparecer
        DrawWeapon(i);
    }

    void DrawWeapon(int i)
    {
        var w = weapons[i];
        if (fistsWeapon && w != fistsWeapon) fistsWeapon.gameObject.SetActive(false);
        w.gameObject.SetActive(true);
        if (VM(w)) VM(w).Draw();
        Current = i;
    }

    void ShowFists()
    {
        if (!fistsWeapon) return;
        fistsWeapon.gameObject.SetActive(true);
        if (VM(fistsWeapon)) VM(fistsWeapon).ShowUnarmed();
    }
}
