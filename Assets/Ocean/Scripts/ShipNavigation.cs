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
// Тест циркуляции (контекстное меню компонента или F5): скорость удерживается
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
    [Tooltip("Клавиша F5 запускает/останавливает тест")]
    public bool testHotkey = true;

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
    /// <summary>Вектор скорости относительно воды (x — восток, y — север), м/с.</summary>
    public Vector2 Velocity => model != null
        ? new Vector2((float)(model.U * Math.Sin(model.Psi)), (float)(model.U * Math.Cos(model.Psi)))
        : Vector2.zero;
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

    void OnEnable()
    {
        if (worldOrigin != null)
            worldOrigin.ExternalControl = true;
    }

    void OnDisable()
    {
        if (worldOrigin != null)
            worldOrigin.ExternalControl = false;
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
        model.Psi = ShipDynamicsModel.Wrap2Pi((transform.eulerAngles.y + bowYawOffset) * Mathf.Deg2Rad);
        if (worldOrigin != null)
        {
            model.East = worldOrigin.ShipPosition.East;
            model.North = worldOrigin.ShipPosition.North;
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

        if (testHotkey && Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
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
            worldOrigin.SetShipPosition(eShown, nShown);
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
            worldOrigin.SetShipPosition(model.East, model.North);
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
        model.RudderCommandDeg = testActive ? testRudderDeg : (wheel != null ? wheelSignValue * wheel.RudderAngle : 0.0);
        model.SpeedFactor = telegraph != null ? telegraph.SpeedFactor : 0.0;
        model.HoldServiceSpeed = testActive;   // испытание на циркуляцию — при неизменной скорости
        model.EngineFailed = EngineFailed;
        model.SteeringFailed = SteeringFailed;
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
