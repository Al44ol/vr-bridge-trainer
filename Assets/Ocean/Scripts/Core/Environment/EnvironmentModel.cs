// ============================================================
// EnvironmentModel.cs — Освещение сцены: погода × время суток (B4)
//
// До B4 ночь была пресетом погоды: солнце почти выключалось, а небо,
// рассеянный свет и отражения оставались дневными. Теперь это две
// независимые оси:
//   • погода (WeatherLook) — волнение, ветер, дождь, видимость (туман),
//     облачность и дневной вид пресета (как до B4);
//   • время суток — фаза s: 0 — день, 1 — сумерки, 2 — ночь. Переход
//     день → ночь идёт через сумерки (s меняется непрерывно).
// Combine(погода, s, профиль) даёт итоговое освещение (EnvironmentLighting).
//
// Веса фаз (кусочно-линейные, сумма = 1):
//   s ≤ 1: w_д = 1 − s, w_с = s;   s > 1: w_с = 2 − s, w_н = s − 1.
// Каждая величина — взвешенная сумма «вида» дня, сумерек и ночи. Вид дня —
// ровно значения пресета погоды (днём картинка как до B4, проверяет тест).
// Цвет и угол солнца — среднее по весам дня и сумерек (ночью солнца нет).
//
// Облачность c = smoothstep(0,45; 0,8; seaState) — та же, что у туч неба
// (SkyWeather): ночью луна × (1 − 0,85c), звёзды × (1 − c), рассеянный ×
// (1 − 0,4c), туман × (1 − 0,3c). В сумерки свет × k_w = √clamp(I_дня; 0,1; 1)
// (пасмурный день — темнее и сумерки).
// Без вспышек: при E ≤ 1 вид сумерек покомпонентно не ярче дня, вид ночи — не ярче
// сумерек (минимум), поэтому яркость на переходе день → ночь не растёт ни при какой погоде.
// Видимость (плотность тумана) всегда берётся из погоды — ночью тоже (C4).
//
// Экспозиция E (NightProfile.Exposure) — один множитель яркости ночи и
// сумерек: интенсивности источников × E, цвета (заданы в sRGB) × E^(1/2,2),
// то есть линейная яркость × E. Днём не действует.
// «Темно» (Darkness): 0 днём, 0,6 в сумерки, 1 ночью — огни судов и знаков
// горят при Darkness ≥ 0,5 или видимости < 2 миль (LightsRequired).
// Все цвета — значения для RenderSettings / Light.color (sRGB).
// ============================================================
using System;

/// <summary>Цвет RGB (sRGB, 0…1) — без UnityEngine.Color, чтобы модель проверялась тестами вне Unity.</summary>
[Serializable]
public struct Rgb
{
    public float R, G, B;

    public Rgb(float r, float g, float b) { R = r; G = g; B = b; }

    public static Rgb operator +(Rgb a, Rgb b) => new Rgb(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Rgb operator *(Rgb a, float k) => new Rgb(a.R * k, a.G * k, a.B * k);
    public static Rgb operator *(float k, Rgb a) => a * k;

    public static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    /// <summary>Покомпонентный минимум.</summary>
    public static Rgb Min(Rgb a, Rgb b) => new Rgb(Math.Min(a.R, b.R), Math.Min(a.G, b.G), Math.Min(a.B, b.B));

    /// <summary>Яркость (коэффициенты Rec. 709).</summary>
    public float Luma => 0.2126f * R + 0.7152f * G + 0.0722f * B;

    /// <summary>Наибольшая по модулю разность компонент.</summary>
    public static float MaxDiff(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    public override string ToString() => $"({R:0.###}; {G:0.###}; {B:0.###})";
}

/// <summary>Погода — то, что от времени суток не зависит, и дневной вид пресета (значения WeatherController до B4).</summary>
public struct WeatherLook
{
    public float SeaState, WindSpeed, Rain;
    /// <summary>Плотность тумана ρ (ExponentialSquared), 1/м: видимость V ≈ 1,73/ρ.</summary>
    public float FogDensity;
    // Дневной вид
    public float SunIntensity;
    public Rgb SunColor;
    /// <summary>Высота солнца днём, градусы (поворот источника по X).</summary>
    public float SunElevationDeg;
    public Rgb AmbientSky, AmbientGround, FogColor;

    public static WeatherLook Lerp(in WeatherLook a, in WeatherLook b, float t) => new WeatherLook
    {
        SeaState = a.SeaState + (b.SeaState - a.SeaState) * t,
        WindSpeed = a.WindSpeed + (b.WindSpeed - a.WindSpeed) * t,
        Rain = a.Rain + (b.Rain - a.Rain) * t,
        FogDensity = a.FogDensity + (b.FogDensity - a.FogDensity) * t,
        SunIntensity = a.SunIntensity + (b.SunIntensity - a.SunIntensity) * t,
        SunColor = Rgb.Lerp(a.SunColor, b.SunColor, t),
        SunElevationDeg = a.SunElevationDeg + (b.SunElevationDeg - a.SunElevationDeg) * t,
        AmbientSky = Rgb.Lerp(a.AmbientSky, b.AmbientSky, t),
        AmbientGround = Rgb.Lerp(a.AmbientGround, b.AmbientGround, t),
        FogColor = Rgb.Lerp(a.FogColor, b.FogColor, t)
    };
}

/// <summary>Параметры ночи и сумерек (экранные значения, подбираются в шлеме).</summary>
[Serializable]
public sealed class NightProfile
{
    /// <summary>Экспозиция ночи: множитель линейной яркости ночи и сумерек (1 — по умолчанию).</summary>
    public float Exposure = 1f;

    // Луна
    public float MoonIntensity = 0.12f;
    public Rgb MoonColor = new Rgb(0.62f, 0.72f, 0.95f);
    /// <summary>Азимут луны (откуда светит), градусы: 0 — N, по часовой. 330° — слева по носу на курсе 000° сценариев B2/B3.</summary>
    public float MoonAzimuthDeg = 330f;
    public float MoonElevationDeg = 22f;
    /// <summary>Яркость диска луны на небе (0…1).</summary>
    public float MoonDiscBrightness = 0.9f;

    // Ночь (голубоватый оттенок — сумеречное зрение, эффект Пуркинье)
    public Rgb NightAmbientSky = new Rgb(0.10f, 0.12f, 0.18f);
    public Rgb NightAmbientEquator = new Rgb(0.07f, 0.08f, 0.11f);
    public Rgb NightAmbientGround = new Rgb(0.03f, 0.03f, 0.04f);
    public Rgb NightFogColor = new Rgb(0.03f, 0.04f, 0.065f);
    public Rgb NightSkyZenith = new Rgb(0.008f, 0.011f, 0.03f);
    public float StarBrightness = 1f;
    /// <summary>Лунная дорожка: дополнительное усиление блика воды (0 — как днём).</summary>
    public float WaterSpecGain = 7f;
    /// <summary>Лунная дорожка: расширение лепестка блика — показатель делится на (1 + w).</summary>
    public float WaterSpecWidening = 6f;

    // Сумерки (гражданские, после захода)
    public float TwilightSunIntensity = 0.3f;
    public Rgb TwilightSunColor = new Rgb(1f, 0.58f, 0.34f);
    public float TwilightSunElevationDeg = 3f;
    public Rgb TwilightAmbientSky = new Rgb(0.20f, 0.19f, 0.27f);
    public Rgb TwilightAmbientEquator = new Rgb(0.20f, 0.15f, 0.15f);
    public Rgb TwilightAmbientGround = new Rgb(0.05f, 0.045f, 0.045f);
    public Rgb TwilightFogColor = new Rgb(0.32f, 0.25f, 0.28f);
    /// <summary>Доля ночного неба в сумерки (звёзды, тёмный зенит).</summary>
    public float TwilightSkyNight = 0.5f;
    public Rgb TwilightGlowColor = new Rgb(0.9f, 0.42f, 0.18f);
    public float TwilightDarkness = 0.6f;
    /// <summary>Доля луны и звёзд в сумерки.</summary>
    public float TwilightMoonShare = 0.3f;
    public float TwilightStarShare = 0.25f;

    public NightProfile Clone() => (NightProfile)MemberwiseClone();
}

/// <summary>Итоговое освещение сцены.</summary>
public struct EnvironmentLighting
{
    public float SunIntensity;
    public Rgb SunColor;
    public float SunElevationDeg;

    public float MoonIntensity;
    public Rgb MoonColor;
    public float MoonAzimuthDeg, MoonElevationDeg;
    public float MoonDiscBrightness;

    public Rgb AmbientSky, AmbientEquator, AmbientGround;
    public Rgb FogColor;
    public float FogDensity;

    /// <summary>Затемнение дневной кубмапы неба, 0…1 (формула SkyWeather до B4 по итоговой интенсивности солнца).</summary>
    public float SkyDarkening;
    /// <summary>Сплошная облачность (тучи шторма), 0…1.</summary>
    public float SkyOvercast;
    /// <summary>Доля ночного неба, 0…1.</summary>
    public float SkyNight;
    public Rgb SkyNightZenith;
    public float StarBrightness;
    public float TwilightGlow;
    public Rgb TwilightGlowColor;

    /// <summary>Блик воды: доп. усиление и расширение лепестка (0, 0 — как днём).</summary>
    public float WaterSpecGain, WaterSpecWidening;

    /// <summary>«Темно»: 0 — день, 1 — ночь.</summary>
    public float Darkness;
}

public static class EnvironmentModel
{
    public const float DayPhase = 0f, TwilightPhase = 1f, NightPhase = 2f;
    /// <summary>Порог «темно» для огней (сумерки 0,6 и ночь 1 — горят).</summary>
    public const float DarknessForLights = 0.5f;
    /// <summary>Ограниченная видимость для огней (правило 20 б, в), мили.</summary>
    public const double RestrictedVisibilityNm = 2.0;

    public static float PhaseOf(TimeOfDay t) => t == TimeOfDay.Night ? NightPhase : t == TimeOfDay.Twilight ? TwilightPhase : DayPhase;

    /// <summary>Веса дня, сумерек и ночи по фазе s ∈ [0; 2].</summary>
    public static void Weights(float phase, out float day, out float twilight, out float night)
    {
        float s = Clamp(phase, 0f, 2f);
        if (s <= 1f) { day = 1f - s; twilight = s; night = 0f; }
        else { day = 0f; twilight = 2f - s; night = s - 1f; }
    }

    /// <summary>Облачность по волнению: smoothstep(0,45; 0,8; seaState) — как тучи SkyWeather.</summary>
    public static float Overcast(float seaState, float from = 0.45f, float to = 0.8f)
    {
        float x = Clamp((seaState - from) / (to - from), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>Затемнение дневного неба по интенсивности солнца (SkyWeather до B4): 1 − clamp((I/I_дня)^0,6; 0,03; 1).</summary>
    public static float DaySkyDarkening(float sunIntensity, float daylightIntensity = 1f, float minBrightness = 0.03f)
    {
        float b = (float)Math.Pow(Math.Max(sunIntensity, 0f) / Math.Max(daylightIntensity, 0.01f), 0.6);
        return 1f - Clamp(b, minBrightness, 1f);
    }

    /// <summary>Горят ли огни (режим «авто»): темно или видимость меньше 2 миль.</summary>
    public static bool LightsRequired(float darkness, double visibilityM) =>
        darkness >= DarknessForLights || visibilityM < RestrictedVisibilityNm * NavigationLights.MetersPerNm;

    public static EnvironmentLighting Combine(in WeatherLook w, float phase, NightProfile p)
    {
        if (p == null) p = new NightProfile();
        Weights(phase, out float bD, out float bT, out float bN);
        float c = Overcast(w.SeaState);
        float e = Math.Max(p.Exposure, 0.01f);
        float eC = (float)Math.Pow(e, 1.0 / 2.2);          // для цветов в sRGB: линейная яркость × E
        // Погода в сумерки: k_w = √clamp(I_дня; 0,1; 1) — пасмурно (шторм) темнее
        float kW = (float)Math.Sqrt(Clamp(w.SunIntensity, 0.1f, 1f));
        float nightCloud = 1f - 0.4f * c;                    // рассеянный свет ночью под тучами
        float fogCloud = 1f - 0.3f * c;

        // Виды дня, сумерек и ночи. При E ≤ 1 вид сумерек не ярче дня, ночи — не ярче сумерек
        // (покомпонентный минимум): переход день → ночь — без вспышек при любой погоде.
        bool cap = e <= 1f;
        Rgb dSky = w.AmbientSky, dGround = w.AmbientGround, dEquator = (w.AmbientSky + w.AmbientGround) * 0.5f, dFog = w.FogColor;
        Rgb tSky = p.TwilightAmbientSky * (kW * eC), tEquator = p.TwilightAmbientEquator * (kW * eC);
        Rgb tGround = p.TwilightAmbientGround * (kW * eC), tFog = p.TwilightFogColor * (kW * eC);
        if (cap) { tSky = Rgb.Min(tSky, dSky); tEquator = Rgb.Min(tEquator, dEquator); tGround = Rgb.Min(tGround, dGround); tFog = Rgb.Min(tFog, dFog); }
        Rgb nSky = p.NightAmbientSky * (nightCloud * eC), nEquator = p.NightAmbientEquator * (nightCloud * eC);
        Rgb nGround = p.NightAmbientGround * (nightCloud * eC), nFog = p.NightFogColor * (fogCloud * eC);
        if (cap) { nSky = Rgb.Min(nSky, tSky); nEquator = Rgb.Min(nEquator, tEquator); nGround = Rgb.Min(nGround, tGround); nFog = Rgb.Min(nFog, tFog); }

        var L = new EnvironmentLighting();

        // Солнце: день — пресет погоды, сумерки — низкое тёплое; ночью нет
        float sunTw = p.TwilightSunIntensity * kW * e;
        if (cap) sunTw = Math.Min(sunTw, w.SunIntensity);
        L.SunIntensity = bD * w.SunIntensity + bT * sunTw;
        float sunW = bD + bT;
        if (bD == 1f) { L.SunColor = w.SunColor; L.SunElevationDeg = w.SunElevationDeg; }   // день — бит в бит
        else if (sunW > 1e-6f)
        {
            L.SunColor = (w.SunColor * bD + p.TwilightSunColor * bT) * (1f / sunW);
            L.SunElevationDeg = (w.SunElevationDeg * bD + p.TwilightSunElevationDeg * bT) / sunW;
        }
        else
        {
            L.SunColor = p.TwilightSunColor;
            L.SunElevationDeg = p.TwilightSunElevationDeg;
        }

        // Луна
        float moonShare = bT * p.TwilightMoonShare + bN;
        L.MoonIntensity = moonShare * p.MoonIntensity * (1f - 0.85f * c) * e;
        L.MoonColor = p.MoonColor;
        L.MoonAzimuthDeg = p.MoonAzimuthDeg;
        L.MoonElevationDeg = p.MoonElevationDeg;
        L.MoonDiscBrightness = moonShare * p.MoonDiscBrightness * (1f - c);

        // Рассеянный свет (градиент неба: верх — горизонт — низ) и цвет тумана
        L.AmbientSky = dSky * bD + tSky * bT + nSky * bN;
        L.AmbientEquator = dEquator * bD + tEquator * bT + nEquator * bN;
        L.AmbientGround = dGround * bD + tGround * bT + nGround * bN;
        L.FogColor = dFog * bD + tFog * bT + nFog * bN;
        L.FogDensity = w.FogDensity;                         // видимость — всегда погода

        // Небо
        L.SkyDarkening = DaySkyDarkening(L.SunIntensity);
        L.SkyOvercast = c;
        L.SkyNight = bT * p.TwilightSkyNight + bN;
        L.SkyNightZenith = p.NightSkyZenith * eC;
        L.StarBrightness = (bT * p.TwilightStarShare + bN) * p.StarBrightness * (1f - c) * eC;
        L.TwilightGlow = bT * (1f - 0.7f * c);
        L.TwilightGlowColor = p.TwilightGlowColor;

        // Вода: лунная дорожка — шире и ярче блик главного источника (ночью это луна)
        L.WaterSpecGain = bN * p.WaterSpecGain;
        L.WaterSpecWidening = bN * p.WaterSpecWidening;

        L.Darkness = bT * p.TwilightDarkness + bN;
        return L;
    }

    private static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
}
