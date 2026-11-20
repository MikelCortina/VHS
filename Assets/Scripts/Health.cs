using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Vida genérica: sirve para enemigos, barriles explosivos o el jugador.
/// </summary>
public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    public UnityEvent onDamaged;
    public UnityEvent onDeath;

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    void Awake() => Current = maxHealth;

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        Current -= amount;
        onDamaged?.Invoke();
        if (IsDead) onDeath?.Invoke();
    }
}
