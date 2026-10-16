// ============================================================
// ShipParameters.cs — Параметры математической модели судна (A2)
//
// Значения по умолчанию — ЗАГЛУШКА для среднего торгового судна,
// калибруются на шаге A3 (по натурным испытаниям / формулярам манёвренности).
//
// Связь параметров с моделью (подробно — в ShipNavigation.cs):
//   поворот (Номото 1-го порядка): T·dr/dt + r = K·δ,
//       K = K'·U/L,  T = T'·L/U;
//   установившийся диаметр циркуляции D = 2L / (K'·δ_рад):
//       при K' = 0.8, δ = 35° → D ≈ 4.1·L;
//   скорость: du/dt = (u_зад − u)/τ, u_зад = SF·V_эксп (·k_зх на заднем ходу);
//   рулевая машина: |dδ/dt| ≤ ω_δ; СОЛАС гл. II-1 правило 29:
//       35° одного борта → 30° другого не более чем за 28 с → ω_δ ≥ 65/28 ≈ 2.32 °/с.
//
// A3.2 (калибровка) — переключаемые расширения, по умолчанию выключены
// (yawModel = Nomoto, α' = 0, C_r' = 0 → прежняя модель бит в бит):
//   Норрбин: T·dr/dt + r + α·r³ = K·δ, α = α'·(L/U)²;
//   потеря скорости на циркуляции: du/dt = (u_зад − u)/τ − C_r'·L·r².
//
// Запрос 4 (качка на волнах, SeakeepingModel): ширина и осадка, остойчивость,
// собственные периоды и затухания бортовой, вертикальной и килевой качки.
// Периоды можно оценить по формулам (контекстное меню «Оценить периоды качки»):
//   T_φ = 2·C·B/√GM, C = 0.373 + 0.023·B/d − 0.043·L/100 (IS Code 2008, ч. A, 2.3.4);
//   T_z = 2π·√(2·C_B·d/(C_WP·g)) (a₃₃ ≈ m),  T_θ ≈ T_z.
// ============================================================
using UnityEngine;

/// <summary>
/// Модель движения (запрос 19.1): Course1Dof — Номото/Норрбин (рыскание + ход, без дрейфа),
/// Manoeuvring3Dof — продольная, поперечная, рыскание (Manoeuvring3Dof.cs), в т. ч. на малых скоростях.
/// </summary>
public enum MotionModel
{
    Course1Dof = 0,
    Manoeuvring3Dof = 1,
}

/// <summary>Уравнение поворота: Номото 1-го порядка или нелинейная модель Норрбина.</summary>
public enum YawModel
{
    Nomoto = 0,
    Norrbin = 1,
}

[CreateAssetMenu(fileName = "ShipParameters", menuName = "VR Bridge/Ship Parameters")]
public class ShipParameters : ScriptableObject
{
    [Header("Главные размерения (заглушка, калибруется в A3)")]
    [Tooltip("Длина судна L, м")]
    public float length = 150f;

    [Header("Ход")]
    [Tooltip("Эксплуатационная скорость V_эксп (телеграф «Полный вперёд»), узлы")]
    public float serviceSpeedKnots = 15f;
    [Tooltip("Постоянная времени разгона τ_разг, с")]
    public float accelerationTimeConstant = 90f;
    [Tooltip("Постоянная времени торможения τ_торм, с")]
    public float decelerationTimeConstant = 150f;
    [Tooltip("Коэффициент заднего хода k_зх: u_зад = SF·V_эксп·k_зх при SF < 0")]
    [Range(0f, 1f)] public float asternFactor = 0.6f;

    [Header("Поворотливость (модель Номото 1-го порядка)")]
    [Tooltip("Безразмерный коэффициент усиления K' (K = K'·U/L)")]
    public float nomotoKPrime = 0.8f;
    [Tooltip("Безразмерная постоянная времени T' (T = T'·L/U)")]
    public float nomotoTPrime = 2.0f;
    [Tooltip("Минимальная расчётная скорость U_min, м/с — только защита от деления на ноль")]
    public float minCalcSpeed = 0.05f;

    [Header("Расширения модели (A3.2), по умолчанию выключены")]
    [Tooltip("Nomoto: T·ṙ + r = K·δ. Norrbin: T·ṙ + r + α·r³ = K·δ")]
    public YawModel yawModel = YawModel.Nomoto;
    [Tooltip("Безразмерный коэффициент Норрбина α' (α = α'·(L/U)²); используется при yawModel = Norrbin")]
    public float norrbinAlphaPrime = 0f;
    [Tooltip("Коэффициент потери скорости на циркуляции C_r': du/dt = … − C_r'·L·r² (0 — выключено)")]
    public float turnSpeedLossCoeff = 0f;

    [Header("Рулевая машина")]
    [Tooltip("Максимальная перекладка руля δ_max, градусы на борт")]
    public float maxRudderAngle = 35f;
    [Tooltip("Скорость перекладки ω_δ, °/с. По СОЛАС: (35+30)/28 ≈ 2.32")]
    public float rudderRate = 65f / 28f;

    [Header("Мореходность: размерения и остойчивость (запрос 4)")]
    [Tooltip("Ширина B, м (сетка точек качки поперёк корпуса)")]
    public float beam = 20f;
    [Tooltip("Осадка d, м")]
    public float draft = 8f;
    [Tooltip("Поперечная метацентрическая высота GM, м")]
    public float metacentricHeight = 1f;
    [Tooltip("Коэффициент общей полноты C_B")]
    public float blockCoefficient = 0.7f;
    [Tooltip("Коэффициент полноты ватерлинии C_WP")]
    public float waterplaneCoefficient = 0.8f;

    [Header("Мореходность: собственные периоды и затухание")]
    [Tooltip("Период бортовой качки T_φ, с")]
    public float rollPeriod = 12f;
    [Tooltip("Период вертикальной качки T_z, с")]
    public float heavePeriod = 7f;
    [Tooltip("Период килевой качки T_θ, с")]
    public float pitchPeriod = 7f;
    [Tooltip("Относительное затухание бортовой качки ζ_φ (эквивалентное линейное)")]
    [Range(0f, 0.95f)] public float rollDamping = 0.1f;
    [Tooltip("Относительное затухание вертикальной качки ζ_z")]
    [Range(0f, 0.95f)] public float heaveDamping = 0.25f;
    [Tooltip("Относительное затухание килевой качки ζ_θ")]
    [Range(0f, 0.95f)] public float pitchDamping = 0.25f;

    [Header("Модель 3 СС: продольная, поперечная, рыскание (запрос 19.1)")]
    [Tooltip("Course1Dof — Номото/Норрбин (как до запроса 19); Manoeuvring3Dof — дрейф, движение лагом, разворот на месте. " +
             "Своему судну модель можно переключить в сцене (ShipNavigation), не меняя ассет")]
    public MotionModel motionModel = MotionModel.Course1Dof;
    [Tooltip("Радиус инерции относительно вертикальной оси R₆₆/L (малые суда ≈ 0,25, танкеры ≈ 0,27; MSS clarke83.m)")]
    public float yawGyrationRadiusL = 0.25f;
    [Tooltip("Отстояние центра тяжести от миделя x_G, м (> 0 — в нос)")]
    public float centreOfGravityX = 0f;
    [Tooltip("Коэффициент поперечного сопротивления C_d (0 — по кривой Хёрнера от B/(2d))")]
    public float crossFlowDragCoeff = 0f;
    [Tooltip("Подбирать множитель поперечного сопротивления на ходу по установившейся циркуляции с рулём 35° " +
             "модели Номото/Норрбина этого ассета (на малых скоростях C_d — по Хёрнеру). false — C_d одинаков на всех скоростях")]
    public bool matchTurningCircle = true;
    [Tooltip("Плечо руля x_R/L (руль в корме: −0,5)")]
    public float rudderLeverL = -0.5f;
    [Tooltip("Обдув руля струёй винта k_ps: q = u|u| + k_ps·max(0, u_зад² − u|u|) при работе вперёд (допущение)")]
    [Range(0f, 2f)] public float rudderSlipstreamFactor = 0.5f;
    [Tooltip("Поперечная сила винта на заднем ходу, доля упора (правый винт — корму влево; допущение)")]
    [Range(0f, 0.3f)] public float propellerSideForceFactor = 0.05f;
    [Tooltip("19.3а: диагональная матрица присоединённых масс (Y'_ṙ = N'_v̇ = 0). Для коротких широких корпусов (буксир, B/L ≈ 0,36) " +
             "регрессия Clarke вне области применимости и даёт неположительную матрицу масс")]
    public bool diagonalAddedMass = false;
    [Tooltip("19.3а: подбирать демпфирование рыскания k_N и Y'_δ по K', T' (false — k_N = 1 по Clarke, руля нет: Y'_δ = 0; буксир с колонками)")]
    public bool fitYawToNomoto = true;

    [Header("Контур корпуса для контактов (19.3б-2)")]
    [Tooltip("Конец цилиндрической вставки к носу от миделя, доля L: дальше — клин до форштевня (MooringHull). 0,25 — как до 19.3б-2; " +
             "Model45m — 0,40: так нарисован корпус сцены (Hull — полной ширины до 0,40·L, BowTip — клин)")]
    [Range(0f, 0.49f)] public float hullParallelForwardL = (float)MooringHull.ParallelForwardL;

    public const float KnotsToMs = 0.514444f;
    public float ServiceSpeedMs => serviceSpeedKnots * KnotsToMs;

    /// <summary>Копия параметров в памяти (без ассета) — для идентификации и испытаний.</summary>
    public ShipParameters Clone()
    {
        var c = CreateInstance<ShipParameters>();
        c.name = name;
        c.CopyFrom(this);
        return c;
    }

    /// <summary>Скопировать значения всех параметров (без имени ассета).</summary>
    public void CopyFrom(ShipParameters o)
    {
        length = o.length;
        serviceSpeedKnots = o.serviceSpeedKnots;
        accelerationTimeConstant = o.accelerationTimeConstant;
        decelerationTimeConstant = o.decelerationTimeConstant;
        asternFactor = o.asternFactor;
        nomotoKPrime = o.nomotoKPrime;
        nomotoTPrime = o.nomotoTPrime;
        minCalcSpeed = o.minCalcSpeed;
        yawModel = o.yawModel;
        norrbinAlphaPrime = o.norrbinAlphaPrime;
        turnSpeedLossCoeff = o.turnSpeedLossCoeff;
        maxRudderAngle = o.maxRudderAngle;
        rudderRate = o.rudderRate;
        beam = o.beam;
        draft = o.draft;
        metacentricHeight = o.metacentricHeight;
        blockCoefficient = o.blockCoefficient;
        waterplaneCoefficient = o.waterplaneCoefficient;
        rollPeriod = o.rollPeriod;
        heavePeriod = o.heavePeriod;
        pitchPeriod = o.pitchPeriod;
        rollDamping = o.rollDamping;
        heaveDamping = o.heaveDamping;
        pitchDamping = o.pitchDamping;
        motionModel = o.motionModel;
        yawGyrationRadiusL = o.yawGyrationRadiusL;
        centreOfGravityX = o.centreOfGravityX;
        crossFlowDragCoeff = o.crossFlowDragCoeff;
        matchTurningCircle = o.matchTurningCircle;
        rudderLeverL = o.rudderLeverL;
        rudderSlipstreamFactor = o.rudderSlipstreamFactor;
        propellerSideForceFactor = o.propellerSideForceFactor;
        diagonalAddedMass = o.diagonalAddedMass;
        fitYawToNomoto = o.fitYawToNomoto;
        hullParallelForwardL = o.hullParallelForwardL;
    }

    /// <summary>Оценить собственные периоды качки по размерениям (формулы — в шапке файла).</summary>
    [ContextMenu("Оценить периоды качки по формулам")]
    public void EstimateMotionPeriods()
    {
        rollPeriod = (float)SeakeepingModel.RollPeriodIsCode(length, beam, draft, metacentricHeight);
        heavePeriod = (float)SeakeepingModel.HeavePeriod(draft, blockCoefficient, waterplaneCoefficient);
        pitchPeriod = heavePeriod;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Теоретический диаметр установившейся циркуляции линейной модели Номото, м: D = 2L/(K'·δ).</summary>
    public float TheoreticalCircleDiameter(float rudderDeg)
    {
        float d = Mathf.Abs(rudderDeg) * Mathf.Deg2Rad;
        return d > 1e-4f ? 2f * length / (nomotoKPrime * d) : float.PositiveInfinity;
    }
}
