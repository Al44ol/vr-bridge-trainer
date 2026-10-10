// ============================================================
// GroundingSounds.cs — Процедурные звуки посадки и сигнализации (B1)
//
// Заглушки (как AmbientSounds/ShipHorn): удар о грунт, скрежет (петля),
// сигнал опасного приближения и сигнал «на мели». Свои клипы (.wav/.ogg)
// назначаются в инспекторе GroundingMonitor и заменяют заглушки.
// Генератор с фиксированным зерном — звук одинаков от запуска к запуску.
// ============================================================
using UnityEngine;

public static class GroundingSounds
{
    private const int Rate = 44100;

    /// <summary>Удар: низкий глухой удар корпуса + шум грунта + скрип набора, ≈1,6 с.</summary>
    public static AudioClip Impact()
    {
        var rnd = new System.Random(11);
        int len = (int)(Rate * 1.6f);
        var d = new float[len];
        float lp = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lp += 0.04f * (noise - lp);                                   // грунт: низкочастотный шум
            float thump = (Mathf.Sin(2f * Mathf.PI * 38f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 57f * t)) * Mathf.Exp(-t * 4f);
            float crunch = lp * 6f * Mathf.Exp(-t * 7f);
            float creak = 0.18f * Mathf.Sin(2f * Mathf.PI * (170f + 25f * Mathf.Sin(9f * t)) * t) * Mathf.Exp(-t * 2.5f) * Mathf.Clamp01(t * 8f);
            float attack = Mathf.Clamp01(t * 200f);
            d[i] = (thump * 0.7f + crunch + creak) * attack;
        }
        Normalize(d, 0.9f);
        return Make("GroundingImpact", d);
    }

    /// <summary>Скрежет корпуса по грунту: полосовой шум с рывками («прилипание — срыв»), петля 3 с.</summary>
    public static AudioClip Scrape()
    {
        var rnd = new System.Random(23);
        int len = Rate * 3;
        var d = new float[len];
        float lpA = 0f, lpB = 0f, rumble = 0f, env = 0f, envTarget = 0.5f;
        int nextGrain = 0;
        for (int i = 0; i < len; i++)
        {
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lpA += 0.10f * (noise - lpA);       // ≈ 750 Гц
            lpB += 0.02f * (noise - lpB);       // ≈ 140 Гц
            float band = lpA - lpB;
            rumble += 0.004f * (noise - rumble);
            if (i >= nextGrain)
            {
                envTarget = 0.25f + 0.75f * (float)rnd.NextDouble();
                nextGrain = i + (int)(Rate * (0.04 + 0.12 * rnd.NextDouble()));
            }
            env += 0.002f * (envTarget - env);
            d[i] = band * env * 3f + rumble * 8f;
        }
        // Бесшовная петля: перекрёстное затухание концов
        int fade = Rate / 5;
        for (int i = 0; i < fade; i++)
        {
            float w = (float)i / fade;
            d[i] = d[i] * w + d[len - fade + i] * (1f - w);
        }
        System.Array.Resize(ref d, len - fade);
        Normalize(d, 0.8f);
        return Make("GroundingScrape", d);
    }

    /// <summary>Сигнал опасного приближения: два коротких тона 1200 Гц.</summary>
    public static AudioClip WarningBeep() => Beeps("ShoalWarning", 1200f, 2, 0.12f, 0.1f);

    /// <summary>Сигнал «на мели»: три тона 880 Гц.</summary>
    public static AudioClip GroundedAlarm() => Beeps("GroundedAlarm", 880f, 3, 0.16f, 0.08f);

    private static AudioClip Beeps(string name, float freq, int count, float on, float off)
    {
        int len = (int)(Rate * (count * (on + off) + 0.05f));
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float period = on + off;
            int k = (int)(t / period);
            float local = t - k * period;
            if (k >= count || local > on) continue;
            float edge = Mathf.Clamp01(local / 0.008f) * Mathf.Clamp01((on - local) / 0.008f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * freq * t)) * edge;
        }
        Normalize(d, 0.6f);
        return Make(name, d);
    }

    private static void Normalize(float[] d, float peak)
    {
        float max = 1e-6f;
        foreach (float v in d) max = Mathf.Max(max, Mathf.Abs(v));
        float k = peak / max;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }

    private static AudioClip Make(string name, float[] d)
    {
        AudioClip clip = AudioClip.Create(name, d.Length, 1, Rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
