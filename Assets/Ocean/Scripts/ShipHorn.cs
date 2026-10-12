// ============================================================
// ShipHorn.cs — Судовой гудок (процедурный звук)
//
// Генерирует звук сирены/гудка без внешних аудиофайлов.
// Присоединяется к кнопке гудка на мостике.
// Вызов: horn.PlayHorn() — из XR Interactable события.
//
// C4: гудок звучит, ПОКА КНОПКУ ДЕРЖАТ (holdToSound): нажатие (selectEntered → PlayHorn,
// как раньше, в сцене) — начало, отпускание (selectExited → StopHorn, подписка в коде) — конец.
// Так обучающийся подаёт короткий (около 1 с) и продолжительный (4–6 с) звуки правила 32 и
// сигналы из них (правила 34, 35); длительность и тип пишет BridgeActionMonitor (HornEnded,
// SoundSignal). Звук — зацикленный отрезок (HornSynth.Loop) с нарастанием attackSeconds
// и спадом releaseSeconds громкостью источника; тембр и частота — прежние.
// IsSounding — от нажатия до отпускания (спад после отпускания в длительность не входит).
// holdToSound = false — прежний режим: фиксированные duration секунд.
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(AudioSource))]
public class ShipHorn : MonoBehaviour
{
    [Header("Параметры гудка")]
    [Tooltip("Основная частота гудка (Гц). Типичный грузовой судовой гудок: 130-350 Гц")]
    public float frequency = 180f;

    [Tooltip("Длительность звука в секундах (только при выключенном «Звучит, пока держат»)")]
    public float duration = 3f;

    [Tooltip("Громкость (0..1)")]
    [Range(0f, 1f)]
    public float volume = 0.7f;

    [Header("Звучит, пока держат (C4)")]
    [Tooltip("Гудок звучит, пока кнопку держат (правило 32: короткий ≈ 1 с, продолжительный 4–6 с)")]
    public bool holdToSound = true;
    [Tooltip("Нарастание звука, с")]
    public float attackSeconds = 0.12f;
    [Tooltip("Спад после отпускания, с")]
    public float releaseSeconds = 0.3f;
    [Tooltip("Защита: гудок выключается сам через … с (0 — без ограничения)")]
    public float maxBlastSeconds = 30f;

    private AudioSource audioSource;
    private AudioClip hornClip, loopClip;
    private XRBaseInteractable interactable;
    private bool held;
    private float heldTime;
    private float gain;

    /// <summary>Гудок звучит (запись действий C1: начало/конец, длительность).</summary>
    public bool IsSounding => holdToSound ? held : audioSource != null && audioSource.isPlaying;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.spatialBlend = 1f;  // 3D-звук
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.maxDistance = 500f;
        audioSource.playOnAwake = false;

        GenerateHornClip();
        loopClip = HornSynth.Loop(frequency, volume);
        interactable = GetComponent<XRBaseInteractable>();
    }

    void OnEnable()
    {
        if (interactable != null) interactable.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        if (interactable != null) interactable.selectExited.RemoveListener(OnReleased);
        held = false;
        gain = 0f;
        if (audioSource != null && holdToSound) audioSource.Stop();
    }

    private void OnReleased(SelectExitEventArgs args) => StopHorn();

    void Update()
    {
        if (!holdToSound || audioSource == null) return;
        if (held)
        {
            heldTime += Time.deltaTime;
            if (maxBlastSeconds > 0f && heldTime >= maxBlastSeconds) held = false;
        }
        float target = held ? 1f : 0f;
        float rate = held ? 1f / Mathf.Max(attackSeconds, 1e-3f) : 1f / Mathf.Max(releaseSeconds, 1e-3f);
        gain = Mathf.MoveTowards(gain, target, rate * Time.deltaTime);
        audioSource.volume = gain;
        if (!held && gain <= 0f && audioSource.isPlaying) audioSource.Stop();
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
    /// В режиме «пока держат» — начало звука (конец — StopHorn при отпускании).
    /// </summary>
    public void PlayHorn()
    {
        if (holdToSound)
        {
            if (held) return;
            held = true;
            heldTime = 0f;
            if (audioSource.clip != loopClip || !audioSource.isPlaying)
            {
                audioSource.clip = loopClip;
                audioSource.loop = true;
                audioSource.volume = gain;
                audioSource.Play();
            }
            return;
        }
        if (!audioSource.isPlaying)
        {
            audioSource.clip = hornClip;
            audioSource.loop = false;
            audioSource.volume = 1f;
            audioSource.Play();
        }
    }

    /// <summary>
    /// Остановить гудок (если зажимная кнопка). В режиме «пока держат» — отпускание: спад releaseSeconds.
    /// </summary>
    public void StopHorn()
    {
        if (holdToSound)
        {
            held = false;
            return;
        }
        audioSource.Stop();
    }
}
