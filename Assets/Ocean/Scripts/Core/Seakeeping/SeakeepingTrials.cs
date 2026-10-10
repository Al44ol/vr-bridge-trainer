// ============================================================
// SeakeepingTrials.cs — Стенд мореходности: качка на прямом курсе (запрос 4)
//
// Чистый C# (без Play Mode): судно идёт прямым курсом с постоянной скоростью
// по полю волн OceanWaveField; SeakeepingModel считает качку с шагом dt.
// Курсовой угол волны q — угол прихода главной волны (волна 1) от носа:
//   q = 0° — волна встречная (носом на волну), 90° — лаг (с ПБ), 180° — попутная.
// Курс судна ψ = χ − q, где χ — пеленг «откуда идёт» волна 1.
// Угол между курсом и направлением распространения волны i: μ_i = ψ − β_i
// (β_i — пеленг распространения); для волны 1 μ = 180° − q.
//
// Показатели (после переходного процесса t_пп):
//   σ — среднеквадратичное отклонение от среднего; значительная амплитуда
//   (средняя из 1/3 наибольших для узкополосного процесса) ≈ 2σ; максимум |x|;
//   доминирующий период — по максимуму амплитудного спектра (ДПФ на сетке ω);
//   теоретический период встречи T_e,i = 2π/|ω_i − k_i·U·cos μ_i| для каждой волны.
// ============================================================
using System;
using System.Collections.Generic;

public sealed class SeakeepingCase
{
    public string Preset = "";
    public float Intensity;
    public double WaveSpeedFactor = 1.0;    // s — множитель скорости волн от погоды
    public double HeadingDeg;               // курсовой угол волны q, °
    public double SpeedKnots;
    public string Id => $"{Preset}_q{HeadingDeg:000}_v{SpeedKnots:00}";
}

public struct SeakeepingSample
{
    public double T;
    public double Heave, Roll, Pitch;               // отклик: м, рад, рад
    public double WaveHeave, WaveRoll, WavePitch;   // возмущение (плоскость МНК)
}

public sealed class SeakeepingResult
{
    public SeakeepingCase Case;
    public double PsiDeg;
    public List<SeakeepingSample> Samples = new List<SeakeepingSample>();

    // σ: отклик и возмущение (м, градусы)
    public double SigmaHeave, SigmaRollDeg, SigmaPitchDeg;
    public double SigmaWaveHeave, SigmaWaveRollDeg, SigmaWavePitchDeg;
    public double MaxHeave, MaxRollDeg, MaxPitchDeg;
    // Доминирующие периоды (с) отклика и возмущения
    public double PeakPeriodHeave, PeakPeriodRoll, PeakPeriodPitch, PeakPeriodWaveHeave;
    // Теоретические периоды встречи по волнам (с)
    public double[] EncounterPeriods = new double[OceanWaveField.WaveCount];
    // Среднее время шага модели качки (мкс) — сэмплер + МНК + динамика
    public double StepMicroseconds;
}

public sealed class SeakeepingTrialSettings
{
    public double Dt = 0.02;
    public double Duration = 600.0;
    public double Transient = 100.0;
    public double SampleInterval = 0.1;
    public int SamplesAlong = 5, SamplesAcross = 3;
    public double CenterForward, CenterStarboard;
}

public static class SeakeepingTrials
{
    public const double KnotsToMs = 0.514444;
    private const double Deg = 180.0 / Math.PI;

    /// <summary>Пеленг «откуда идёт» волна i, градусы (0 = N, по часовой).</summary>
    public static double WaveFromBearingDeg(OceanWaveField f, int i) =>
        Wrap360(Math.Atan2(-f.DirEast[i], -f.DirNorth[i]) * Deg);

    public static SeakeepingResult Run(ShipParameters p, OceanWaveField template, SeakeepingCase c, SeakeepingTrialSettings s)
    {
        var field = new OceanWaveField();
        field.CopyFrom(template);
        field.Intensity = c.Intensity;
        field.Speed = c.WaveSpeedFactor;
        for (int i = 0; i < OceanWaveField.WaveCount; i++) field.TimePhase[i] = 0.0;

        double psiDeg = Wrap360(WaveFromBearingDeg(field, 0) - c.HeadingDeg);
        double psi = psiDeg / Deg;
        double u = c.SpeedKnots * KnotsToMs;
        double east = 0.0, north = 0.0;
        field.CenterEast = east; field.CenterNorth = north;

        var model = new SeakeepingModel(p)
        {
            SamplesAlong = s.SamplesAlong, SamplesAcross = s.SamplesAcross,
            CenterForward = s.CenterForward, CenterStarboard = s.CenterStarboard,
        };
        model.Reset(field, east, north, psi);

        var r = new SeakeepingResult { Case = c, PsiDeg = psiDeg };
        int steps = (int)Math.Round(s.Duration / s.Dt);
        int every = Math.Max(1, (int)Math.Round(s.SampleInterval / s.Dt));
        long ticks = 0;
        for (int n = 1; n <= steps; n++)
        {
            east += u * Math.Sin(psi) * s.Dt;
            north += u * Math.Cos(psi) * s.Dt;
            field.Advance(s.Dt);
            field.CenterEast = east; field.CenterNorth = north;   // океан движется вместе с судном

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            model.Step(field, east, north, psi, s.Dt);
            ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

            if (n % every == 0)
                r.Samples.Add(new SeakeepingSample
                {
                    T = n * s.Dt,
                    Heave = model.Heave, Roll = model.Roll, Pitch = model.Pitch,
                    WaveHeave = model.WaveHeave, WaveRoll = model.WaveRoll, WavePitch = model.WavePitch,
                });
        }
        r.StepMicroseconds = ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, steps);

        // Статистика после переходного процесса
        var steady = r.Samples.FindAll(x => x.T >= s.Transient);
        double dtS = every * s.Dt;
        r.SigmaHeave = Sigma(steady, x => x.Heave);
        r.SigmaRollDeg = Sigma(steady, x => x.Roll) * Deg;
        r.SigmaPitchDeg = Sigma(steady, x => x.Pitch) * Deg;
        r.SigmaWaveHeave = Sigma(steady, x => x.WaveHeave);
        r.SigmaWaveRollDeg = Sigma(steady, x => x.WaveRoll) * Deg;
        r.SigmaWavePitchDeg = Sigma(steady, x => x.WavePitch) * Deg;
        r.MaxHeave = MaxAbs(steady, x => x.Heave);
        r.MaxRollDeg = MaxAbs(steady, x => x.Roll) * Deg;
        r.MaxPitchDeg = MaxAbs(steady, x => x.Pitch) * Deg;
        r.PeakPeriodHeave = PeakPeriod(steady, x => x.Heave, dtS);
        r.PeakPeriodRoll = PeakPeriod(steady, x => x.Roll, dtS);
        r.PeakPeriodPitch = PeakPeriod(steady, x => x.Pitch, dtS);
        r.PeakPeriodWaveHeave = PeakPeriod(steady, x => x.WaveHeave, dtS);

        for (int i = 0; i < OceanWaveField.WaveCount; i++)
        {
            double beta = Math.Atan2(field.DirEast[i], field.DirNorth[i]);   // пеленг распространения
            double omega = field.Omega[i] * field.SpeedScale[i] * field.Speed;
            r.EncounterPeriods[i] = SeakeepingModel.EncounterPeriod(omega, field.K[i], u, psi - beta);
        }
        return r;
    }

    public static double Sigma(List<SeakeepingSample> xs, Func<SeakeepingSample, double> f)
    {
        if (xs.Count < 2) return 0.0;
        double m = 0.0;
        foreach (var x in xs) m += f(x);
        m /= xs.Count;
        double v = 0.0;
        foreach (var x in xs) { double d = f(x) - m; v += d * d; }
        return Math.Sqrt(v / xs.Count);
    }

    public static double MaxAbs(List<SeakeepingSample> xs, Func<SeakeepingSample, double> f)
    {
        double m = 0.0;
        foreach (var x in xs) m = Math.Max(m, Math.Abs(f(x)));
        return m;
    }

    /// <summary>
    /// Доминирующий период (с): максимум амплитудного спектра |Σ x_n·e^{−iωt_n}| на сетке
    /// ω = 0,05…3 рад/с, шаг 0,002 рад/с (среднее вычтено). NaN — сигнала нет.
    /// </summary>
    public static double PeakPeriod(List<SeakeepingSample> xs, Func<SeakeepingSample, double> f, double dt)
    {
        int n = xs.Count;
        if (n < 16) return double.NaN;
        var v = new double[n];
        double m = 0.0;
        for (int j = 0; j < n; j++) { v[j] = f(xs[j]); m += v[j]; }
        m /= n;
        double energy = 0.0;
        for (int j = 0; j < n; j++) { v[j] -= m; energy += v[j] * v[j]; }
        if (energy < 1e-18) return double.NaN;

        double best = 0.0, bestW = double.NaN;
        for (double w = 0.05; w <= 3.0; w += 0.002)
        {
            // Поворот фазора рекуррентно: e^{−iω·dt}
            double cr = Math.Cos(w * dt), ci = -Math.Sin(w * dt);
            double pr = 1.0, pi = 0.0, re = 0.0, im = 0.0;
            for (int j = 0; j < n; j++)
            {
                re += v[j] * pr; im += v[j] * pi;
                double t = pr * cr - pi * ci; pi = pr * ci + pi * cr; pr = t;
            }
            double a = re * re + im * im;
            if (a > best) { best = a; bestW = w; }
        }
        return double.IsNaN(bestW) ? double.NaN : 2.0 * Math.PI / bestW;
    }

    public static double Wrap360(double a)
    {
        a %= 360.0;
        return a < 0.0 ? a + 360.0 : a;
    }
}
