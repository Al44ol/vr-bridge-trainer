// ============================================================
// ShipHorn.cs — Судовой гудок (процедурный звук)
//
// Генерирует звук сирены/гудка без внешних аудиофайлов.
// Присоединяется к кнопке гудка на мостике.
// Вызов: horn.PlayHorn() — из XR Interactable события.
// ============================================================
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ShipHorn : MonoBehaviour
{
    [Header("Параметры гудка")]
    [Tooltip("Основная частота гудка (Гц). Типичный грузовой судовой гудок: 130-350 Гц")]
    public float frequency = 180f;

    [Tooltip("Длительность звука в секундах")]
    public float duration = 3f;

    [Tooltip("Громкость (0..1)")]
    [Range(0f, 1f)]
    public float volume = 0.7f;

    private AudioSource audioSource;
    private AudioClip hornClip;

    /// <summary>Гудок звучит (запись действий C1: начало/конец, длительность).</summary>
    public bool IsSounding => audioSource != null && audioSource.isPlaying;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.spatialBlend = 1f;  // 3D-звук
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.maxDistance = 500f;
        audioSource.playOnAwake = false;

        GenerateHornClip();
    }

    /// <summary>
    /// Генерация процедурного звука гудка.
    /// Сумма двух частот + огибающая (атака, сустейн, затухание).
    /// </summary>
    void GenerateHornClip()
    {
        int sampleRate = 44100;
        int totalSamples = (int)(sampleRate * duration);
        float[] samples = new float[totalSamples];

        float freq1 = frequency;
        float freq2 = frequency * 1.5f;   // квинта — характерный судовой тембр
        float freq3 = frequency * 0.5f;   // суб-бас

        float attackTime = 0.4f;
        float releaseTime = 0.8f;

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / sampleRate;
            float normalizedT = (float)i / totalSamples;

            // Огибающая: плавная атака и затухание
            float envelope = 1f;
            if (t < attackTime)
                envelope = t / attackTime;
            else if (normalizedT > (1f - releaseTime / duration))
                envelope = (1f - normalizedT) / (releaseTime / duration);
            envelope = Mathf.Clamp01(envelope);

            // Звук: сумма синусоид + небольшой шум для реалистичности
            float sample = 0f;
            sample += Mathf.Sin(2f * Mathf.PI * freq1 * t) * 0.5f;
            sample += Mathf.Sin(2f * Mathf.PI * freq2 * t) * 0.25f;
            sample += Mathf.Sin(2f * Mathf.PI * freq3 * t) * 0.15f;

            // Лёгкая вибрация (тремоло) — гудок не идеально ровный
            float tremolo = 1f + Mathf.Sin(2f * Mathf.PI * 5f * t) * 0.05f;

            samples[i] = sample * envelope * tremolo * volume;
        }

        hornClip = AudioClip.Create("ShipHorn", totalSamples, 1, sampleRate, false);
        hornClip.SetData(samples, 0);
        audioSource.clip = hornClip;
    }

    /// <summary>
    /// Воспроизвести гудок. Вызывается из XR Interactable или кнопки.
    /// </summary>
    public void PlayHorn()
    {
        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    /// <summary>
    /// Остановить гудок (если зажимная кнопка).
    /// </summary>
    public void StopHorn()
    {
        audioSource.Stop();
    }
}
