// ============================================================
// HornSynth.cs — Процедурный звук судового свистка (C4)
//
// Тембр — как у прежнего ShipHorn: основная частота f, квинта 1,5·f, суббас 0,5·f
// (0,5 / 0,25 / 0,15), лёгкое тремоло 5 Гц (±5 %). Без внешних аудиофайлов.
//   • Loop — зацикленный отрезок для гудка «пока держат» (своё судно): длина подобрана так,
//     что все составляющие укладываются целым числом периодов — склейки не слышно;
//     нарастание и спад делает ShipHorn громкостью источника;
//   • Signal — туманный сигнал другого судна целиком: один продолжительный звук (5 с) или два
//     с паузой 2 с (правило 35 а, б), с нарастанием 0,3 с и спадом 0,6 с.
// Клипы целей кэшируются по частоте и типу сигнала.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class HornSynth
{
    public const int SampleRate = 44100;
    private static readonly Dictionary<long, AudioClip> signalCache = new Dictionary<long, AudioClip>();

    private static float Tone(float f, float t)
    {
        float s = Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f
                + Mathf.Sin(2f * Mathf.PI * f * 1.5f * t) * 0.25f
                + Mathf.Sin(2f * Mathf.PI * f * 0.5f * t) * 0.15f;
        return s * (1f + Mathf.Sin(2f * Mathf.PI * 5f * t) * 0.05f);
    }

    /// <summary>Зацикленный звук: 1 с при целой частоте (все составляющие — целое число периодов).</summary>
    public static AudioClip Loop(float frequency, float volume)
    {
        // 0,5·f и 5 Гц должны укладываться целым числом периодов: длина = 1 с при f, кратной 2 Гц
        float f = Mathf.Max(2f, Mathf.Round(frequency / 2f) * 2f);
        int n = SampleRate;
        var samples = new float[n];
        for (int i = 0; i < n; i++) samples[i] = Tone(f, (float)i / SampleRate) * volume;
        AudioClip clip = AudioClip.Create($"HornLoop_{f:F0}", n, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Туманный сигнал целиком: «—» или «— —» (FogSignals.ProlongedS, PauseS).</summary>
    public static AudioClip Signal(float frequency, SoundSignalKind kind)
    {
        long key = ((long)Mathf.RoundToInt(frequency * 10f) << 8) | (long)kind;
        if (signalCache.TryGetValue(key, out AudioClip cached) && cached != null) return cached;
        float blast = (float)FogSignals.ProlongedS, pause = (float)FogSignals.PauseS;
        int blasts = kind == SoundSignalKind.FogNotMakingWay ? 2 : 1;
        float total = blasts * blast + (blasts - 1) * pause;
        int n = Mathf.CeilToInt(total * SampleRate);
        var samples = new float[n];
        const float attack = 0.3f, release = 0.6f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float local = t;
            if (blasts == 2 && t > blast) local = t - blast - pause;
            if (local < 0f || local > blast) continue;
            float env = Mathf.Clamp01(Mathf.Min(local / attack, (blast - local) / release));
            samples[i] = Tone(frequency, t) * env * 0.8f;
        }
        AudioClip clip = AudioClip.Create($"FogSignal_{frequency:F0}_{kind}", n, 1, SampleRate, false);
        clip.SetData(samples, 0);
        signalCache[key] = clip;
        return clip;
    }
}
