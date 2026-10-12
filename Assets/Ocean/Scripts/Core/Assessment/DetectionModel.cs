// ============================================================
// DetectionModel.cs — Дистанция визуального обнаружения цели (C2)
//
// Видимость V (метеорологическая дальность видимости, м):
//   • задана в сценарии (visibility_m > 0, C4) — она;
//   • иначе — по пресету погоды: туман ExponentialSquared, f = exp(−(ρd)²),
//     порог контраста 5 % (Кошмидер): V = √(−ln 0,05)/ρ ≈ 1,73/ρ (п. 5.4).
//     Плотности ρ пресетов — те же, что у WeatherController (проверка при старте).
// Дистанция обнаружения D_обн:
//   • задана в сценарии (detection_range_nm > 0) — она (например, «по радару»);
//   • сумерки и ночь (огни горят, Darkness ≥ 0,5, п. 9.8) — световая дальность
//     огня цели, видимого с курсового угла q_ц, под которым своё судно видно с цели:
//         q_ц ∈ [0°; 112,5°] ∪ [247,5°; 360°) — топовый огонь (сектор 225°, правило 21 а),
//         q_ц ∈ (112,5°; 247,5°)             — кормовой огонь (сектор 135°, правило 21 в);
//     номинальная дальность — правило 22 по длине цели, сила света — Приложение I, п. 8,
//     дальность при видимости V — закон Алларда с K = 0,05^(1852/V) (п. 8.8.3);
//   • день — D_обн = V (корпус теряется в дымке на метеорологической дальности).
// Бортовые огни в секторе топового заметнее не дают (их дальность меньше),
// поэтому для обнаружения не рассматриваются. Обнаружение — визуальное:
// радар (САРП, правило 7 б) не моделируется.
// ============================================================
using System;
using System.Collections.Generic;

public static class WeatherVisibility
{
    /// <summary>√(−ln 0,05): V = k/ρ для тумана ExponentialSquared при пороге контраста 5 %.</summary>
    public const double FogCoefficient = 1.7308;

    /// <summary>Плотность тумана пресета (как в WeatherController; B1, B3).</summary>
    public static float FogDensity(WeatherPresetId w)
    {
        switch (w)
        {
            case WeatherPresetId.Calm: return 0.0003f;
            case WeatherPresetId.Storm: return 0.0015f;
            case WeatherPresetId.Night: return 0.00015f;
            default: return 0.0004f;
        }
    }

    /// <summary>Метеорологическая видимость, м, по плотности тумана ExponentialSquared.</summary>
    public static double FromFogDensity(double density) => density > 1e-9 ? FogCoefficient / density : double.PositiveInfinity;

    /// <summary>
    /// Плотность тумана ExponentialSquared для видимости V, м (C4): ρ = √(−ln 0,05)/V.
    /// На дистанции V предмет закрыт туманом на 95 % (f = e^(−(ρV)²) = 0,05), на V/2 — на 53 %.
    /// </summary>
    public static float DensityFor(double visibilityM) => visibilityM > 1.0 ? (float)(FogCoefficient / visibilityM) : 0f;

    public static double VisibilityM(WeatherPresetId w) => FromFogDensity(FogDensity(w));
}

public static class DetectionModel
{
    /// <summary>Огни целей видны (сумерки и ночь — Darkness ≥ 0,5, п. 9.8).</summary>
    public static bool LightsVisible(TimeOfDay t) => t != TimeOfDay.Day;

    private static readonly Dictionary<long, double> cache = new Dictionary<long, double>();

    /// <summary>Световая дальность огня с номинальной дальностью nominalNm при видимости V, мили.</summary>
    public static double LightRangeNm(double nominalNm, double visibilityM)
    {
        long key = ((long)Math.Round(nominalNm * 100) << 32) ^ (double.IsInfinity(visibilityM) ? 0x7fffffffL : (long)Math.Round(visibilityM));
        lock (cache)
            if (cache.TryGetValue(key, out double r)) return r;
        double range = NavigationLights.LuminousRangeNm(NavigationLights.IntensityForRange(nominalNm),
                                                        NavigationLights.TransmissivityFromVisibility(visibilityM));
        lock (cache) cache[key] = range;
        return range;
    }

    /// <summary>Огонь, видимый с курсового угла q_ц (°): топовый (true) или кормовой (false).</summary>
    public static bool MastheadSector(double aspectDeg)
    {
        double q = aspectDeg % 360.0;
        if (q < 0) q += 360.0;
        return q <= 112.5 || q >= 247.5;
    }

    /// <summary>
    /// Дистанция обнаружения, мили. lengthM — длина цели; aspectDeg — курсовой угол
    /// своего судна с цели q_ц; visibilityM — V; overrideNm > 0 — задана сценарием.
    /// </summary>
    public static double RangeNm(double lengthM, double aspectDeg, TimeOfDay timeOfDay, double visibilityM, double overrideNm)
    {
        if (overrideNm > 0.0) return overrideNm;
        double vNm = visibilityM / NavigationLights.MetersPerNm;
        if (!LightsVisible(timeOfDay)) return vNm;
        NavigationLights.Ranges(lengthM > 0 ? lengthM : 50.0, out double mast, out _, out double stern, out _);
        return LightRangeNm(MastheadSector(aspectDeg) ? mast : stern, visibilityM);
    }

    /// <summary>Видимость, м: из сценария (C4) или по пресету погоды.</summary>
    public static double Visibility(WeatherPresetId weather, double scenarioVisibilityM) =>
        scenarioVisibilityM > 0.0 ? scenarioVisibilityM : WeatherVisibility.VisibilityM(weather);
}
