// ============================================================
// NavigationLights.cs — Навигационные огни по МППСС-72 и закон Алларда (B3)
//
// Чистый класс: отображение огней в сцене (TrafficShipView, AidLightsView),
// стенд Traffic Report и тесты. Ссылки — на МППСС-72 (COLREG 1972, ред. с
// поправками) и Приложение I к ним; сверить с официальным текстом перед ВКР.
//
// 1. Состав (правило 23 а): механическое судно на ходу — топовый огонь впереди;
//    второй топовый огонь позади и выше первого — для судов L ≥ 50 м (для
//    L < 50 м не обязателен); бортовые огни; кормовой огонь.
// 2. Секторы (правило 21), курсовой угол наблюдателя q — от носа цели по часовой:
//    топовый — 225° (от 112,5° ЛБ через нос до 112,5° ПБ); бортовые — по 112,5°
//    (зелёный 0…112,5° ПБ, красный 0…112,5° ЛБ); кормовой — 135° (67,5° от
//    кормы на каждый борт).
// 3. Границы секторов (Приложение I, п. 9 а): бортовые огни в направлении
//    вперёд гаснут до практического нуля между 1° и 3° за границей сектора
//    («острая» граница: 1 до 1° снаружи, линейно до 0 к 3°); у топовых, кормового
//    и у кормовой границы бортовых сила света сохраняется до 5° внутри сектора,
//    от 5° внутри до границы может спадать на 50 %, к 5° снаружи — до нуля
//    («мягкая» граница: k = clamp(0,5 + 0,1·x), x — угол внутрь от границы, °).
//    Поэтому прямо по носу видны оба бортовых огня, а на границах — небольшое
//    перекрытие, как у настоящих фонарей.
// 4. Дальность видимости (правило 22), мили:
//        L ≥ 50 м:       топовый 6, бортовые 3, кормовой 3;
//        20 ≤ L < 50 м:  топовый 5, бортовые 2, кормовой 2;
//        12 ≤ L < 20 м:  топовый 3, бортовые 2, кормовой 2;
//        L < 12 м:       топовый 2, бортовые 1, кормовой 2.
// 5. Сила света (Приложение I, п. 8): I = 3,43·10⁶·T·D²·K^(−D), кд,
//    T = 2·10⁻⁷ лк — порог освещённости в глазу наблюдателя ночью,
//    K = 0,8 — коэффициент пропускания атмосферы на милю (видимость ≈ 13 миль),
//    D — дальность по правилу 22, мили; 3,43·10⁶ = 1852² (метры в миле в квадрате).
//    Освещённость у наблюдателя на дистанции d (закон Алларда):
//        E(d) = I·K^(d/1852)/d²,  d — м;  огонь виден, если E ≥ T.
//    По построению E(D) = T при K = 0,8. В тумане K меньше: по Кошмидеру
//    (порог контраста 5 %) K = 0,05^(1852/V), V — метеорологическая видимость, м;
//    в сцене V = 1,73/ρ (туман ExponentialSquared, WeatherController).
// 6. Расположение (Приложение I, пп. 2, 3), упрощённо по HullProfile:
//    передний топовый — над корпусом не ниже 6 м, а при ширине B > 6 м — не ниже B,
//    но не выше 12 м (L < 20 м — 2,5 м), не дальше L/4 от форштевня; задний —
//    выше переднего не менее чем на 4,5 м, по горизонтали между ними не менее
//    L/2 (но не обязательно более 100 м); бортовые — у борта, не выше 3/4 высоты
//    переднего топового над корпусом и не впереди него; кормовой — у кормы.
// 7. Яркость на экране (не физиология, а отображение): v = E/T — во сколько раз
//    освещённость выше порога; уровень = clamp(lg v / n_дек, 0, 1), n_дек = 2 —
//    на номинальной дальности огонь гаснет до нуля, на два порядка выше порога —
//    полная яркость.
// 8. Огни знаков МАМС (B1, AidToNavigation.Light): характеристика «Fl R 4s» и т. п.
//    разбирается в LightCharacter (длительности проблесков — в шапке класса).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;

public enum NavLightKind { MastheadForward, MastheadAft, SidelightStarboard, SidelightPort, Sternlight, AllRound }
public enum NavLightColor { White, Red, Green, Yellow }
/// <summary>Граница сектора: острая (бортовые вперёд) или мягкая (Приложение I, п. 9).</summary>
public enum SectorEdge { Soft, Sharp }

/// <summary>Один огонь: место на судне, сектор, дальность, сила света.</summary>
public struct NavLight
{
    public NavLightKind Kind;
    public NavLightColor Color;
    /// <summary>Положение: к носу от миделя, к правому борту, над ватерлинией, м.</summary>
    public double Forward, Starboard, Height;
    /// <summary>Сектор: центр (курсовой угол, °) и полуширина, °; HalfWidth ≥ 180 — круговой.</summary>
    public double CenterDeg, HalfWidthDeg;
    public SectorEdge LowEdge, HighEdge;
    /// <summary>Дальность видимости по правилу 22, мили.</summary>
    public double NominalRangeNm;
    /// <summary>Сила света по Приложению I, п. 8, кд.</summary>
    public double IntensityCd;
}

/// <summary>Профиль корпуса цели: общие размеры для модели (TrafficMeshes) и мест огней.</summary>
public struct HullProfile
{
    public TrafficVesselType Type;
    public double Length, Beam, Draft;
    /// <summary>Высота палубы над ватерлинией, м.</summary>
    public double Freeboard;
    /// <summary>Надстройка: x носовой и кормовой стенок (к носу от миделя), м.</summary>
    public double HouseFore, HouseAft;
    public int HouseTiers;
    public double TierHeight;
    /// <summary>Фок-мачта (передний топовый огонь); NaN — нет.</summary>
    public double ForemastX;

    public double HouseTop => Freeboard + HouseTiers * TierHeight;
    public double HouseCenter => 0.5 * (HouseFore + HouseAft);

    /// <summary>Стилизованные пропорции по типу (не чертёж конкретного судна).</summary>
    public static HullProfile For(TrafficVesselType type, double length, double beam, double draft)
    {
        double L = Math.Max(5.0, length);
        var h = new HullProfile { Type = type, Length = L, Beam = Math.Max(2.0, beam), Draft = Math.Max(0.5, draft), TierHeight = 2.8 };
        switch (type)
        {
            case TrafficVesselType.Tanker:
                h.Freeboard = Clamp(0.03 * L, 1.5, 10.0);
                h.HouseFore = -0.33 * L; h.HouseAft = -0.46 * L; h.HouseTiers = 5;
                h.ForemastX = 0.38 * L;
                break;
            case TrafficVesselType.Small:
                h.Freeboard = Clamp(0.06 * L, 1.2, 4.0);
                h.HouseFore = 0.30 * L; h.HouseAft = 0.02 * L; h.HouseTiers = 2;
                h.ForemastX = double.NaN;
                break;
            default:
                h.Freeboard = Clamp(0.04 * L, 2.0, 12.0);
                h.HouseFore = -0.29 * L; h.HouseAft = -0.44 * L; h.HouseTiers = 4;
                h.ForemastX = 0.37 * L;
                break;
        }
        return h;
    }

    private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;
}

public static class NavigationLights
{
    /// <summary>Порог освещённости ночью T, лк (Приложение I, п. 8).</summary>
    public const double ThresholdLux = 2e-7;
    /// <summary>Коэффициент пропускания атмосферы K на милю (Приложение I, п. 8).</summary>
    public const double ClearTransmissivity = 0.8;
    public const double MetersPerNm = 1852.0;
    /// <summary>Порог контраста Кошмидера для метеорологической видимости.</summary>
    public const double ContrastThreshold = 0.05;
    /// <summary>Отображение: сколько десятичных порядков над порогом до полной яркости.</summary>
    public const double DisplayDecades = 2.0;
    /// <summary>
    /// Порог освещённости днём, лк (C4; ориентир МАМС для дневного наблюдения огня на фоне неба — сверить
    /// с первоисточником). Днём огонь виден много ближе, чем ночью: иначе в тумане он «светился» бы
    /// дальше, чем виден корпус судна.
    /// </summary>
    public const double DayThresholdLux = 1e-3;

    /// <summary>
    /// Порог освещённости по «темноте» (WeatherController.Darkness: 0 день, 0,6 сумерки, 1 ночь):
    /// при Darkness ≥ 0,5 — ночной T (как в B3, B4); от дня к 0,5 — логарифмически от дневного к ночному.
    /// </summary>
    public static double ThresholdFor(double darkness)
    {
        if (double.IsNaN(darkness) || darkness >= EnvironmentModel.DarknessForLights) return ThresholdLux;
        double k = Math.Max(0.0, darkness) / EnvironmentModel.DarknessForLights;
        return Math.Pow(10.0, Math.Log10(DayThresholdLux) + (Math.Log10(ThresholdLux) - Math.Log10(DayThresholdLux)) * k);
    }

    // ─────────── Правило 22 ───────────
    public static void Ranges(double lengthM, out double mastheadNm, out double sideNm, out double sternNm, out double allRoundNm)
    {
        if (lengthM >= 50.0) { mastheadNm = 6; sideNm = 3; sternNm = 3; allRoundNm = 3; }
        else if (lengthM >= 20.0) { mastheadNm = 5; sideNm = 2; sternNm = 2; allRoundNm = 2; }
        else if (lengthM >= 12.0) { mastheadNm = 3; sideNm = 2; sternNm = 2; allRoundNm = 2; }
        else { mastheadNm = 2; sideNm = 1; sternNm = 2; allRoundNm = 2; }
    }

    // ─────────── Приложение I, п. 8: сила света и закон Алларда ───────────
    /// <summary>Сила света, кд, для дальности D (мили) при K = 0,8.</summary>
    public static double IntensityForRange(double rangeNm) =>
        MetersPerNm * MetersPerNm * ThresholdLux * rangeNm * rangeNm * Math.Pow(ClearTransmissivity, -rangeNm);

    /// <summary>Освещённость у наблюдателя, лк: E = I·K^(d/1852)/d².</summary>
    public static double Illuminance(double intensityCd, double distanceM, double transmissivity)
    {
        double d = Math.Max(1.0, distanceM);
        return intensityCd * Math.Pow(transmissivity, d / MetersPerNm) / (d * d);
    }

    /// <summary>K на милю по метеорологической видимости V (м), Кошмидер: K = 0,05^(1852/V).</summary>
    public static double TransmissivityFromVisibility(double visibilityM)
    {
        if (double.IsNaN(visibilityM) || double.IsInfinity(visibilityM) || visibilityM <= 0.0) return ClearTransmissivity;
        return Math.Pow(ContrastThreshold, MetersPerNm / visibilityM);
    }

    /// <summary>Дальность, на которой E = T (мили) — «световая дальность» огня при данном K.</summary>
    public static double LuminousRangeNm(double intensityCd, double transmissivity) => LuminousRangeNm(intensityCd, transmissivity, ThresholdLux);

    /// <summary>Дальность, на которой E = порогу thresholdLux, мили.</summary>
    public static double LuminousRangeNm(double intensityCd, double transmissivity, double thresholdLux)
    {
        double lo = 0.001, hi = 60.0;
        if (Illuminance(intensityCd, hi * MetersPerNm, transmissivity) >= thresholdLux) return hi;
        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Illuminance(intensityCd, mid * MetersPerNm, transmissivity) >= thresholdLux) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Уровень яркости на экране 0…1 по отношению E/T.</summary>
    public static double DisplayLevel(double illuminanceLux) => DisplayLevel(illuminanceLux, ThresholdLux);

    /// <summary>Уровень яркости 0…1 по отношению E/T при пороге thresholdLux (C4: днём порог выше).</summary>
    public static double DisplayLevel(double illuminanceLux, double thresholdLux)
    {
        double v = illuminanceLux / thresholdLux;
        if (!(v > 1.0)) return 0.0;
        double l = Math.Log10(v) / DisplayDecades;
        return l > 1.0 ? 1.0 : l;
    }

    // ─────────── Правило 21 и Приложение I, п. 9: секторы ───────────
    /// <summary>Доля силы света огня в направлении наблюдателя с курсовым углом q (°).</summary>
    public static double SectorFactor(in NavLight light, double relativeBearingDeg)
    {
        if (light.HalfWidthDeg >= 180.0) return 1.0;
        double off = Math.IEEERemainder(relativeBearingDeg - light.CenterDeg, 360.0);
        double inside = light.HalfWidthDeg - Math.Abs(off);   // угол внутрь от ближайшей границы, °
        return EdgeFactor(off < 0.0 ? light.LowEdge : light.HighEdge, inside);
    }

    /// <summary>Спад силы света у границы сектора; x — угол внутрь сектора от границы, °.</summary>
    public static double EdgeFactor(SectorEdge edge, double x)
    {
        if (edge == SectorEdge.Sharp)
        {
            if (x >= -1.0) return 1.0;
            if (x <= -3.0) return 0.0;
            return (x + 3.0) / 2.0;
        }
        double k = 0.5 + 0.1 * x;
        return k < 0.0 ? 0.0 : k > 1.0 ? 1.0 : k;
    }

    // ─────────── Правило 23: механическое судно на ходу ───────────
    /// <summary>Огни механического судна на ходу с расположением по Приложению I.</summary>
    public static List<NavLight> PowerDrivenUnderway(in HullProfile h)
    {
        var lights = new List<NavLight>();
        double L = h.Length;
        Ranges(L, out double rMast, out double rSide, out double rStern, out _);

        // Высота переднего топового над корпусом (п. 2 а i; 2 в для 12…20 м)
        double h1 = L >= 20.0 ? Math.Max(6.0, Math.Min(h.Beam, 12.0)) : L >= 12.0 ? 2.5 : 1.5;
        bool twoMasthead = L >= 50.0;
        double xFwd = !double.IsNaN(h.ForemastX) ? h.ForemastX : h.HouseCenter;
        xFwd = Math.Min(xFwd, 0.5 * L - 1.0);
        double yFwd = h.Freeboard + h1;
        if (!twoMasthead) yFwd = Math.Max(yFwd, h.HouseTop + 2.0);   // единственный топовый — над рубкой
        lights.Add(Light(NavLightKind.MastheadForward, NavLightColor.White, xFwd, 0.0, yFwd, 0.0, 112.5, SectorEdge.Soft, SectorEdge.Soft, rMast));

        if (twoMasthead)
        {
            // Задний: на мачте надстройки, не ближе min(L/2, 100 м) к переднему, выше на ≥ 4,5 м
            double sep = Math.Min(0.5 * L, 100.0);
            double xAft = Math.Min(h.HouseCenter, xFwd - sep);
            xAft = Math.Max(xAft, -0.5 * L + 1.0);
            double yAft = Math.Max(yFwd + 4.5, h.HouseTop + 3.0);
            lights.Add(Light(NavLightKind.MastheadAft, NavLightColor.White, xAft, 0.0, yAft, 0.0, 112.5, SectorEdge.Soft, SectorEdge.Soft, rMast));
        }

        // Бортовые: на крыльях мостика, не выше 3/4 h1 над корпусом (п. 2 ж), не впереди топового (п. 3 б)
        double xSide = Math.Min(h.HouseFore, xFwd - 0.5);
        double ySide = Math.Min(h.Freeboard + 0.75 * h1, h.HouseTop - 0.5);
        ySide = Math.Max(ySide, h.Freeboard + 1.0);
        double half = 0.5 * h.Beam;
        lights.Add(Light(NavLightKind.SidelightStarboard, NavLightColor.Green, xSide, half, ySide, 56.25, 56.25, SectorEdge.Sharp, SectorEdge.Soft, rSide));
        lights.Add(Light(NavLightKind.SidelightPort, NavLightColor.Red, xSide, -half, ySide, -56.25, 56.25, SectorEdge.Soft, SectorEdge.Sharp, rSide));

        // Кормовой: у кормы, над фальшбортом
        lights.Add(Light(NavLightKind.Sternlight, NavLightColor.White, -0.5 * L + 0.5, 0.0, h.Freeboard + 1.5, 180.0, 67.5, SectorEdge.Soft, SectorEdge.Soft, rStern));
        return lights;
    }

    private static NavLight Light(NavLightKind kind, NavLightColor color, double x, double y, double z,
                                  double center, double half, SectorEdge low, SectorEdge high, double rangeNm) =>
        new NavLight
        {
            Kind = kind, Color = color, Forward = x, Starboard = y, Height = z,
            CenterDeg = center, HalfWidthDeg = half, LowEdge = low, HighEdge = high,
            NominalRangeNm = rangeNm, IntensityCd = IntensityForRange(rangeNm)
        };

    public static string Title(NavLightKind k)
    {
        switch (k)
        {
            case NavLightKind.MastheadForward: return "топовый (передний)";
            case NavLightKind.MastheadAft: return "топовый (задний)";
            case NavLightKind.SidelightStarboard: return "бортовой зелёный";
            case NavLightKind.SidelightPort: return "бортовой красный";
            case NavLightKind.Sternlight: return "кормовой";
            default: return "круговой";
        }
    }
}

/// <summary>
/// Характеристика огня знака (B1: «Fl R 4s», «Q(6)+LFl 15s» …) → включён / выключен по времени.
/// Поддерживаются: F (постоянный), Fl, Fl(n), LFl, Q, Q(n), VQ, VQ(n), Iso, Oc и сочетание «+»;
/// цвет R, G, W, Y (по умолчанию белый); период «Ns» (без периода — непрерывный ритм).
/// Длительности (предварительные, порядок величин по рекомендациям МАМС — сверить):
/// проблеск Fl — 0,5 с, промежуток в группе — 1,0 с; длительный LFl — 2,0 с;
/// частый Q — 60 в минуту (0,4 с свет / 0,6 с темнота), очень частый VQ — 120 в минуту
/// (0,2 / 0,3 с); Iso — свет и темнота поровну; Oc — темнота 1 с за период.
/// </summary>
public sealed class LightCharacter
{
    public NavLightColor Color = NavLightColor.White;
    /// <summary>Период, с.</summary>
    public double Period = 1.0;
    /// <summary>Интервалы света в пределах периода: [начало, конец), с.</summary>
    public readonly List<(double on, double off)> Flashes = new List<(double, double)>();
    public string Source = "";

    public const double FlashOn = 0.5, FlashGap = 1.0, LongFlashOn = 2.0;
    public const double QuickOn = 0.4, QuickOff = 0.6, VeryQuickOn = 0.2, VeryQuickOff = 0.3;

    /// <summary>Огонь горит в момент t (с).</summary>
    public bool IsOn(double t)
    {
        if (Period <= 0.0) return true;
        double p = t % Period;
        if (p < 0) p += Period;
        foreach (var f in Flashes)
            if (p >= f.on && p < f.off) return true;
        return false;
    }

    public static LightCharacter Parse(string text)
    {
        var c = new LightCharacter { Source = text ?? "" };
        string s = (text ?? "").Trim();
        if (s.Length == 0) { c.Flashes.Add((0.0, FlashOn)); c.Period = 4.0; return c; }

        double period = double.NaN;
        string rhythm = "";
        foreach (string token in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = token.Trim();
            if (t.EndsWith("s", StringComparison.Ordinal) &&
                double.TryParse(t.Substring(0, t.Length - 1).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double sec))
                period = sec;
            else if (t == "R") c.Color = NavLightColor.Red;
            else if (t == "G") c.Color = NavLightColor.Green;
            else if (t == "W") c.Color = NavLightColor.White;
            else if (t == "Y") c.Color = NavLightColor.Yellow;
            else if (rhythm.Length == 0) rhythm = t;
        }

        double cursor = 0.0;
        foreach (string part in rhythm.Split('+'))
            cursor = AppendPart(c, part.Trim(), cursor, period);
        if (c.Flashes.Count == 0) { c.Flashes.Add((0.0, FlashOn)); cursor = FlashOn + FlashGap; }

        // Iso / Oc / F задают период сами
        // Период из характеристики; без него (или если ритм длиннее) — длина ритма
        c.Period = !double.IsNaN(period) && period > cursor - 1e-9 ? period : Math.Max(cursor, 1e-3);
        if (rhythm == "F") c.Period = 0.0;
        return c;
    }

    // Добавить часть ритма начиная с cursor; возвращает конец (с учётом паузы после части)
    private static double AppendPart(LightCharacter c, string part, double cursor, double period)
    {
        int n = 1;
        string kind = part;
        int br = part.IndexOf('(');
        if (br > 0)
        {
            kind = part.Substring(0, br);
            int close = part.IndexOf(')', br);
            if (close > br && int.TryParse(part.Substring(br + 1, close - br - 1), out int cnt)) n = Math.Max(1, cnt);
        }
        switch (kind)
        {
            case "F":
                c.Flashes.Add((0.0, double.MaxValue));
                return 1.0;
            case "Iso":
            {
                double p = double.IsNaN(period) ? 4.0 : period;
                c.Flashes.Add((cursor, cursor + 0.5 * p));
                return cursor + p;
            }
            case "Oc":
            {
                double p = double.IsNaN(period) ? 4.0 : period;
                c.Flashes.Add((cursor, cursor + Math.Max(0.1, p - 1.0)));
                return cursor + p;
            }
            case "LFl":
                for (int i = 0; i < n; i++) { c.Flashes.Add((cursor, cursor + LongFlashOn)); cursor += LongFlashOn + FlashGap; }
                return cursor;
            case "Q":
                for (int i = 0; i < n; i++) { c.Flashes.Add((cursor, cursor + QuickOn)); cursor += QuickOn + QuickOff; }
                return cursor;
            case "VQ":
                for (int i = 0; i < n; i++) { c.Flashes.Add((cursor, cursor + VeryQuickOn)); cursor += VeryQuickOn + VeryQuickOff; }
                return cursor;
            default:   // Fl, Fl(n) и неизвестные (Mo и т. п.) — как проблесковый
                for (int i = 0; i < n; i++) { c.Flashes.Add((cursor, cursor + FlashOn)); cursor += FlashOn + FlashGap; }
                return cursor;
        }
    }
}
