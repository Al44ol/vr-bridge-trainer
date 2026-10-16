// ============================================================
// ShipNavigation.cs — Модуль навигационного моделирования судна (A2)
//
// С A3 математика модели вынесена в чистый класс ShipDynamicsModel
// (Scripts/Core, сборка VRBridge.Core); здесь — органы управления → модель
// в FixedUpdate и отображение результата в сцене.
//
// Берёт команды со штурвала (SteeringWheel) и машинного телеграфа
// (EngineTelegraph), интегрирует курс, скорость и позицию судна и отдаёт:
//   • курс — поворотом объекта ShipRoot вокруг вертикали (yaw);
//     качка ShipMotion остаётся на дочернем объекте Ship и добавляется поверх;
//   • позицию — в плавающее начало координат WorldOrigin (мир движется,
//     судно остаётся около (0,0,0)); солнце, туман и океан от курса не вращаются.
//
// ─────────── Математическая модель ───────────
// Обозначения: ψ — курс (рад, 0 = N, по часовой), r = dψ/dt — угловая скорость
// (рад/с, > 0 — поворот вправо), u — скорость хода (м/с, < 0 — задний ход),
// δ — фактическая перекладка руля (> 0 — на правый борт), L — длина судна.
//
// 1. Рулевая машина: фактический угол догоняет заданный с ограниченной скоростью
//        dδ/dt = clamp((δ_зад − δ)/dt, −ω_δ, +ω_δ),   |δ| ≤ δ_max.
//    ω_δ по умолчанию из СОЛАС гл. II-1, правило 29: перекладка с 35° одного
//    борта на 30° другого не более чем за 28 с → ω_δ = 65°/28 с ≈ 2.32 °/с.
//
// 2. Скорость — уравнение первого порядка:
//        du/dt = (u_зад − u) / τ,
//        u_зад = SF · V_эксп            при SF ≥ 0,
//        u_зад = SF · V_эксп · k_зх      при SF < 0  (задний ход слабее, k_зх < 1),
//    SF — положение телеграфа (−1..1); τ = τ_разг, если |u| растёт к u_зад
//    (того же знака), иначе τ = τ_торм.
//    Дискретизация точная при постоянном u_зад на шаге dt:
//        u(t+dt) = u_зад + (u(t) − u_зад) · e^(−dt/τ).
//
// 3. Поворот — модель Номото 1-го порядка:
//        T · dr/dt + r = K · δ,
//        K = K' · u / L   [1/с],    T = T' · L / U   [с],    U = max(|u|, U_min);
//        знак K совпадает со знаком u (на заднем ходу руль поворачивает судно
//        в обратную сторону).
//    Установившаяся угловая скорость r_уст = K·δ = K'·u·δ / L ∝ u:
//    на стопе (u = 0) руль не поворачивает судно; U_min — только защита
//    от деления на ноль в T (A3: раньше U_min входил и в K — исправлено).
//    Радиус установившейся циркуляции R = U / r_уст = L / (K'·δ) от скорости
//    не зависит; диаметр D = 2L / (K'·δ_рад) (K' = 0.8, δ = 35° → D ≈ 4.1·L).
//    Дискретизация точная при постоянном δ на шаге (устойчива при любом T):
//        r(t+dt) = K·δ + (r(t) − K·δ) · e^(−dt/T).
//
// 4. Кинематика (в FixedUpdate, позиция в double; +X — восток, +Z — север):
//        ψ  += r · dt,
//        E  += u · sin ψ · dt,
//        N  += u · cos ψ · dt.
//
// Отображение: физика идёт с шагом FixedUpdate (50 Гц), кадры в VR — 90 Гц.
// Чтобы мостик с игроком не дёргался, в Update курс и позиция интерполируются
// между двумя последними шагами: x = x_пред + (x − x_пред)·α,
// α = (Time.time − Time.fixedTime) / Time.fixedDeltaTime.
//
// Запрос 19.1: модель 3 СС (продольная, поперечная, рыскание — Core/Manoeuvring/Manoeuvring3Dof.cs)
// включается полем motionModel (по умолчанию — как в ассете) или меню A «Судно» → «Модель движения»
// вне упражнения; в «Отладке» — сила в борт и пара сил (только 3 СС), чтобы почувствовать дрейф
// и разворот на месте. Позиция при этом — с поперечной скоростью, курс по-прежнему ψ.
//
// Запрос 19.2б: внешние силы — событие BeforeStep (модель, dt) перед model.Step на каждом шаге
// (MooringController: причал, кранцы, концы, подруливающее — MooringSystem.Apply); SetMotionModel —
// смена модели движения кодом (отладка «Судно к причалу», сценарий швартовки), только вне упражнения.
//
// Запрос 19.4а: тип судна «буксир» — SetAzimuthDrive(true): штурвал и телеграф в модель не идут, модель — 3 СС,
// упор колонок по джойстику прикладывает OwnTugDrive в BeforeStep.
//
// Тест циркуляции (контекстное меню компонента, меню A «Отладка»; F5 — при keyboardShortcuts): скорость удерживается
// равной V_эксп, руль 35° ПБ; в лог — координаты каждые 30° изменения курса,
// выдвиг (90°), тактический диаметр (180°), диаметр установившейся
// циркуляции (по точкам 360° и 540°) и теоретический D = 2L/(K'·δ).
// ============================================================
using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-90)]
public class ShipNavigation : MonoBehaviour
{
    public enum WheelSignMode { Auto, Normal, Inverted }

    [Header("Параметры судна")]
    public ShipParameters parameters;

    [Header("Органы управления")]
    public SteeringWheel wheel;
    public EngineTelegraph telegraph;
    [Tooltip("Auto — поворот штурвала по часовой стрелке (с места оператора) = руль на правый борт")]
    public WheelSignMode wheelSign = WheelSignMode.Auto;

    [Header("Плавающее начало координат")]
    public WorldOrigin worldOrigin;

    [Header("Тест циркуляции (F5)")]
    public float testRudderDeg = 35f;
    [Tooltip("Ускорение моделирования во время теста (подшагов на один FixedUpdate)")]
    [Range(1, 20)] public int testSpeedup = 5;
    [Tooltip("Клавиша F5 запускает/останавливает тест (D1: только вместе с keyboardShortcuts; без клавиатуры — меню A «Отладка»)")]
    public bool testHotkey = true;
    [Tooltip("Клавиши клавиатуры (D1: по умолчанию выкл — всё из меню)")]
    public bool keyboardShortcuts;
    private BridgeMenuCommand testCommand;

    public enum MotionChoice { AsAsset, Course1Dof, Manoeuvring3Dof }
    [Header("Модель движения (запрос 19.1)")]
    [Tooltip("AsAsset — как в ShipParameters.motionModel; меню A «Судно» → «Модель движения» (вне упражнения)")]
    public MotionChoice motionModel = MotionChoice.AsAsset;
    [Tooltip("Отладочная сила (меню A «Отладка», только 3 СС, вне упражнения), т; 0 — 0,002·Δ·g")]
    public float debugForceTonnes = 0f;
    private BridgeMenuCommand motionCommand, pushCommand, spinCommand;
    private int debugPush, debugSpin;   // −1 / 0 / +1: ЛБ / нет / ПБ; влево / нет / вправо
    private ScenarioRunner runner;

    // ── Модель (A3: вынесена в чистый класс ShipDynamicsModel) ──
    private ShipDynamicsModel model;
    public ShipDynamicsModel Model => model;

    // Предыдущий шаг — для интерполяции отображения
    private double psiPrev, eastPrev, northPrev;

    private float bowYawOffset = 180f;   // курс носа при yaw ShipRoot = 0
    private int wheelSignValue = 1;
    private bool wheelSignResolved;
    private bool initialized;

    // ── Тест циркуляции ──
    private bool testActive;
    private double testTime, testTurned, testNextLog;
    private double testPsi0, testE0, testN0, testE360, testN360;

    // ── Публичные свойства для будущих модулей (логирование, сценарии, CPA/TCPA) ──
    /// <summary>Курс, градусы 0..360 (0 = N, по часовой).</summary>
    public float Heading => model != null ? (float)(model.Psi * Mathf.Rad2Deg) : 0f;
    /// <summary>Скорость хода, м/с (< 0 — задний ход).</summary>
    public float SpeedMs => model != null ? (float)model.U : 0f;
    /// <summary>Скорость хода, узлы.</summary>
    public float SpeedKnots => SpeedMs / ShipParameters.KnotsToMs;
    /// <summary>Угловая скорость поворота, °/мин (> 0 — вправо).</summary>
    public float RateOfTurn => model != null ? (float)(model.R * Mathf.Rad2Deg * 60.0) : 0f;
    /// <summary>Заданный угол руля, градусы (> 0 — правый борт).</summary>
    public float RudderOrdered => model != null ? (float)model.Ordered : 0f;
    /// <summary>Фактический угол руля, градусы (> 0 — правый борт).</summary>
    public float RudderActual => model != null ? (float)model.Delta : 0f;
    /// <summary>Истинная позиция судна (восток, север), м.</summary>
    public WorldPosition PositionEastNorth => model != null ? new WorldPosition(model.East, model.North) : default;
    /// <summary>Вектор скорости относительно воды (x — восток, y — север), м/с; с запроса 19.1 — с поперечной скоростью (3 СС).</summary>
    public Vector2 Velocity => model != null
        ? new Vector2((float)model.VelocityEast, (float)model.VelocityNorth)
        : Vector2.zero;
    /// <summary>Поперечная скорость, узлы (> 0 — на правый борт); только модель 3 СС.</summary>
    public float SwayKnots => model != null ? (float)(model.V / ShipParameters.KnotsToMs) : 0f;
    /// <summary>Действует модель 3 СС (запрос 19.1).</summary>
    public bool Uses3Dof => model != null && model.Uses3Dof;
    /// <summary>Название положения телеграфа.</summary>
    public string TelegraphPosition => telegraph != null ? telegraph.PositionName : "—";
    public bool CirculationTestActive => testActive;
    /// <summary>
    /// Модель стоит (брифинг сценария B2; пауза инструктора D1): шаг не выполняется,
    /// судно неподвижно в мире, качка продолжается.
    /// </summary>
    public bool Paused { get; set; }
    /// <summary>D1: вводная «отказ главного двигателя» — телеграф не действует (ShipDynamicsModel.EngineFailed).</summary>
    public bool EngineFailed { get; set; }
    /// <summary>D1: вводная «отказ рулевого устройства» — руль заклинило (ShipDynamicsModel.SteeringFailed).</summary>
    public bool SteeringFailed { get; set; }
    /// <summary>
    /// 19.2б: внешние силы на шаг — вызывается перед model.Step (ShipDynamicsModel.AddForce / AddMoment;
    /// действуют только в модели 3 СС). Параметры: модель, шаг dt, с.
    /// </summary>
    public event Action<ShipDynamicsModel, double> BeforeStep;
    /// <summary>Смена модели движения (меню, SetMotionModel): true — теперь 3 СС.</summary>
    public event Action<bool> MotionModelChanged;
    /// <summary>
    /// 19.4а: свой буксир — движитель из двух азимутальных колонок (OwnTugDrive, через BeforeStep): штурвал и телеграф
    /// в модель не идут (руль 0, SF = 0 — закон телеграфа даёт сопротивление корпуса), модель — всегда 3 СС.
    /// </summary>
    public bool AzimuthDriveActive { get; private set; }

    /// <summary>19.4а: включить/выключить движитель буксира (ShipTypeSwitcher при смене типа судна).</summary>
    public void SetAzimuthDrive(bool on)
    {
        if (AzimuthDriveActive == on) return;
        bool was3Dof = model != null && model.Uses3Dof;
        AzimuthDriveActive = on;
        ApplyMotionModel();
        bool now3Dof = model != null && model.Uses3Dof;
        if (model != null && was3Dof != now3Dof) MotionModelChanged?.Invoke(now3Dof);
    }

    // ─────────── 19.3б-2: точка модели = мидель видимого корпуса ───────────
    // Корпус сцены (Hull + BowTip) построен не вокруг ShipRoot: его мидель — центр качки ShipMotion.pivotLocal
    // (у модели 45 м — 5,4 м к носу и 2,7 м к ЛБ от ShipRoot). Контуры контактов (кранцы, кромка, буксир, мели, цели)
    // считаются вокруг точки модели, поэтому ShipRoot ставится со сдвигом: мидель корпуса — в точке модели.
    // Модель, запись и оценка от этого не меняются — только где судно нарисовано относительно мира.
    [Header("Корпус (19.3б-2)")]
    [Tooltip("Точка модели — мидель видимого корпуса (центр качки ShipMotion). Выкл — как до 19.3б-2 (точка модели = ShipRoot)")]
    public bool alignHullCentre = true;
    private ShipMotion hullFrame;

    /// <summary>Мидель корпуса от ShipRoot в осях судна: к носу, к ПБ, м (0 — без совмещения).</summary>
    public void HullOffset(out double forward, out double starboard)
    {
        forward = starboard = 0.0;
        if (!alignHullCentre) return;
        if (hullFrame == null)
        {
            Transform ship = worldOrigin != null ? worldOrigin.ship : null;
            hullFrame = ship != null ? ship.GetComponent<ShipMotion>() : GetComponentInChildren<ShipMotion>();
        }
        if (hullFrame == null) return;
        hullFrame.HullCentre(out float f, out float s);
        forward = f; starboard = s;
    }

    /// <summary>Точка ShipRoot в мире при позиции модели (восток, север) и курсе psi (рад).</summary>
    public WorldPosition RootFor(double east, double north, double psi)
    {
        HullOffset(out double f, out double s);
        double sn = Math.Sin(psi), cs = Math.Cos(psi);
        return new WorldPosition(east - (f * sn + s * cs), north - (f * cs - s * sn));
    }

    void OnEnable()
    {
        if (worldOrigin != null)
            worldOrigin.ExternalControl = true;
        // D1: без клавиатуры — меню A «Отладка»
        testCommand = BridgeMenu.Register("Отладка", () => testActive ? "Остановить тест циркуляции" : $"Тест циркуляции (руль {testRudderDeg:F0}° ПБ)",
            () => { if (testActive) StopCirculationTest("остановлен вручную"); else StartCirculationTest(); },
            () => initialized, instructorOnly: true);

        // Запрос 19.1: модель движения и отладочные силы (вне упражнения)
        motionCommand = BridgeMenu.RegisterStepper("Судно", () => $"Модель движения: {MotionTitle}",
            () => StepMotion(), () => StepMotion(), () => initialized && OutsideExercise && !AzimuthDriveActive);
        pushCommand = BridgeMenu.RegisterStepper("Отладка", () => $"Сила в борт ({DebugForceTonnes:0.#} т): {Side(debugPush, "ПБ", "ЛБ")}",
            () => debugPush = Cycle(debugPush, -1), () => debugPush = Cycle(debugPush, +1),
            () => initialized && OutsideExercise && model.Uses3Dof, instructorOnly: true);
        spinCommand = BridgeMenu.RegisterStepper("Отладка", () => $"Пара сил ({DebugForceTonnes:0.#} т): {Side(debugSpin, "нос вправо", "нос влево")}",
            () => debugSpin = Cycle(debugSpin, -1), () => debugSpin = Cycle(debugSpin, +1),
            () => initialized && OutsideExercise && model.Uses3Dof, instructorOnly: true);
    }

    void OnDisable()
    {
        if (worldOrigin != null)
            worldOrigin.ExternalControl = false;
        BridgeMenu.Unregister(testCommand);
        testCommand = null;
        BridgeMenu.Unregister(motionCommand);
        BridgeMenu.Unregister(pushCommand);
        BridgeMenu.Unregister(spinCommand);
        motionCommand = pushCommand = spinCommand = null;
    }

    // ─────────── Модель движения и отладочные силы (запрос 19.1) ───────────

    private bool OutsideExercise
    {
        get
        {
            if (runner == null) runner = FindAnyObjectByType<ScenarioRunner>();
            return runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Running;
        }
    }

    private string MotionTitle => model != null && model.Uses3Dof ? "3 СС (дрейф, лаг)" : "Номото";

    private void StepMotion()
    {
        if (model == null || !OutsideExercise) return;
        SetMotionModel(model.Uses3Dof ? MotionChoice.Course1Dof : MotionChoice.Manoeuvring3Dof);
    }

    /// <summary>
    /// 19.2б: модель движения кодом (отладка «Судно к причалу», сценарий швартовки) — только вне упражнения.
    /// false — не сменилась (упражнение идёт или модель ещё не создана).
    /// </summary>
    public bool SetMotionModel(MotionChoice choice)
    {
        if (model == null || !OutsideExercise) return false;
        bool was3Dof = model.Uses3Dof;
        motionModel = choice;
        ApplyMotionModel();
        bool to3Dof = model.Uses3Dof;
        if (to3Dof == was3Dof) return true;
        if (to3Dof)
        {
            Manoeuvring3DofCoefficients c = model.Coefficients3Dof;   // построить сразу, а не на первом шаге
            Debug.Log($"[Навигация] Модель движения: 3 СС ({parameters.name}): k_N = {c.YawDampingFactor:F3}, Y'_δ = {c.YdeltaPrime:F5}, " +
                      $"K'/T' = {c.LinearKPrime:F3}/{c.LinearTPrime:F3}, C_d = {c.CrossFlowCd:F3}, k_∞ = {c.CrossFlowHighSpeedFactor:F3}, " +
                      $"циркуляция {c.CircleD:F0} м (цель {c.CircleTargetD:F0} м). {c.FitNote} {c.CircleNote}");
        }
        else
        {
            debugPush = debugSpin = 0;
            Debug.Log("[Навигация] Модель движения: Номото (поперечная скорость обнулена)");
        }
        MotionModelChanged?.Invoke(to3Dof);
        return true;
    }

    private void ApplyMotionModel()
    {
        if (model == null) return;
        if (AzimuthDriveActive) { model.MotionOverride = MotionModel.Manoeuvring3Dof; return; }   // 19.4а: буксир — только 3 СС
        model.MotionOverride = motionModel == MotionChoice.AsAsset ? (MotionModel?)null
            : motionModel == MotionChoice.Course1Dof ? MotionModel.Course1Dof : MotionModel.Manoeuvring3Dof;
    }

    private float DebugForceTonnes => debugForceTonnes > 0f || model == null
        ? debugForceTonnes
        : (float)(LowSpeedTrials.ReferenceForce(parameters) / 9810.0);

    private static int Cycle(int v, int dir) => v + dir > 1 ? -1 : v + dir < -1 ? 1 : v + dir;
    private static string Side(int v, string plus, string minus) => v > 0 ? plus : v < 0 ? minus : "нет";

    // Отладочные силы на шаг: в борт — на миделе; пара — ±F на ±0,45L (как в LowSpeedTrials)
    private void ApplyDebugForces()
    {
        if (debugPush == 0 && debugSpin == 0) return;
        if (!model.Uses3Dof || !OutsideExercise) { debugPush = debugSpin = 0; return; }
        double f = DebugForceTonnes * 9810.0;
        if (debugPush != 0) model.AddForce(0.0, debugPush * f, 0.0);
        if (debugSpin != 0)
        {
            double arm = 0.45 * parameters.length;
            model.AddForce(0.0, debugSpin * f, arm);
            model.AddForce(0.0, -debugSpin * f, -arm);
        }
    }

    void Start()
    {
        if (parameters == null)
        {
            Debug.LogError("[ShipNavigation] Не назначен ShipParameters — навигация отключена.", this);
            enabled = false;
            return;
        }

        // Курс носа при нулевом повороте ShipRoot (нос модели — по bowLocalAxis WorldOrigin, сейчас −Z → 180°)
        Vector3 bowAxis = worldOrigin != null ? worldOrigin.bowLocalAxis : Vector3.back;
        Transform ship = worldOrigin != null && worldOrigin.ship != null ? worldOrigin.ship : null;
        if (ship != null && ship.parent == transform)
            bowAxis = ship.localRotation * bowAxis;
        bowYawOffset = Mathf.Atan2(bowAxis.x, bowAxis.z) * Mathf.Rad2Deg;

        model = new ShipDynamicsModel(parameters);
        ApplyMotionModel();
        model.Psi = ShipDynamicsModel.Wrap2Pi((transform.eulerAngles.y + bowYawOffset) * Mathf.Deg2Rad);
        if (worldOrigin != null)
        {
            // 19.3б-2: WorldOrigin хранит точку ShipRoot, модель — мидель корпуса
            HullOffset(out double f, out double s);
            double sn = Math.Sin(model.Psi), cs = Math.Cos(model.Psi);
            model.East = worldOrigin.ShipPosition.East + f * sn + s * cs;
            model.North = worldOrigin.ShipPosition.North + f * cs - s * sn;
            worldOrigin.ExternalControl = true;
        }
        psiPrev = model.Psi; eastPrev = model.East; northPrev = model.North;
        initialized = true;
    }

    void FixedUpdate()
    {
        if (!initialized) return;
        ResolveWheelSign();

        psiPrev = model.Psi; eastPrev = model.East; northPrev = model.North;
        if (Paused) return;

        int steps = testActive ? Mathf.Max(1, testSpeedup) : 1;
        double dt = Time.fixedDeltaTime;
        for (int i = 0; i < steps; i++)
        {
            Step(dt);
            if (testActive) TrackCirculationTest(dt);
        }
    }

    void Update()
    {
        if (!initialized) return;

        if (keyboardShortcuts && testHotkey && Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
        {
            if (testActive) StopCirculationTest("остановлен вручную");
            else StartCirculationTest();
        }

        // Интерполяция между шагами физики (против дёрганья в VR)
        double alpha = Time.fixedDeltaTime > 0f ? Math.Min(1.0, Math.Max(0.0, (Time.time - Time.fixedTime) / Time.fixedDeltaTime)) : 1.0;
        double dPsi = Math.IEEERemainder(model.Psi - psiPrev, 2.0 * Math.PI);
        double psiShown = psiPrev + dPsi * alpha;
        double eShown = eastPrev + (model.East - eastPrev) * alpha;
        double nShown = northPrev + (model.North - northPrev) * alpha;

        transform.rotation = Quaternion.Euler(0f, (float)(psiShown * Mathf.Rad2Deg) - bowYawOffset, 0f);
        if (worldOrigin != null)
        {
            WorldPosition root = RootFor(eShown, nShown, psiShown);   // 19.3б-2: мидель корпуса — в точке модели
            worldOrigin.SetShipPosition(root.East, root.North);
            worldOrigin.ShipSpeed = Paused ? 0f : (float)model.U;   // на паузе — без кильватерного следа
        }
    }

    /// <summary>
    /// Переставить судно (снятие с мели инструктором, B1; начальные условия сценария, B2):
    /// позиция, курс (градусы), скорость (уз; по умолчанию стоп), фактический руль
    /// (градусы, > 0 — ПБ; null — не трогать); без интерполяции от прежнего положения.
    /// Органы управления (телеграф, штурвал) ставит вызывающий: EngineTelegraph.SetSpeedFactor, SetRudderOrder.
    /// </summary>
    public void TeleportTo(double east, double north, double headingDeg, double speedKnots = 0.0, double? rudderDeg = null)
    {
        if (!initialized) return;
        model.Teleport(east, north, headingDeg * Math.PI / 180.0, speedKnots * ShipParameters.KnotsToMs, rudderDeg);
        psiPrev = model.Psi; eastPrev = model.East; northPrev = model.North;
        transform.rotation = Quaternion.Euler(0f, (float)(model.Psi * Mathf.Rad2Deg) - bowYawOffset, 0f);
        if (worldOrigin != null)
        {
            WorldPosition root = RootFor(model.East, model.North, model.Psi);
            worldOrigin.SetShipPosition(root.East, root.North);
            worldOrigin.ShipSpeed = Paused ? 0f : (float)model.U;
        }
    }

    /// <summary>
    /// Заданный руль (градусы, > 0 — ПБ) — поворотом штурвала с учётом знака штурвала
    /// (начальные условия сценария, B2). Рулевая машина отрабатывает его сама.
    /// </summary>
    public void SetRudderOrder(float rudderDeg)
    {
        if (wheel == null) return;
        ResolveWheelSign();
        wheel.SetRudderAngle(wheelSignValue * rudderDeg);
    }

    // ─────────── Один шаг: органы управления → модель ───────────
    private void Step(double dt)
    {
        model.Parameters = parameters;   // ассет можно сменить в инспекторе на ходу
        model.RudderCommandDeg = testActive ? testRudderDeg : (wheel != null && !AzimuthDriveActive ? wheelSignValue * wheel.RudderAngle : 0.0);
        model.SpeedFactor = telegraph != null && !AzimuthDriveActive ? telegraph.SpeedFactor : 0.0;   // 19.4а: буксир — колонки
        model.HoldServiceSpeed = testActive;   // испытание на циркуляцию — при неизменной скорости
        model.EngineFailed = EngineFailed;
        model.SteeringFailed = SteeringFailed;
        ApplyDebugForces();
        BeforeStep?.Invoke(model, dt);   // 19.2б: причал, концы, подруливающее
        model.Step(dt);
    }

    // Знак штурвала: поворот по часовой стрелке с места оператора → руль на правый борт.
    // Положительный поворот вокруг оси в Unity выглядит «по часовой», если смотреть с конца оси.
    private void ResolveWheelSign()
    {
        if (wheelSignResolved || wheel == null) return;
        switch (wheelSign)
        {
            case WheelSignMode.Normal:   wheelSignValue = 1;  wheelSignResolved = true; return;
            case WheelSignMode.Inverted: wheelSignValue = -1; wheelSignResolved = true; return;
        }
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 toOperator = cam.transform.position - wheel.transform.position;
        wheelSignValue = Vector3.Dot(wheel.WorldAxis, toOperator) >= 0f ? 1 : -1;
        wheelSignResolved = true;
    }

    // ─────────── Тест циркуляции ───────────
    [ContextMenu("Тест циркуляции (руль 35° ПБ, V эксп)")]
    public void StartCirculationTest()
    {
        if (!Application.isPlaying || !initialized)
        {
            Debug.Log("[Циркуляция] Тест запускается только в Play Mode.");
            return;
        }
        testActive = true;
        testTime = 0; testTurned = 0; testNextLog = 30;
        testPsi0 = model.Psi; testE0 = model.East; testN0 = model.North;
        float dTheory = parameters.TheoreticalCircleDiameter(testRudderDeg);
        Debug.Log($"[Циркуляция] Старт: курс {Heading:F1}°, V = {parameters.serviceSpeedKnots:F1} уз, руль {testRudderDeg:F0}° ПБ, " +
                  $"L = {parameters.length:F0} м. Теоретический D = 2L/(K'·δ) = {dTheory:F0} м = {dTheory / parameters.length:F2} L. " +
                  $"Ускорение ×{testSpeedup}.");
    }

    [ContextMenu("Остановить тест циркуляции")]
    public void StopCirculationTestMenu() => StopCirculationTest("остановлен вручную");

    private void StopCirculationTest(string reason)
    {
        if (!testActive) return;
        testActive = false;
        Debug.Log($"[Циркуляция] Тест {reason}. Руль снова от штурвала, скорость — от телеграфа.");
    }

    private void TrackCirculationTest(double dt)
    {
        testTime += dt;
        testTurned += model.R * dt * Mathf.Rad2Deg;   // изменение курса с начала теста, градусы

        // Смещение относительно начальной точки: вдоль начального курса и вправо от него
        double dE = model.East - testE0, dN = model.North - testN0;
        double along = dE * Math.Sin(testPsi0) + dN * Math.Cos(testPsi0);
        double across = dE * Math.Cos(testPsi0) - dN * Math.Sin(testPsi0);

        while (testTurned >= testNextLog)
        {
            Debug.Log($"[Циркуляция] t = {testTime:F0} с, Δψ = {testNextLog:F0}°, курс {Heading:F1}°, " +
                      $"E = {model.East:F1} м, N = {model.North:F1} м, вдоль = {along:F0} м, вправо = {across:F0} м");

            if (Math.Abs(testNextLog - 90) < 0.5)
                Debug.Log($"[Циркуляция] Выдвиг (advance) = {along:F0} м = {along / parameters.length:F2} L, прямое смещение (transfer) = {across:F0} м");
            if (Math.Abs(testNextLog - 180) < 0.5)
                Debug.Log($"[Циркуляция] Тактический диаметр = {across:F0} м = {across / parameters.length:F2} L");
            if (Math.Abs(testNextLog - 360) < 0.5)
            {
                testE360 = model.East; testN360 = model.North;
            }
            if (Math.Abs(testNextLog - 540) < 0.5)
            {
                double d = Math.Sqrt((model.East - testE360) * (model.East - testE360) + (model.North - testN360) * (model.North - testN360));
                float dTheory = parameters.TheoreticalCircleDiameter(testRudderDeg);
                Debug.Log($"[Циркуляция] ИТОГ: диаметр установившейся циркуляции D = {d:F0} м = {d / parameters.length:F2} L " +
                          $"(теория {dTheory:F0} м = {dTheory / parameters.length:F2} L), время 540° = {testTime:F0} с.");
                StopCirculationTest("завершён");
                return;
            }
            testNextLog += 30;
        }

        if (testTime > 3600)
        {
            Debug.LogWarning("[Циркуляция] За 3600 с судно не развернулось на 540° — проверьте K', руль и скорость.");
            StopCirculationTest("прерван по времени");
        }
    }
}
