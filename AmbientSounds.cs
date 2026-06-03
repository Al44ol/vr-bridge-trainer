// ============================================================
// AmbientSounds.cs — Фоновые звуки (процедурные)
// Волны, ветер, двигатель — громкость зависит от погоды.
// Повесить на объект Ship или отдельный AmbientAudio.
// ============================================================
using UnityEngine;

public class AmbientSounds : MonoBehaviour
{
    [Header("Ссылка на качку (для чтения seaState)")]
    public ShipMotion shipMotion;

    [Header("Громкость")]
    [Range(0f, 1f)] public float masterVolume = 0.4f;
    [Range(0f, 1f)] public float wavesVolume = 0.5f;
    [Range(0f, 1f)] public float windVolume = 0.3f;
    [Range(0f, 1f)] public float engineVolume = 0.2f;

    private AudioSource wavesSource;
    private AudioSource windSource;
    private AudioSource engineSource;

    void Start()
    {
        wavesSource = CreateSource("Waves", GenerateWavesClip(), true);
        windSource = CreateSource("Wind", GenerateWindClip(), true);
        engineSource = CreateSource("Engine", GenerateEngineClip(), true);
    }

    void Update()
    {
        float sea = shipMotion != null ? shipMotion.seaState : 0.3f;

        // Волны: громче при шторме
        wavesSource.volume = masterVolume * wavesVolume * Mathf.Lerp(0.2f, 1f, sea);
        wavesSource.pitch = Mathf.Lerp(0.8f, 1.3f, sea);

        // Ветер: появляется при волнении
        windSource.volume = masterVolume * windVolume * Mathf.Lerp(0f, 1f, sea);
        windSource.pitch = Mathf.Lerp(0.7f, 1.5f, sea);

        // Двигатель: постоянный низкий гул
        engineSource.volume = masterVolume * engineVolume;
    }

    AudioSource CreateSource(string name, AudioClip clip, bool loop)
    {
        GameObject go = new GameObject("Sound_" + name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.spatialBlend = 0f; // 2D — фоновый звук, одинаковый везде
        src.playOnAwake = false;
        src.Play();
        return src;
    }

    // ─── Процедурная генерация звуков ───

    AudioClip GenerateWavesClip()
    {
        // Фильтрованный шум с медленной модуляцией = звук прибоя
        int rate = 22050;
        int len = rate * 8; // 8 секунд, зацикливается
        float[] data = new float[len];
        float filtered = 0f;

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float noise = Random.Range(-1f, 1f);

            // Низкочастотный фильтр
            filtered = filtered * 0.98f + noise * 0.02f;

            // Медленная модуляция (имитация набегающих волн)
            float mod = (Mathf.Sin(t * 0.8f) * 0.5f + 0.5f) *
                        (Mathf.Sin(t * 0.3f + 1f) * 0.3f + 0.7f);

            data[i] = filtered * mod * 0.6f;
        }

        // Плавное зацикливание (crossfade концов)
        int fade = rate; // 1 секунда
        for (int i = 0; i < fade; i++)
        {
            float f = (float)i / fade;
            data[i] = data[i] * f + data[len - fade + i] * (1f - f);
        }

        AudioClip clip = AudioClip.Create("Waves", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateWindClip()
    {
        // Более высокочастотный шум = ветер
        int rate = 22050;
        int len = rate * 6;
        float[] data = new float[len];
        float filtered = 0f;

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float noise = Random.Range(-1f, 1f);
            filtered = filtered * 0.92f + noise * 0.08f;

            float mod = Mathf.Sin(t * 1.2f) * 0.4f + 0.6f;
            data[i] = filtered * mod * 0.4f;
        }

        int fade = rate;
        for (int i = 0; i < fade; i++)
        {
            float f = (float)i / fade;
            data[i] = data[i] * f + data[len - fade + i] * (1f - f);
        }

        AudioClip clip = AudioClip.Create("Wind", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateEngineClip()
    {
        // Низкий гул двигателя: сумма низких частот + вибрация
        int rate = 22050;
        int len = rate * 4;
        float[] data = new float[len];

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float engine =
                Mathf.Sin(2f * Mathf.PI * 28f * t) * 0.3f +  // основной гул
                Mathf.Sin(2f * Mathf.PI * 56f * t) * 0.2f +  // гармоника
                Mathf.Sin(2f * Mathf.PI * 84f * t) * 0.1f +  // 3-я гармоника
                Random.Range(-1f, 1f) * 0.02f;                // шум

            // Лёгкая вибрация
            float vibrato = 1f + Mathf.Sin(2f * Mathf.PI * 2f * t) * 0.1f;
            data[i] = engine * vibrato * 0.5f;
        }

        AudioClip clip = AudioClip.Create("Engine", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
