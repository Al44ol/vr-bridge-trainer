// ============================================================
// SeakeepingTests.cs — EditMode-тесты качки на волнах (запрос 4)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class SeakeepingTests
{
    private const double Dt = 0.02;
    private const double Deg = 180.0 / Math.PI;

    // Судно сцены (Model45m после Setup Seakeeping)
    private static ShipParameters MakeShip()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 45f; p.beam = 8.6f; p.draft = 3f; p.metacentricHeight = 0.8f;
        p.blockCoefficient = 0.6f; p.waterplaneCoefficient = 0.75f;
        p.rollPeriod = 8.1f; p.heavePeriod = 4.4f; p.pitchPeriod = 4.4f;
        p.rollDamping = 0.1f; p.heaveDamping = 0.25f; p.pitchDamping = 0.25f;
        return p;
    }

    // Волны по умолчанию OceanWaves (λ, a при I = 1, показатель, отклонение от ветра), ветер с 160°
    private static readonly double[,] DefaultWaves =
    {
        { 90.0, 2.5, 1.5, 0.0 }, { 45.0, 1.3, 1.2, 25.0 }, { 20.0, 0.55, 1.0, -20.0 }, { 9.0, 0.18, 0.6, 40.0 },
    };

    private static OceanWaveField MakeDefaultField(float intensity, double windFromDeg = 160.0)
    {
        var f = new OceanWaveField { Intensity = intensity };
        for (int i = 0; i < 4; i++)
        {
            double b = (windFromDeg + 180.0 + DefaultWaves[i, 3]) / Deg;
            float k = (float)(2.0 * Math.PI / DefaultWaves[i, 0]);
            f.SetWave(i, (float)Math.Sin(b), (float)Math.Cos(b), k, k * (float)DefaultWaves[i, 1], (float)DefaultWaves[i, 2], 1f);
        }
        return f;
    }

    // Одна волна: длина λ (м), амплитуда a (м), распространяется на пеленг toDeg
    private static OceanWaveField MakeSingleWave(double lambda, double a, double toDeg)
    {
        var f = new OceanWaveField { Intensity = 1f };
        float k = (float)(2.0 * Math.PI / lambda);
        f.SetWave(0, (float)Math.Sin(toDeg / Deg), (float)Math.Cos(toDeg / Deg), k, (float)(k * a), 1f, 1f);
        for (int i = 1; i < 4; i++) f.ClearWave(i);
        return f;
    }

    // ─────────── Поле волн и сэмплер ───────────

    [Test]
    public void GerstnerInverse_ConvergesToSurfaceHeight()
    {
        // Шторм: Σk·a ≈ 0,55. Точка поверхности — прямой формулой, высота — обратной задачей
        OceanWaveField f = MakeDefaultField(0.85f);
        f.Advance(123.4);
        var rnd = new System.Random(7);
        double[] rms = new double[5];
        double max3 = 0.0;
        const int n = 2000;
        for (int j = 0; j < n; j++)
        {
            double e0 = rnd.NextDouble() * 500.0, n0 = rnd.NextDouble() * 500.0;
            f.SurfacePoint(e0, n0, 0.0, out double e, out double nn, out double h);
            for (int it = 1; it <= 4; it++)
            {
                double err = f.SampleHeight(e, nn, 0.0, it) - h;
                rms[it] += err * err;
                if (it == 3) max3 = Math.Max(max3, Math.Abs(err));
            }
        }
        for (int it = 1; it <= 4; it++) rms[it] = Math.Sqrt(rms[it] / n);
        TestContext.WriteLine($"RMS ошибки высоты, м: 1 ит. {rms[1]:E2}; 2 — {rms[2]:E2}; 3 — {rms[3]:E2}; 4 — {rms[4]:E2}; макс. при 3 — {max3:E2}");

        Assert.Less(rms[2], rms[1]);
        Assert.Less(rms[3], rms[2]);
        Assert.Less(rms[3], 0.02, "3 итерации: RMS < 2 см");
        Assert.Less(max3, 0.15, "3 итерации: максимум < 15 см");
    }

    [Test]
    public void ShaderFormula_MatchesFieldFarFromOrigin()
    {
        // Шейдер: f = k·(D·x_сцены) + φ, φ = k·(D·P_судна) − θ (mod 2π) — float.
        // Поле: f = k·(D·P) − θ — double. Судно в 20 км от начала мира.
        OceanWaveField f = MakeDefaultField(0.85f);
        f.FadeEnd = 0f;   // без затухания
        f.Advance(3600.0);
        double shipE = 20345.6, shipN = -13021.9;
        double maxErr = 0.0;
        for (int j = 0; j < 50; j++)
        {
            float x = -40f + 1.7f * j, z = 30f - 1.3f * j;
            float y = 0f;
            for (int i = 0; i < 4; i++)
            {
                float phi = (float)f.ShaderPhase(i, shipE, shipN);
                float fi = f.K[i] * (f.DirEast[i] * x + f.DirNorth[i] * z) + phi;
                y += (float)(f.Steepness[i] * f.IntensityResponse(i) / f.K[i]) * Mathf.Sin(fi);
            }
            f.SurfacePoint(shipE + x, shipN + z, 0.0, out _, out _, out double h);
            maxErr = Math.Max(maxErr, Math.Abs(h - y));
        }
        Assert.Less(maxErr, 0.002, "шейдер и C# дают одну и ту же поверхность (< 2 мм)");
    }

    [Test]
    public void RenderTimeOffset_IsExact()
    {
        // Фаза кадра с переносом на −dt после шага = фаза до шага
        OceanWaveField f = MakeDefaultField(0.3f);
        f.Speed = 1.3;
        f.Advance(57.1);
        double before = f.ShaderPhase(1, 1000.0, -500.0);
        f.Advance(Dt);
        double after = f.ShaderPhase(1, 1000.0, -500.0, -Dt);
        Assert.AreEqual(0.0, Math.IEEERemainder(after - before, 2.0 * Math.PI), 1e-9);
    }

    // ─────────── МНК и динамика ───────────

    [Test]
    public void LeastSquaresPlane_RecoversExactPlane()
    {
        var x = new List<double>(); var y = new List<double>();
        for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++) { x.Add(-22.5 + 11.25 * i); y.Add(-4.3 + 4.3 * j); }
        double[,] w = SeakeepingModel.LeastSquaresPlane(x.ToArray(), y.ToArray());
        double h = 0, sx = 0, sy = 0;
        for (int j = 0; j < x.Count; j++)
        {
            double z = 0.7 - 0.05 * x[j] + 0.12 * y[j];
            h += w[0, j] * z; sx += w[1, j] * z; sy += w[2, j] * z;
        }
        Assert.AreEqual(0.7, h, 1e-12);
        Assert.AreEqual(-0.05, sx, 1e-12);
        Assert.AreEqual(0.12, sy, 1e-12);
    }

    [Test]
    public void Oscillator_FreeDecay_MatchesAnalytic()
    {
        const double T0 = 8.0, zeta = 0.1;
        double x = 1.0, v = 0.0;
        for (int n = 0; n < 500; n++) SeakeepingModel.StepOscillator(ref x, ref v, 0.0, T0, zeta, Dt);
        double t = 500 * Dt, w0 = 2 * Math.PI / T0, s = zeta * w0, wd = w0 * Math.Sqrt(1 - zeta * zeta);
        double expected = Math.Exp(-s * t) * (Math.Cos(wd * t) + s / wd * Math.Sin(wd * t));
        Assert.AreEqual(expected, x, 1e-9);
    }

    [Test]
    public void Oscillator_ConstantInput_StaticGainIsOne()
    {
        double x = 0.0, v = 0.0;
        for (int n = 0; n < 30000; n++) SeakeepingModel.StepOscillator(ref x, ref v, 0.25, 8.0, 0.1, Dt);
        Assert.AreEqual(0.25, x, 1e-9);
    }

    [Test]
    public void SoftLimit_BoundedAndSmooth()
    {
        Assert.AreEqual(0.0, SeakeepingModel.SoftLimit(0.0, 20.0), 1e-12);
        Assert.AreEqual(1.0, SeakeepingModel.SoftLimit(1e-3, 20.0) / 1e-3, 1e-5, "у нуля наклон 1 — малая качка не искажается");
        Assert.Less(SeakeepingModel.SoftLimit(1000.0, 20.0), 20.0 + 1e-9);
        Assert.Greater(SeakeepingModel.SoftLimit(-1000.0, 20.0), -20.0 - 1e-9);
        double prev = -1e9;
        for (double a = -100; a <= 100; a += 0.5)
        {
            double y = SeakeepingModel.SoftLimit(a, 20.0);
            Assert.Greater(y, prev, "монотонно");
            prev = y;
        }
        Assert.AreEqual(5.0, SeakeepingModel.SoftLimit(5.0, 0.0), 1e-12, "предел 0 — выключен");
    }

    // ─────────── Отклик на волны ───────────

    // Прямой курс psiDeg со скоростью u (м/с) по полю f; возвращает σ отклика после переходного процесса
    private static SeakeepingResult RunStraight(OceanWaveField f, double headingRelDeg, double knots, float intensity, double duration = 400)
    {
        var c = new SeakeepingCase { Preset = "test", Intensity = intensity, HeadingDeg = headingRelDeg, SpeedKnots = knots };
        var s = new SeakeepingTrialSettings { Duration = duration, Transient = 100, SampleInterval = 0.1 };
        return SeakeepingTrials.Run(MakeShip(), f, c, s);
    }

    // АЧХ звена 2-го порядка: |H| = 1/√((1 − Λ²)² + (2ζΛ)²), Λ = T₀/T_e
    private static double Rao(double t0, double zeta, double te)
    {
        double l = t0 / te;
        return 1.0 / Math.Sqrt((1 - l * l) * (1 - l * l) + (2 * zeta * l) * (2 * zeta * l));
    }

    [Test]
    public void LongWave_ResponseMatchesTransferFunction()
    {
        // λ = 2000 м (T ≈ 36 с ≫ T_z, T_φ): судно повторяет поверхность, отклик = возмущение·|H|
        OceanWaveField f = MakeSingleWave(2000.0, 1.0, 0.0);
        ShipParameters p = MakeShip();
        double tw = 2 * Math.PI / Math.Sqrt(OceanWaveField.Gravity * f.K[0]);

        SeakeepingResult head = RunStraight(f, 0, 0, 1f, 600);
        Assert.AreEqual(1.0 / Math.Sqrt(2.0), head.SigmaWaveHeave, 0.01, "σ возмущения = a/√2 (осреднение по 45 м ничтожно)");
        Assert.AreEqual(head.SigmaWaveHeave * Rao(p.heavePeriod, p.heaveDamping, tw), head.SigmaHeave, 0.02 * head.SigmaHeave);
        Assert.AreEqual(head.SigmaWavePitchDeg * Rao(p.pitchPeriod, p.pitchDamping, tw), head.SigmaPitchDeg, 0.02 * head.SigmaPitchDeg);

        SeakeepingResult beam = RunStraight(f, 90, 0, 1f, 600);
        Assert.AreEqual(beam.SigmaWaveRollDeg * Rao(p.rollPeriod, p.rollDamping, tw), beam.SigmaRollDeg, 0.02 * beam.SigmaRollDeg);
        TestContext.WriteLine($"T волны {tw:F1} с: |H| z {Rao(p.heavePeriod, p.heaveDamping, tw):F3}, φ {Rao(p.rollPeriod, p.rollDamping, tw):F3}; " +
                              $"σ_z {head.SigmaHeave:F3} м, σ_φ/σ_φw {beam.SigmaRollDeg / beam.SigmaWaveRollDeg:F3}");
    }

    [Test]
    public void ResonantWave_RollAmplifiedBy1Over2Zeta()
    {
        // Волна с периодом T_φ лагом: крен = уклон·1/(2ζ)
        ShipParameters p = MakeShip();
        double k = Math.Pow(2 * Math.PI / p.rollPeriod, 2) / OceanWaveField.Gravity;
        OceanWaveField f = MakeSingleWave(2 * Math.PI / k, 0.05, 0.0);
        SeakeepingResult beam = RunStraight(f, 90, 0, 1f, 800);
        double gain = beam.SigmaRollDeg / beam.SigmaWaveRollDeg;
        TestContext.WriteLine($"λ = {2 * Math.PI / k:F1} м, усиление крена {gain:F2}, теория 1/(2ζ) = {1 / (2 * p.rollDamping):F2}");
        Assert.AreEqual(1 / (2 * p.rollDamping), gain, 0.05 / (2 * p.rollDamping));
    }

    [Test]
    public void ShortWave_IsFilteredOut()
    {
        // λ = 9 м: короткая волна почти не качает судно (осреднение по корпусу + АЧХ).
        // Лагом 3 точки поперёк (шаг 4,3 м ≈ λ/2) осредняют волну грубо — отсюда до ~13 % по z
        // (для сплошного корпуса sin(kB/2)/(kB/2) ≈ 5 %); в абсолютных величинах — сантиметры.
        OceanWaveField f = MakeSingleWave(9.0, 0.3, 0.0);
        double waveSigma = 0.3 / Math.Sqrt(2.0);
        foreach (double q in new[] { 0.0, 45.0, 90.0 })
        {
            SeakeepingResult r = RunStraight(f, q, 0, 1f);
            TestContext.WriteLine($"q = {q}°: σ_z = {r.SigmaHeave:F4} м ({r.SigmaHeave / waveSigma:P1} волны), σ_φ = {r.SigmaRollDeg:F3}°, σ_θ = {r.SigmaPitchDeg:F3}°");
            Assert.Less(r.SigmaHeave, 0.15 * waveSigma);
            Assert.Less(r.SigmaRollDeg, 0.5);
            Assert.Less(r.SigmaPitchDeg, 0.2);
        }
    }

    [TestCase(0.0)]     // носом на волну
    [TestCase(180.0)]   // волна попутная
    [TestCase(60.0)]
    public void EncounterPeriod_MatchesTheory(double q)
    {
        // Одна волна λ = 90 м, ход 15 уз: период возмущения = 2π/|ω − k·U·cos μ|
        OceanWaveField f = MakeSingleWave(90.0, 1.0, 0.0);
        double knots = 15.0, u = knots * SeakeepingTrials.KnotsToMs;
        SeakeepingResult r = RunStraight(f, q, knots, 1f, 600);

        double mu = (180.0 - q) / Deg;
        double theory = SeakeepingModel.EncounterPeriod(Math.Sqrt(OceanWaveField.Gravity * f.K[0]), f.K[0], u, mu);
        Assert.AreEqual(theory, r.EncounterPeriods[0], 1e-9);

        double measured = MeanUpCrossingPeriod(r.Samples, x => x.WaveHeave, 100.0);
        TestContext.WriteLine($"q = {q}°: T_e теория {theory:F3} с, измерено {measured:F3} с; T волны {2 * Math.PI / Math.Sqrt(9.81 * f.K[0]):F3} с");
        Assert.AreEqual(theory, measured, 0.01 * theory);
    }

    [Test]
    public void Storm_BeamSeasRoll_HeadSeasPitch()
    {
        // Лагом — больше бортовой, носом на волну — больше килевой
        OceanWaveField f = MakeDefaultField(0.85f);
        SeakeepingResult head = RunStraight(f, 0, 0, 0.85f);
        SeakeepingResult beam = RunStraight(f, 90, 0, 0.85f);
        TestContext.WriteLine($"Носом: σ_φ {head.SigmaRollDeg:F2}°, σ_θ {head.SigmaPitchDeg:F2}°, σ_z {head.SigmaHeave:F2} м; " +
                              $"лагом: σ_φ {beam.SigmaRollDeg:F2}°, σ_θ {beam.SigmaPitchDeg:F2}°, σ_z {beam.SigmaHeave:F2} м");
        // Волны идут веером ±40° от ветра, поэтому и лагом есть килевая качка от волн 2–4
        Assert.Greater(beam.SigmaRollDeg, 3.0 * head.SigmaRollDeg);
        Assert.Greater(head.SigmaPitchDeg, 1.5 * beam.SigmaPitchDeg);
    }

    [Test]
    public void Calm_AlmostNoMotion()
    {
        OceanWaveField f = MakeDefaultField(0.08f);
        foreach (double q in new[] { 0.0, 90.0 })
        {
            SeakeepingResult r = RunStraight(f, q, 0, 0.08f);
            TestContext.WriteLine($"Штиль, q = {q}°: max φ {r.MaxRollDeg:F2}°, max θ {r.MaxPitchDeg:F2}°, max z {r.MaxHeave:F3} м");
            Assert.Less(r.MaxRollDeg, 1.5);
            Assert.Less(r.MaxPitchDeg, 0.5);
            Assert.Less(r.MaxHeave, 0.15);
        }
    }

    [Test]
    public void Model_IsDeterministic()
    {
        OceanWaveField f = MakeDefaultField(0.85f);
        SeakeepingResult a = RunStraight(f, 45, 10, 0.85f, 200);
        SeakeepingResult b = RunStraight(f, 45, 10, 0.85f, 200);
        Assert.AreEqual(a.Samples.Count, b.Samples.Count);
        for (int i = 0; i < a.Samples.Count; i++)
        {
            Assert.AreEqual(a.Samples[i].Roll, b.Samples[i].Roll);
            Assert.AreEqual(a.Samples[i].Pitch, b.Samples[i].Pitch);
            Assert.AreEqual(a.Samples[i].Heave, b.Samples[i].Heave);
        }
    }

    [Test]
    public void PeriodEstimates_MatchFormulas()
    {
        // IS Code 2008, ч. A, 2.3.4: C = 0,373 + 0,023·B/d − 0,043·L/100; T = 2CB/√GM
        double c = 0.373 + 0.023 * (8.6 / 3.0) - 0.043 * 0.45;
        Assert.AreEqual(2 * c * 8.6 / Math.Sqrt(0.8), SeakeepingModel.RollPeriodIsCode(45, 8.6, 3, 0.8), 1e-12);
        Assert.AreEqual(8.07, SeakeepingModel.RollPeriodIsCode(45, 8.6, 3, 0.8), 0.01);
        // T_z = 2π√(2·C_B·d/(C_WP·g))
        Assert.AreEqual(4.40, SeakeepingModel.HeavePeriod(3, 0.6, 0.75), 0.01);
    }

    // Средний период по пересечениям нуля снизу вверх (с линейной интерполяцией)
    private static double MeanUpCrossingPeriod(List<SeakeepingSample> xs, Func<SeakeepingSample, double> f, double tFrom)
    {
        var steady = xs.FindAll(x => x.T >= tFrom);
        double mean = 0.0;
        foreach (var x in steady) mean += f(x);
        mean /= steady.Count;
        double first = double.NaN, last = double.NaN;
        int count = 0;
        for (int i = 1; i < steady.Count; i++)
        {
            double a = f(steady[i - 1]) - mean, b = f(steady[i]) - mean;
            if (a < 0.0 && b >= 0.0)
            {
                double t = steady[i - 1].T + (steady[i].T - steady[i - 1].T) * (-a / (b - a));
                if (double.IsNaN(first)) first = t; else count++;
                last = t;
            }
        }
        return count > 0 ? (last - first) / count : double.NaN;
    }
}
