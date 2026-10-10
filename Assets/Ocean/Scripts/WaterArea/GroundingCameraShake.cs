// ============================================================
// GroundingCameraShake.cs — Тряска камеры при посадке на мель (B1)
//
// Висит на «Camera Offset» XR-рига (родитель камеры и контроллеров):
// небольшое смещение и поворот этого объекта трясут взгляд вместе с руками,
// трекинг шлема не трогается.
//   • удар — импульс, гаснущий с постоянной τ_уд;
//   • скрежет — пока судно ползёт по грунту или работает машиной на мели,
//     амплитуда ∝ уровню скрежета (0…1, задаёт GroundingMonitor).
// Шум Перлина ≈ 11 Гц. Амплитуды малые (сантиметры, доли градуса) и
// умножаются на comfortScale — против укачивания в VR.
// Смещение добавляется к позиции, которую задают другие скрипты (HeightAdjust,
// XR Origin): если объект изменили извне — новое значение берётся за основу.
// ============================================================
using UnityEngine;

public class GroundingCameraShake : MonoBehaviour
{
    [Tooltip("Трясомый объект; пусто — этот")]
    public Transform target;
    public bool enableShake = true;
    [Range(0f, 1f)] public float comfortScale = 1f;

    [Header("Удар")]
    public float impactAmplitude = 0.03f;        // м
    public float impactRotationDeg = 0.6f;
    public float impactDecay = 0.35f;            // с

    [Header("Скрежет")]
    public float scrapeAmplitude = 0.012f;       // м
    public float scrapeRotationDeg = 0.25f;
    public float frequency = 11f;                // Гц

    /// <summary>Уровень скрежета 0…1 (каждый кадр задаёт GroundingMonitor).</summary>
    public float ScrapeLevel { get; set; }

    private float impact;
    private Vector3 lastOffset, writtenPos;
    private Quaternion lastRot = Quaternion.identity, writtenRot = Quaternion.identity;
    private bool applied;

    private Transform T => target != null ? target : transform;

    /// <summary>Удар силой 0…1.</summary>
    public void Impact(float strength) => impact = Mathf.Max(impact, Mathf.Clamp01(strength));

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        impact *= Mathf.Exp(-dt / Mathf.Max(0.02f, impactDecay));
        if (impact < 1e-3f) impact = 0f;

        float k = enableShake ? comfortScale : 0f;
        float scrape = Mathf.Clamp01(ScrapeLevel);
        float amp = k * (impact * impactAmplitude + scrape * scrapeAmplitude);
        float rotAmp = k * (impact * impactRotationDeg + scrape * scrapeRotationDeg);

        if (amp <= 1e-5f && rotAmp <= 1e-4f)
        {
            if (applied) Apply(Vector3.zero, Quaternion.identity);
            applied = false;
            return;
        }

        float t = Time.time * frequency;
        Vector3 offset = new Vector3(Noise(t, 0.1f), Noise(t, 3.7f), Noise(t, 7.3f)) * amp;
        Quaternion rot = Quaternion.Euler(Noise(t, 11.9f) * rotAmp, 0f, Noise(t, 17.1f) * rotAmp);
        Apply(offset, rot);
        applied = true;
    }

    void OnDisable()
    {
        if (applied) Apply(Vector3.zero, Quaternion.identity);
        applied = false;
    }

    private void Apply(Vector3 offset, Quaternion rot)
    {
        Transform tr = T;
        // Основа — текущее значение без нашей прошлой добавки (если его не меняли извне)
        Vector3 basePos = applied && tr.localPosition == writtenPos ? tr.localPosition - lastOffset : tr.localPosition;
        Quaternion baseRot = applied && tr.localRotation == writtenRot ? tr.localRotation * Quaternion.Inverse(lastRot) : tr.localRotation;
        tr.localPosition = basePos + offset;
        tr.localRotation = baseRot * rot;
        writtenPos = tr.localPosition;
        writtenRot = tr.localRotation;
        lastOffset = offset;
        lastRot = rot;
    }

    private static float Noise(float t, float seed) => Mathf.PerlinNoise(t, seed) * 2f - 1f;
}
