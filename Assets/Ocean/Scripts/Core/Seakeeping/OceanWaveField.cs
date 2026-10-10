// ============================================================
// OceanWaveField.cs — Поле волн Герстнера: единый источник для шейдера и C# (запрос 4)
//
// Чистый C#-класс (сборка VRBridge.Core). Его заполняет OceanWaves из своих
// сериализуемых параметров; из него же OceanWaves берёт значения для шейдера
// OceanSurface, а сэмплер качки (SeakeepingModel), буи (B1) и чужие суда (B3)
// читают высоту воды. Константы волн хранятся только здесь.
//
// Волна i (всё в мировых координатах тренажёра: E — восток, N — север, м):
//   f_i(P) = k_i·(D_i·P) − θ_i,                 θ_i — временная фаза,
//   θ_i(t + dt) = θ_i(t) + ω_i·c_i·s·dt,         ω_i = √(g·k_i) (глубокая вода),
//   a_i = (k·a)_i⁰ · I^{n_i} · F(ρ) · M_i(ρ) / k_i,
//     I — интенсивность волнения (_WaveIntensity), n_i — показатель,
//     F — затухание с расстоянием ρ от центра океана (судна),
//     M_i — подавление волн мельче шага сетки (как в шейдере);
//   точка поверхности, «рождённая» в P₀:
//     P = P₀ + Σ D_i·a_i·cos f_i(P₀),   z = Σ a_i·sin f_i(P₀).
//
// Высота воды в заданной точке P: волна Герстнера сдвигает точки по горизонтали,
// поэтому сначала находится P₀ — итерациями P₀ ← P − Σ D_i·a_i·cos f_i(P₀)
// (сжимающее отображение при Σ k_i·a_i < 1), затем z(P₀).
//
// Шейдер работает в координатах сцены x = P − P_судна, его фаза
// φ_i = k_i·(D_i·P_судна) − θ_i (mod 2π) считается здесь в double —
// f совпадает с C# с точностью до округления float в шейдере.
// ============================================================
using System;

public sealed class OceanWaveField
{
    public const int WaveCount = 4;
    public const double Gravity = 9.81;
    private const double TwoPi = 2.0 * Math.PI;

    // ── Параметры волн (float — те же значения, что получает шейдер) ──
    /// <summary>Направление распространения: восток, север (единичный вектор).</summary>
    public readonly float[] DirEast = new float[WaveCount], DirNorth = new float[WaveCount];
    /// <summary>Волновое число k = 2π/λ, 1/м.</summary>
    public readonly float[] K = new float[WaveCount];
    /// <summary>Крутизна (k·a)⁰ при I = 1, безразмерная; 0 — волны нет.</summary>
    public readonly float[] Steepness = new float[WaveCount];
    /// <summary>Показатель n: амплитуда ∝ I^n.</summary>
    public readonly float[] Exponent = new float[WaveCount];
    /// <summary>Множитель скорости волны c_i (1 — физическая дисперсия).</summary>
    public readonly float[] SpeedScale = new float[WaveCount];
    /// <summary>Круговая частота ω = √(g·k), рад/с.</summary>
    public readonly double[] Omega = new double[WaveCount];

    // ── Состояние ──
    /// <summary>Временная фаза θ_i, рад, 0..2π.</summary>
    public readonly double[] TimePhase = new double[WaveCount];
    /// <summary>Интенсивность волнения I (= _WaveIntensity материала воды).</summary>
    public float Intensity = 0.3f;
    /// <summary>Множитель скорости s от погоды (s = 1 + (_WaveSpeed − 1)·speedInfluence).</summary>
    public double Speed = 1.0;

    // ── Эффекты расстояния (как в шейдере) ──
    /// <summary>Центр океана (= позиция судна), м — от него считается затухание.</summary>
    public double CenterEast, CenterNorth;
    /// <summary>Затухание волн с расстоянием: начало и конец, м (_WaveFadeStart/_WaveFadeEnd). End ≤ Start — выключено.</summary>
    public float FadeStart = 1500f, FadeEnd = 6000f;
    /// <summary>Шаг сетки океана: max(MeshMinStep, ρ·MeshAngleStep), м (OceanRadialMesh). 0 — подавление выключено.</summary>
    public float MeshMinStep, MeshAngleStep;

    public OceanWaveField()
    {
        for (int i = 0; i < WaveCount; i++) { K[i] = 1f; Exponent[i] = 1f; SpeedScale[i] = 1f; }
    }

    /// <summary>Задать волну i. dir — направление распространения (восток, север), нормируется вызывающим.</summary>
    public void SetWave(int i, float dirEast, float dirNorth, float k, float steepness, float exponent, float speedScale)
    {
        DirEast[i] = dirEast;
        DirNorth[i] = dirNorth;
        K[i] = Math.Max(k, 1e-4f);
        Steepness[i] = Math.Max(0f, steepness);
        Exponent[i] = Math.Max(0.01f, exponent);
        SpeedScale[i] = speedScale;
        Omega[i] = Math.Sqrt(Gravity * K[i]);
    }

    /// <summary>Волна i отсутствует (амплитуда 0).</summary>
    public void ClearWave(int i) => SetWave(i, 1f, 0f, 1f, 0f, 1f, 1f);

    /// <summary>Продвинуть временные фазы на dt (с) — в симуляционном времени (FixedUpdate).</summary>
    public void Advance(double dt)
    {
        for (int i = 0; i < WaveCount; i++)
            TimePhase[i] = Wrap(TimePhase[i] + Omega[i] * SpeedScale[i] * Speed * dt);
    }

    /// <summary>
    /// Фаза для шейдера: φ_i = k·(D·P_судна) − θ_i(t + timeOffset), рад 0..2π.
    /// timeOffset — сдвиг времени отображения относительно последнего шага (с, обычно ≤ 0).
    /// </summary>
    public double ShaderPhase(int i, double shipEast, double shipNorth, double timeOffset = 0.0)
    {
        double spatial = Wrap((double)K[i] * ((double)DirEast[i] * shipEast + (double)DirNorth[i] * shipNorth));
        return Wrap(spatial - TimePhase[i] - Omega[i] * SpeedScale[i] * Speed * timeOffset);
    }

    /// <summary>Множитель амплитуды от интенсивности: I^n (как IntensityResponse в шейдере).</summary>
    public double IntensityResponse(int i) => Math.Pow(Math.Max(Intensity, 1e-4f), Math.Max(Exponent[i], 0.01f));

    /// <summary>Амплитуда волны i, м, в точке на расстоянии rho (м) от центра океана.</summary>
    public double Amplitude(int i, double rho)
    {
        double ka = Steepness[i] * IntensityResponse(i) * DistanceFade(rho) * MeshAntiAlias(i, rho);
        return ka / K[i];
    }

    /// <summary>Затухание с расстоянием: 1 − smoothstep(FadeStart, FadeEnd, ρ).</summary>
    public double DistanceFade(double rho)
    {
        if (FadeEnd <= FadeStart) return 1.0;
        return 1.0 - SmoothStep(FadeStart, FadeEnd, rho);
    }

    /// <summary>Подавление волны мельче шага сетки: 1 − smoothstep(λ/4, λ/2, шаг).</summary>
    public double MeshAntiAlias(int i, double rho)
    {
        double step = Math.Max(MeshMinStep, rho * MeshAngleStep);
        if (step <= 0.0) return 1.0;
        double lambda = TwoPi / K[i];
        return 1.0 - SmoothStep(lambda * 0.25, lambda * 0.5, step);
    }

    /// <summary>Сумма крутизны Σ k·a при текущей интенсивности (< 1 — итерации сходятся).</summary>
    public double TotalSteepness()
    {
        double s = 0.0;
        for (int i = 0; i < WaveCount; i++) s += Steepness[i] * IntensityResponse(i);
        return s;
    }

    /// <summary>
    /// Высота воды над средним уровнем (м) в мировой точке (east, north).
    /// timeOffset — сдвиг времени относительно последнего шага Advance (с).
    /// iterations — число итераций поиска исходной точки P₀ (2–3 достаточно).
    /// </summary>
    public double SampleHeight(double east, double north, double timeOffset = 0.0, int iterations = 3)
    {
        // Фаза в самой точке P (в double, по модулю 2π) и амплитуды — один раз
        double rho = Math.Sqrt((east - CenterEast) * (east - CenterEast) + (north - CenterNorth) * (north - CenterNorth));
        double p0 = 0, p1 = 0, p2 = 0, p3 = 0, a0 = 0, a1 = 0, a2 = 0, a3 = 0;
        BasePhaseAndAmplitude(0, east, north, timeOffset, rho, ref p0, ref a0);
        BasePhaseAndAmplitude(1, east, north, timeOffset, rho, ref p1, ref a1);
        BasePhaseAndAmplitude(2, east, north, timeOffset, rho, ref p2, ref a2);
        BasePhaseAndAmplitude(3, east, north, timeOffset, rho, ref p3, ref a3);

        // Смещение P₀ − P (м): P₀ ← P − Σ D·a·cos f(P₀)
        double dE = 0.0, dN = 0.0;
        for (int it = 0; it < iterations; it++)
        {
            double f0 = p0 + K[0] * (DirEast[0] * dE + DirNorth[0] * dN);
            double f1 = p1 + K[1] * (DirEast[1] * dE + DirNorth[1] * dN);
            double f2 = p2 + K[2] * (DirEast[2] * dE + DirNorth[2] * dN);
            double f3 = p3 + K[3] * (DirEast[3] * dE + DirNorth[3] * dN);
            double c0 = a0 * Math.Cos(f0), c1 = a1 * Math.Cos(f1), c2 = a2 * Math.Cos(f2), c3 = a3 * Math.Cos(f3);
            dE = -(DirEast[0] * c0 + DirEast[1] * c1 + DirEast[2] * c2 + DirEast[3] * c3);
            dN = -(DirNorth[0] * c0 + DirNorth[1] * c1 + DirNorth[2] * c2 + DirNorth[3] * c3);
        }

        return a0 * Math.Sin(p0 + K[0] * (DirEast[0] * dE + DirNorth[0] * dN))
             + a1 * Math.Sin(p1 + K[1] * (DirEast[1] * dE + DirNorth[1] * dN))
             + a2 * Math.Sin(p2 + K[2] * (DirEast[2] * dE + DirNorth[2] * dN))
             + a3 * Math.Sin(p3 + K[3] * (DirEast[3] * dE + DirNorth[3] * dN));
    }

    /// <summary>
    /// Точка поверхности, «рождённая» в (east0, north0) — прямая формула Герстнера (как в вершинном шейдере).
    /// Нужна для проверки обратной задачи.
    /// </summary>
    public void SurfacePoint(double east0, double north0, double timeOffset,
                             out double east, out double north, out double height)
    {
        double rho = Math.Sqrt((east0 - CenterEast) * (east0 - CenterEast) + (north0 - CenterNorth) * (north0 - CenterNorth));
        east = east0; north = north0; height = 0.0;
        for (int i = 0; i < WaveCount; i++)
        {
            double p = 0, a = 0;
            BasePhaseAndAmplitude(i, east0, north0, timeOffset, rho, ref p, ref a);
            east += DirEast[i] * a * Math.Cos(p);
            north += DirNorth[i] * a * Math.Cos(p);
            height += a * Math.Sin(p);
        }
    }

    private void BasePhaseAndAmplitude(int i, double east, double north, double timeOffset, double rho,
                                       ref double phase, ref double amplitude)
    {
        double spatial = Wrap((double)K[i] * ((double)DirEast[i] * east + (double)DirNorth[i] * north));
        phase = spatial - TimePhase[i] - Omega[i] * SpeedScale[i] * Speed * timeOffset;
        amplitude = Amplitude(i, rho);
    }

    /// <summary>smoothstep как в HLSL.</summary>
    public static double SmoothStep(double a, double b, double x)
    {
        double t = (x - a) / (b - a);
        t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
        return t * t * (3.0 - 2.0 * t);
    }

    public static double Wrap(double x, double period = TwoPi)
    {
        x %= period;
        return x < 0.0 ? x + period : x;
    }

    /// <summary>Скопировать параметры и состояние (для стенда и тестов).</summary>
    public void CopyFrom(OceanWaveField o)
    {
        Array.Copy(o.DirEast, DirEast, WaveCount); Array.Copy(o.DirNorth, DirNorth, WaveCount);
        Array.Copy(o.K, K, WaveCount); Array.Copy(o.Steepness, Steepness, WaveCount);
        Array.Copy(o.Exponent, Exponent, WaveCount); Array.Copy(o.SpeedScale, SpeedScale, WaveCount);
        Array.Copy(o.Omega, Omega, WaveCount); Array.Copy(o.TimePhase, TimePhase, WaveCount);
        Intensity = o.Intensity; Speed = o.Speed;
        CenterEast = o.CenterEast; CenterNorth = o.CenterNorth;
        FadeStart = o.FadeStart; FadeEnd = o.FadeEnd;
        MeshMinStep = o.MeshMinStep; MeshAngleStep = o.MeshAngleStep;
    }
}
