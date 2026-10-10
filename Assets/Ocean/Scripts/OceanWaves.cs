// ============================================================
// OceanWaves.cs — Параметры волн Герстнера и их фазы
//
// Единый источник параметров волн для шейдера OceanSurface
// (и в будущем — для расчёта качки на CPU).
//
// Плавающее начало координат: шейдер работает с координатами сцены
// (около судна), а смещение судна в мире учитывается фазой волны.
// Фазы считаются здесь в double по модулю 2π:
//   φᵢ = (kᵢ · dot(Dᵢ, позиция судна) − θᵢ) mod 2π,
//   θᵢ += ωᵢ · speedScaleᵢ · s · dt,   ωᵢ = √(g·kᵢ)  (дисперсия глубокой воды),
//   s = 1 + (_WaveSpeed − 1) · speedInfluence.
// В шейдер уходят готовые фазы — точность не теряется даже через 20+ км.
//
// Направление волн задаётся одним параметром — направлением ветра
// (морской пеленг «откуда дует», 0° = N, по часовой). Каждая волна
// отклонена от направления ветра на свой угол (веер ±40°).
//
// Кроме 4 «геометрических» волн есть 4 мелкие волны ряби — они влияют
// только на нормали (попиксельно), вершины не смещают.
//
// Барашки привязаны к воде: смещение шума пены считается в double
// по модулю периода шума; два слоя попеременно появляются и гаснут.
//
// _WaveIntensity и _WaveSpeed остаются свойствами материала
// (их пишут ShipMotion и WeatherController). Всё остальное передаётся
// глобальными переменными шейдера — ассет материала не изменяется.
// ============================================================
using UnityEngine;

[ExecuteAlways]
public class OceanWaves : MonoBehaviour
{
    [System.Serializable]
    public struct Wave
    {
        [Tooltip("Отклонение направления волны от направления ветра, градусы")]
        public float angleOffsetDeg;
        [Tooltip("Длина волны, м")]
        public float wavelength;
        [Tooltip("Амплитуда (полувысота), м — при _WaveIntensity = 1")]
        public float amplitude;
        [Tooltip("Показатель степени: амплитуда = amplitude · _WaveIntensity^exponent. " +
                 ">1 — волна растёт только в сильное волнение (зыбь), <1 — есть и в штиль (рябь)")]
        public float intensityExponent;
        [Tooltip("Множитель скорости (1 = физическая дисперсия)")]
        public float speedScale;
    }

    public const int WaveCount = 4;
    private const double TwoPi = 2.0 * System.Math.PI;
    private const double Gravity = 9.81;

    [Header("Ветер")]
    [Tooltip("Откуда дует ветер, морской пеленг (0° = N, 90° = E). " +
             "Нос судна сейчас смотрит на юг (180°), 160° — волны идут навстречу, чуть с левой скулы.")]
    [Range(0f, 360f)] public float windFromDeg = 160f;

    [Header("Скорость волн")]
    [Tooltip("Насколько _WaveSpeed из погоды ускоряет волны: 0 — только физика, 1 — как раньше. " +
             "Скорость длинной волны и так больше короткой (дисперсия).")]
    [Range(0f, 1f)] public float speedInfluence = 0.25f;

    // Масштаб волн подобран под мостик (глаза ~7–20 м над водой).
    // Высота волны ≈ 2·Σ амплитуд при seaState пресетов:
    //   штиль 0.08 → ~0.3 м, умеренное 0.3 → ~1.5 м, шторм 0.85 → ~5–7 м.
    // Σ k·a при _WaveIntensity = 1 ≈ 0.6 < 1 — гребни не «заворачиваются».
    [Header("Волны (смещают вершины)")]
    public Wave[] waves = DefaultWaves();

    [Header("Рябь (только нормали)")]
    public Wave[] detailWaves = DefaultDetailWaves();
    [Tooltip("Общий множитель силы ряби (1 = значения выше)")]
    [Range(0f, 2f)] public float detailStrength = 0.6f;

    [Header("Барашки (пена на гребнях)")]
    [Tooltip("Время жизни барашка, с: два слоя шума попеременно гаснут и появляются со случайным сдвигом")]
    public float whitecapLifetime = 7f;
    [Tooltip("Скорость дрейфа пены по ветру при _WaveIntensity = 1, м/с")]
    public float foamDriftSpeed = 0.6f;

    [Header("Ссылки")]
    [Tooltip("Материал воды — отсюда читаются _WaveSpeed и _WaveIntensity")]
    public Material oceanMaterial;
    [Tooltip("Плавающее начало координат. Если пусто — берётся WorldOrigin.Instance.")]
    public WorldOrigin worldOrigin;

    // Период шума пены в шейдере (м) и растяжение пятен вдоль ветра — должны совпадать с OceanSurface.shader
    public const double FoamPeriod = 240.0;
    public const double FoamStretch = 2.5;

    // Накопленная временная фаза каждой волны (рад, 0..2π) и дрейф пены (м)
    private readonly double[] timePhase = new double[WaveCount];
    private readonly double[] detailTimePhase = new double[WaveCount];
    private double foamDrift;
    private double foamCycle;                       // фаза цикла барашков, 0..1
    private Vector2 foamShiftA, foamShiftB;         // случайные сдвиги слоёв (м)
    private System.Random foamRandom = new System.Random(12345);

    private static readonly int WaveSpeedId     = Shader.PropertyToID("_WaveSpeed");
    private static readonly int WaveIntensityId = Shader.PropertyToID("_WaveIntensity");
    private static readonly int DirXId      = Shader.PropertyToID("_OceanWaveDirX");
    private static readonly int DirZId      = Shader.PropertyToID("_OceanWaveDirZ");
    private static readonly int KId         = Shader.PropertyToID("_OceanWaveK");
    private static readonly int AmpId       = Shader.PropertyToID("_OceanWaveAmp");
    private static readonly int ExpId       = Shader.PropertyToID("_OceanWaveExp");
    private static readonly int PhasesId    = Shader.PropertyToID("_OceanWavePhases");
    private static readonly int DDirXId     = Shader.PropertyToID("_OceanDetailDirX");
    private static readonly int DDirZId     = Shader.PropertyToID("_OceanDetailDirZ");
    private static readonly int DKId        = Shader.PropertyToID("_OceanDetailK");
    private static readonly int DAmpId      = Shader.PropertyToID("_OceanDetailAmp");
    private static readonly int DExpId      = Shader.PropertyToID("_OceanDetailExp");
    private static readonly int DPhasesId   = Shader.PropertyToID("_OceanDetailPhases");
    private static readonly int WindDirId   = Shader.PropertyToID("_OceanWindDir");
    private static readonly int FoamOffsetId = Shader.PropertyToID("_OceanFoamOffset");
    private static readonly int FoamBlendId  = Shader.PropertyToID("_OceanFoamBlend");

    public static Wave[] DefaultWaves() => new Wave[WaveCount]
    {
        // Волна 1 — длинная зыбь, заметна только в сильное волнение
        new Wave { angleOffsetDeg =   0f, wavelength = 90f, amplitude = 2.5f,  intensityExponent = 1.5f, speedScale = 1f },
        // Волна 2 — основная ветровая волна
        new Wave { angleOffsetDeg =  25f, wavelength = 45f, amplitude = 1.3f,  intensityExponent = 1.2f, speedScale = 1f },
        // Волна 3 — короткая ветровая волна
        new Wave { angleOffsetDeg = -20f, wavelength = 20f, amplitude = 0.55f, intensityExponent = 1.0f, speedScale = 1f },
        // Волна 4 — мелкая волна, есть даже в штиль
        new Wave { angleOffsetDeg =  40f, wavelength =  9f, amplitude = 0.18f, intensityExponent = 0.6f, speedScale = 1f },
    };

    public static Wave[] DefaultDetailWaves() => new Wave[WaveCount]
    {
        // Рябь: k·a ≈ 0.05–0.08 при _WaveIntensity = 1, показатель 0.4 — заметна и в штиль
        new Wave { angleOffsetDeg = -50f, wavelength = 4.1f,  amplitude = 0.05f,  intensityExponent = 0.4f, speedScale = 1f },
        new Wave { angleOffsetDeg =  35f, wavelength = 2.3f,  amplitude = 0.025f, intensityExponent = 0.4f, speedScale = 1f },
        new Wave { angleOffsetDeg = -15f, wavelength = 1.3f,  amplitude = 0.012f, intensityExponent = 0.4f, speedScale = 1f },
        new Wave { angleOffsetDeg =  70f, wavelength = 0.75f, amplitude = 0.006f, intensityExponent = 0.4f, speedScale = 1f },
    };

    /// <summary>Вернуть параметры волн к значениям по умолчанию (используется Editor-скриптом).</summary>
    public void ResetWavesToDefaults()
    {
        waves = DefaultWaves();
        detailWaves = DefaultDetailWaves();
    }

    /// <summary>Направление распространения волн (по ветру): x = восток, y = север.</summary>
    public Vector2 WindDirection => BearingToVector(windFromDeg + 180f);

    void OnEnable()
    {
        PushToShader(0.0);
    }

    void OnValidate()
    {
        PushToShader(0.0);
    }

    void LateUpdate()
    {
        // В редакторе без Play волны стоят на месте
        PushToShader(Application.isPlaying ? Time.deltaTime : 0.0);
    }

    private static double Wrap(double x, double period = TwoPi)
    {
        x %= period;
        return x < 0.0 ? x + period : x;
    }

    // Морской пеленг (0° = N, по часовой) → вектор (восток, север)
    private static Vector2 BearingToVector(float bearingDeg)
    {
        float b = bearingDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(b), Mathf.Cos(b));
    }

    private void PushToShader(double dt)
    {
        float waveSpeed = 1f, intensity = 0.3f;
        if (oceanMaterial != null)
        {
            if (oceanMaterial.HasProperty(WaveSpeedId))     waveSpeed = oceanMaterial.GetFloat(WaveSpeedId);
            if (oceanMaterial.HasProperty(WaveIntensityId)) intensity = oceanMaterial.GetFloat(WaveIntensityId);
        }
        double speed = 1.0 + (waveSpeed - 1.0) * speedInfluence;

        WorldOrigin origin = worldOrigin != null ? worldOrigin : WorldOrigin.Instance;
        WorldPosition ship = origin != null ? origin.ShipPosition : default;

        PushSet(waves, timePhase, ship, speed, dt, 1f, DirXId, DirZId, KId, AmpId, ExpId, PhasesId);
        PushSet(detailWaves, detailTimePhase, ship, speed, dt, detailStrength, DDirXId, DDirZId, DKId, DAmpId, DExpId, DPhasesId);

        // Пена: координаты вдоль ветра (u) и поперёк (v), по модулю периода шума.
        // Шейдер берёт шум в точке ((u + u0)/stretch, v + v0), период по u — FoamPeriod·stretch.
        // Два слоя (A, B) с весами wA = ½ + ½·cos 2πp и wB = 1 − wA (p — фаза цикла):
        // когда вес слоя равен 0, его сдвиг заменяется случайным — барашки
        // появляются и исчезают в случайных местах, а не стоят пятнами.
        Vector2 wind = WindDirection;
        foamDrift = Wrap(foamDrift + foamDriftSpeed * intensity * dt, FoamPeriod * FoamStretch);
        double u0 = (double)wind.x * ship.East + (double)wind.y * ship.North - foamDrift;
        double v0 = (double)wind.y * ship.East - (double)wind.x * ship.North;

        double prevCycle = foamCycle;
        foamCycle += dt / System.Math.Max(0.5, whitecapLifetime);
        if (prevCycle < 0.5 && foamCycle >= 0.5) foamShiftA = RandomShift();   // слой A невидим
        if (foamCycle >= 1.0) { foamCycle -= 1.0; foamShiftB = RandomShift(); } // слой B невидим
        float weightA = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * (float)foamCycle);

        double uPeriod = FoamPeriod * FoamStretch;
        Shader.SetGlobalVector(WindDirId, new Vector4(wind.x, wind.y, 0f, 0f));
        Shader.SetGlobalVector(FoamOffsetId, new Vector4(
            (float)Wrap(u0 + foamShiftA.x, uPeriod), (float)Wrap(v0 + foamShiftA.y, FoamPeriod),
            (float)Wrap(u0 + foamShiftB.x, uPeriod), (float)Wrap(v0 + foamShiftB.y, FoamPeriod)));
        Shader.SetGlobalVector(FoamBlendId, new Vector4(weightA, 1f - weightA, 0f, 0f));
    }

    private Vector2 RandomShift()
    {
        return new Vector2((float)(foamRandom.NextDouble() * FoamPeriod * FoamStretch),
                           (float)(foamRandom.NextDouble() * FoamPeriod));
    }

    private void PushSet(Wave[] set, double[] phaseAcc, WorldPosition ship, double speed, double dt, float ampScale,
                         int dirXId, int dirZId, int kId, int ampId, int expId, int phasesId)
    {
        Vector4 dirX = Vector4.zero, dirZ = Vector4.zero, kVec = Vector4.zero, amp = Vector4.zero, exps = Vector4.one, phases = Vector4.zero;

        for (int i = 0; i < WaveCount; i++)
        {
            if (set == null || i >= set.Length)
            {
                kVec[i] = 1f; // амплитуда 0 — волна не влияет
                continue;
            }

            Wave w = set[i];
            Vector2 d = BearingToVector(windFromDeg + 180f + w.angleOffsetDeg);
            // Те же float-значения k и D, что получит шейдер, — фазы согласованы точно
            float k = (float)(TwoPi / Mathf.Max(w.wavelength, 0.01f));
            double omega = System.Math.Sqrt(Gravity * k);

            phaseAcc[i] = Wrap(phaseAcc[i] + omega * w.speedScale * speed * dt);
            double spatial = Wrap((double)k * ((double)d.x * ship.East + (double)d.y * ship.North));

            dirX[i]   = d.x;
            dirZ[i]   = d.y;
            kVec[i]   = k;
            amp[i]    = k * Mathf.Max(0f, w.amplitude) * ampScale;   // k·a — безразмерная крутизна
            exps[i]   = Mathf.Max(0.01f, w.intensityExponent);
            phases[i] = (float)Wrap(spatial - phaseAcc[i]);
        }

        Shader.SetGlobalVector(dirXId, dirX);
        Shader.SetGlobalVector(dirZId, dirZ);
        Shader.SetGlobalVector(kId, kVec);
        Shader.SetGlobalVector(ampId, amp);
        Shader.SetGlobalVector(expId, exps);
        Shader.SetGlobalVector(phasesId, phases);
    }
}
