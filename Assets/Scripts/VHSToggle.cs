using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Enciende/apaga el efecto VHS (shader PSX/VHS) con una tecla (C por defecto).
/// Ponlo en el jugador. Necesita el Full Screen Pass "VHS" en PC_Renderer
/// (menú PSX > Montar efecto VHS (tecla E)).
/// Usa variables globales de shader, así que no modifica el material en Play.
/// </summary>
public class VHSToggle : MonoBehaviour
{
    [Tooltip("Tecla que enciende/apaga el VHS")]
    public Key vhsKey = Key.C;

    [Tooltip("Empezar con el VHS encendido")]
    public bool startOn = false;

    [Tooltip("Segundos del fundido entre imagen normal y VHS")]
    public float fadeTime = 0.2f;

    [Tooltip("Segundos que dura el pico de interferencias al pulsar")]
    public float glitchTime = 0.4f;

    [Header("Sonido (opcional)")]
    public AudioSource audioSource;
    public AudioClip turnOnSound;
    public AudioClip turnOffSound;

    static readonly int AmountId = Shader.PropertyToID("_VHSAmount");
    static readonly int GlitchId = Shader.PropertyToID("_VHSGlitch");

    bool isOn;
    float amount;
    float glitch;

    public bool IsOn => isOn;

    void OnEnable()
    {
        isOn = startOn;
        amount = isOn ? 1f : 0f;
        glitch = 0f;
        Apply();
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(AmountId, 0f);
        Shader.SetGlobalFloat(GlitchId, 0f);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[vhsKey].wasPressedThisFrame)
            SetOn(!isOn);

        // unscaledDeltaTime: funciona aunque el juego esté en pausa (timeScale = 0)
        float dt = Time.unscaledDeltaTime;
        amount = Mathf.MoveTowards(amount, isOn ? 1f : 0f, dt / Mathf.Max(0.0001f, fadeTime));
        glitch = Mathf.MoveTowards(glitch, 0f, dt / Mathf.Max(0.0001f, glitchTime));
        Apply();
    }

    public void SetOn(bool on)
    {
        if (on == isOn) return;
        isOn = on;
        glitch = 1f;

        var clip = on ? turnOnSound : turnOffSound;
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    void Apply()
    {
        Shader.SetGlobalFloat(AmountId, amount);
        Shader.SetGlobalFloat(GlitchId, glitch);
    }
}
