// ============================================================
// KcsEfdData.cs — Опубликованные результаты свободноходных испытаний KCS (A3.2)
//
// KCS (KRISO Container Ship) — контейнеровоз-эталон для верификации методов
// расчёта манёвренности (SIMMAN 2008/2014/2020). Свободноходная модель MARIN,
// масштаб 1:37,89, глубокая вода, Fn = 0,26 (≈ 24 уз в натуре), отросток руля
// и винт 5533R; результаты — по таблицам статей, которые сравнивают с ними CFD:
//   [Nath2025] Nath A., Peng H., Qiu W. Numerical analysis and validation of turning
//     circle and zig-zag maneuvers of KRISO Container Ship in calm sea using body force
//     method. Proc. CSME/CFDSC/CSR 2025, Montréal, paper 409. Табл. I, III, IV.
//     https://espace2.etsmtl.ca/32469/
//   [Yun2025] Yun H., Choi W., Min G. et al. CFD analysis of container ship manoeuvring
//     performance under different trim conditions. Ocean Engineering, 2025, 341, 122489.
//     doi:10.1016/j.oceaneng.2025.122489. Табл. 1, 5, 6 (EFD MARIN).
//   Программа испытаний: SIMMAN 2014, «KCS free model tests in deep water»
//     https://simman2014.dk/wp-content/uploads/2015/07/KCS-free-deep.pdf
// В [Nath2025] табл. III единицы подписаны «(m)», но значения — в долях Lpp
// (совпадают с [Yun2025] табл. 5, где подписано «/Lpp»).
// Времена в [Nath2025] табл. IV — в масштабе модели; в натуру: ·√λ = ·√37,89.
// Испытания начинались на левый борт; модель тренажёра симметрична,
// поэтому сравнение ведётся с зеркальным манёвром на правый борт.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Значение из источника, с которым сравнивается показатель стенда.</summary>
public sealed class SourceValue
{
    /// <summary>Id испытания на стенде (turning_35_stbd, zigzag_10_10, …).</summary>
    public string TrialId;
    /// <summary>Имя показателя TrialMetric.Name.</summary>
    public string Metric;
    /// <summary>true — сравнивать в долях L (ValueInL), false — в единицах показателя.</summary>
    public bool InL;
    public double Value;
    public string Unit;
    public string Source;
    /// <summary>false — только справочно, в функцию ошибки не входит.</summary>
    public bool UseInFit = true;
}

public static class KcsEfdData
{
    // Главные размерения натуры [Nath2025] табл. I; Cb [Yun2025] табл. 1
    public const double Lpp = 230.0;          // м
    public const double Beam = 32.2;          // м
    public const double Draft = 10.8;         // м
    public const double Displacement = 52057; // м³
    public const double BlockCoefficient = 0.651;
    public const double Scale = 37.89;        // λ
    public const double Froude = 0.26;
    public const double RudderRateDegS = 2.32; // натура, [Yun2025] табл. 1
    public const double RudderAngleDeg = 35.0;
    public const double Gravity = 9.80665;

    /// <summary>Скорость натуры при Fn = 0,26: U = Fn·√(g·L), м/с (≈ 12,35 м/с = 24,0 уз).</summary>
    public static double SpeedMs => Froude * Math.Sqrt(Gravity * Lpp);

    /// <summary>Время модели → натура, с.</summary>
    public static double ToFullScaleTime(double modelSeconds) => modelSeconds * Math.Sqrt(Scale);

    private const string N3 = "Nath et al. 2025, табл. III (EFD MARIN, ЛБ)";
    private const string N4 = "Nath et al. 2025, табл. IV (EFD MARIN)";

    public static List<SourceValue> Targets()
    {
        return new List<SourceValue>
        {
            new SourceValue { TrialId = "turning_35_stbd", Metric = "Выдвиг (advance)", InL = true, Value = 2.934, Unit = "L", Source = N3 + "; Yun et al. 2025, табл. 5: 2,9341" },
            new SourceValue { TrialId = "turning_35_stbd", Metric = "Прямое смещение (transfer)", InL = true, Value = 1.085, Unit = "L", Source = N3 + "; Yun et al. 2025, табл. 5: 1,0854" },
            new SourceValue { TrialId = "turning_35_stbd", Metric = "Тактический диаметр", InL = true, Value = 2.640, Unit = "L", Source = N3 + "; Yun et al. 2025, табл. 5: 2,6401" },
            new SourceValue { TrialId = "zigzag_10_10", Metric = "1-й угол зарыскивания", Value = 10.761, Unit = "°", Source = N4 + ", 10/10 ЛБ" },
            new SourceValue { TrialId = "zigzag_10_10", Metric = "2-й угол зарыскивания", Value = 20.228, Unit = "°", Source = N4 + ", 10/10 ЛБ" },
            new SourceValue { TrialId = "zigzag_10_10", Metric = "Время до 1-й перекладки", Value = ToFullScaleTime(5.640), Unit = "с", Source = N4 + ", 10/10 ЛБ, t_a = 5,640 с модели ·√λ" },
            new SourceValue { TrialId = "zigzag_20_20", Metric = "1-й угол зарыскивания", Value = 24.398, Unit = "°", Source = N4 + ", 20/20 ЛБ; Yun et al. 2025, табл. 6" },
            new SourceValue { TrialId = "zigzag_20_20", Metric = "2-й угол зарыскивания", Value = 20.044, Unit = "°", Source = N4 + ", 20/20 ЛБ; Yun et al. 2025, табл. 6" },
            new SourceValue { TrialId = "zigzag_20_20", Metric = "Время до 1-й перекладки", Value = ToFullScaleTime(5.500), Unit = "с", Source = N4 + ", 20/20 ЛБ, t_a = 5,500 с модели ·√λ" },
            // Справочно: тот же зигзаг на правый борт (асимметрия модели в бассейне)
            new SourceValue { TrialId = "zigzag_20_20", Metric = "1-й угол зарыскивания", Value = 23.219, Unit = "°", Source = N4 + ", 20/20 ПБ (справочно)", UseInFit = false },
            new SourceValue { TrialId = "zigzag_20_20", Metric = "2-й угол зарыскивания", Value = 21.583, Unit = "°", Source = N4 + ", 20/20 ПБ (справочно)", UseInFit = false },
        };
    }
}
