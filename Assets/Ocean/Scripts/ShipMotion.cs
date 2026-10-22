// ============================================================
// ShipMotion.cs — Качка судна (крен, дифферент, вертикальная; рыскание — в синусоидах)
//
// Присоединяется к объекту «Ship» (дочерний к ShipRoot), который содержит
// мостик и палубу. XR Rig — тоже дочерний к Ship: игрок качается вместе с судном.
// Курс задаёт ShipNavigation поворотом ShipRoot, качка добавляется поверх.
//
// Параметр seaState (0..1) задаёт волнение (его плавно меняет WeatherController):
//   0.08 = штиль, 0.3 = умеренное, 0.85 = шторм.
// Он же пишется в _WaveIntensity материала воды — волны шейдера и качка
// всегда от одного значения.
//
// Режимы (переключатель mode):
//   Waves (по умолчанию, запрос 4) — качка на тех же волнах, что видит
//     обучающийся: в FixedUpdate вода сэмплируется в сетке точек корпуса
//     (OceanWaveField — те же параметры и фазы, что у шейдера), по точкам МНК
//     строится плоскость, отклик фильтруется звеньями 2-го порядка с
//     собственными периодами судна (SeakeepingModel; формулы — в её шапке).
//     Отображение интерполируется между шагами физики, как в ShipNavigation.
//     Поворот — вокруг центра качки pivotLocal (мидель, ватерлиния).
//   Sinusoidal — прежняя кинематическая качка (сумма синусоид), запасной режим.
//
// Комфорт (против укачивания): общий множитель comfortScale и мягкие пределы
// крена и дифферента max·tanh(x/max) — только для отображения.
// Запрос 15: SetHullFrame — другой центр качки и сдвиг судна по вертикали (катер: палуба
// ≈ 1,2 м над водой вместо ≈ 5 м у мостика); задаёт ShipTypeSwitcher.
// К4 (22.10.2026): пункт меню Y «Качка мостика: N %» — по нажатию 100 → 50 → 25 → 0 → 100 %
// (comfortScale; запоминается на станции, PlayerPrefs). Волны, модель качки, движение судна,
// запись и оценка не меняются — только то, как качается мостик вместе с игроком.
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-70)]   // после ShipNavigation (−90) и OceanWaves (−80)
public class ShipMotion : MonoBehaviour
{
    public enum MotionMode { Waves, Sinusoidal }

    [Header("Состояние моря")]
    [Range(0f, 1f)]
    [Tooltip("0 = штиль, 1 = шторм. Синхронизируй с _WaveIntensity материала воды.")]
    public float seaState = 0.3f;

    [Header("Режим качки")]
    [Tooltip("Waves — качка на волнах шейдера (запрос 4). Sinusoidal — прежние синусоиды (запасной режим).")]
    public MotionMode mode = MotionMode.Waves;

    [Header("Качка на волнах")]
    [Tooltip("Вода (OceanWaves). Если пусто — OceanWaves.Instance.")]
    public OceanWaves ocean;
    [Tooltip("Навигация: позиция и курс судна на шаге физики. Если пусто — ищется в родителях.")]
    public ShipNavigation navigation;
    [Tooltip("Параметры судна (периоды и затухания качки, L, B). Если пусто — из ShipNavigation.")]
    public ShipParameters parametersOverride;
    [Tooltip("Центр качки в локальных координатах Ship: мидель, диаметральная плоскость, ватерлиния. " +
             "Вокруг него поворачивается судно, вокруг него же сетка точек сэмплирования. Задаёт Setup Seakeeping.")]
    public Vector3 pivotLocal = new Vector3(0f, -5f, 0f);
    [Tooltip("Точек вдоль корпуса (от кормы до носа)")]
    [Range(2, 9)] public int samplesAlong = 5;
    [Tooltip("Точек поперёк корпуса (от ЛБ до ПБ)")]
    [Range(2, 5)] public int samplesAcross = 3;
    [Tooltip("Сдвиг судна по вертикали относительно исходной позиции, м (запрос 15: катер — палуба ниже мостика; " +
             "задаёт ShipTypeSwitcher)")]
    public float verticalOffset;

    [Header("Комфорт (укачивание)")]
    [Tooltip("Общий множитель амплитуды качки: 1 — как в модели, 0 — без качки")]
    [Range(0f, 1f)] public float comfortScale = 1f;
    [Tooltip("Предел крена, градусы (мягкий: max·tanh(φ/max))")]
    public float maxRollDeg = 20f;
    [Tooltip("Предел дифферента, градусы (мягкий)")]
    public float maxPitchDeg = 8f;

    [Header("Отладка")]
    [Tooltip("Рисовать в Scene View точки сэмплирования на поверхности воды")]
    public bool drawGizmos = true;
    [Tooltip("Среднее время расчёта качки за шаг физики (сэмплер + МНК + динамика), мкс")]
    [SerializeField] private float stepTimeUs;
    [Tooltip("Максимальное время шага за последние ~10 с, мкс")]
    [SerializeField] private float stepTimeMaxUs;
    [SerializeField] private float rollDeg, pitchDeg, heaveM;

    [Header("Синусоиды: крен (Roll — наклон влево-вправо)")]
    [Tooltip("Максимальный угол крена при seaState=1 (градусы)")]
    public float rollAmplitude = 12f;
    [Tooltip("Период крена в секундах")]
    public float rollPeriod = 7f;

    [Header("Синусоиды: дифферент (Pitch — наклон нос-корма)")]
    [Tooltip("Максимальный угол дифферента при seaState=1 (градусы)")]
    public float pitchAmplitude = 5f;
    [Tooltip("Период дифферента в секундах")]
    public float pitchPeriod = 9f;

    [Header("Синусоиды: рыскание (Yaw — поворот вокруг вертикальной оси)")]
    [Tooltip("Максимальный угол рыскания при seaState=1 (градусы)")]
    public float yawAmplitude = 2f;
    [Tooltip("Период рыскания в секундах")]
    public float yawPeriod = 13f;

    [Header("Синусоиды: вертикальная качка (Heave)")]
    [Tooltip("Максимальная амплитуда подъёма/опускания в метрах при seaState=1")]
    public float heaveAmplitude = 1.5f;
    [Tooltip("Период вертикальной качки в секундах")]
    public float heavePeriod = 8f;

    [Header("Синхронизация с шейдером воды")]
    [Tooltip("Материал воды (OceanMaterial) — для автосинхронизации _WaveIntensity")]
    public Material oceanMaterial;

    // Исходная позиция и поворот (запоминаем при старте)
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    // ── Качка на волнах ──
    private SeakeepingModel model;
    private MotionMode lastMode;
    private double heavePrev, rollPrev, pitchPrev;   // состояние на предыдущем шаге — для интерполяции
    private float timingWindowStart;

    // ── Публичные свойства (запись упражнения C1, отображение) ──
    /// <summary>Крен в кадре после ограничений комфорта, градусы (> 0 — на ПБ).</summary>
    public float RollDeg => rollDeg;
    /// <summary>Дифферент в кадре после ограничений комфорта, градусы (> 0 — нос вверх).</summary>
    public float PitchDeg => pitchDeg;
    /// <summary>Вертикальное смещение в кадре, м (> 0 — вверх).</summary>
    public float HeaveM => heaveM;
    /// <summary>Модель качки (физика без комфорта); null в синусоидальном режиме.</summary>
    public SeakeepingModel Model => model;

    private bool started;

    // ── К4: качка мостика для игрока (меню Y) ──
    /// <summary>Ступени «Качка мостика», % (по нажатию — следующая, после 0 — снова 100).</summary>
    public static readonly int[] ComfortSteps = { 100, 50, 25, 0 };
    private const string ComfortPref = "VRBridge.BridgeMotionPct";
    private BridgeMenuCommand comfortCommand;

    /// <summary>Качка мостика для игрока, % (comfortScale × 100) — пишется в session.json.</summary>
    public int ComfortPercent => Mathf.RoundToInt(comfortScale * 100f);

    void Awake()
    {
        try
        {
            if (PlayerPrefs.HasKey(ComfortPref))
                comfortScale = Mathf.Clamp01(PlayerPrefs.GetInt(ComfortPref, 100) / 100f);
        }
        catch (System.Exception) { }   // нет доступа к настройкам — остаётся значение сцены
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder("Общее", -95);
        BridgeMenu.SetGroupExpanded("Общее", true);   // на главной странице меню Y
        comfortCommand = BridgeMenu.Register("Общее", () => $"Качка мостика: {ComfortPercent} %", NextComfortStep)
            .InMenu(BridgeMenu.SettingsMenuId);
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(comfortCommand);
        comfortCommand = null;
    }

    /// <summary>Следующая ступень (100 → 50 → 25 → 0 → 100 %); значение между ступенями — к ближайшей меньшей.</summary>
    public void NextComfortStep()
    {
        int now = ComfortPercent, next = ComfortSteps[0];
        for (int i = 0; i < ComfortSteps.Length; i++)
            if (ComfortSteps[i] < now) { next = ComfortSteps[i]; break; }
        SetComfortPercent(next);
    }

    public void SetComfortPercent(int percent)
    {
        comfortScale = Mathf.Clamp01(percent / 100f);
        try { PlayerPrefs.SetInt(ComfortPref, ComfortPercent); PlayerPrefs.Save(); }
        catch (System.Exception) { }
        Debug.Log($"[Качка] Качка мостика для игрока: {ComfortPercent} % (волны, модель и запись не меняются).", this);
    }

    void Start()
    {
        CaptureInitial();
        if (navigation == null) navigation = GetComponentInParent<ShipNavigation>();
        lastMode = mode;
    }

    private void CaptureInitial()
    {
        if (started) return;
        started = true;
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;
    }

    // Исходная позиция со сдвигом по вертикали (катер, запрос 15)
    private Vector3 BasePosition => initialPosition + Vector3.up * verticalOffset;

    /// <summary>
    /// Запрос 15: сменить центр качки (ватерлиния другого корпуса) и сдвиг судна по вертикали.
    /// Качка начинается заново с равновесия — без рывка от старого состояния.
    /// </summary>
    public void SetHullFrame(Vector3 pivot, float offset)
    {
        CaptureInitial();
        pivotLocal = pivot;
        verticalOffset = offset;
        model = null;
        heavePrev = rollPrev = pitchPrev = 0.0;
        transform.localPosition = BasePosition;
        transform.localRotation = initialRotation;
    }

    private ShipParameters Parameters =>
        parametersOverride != null ? parametersOverride : (navigation != null ? navigation.parameters : null);

    private OceanWaves Ocean => ocean != null ? ocean : OceanWaves.Instance;

    void FixedUpdate()
    {
        if (mode != MotionMode.Waves)
        {
            model = null;   // при возврате в Waves — снова с равновесия, без рывка
            return;
        }

        OceanWaves water = Ocean;
        ShipParameters p = Parameters;
        if (water == null || p == null)
            return;

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        if (model == null) model = new SeakeepingModel(p);
        model.Parameters = p;
        model.SamplesAlong = samplesAlong;
        model.SamplesAcross = samplesAcross;
        SetGridCenter();

        GetShipState(out double east, out double north, out double psi);
        heavePrev = model.Heave; rollPrev = model.Roll; pitchPrev = model.Pitch;
        bool first = model.SampleHeight.Length == 0;
        model.Step(water.Field, east, north, psi, Time.fixedDeltaTime);
        if (first) { heavePrev = model.Heave; rollPrev = model.Roll; pitchPrev = model.Pitch; }

        // Время шага: экспоненциальное среднее и максимум за окно ~10 с
        float us = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency);
        stepTimeUs = stepTimeUs <= 0f ? us : stepTimeUs + (us - stepTimeUs) * 0.02f;
        if (Time.time - timingWindowStart > 10f) { timingWindowStart = Time.time; stepTimeMaxUs = 0f; }
        stepTimeMaxUs = Mathf.Max(stepTimeMaxUs, us);
    }

    void Update()
    {
        if (mode != lastMode)
        {
            lastMode = mode;
            transform.localPosition = BasePosition;
            transform.localRotation = initialRotation;
        }

        if (mode == MotionMode.Waves)
            UpdateWaves();
        else
            UpdateSinusoidal();

        // Синхронизация с шейдером воды
        if (oceanMaterial != null)
        {
            oceanMaterial.SetFloat("_WaveIntensity", seaState);
        }
    }

    // ─────────── Качка на волнах: отображение ───────────
    private void UpdateWaves()
    {
        if (model == null)
            return;

        // Интерполяция между шагами физики — та же α, что у ShipNavigation и OceanWaves
        float dt = Time.fixedDeltaTime;
        float alpha = dt > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / dt) : 1f;
        double heave = heavePrev + (model.Heave - heavePrev) * alpha;
        double roll = rollPrev + (model.Roll - rollPrev) * alpha;
        double pitch = pitchPrev + (model.Pitch - pitchPrev) * alpha;

        // Комфорт: множитель и мягкие пределы
        heaveM = (float)(heave * comfortScale);
        rollDeg = (float)SeakeepingModel.SoftLimit(roll * Mathf.Rad2Deg * comfortScale, maxRollDeg);
        pitchDeg = (float)SeakeepingModel.SoftLimit(pitch * Mathf.Rad2Deg * comfortScale, maxPitchDeg);

        // Оси судна в системе родителя (ShipRoot): нос, правый борт, вверх
        Vector3 up = Vector3.up;
        Vector3 bow = initialRotation * BowLocalAxis;
        bow.y = 0f;
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back;
        Vector3 starboard = Vector3.Cross(up, bow);

        // Поворот вокруг оси на +α переводит ПБ вверх (вокруг носа) и нос вниз (вокруг ПБ),
        // поэтому крен на ПБ и дифферент «нос вверх» — с минусом.
        Quaternion q = Quaternion.AngleAxis(-pitchDeg, starboard) * Quaternion.AngleAxis(-rollDeg, bow);

        // Поворот вокруг центра качки + вертикальное смещение
        Vector3 pivot = initialRotation * pivotLocal;
        transform.localRotation = q * initialRotation;
        transform.localPosition = BasePosition + pivot - q * pivot + up * heaveM;
    }

    private Vector3 BowLocalAxis
    {
        get
        {
            WorldOrigin origin = WorldOrigin.Instance;
            return origin != null ? origin.bowLocalAxis : Vector3.back;
        }
    }

    /// <summary>
    /// 19.3б-2: мидель корпуса (центр качки pivotLocal) от ShipRoot в осях судна: к носу, к ПБ, м.
    /// ShipNavigation ставит ShipRoot так, чтобы эта точка была в точке модели (контуры контактов = видимый корпус).
    /// </summary>
    public void HullCentre(out float forward, out float starboard)
    {
        CaptureInitial();
        Vector3 bow = initialRotation * BowLocalAxis;
        bow.y = 0f;
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back;
        Vector3 stb = Vector3.Cross(Vector3.up, bow);
        Vector3 c = initialPosition + initialRotation * pivotLocal;
        forward = Vector3.Dot(c, bow);
        starboard = Vector3.Dot(c, stb);
    }

    // Центр сетки точек — центр качки в системе судна (к носу, к ПБ) относительно ShipRoot
    private void SetGridCenter()
    {
        Vector3 bow = initialRotation * BowLocalAxis;
        bow.y = 0f;
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back;
        Vector3 starboard = Vector3.Cross(Vector3.up, bow);
        Vector3 c = initialPosition + initialRotation * pivotLocal;
        model.CenterForward = Vector3.Dot(c, bow);
        model.CenterStarboard = Vector3.Dot(c, starboard);
    }

    // Позиция (м) и курс (рад) точки ShipRoot на текущем шаге физики
    private void GetShipState(out double east, out double north, out double psi)
    {
        ShipDynamicsModel nav = navigation != null && navigation.isActiveAndEnabled ? navigation.Model : null;
        if (nav != null)
        {
            // 19.3б-2: точка модели — мидель корпуса; сетка точек считается от ShipRoot
            WorldPosition root = navigation.RootFor(nav.East, nav.North, nav.Psi);
            east = root.East; north = root.North; psi = nav.Psi;
            return;
        }

        // Без навигации: позиция из WorldOrigin, курс — по направлению носа родителя
        WorldOrigin origin = WorldOrigin.Instance;
        WorldPosition pos = origin != null ? origin.ShipPosition : default;
        east = pos.East; north = pos.North;
        Vector3 bow = (transform.parent != null ? transform.parent.rotation : Quaternion.identity) * (initialRotation * BowLocalAxis);
        psi = System.Math.Atan2(bow.x, bow.z);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || model == null || model.SampleHeight.Length == 0)
            return;
        OceanWaves water = Ocean;
        WorldOrigin origin = WorldOrigin.Instance;
        if (water == null || origin == null)
            return;

        // Точки сэмплирования на поверхности воды (на момент последнего шага физики)
        Gizmos.color = Color.yellow;
        for (int j = 0; j < model.SampleHeight.Length; j++)
        {
            Vector3 p = origin.WorldToScene(new WorldPosition(model.SampleEast[j], model.SampleNorth[j]));
            p.y = water.MeanLevel + (float)model.SampleHeight[j];
            Gizmos.DrawSphere(p, 0.3f);
        }
        // Центр качки
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.TransformPoint(pivotLocal), 0.6f);
    }

    // ─────────── Синусоиды (запасной режим, без изменений) ───────────
    private void UpdateSinusoidal()
    {
        float t = Time.time;

        // Рассчитываем смещения с разными фазами для естественности
        float roll  = Mathf.Sin(t * 2f * Mathf.PI / rollPeriod)  * rollAmplitude  * seaState;
        float pitch = Mathf.Sin(t * 2f * Mathf.PI / pitchPeriod + 1.3f) * pitchAmplitude * seaState;
        float yaw   = Mathf.Sin(t * 2f * Mathf.PI / yawPeriod   + 2.7f) * yawAmplitude   * seaState;
        float heave = Mathf.Sin(t * 2f * Mathf.PI / heavePeriod  + 0.8f) * heaveAmplitude * seaState;

        // Добавляем вторую гармонику для реализма (не идеальная синусоида)
        roll  += Mathf.Sin(t * 2f * Mathf.PI / (rollPeriod  * 0.5f) + 0.5f) * rollAmplitude  * seaState * 0.2f;
        pitch += Mathf.Sin(t * 2f * Mathf.PI / (pitchPeriod * 0.6f) + 1.0f) * pitchAmplitude * seaState * 0.15f;

        // Применяем поворот
        // К4: множитель качки мостика — и в запасном режиме
        roll *= comfortScale; pitch *= comfortScale; yaw *= comfortScale; heave *= comfortScale;

        Quaternion rocking = Quaternion.Euler(pitch, yaw, roll);
        transform.localRotation = initialRotation * rocking;

        // Применяем вертикальное смещение
        Vector3 pos = BasePosition;
        pos.y += heave;
        transform.localPosition = pos;

        rollDeg = roll; pitchDeg = pitch; heaveM = heave;
    }

    /// <summary>
    /// Вызывается из UI/скрипта для смены погоды.
    /// Плавно меняет seaState за заданное время.
    /// </summary>
    public void SetSeaState(float targetState, float transitionTime = 3f)
    {
        StopAllCoroutines();
        StartCoroutine(TransitionSeaState(targetState, transitionTime));
    }

    private System.Collections.IEnumerator TransitionSeaState(float target, float duration)
    {
        float startState = seaState;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            seaState = Mathf.Lerp(startState, target, elapsed / duration);
            yield return null;
        }

        seaState = target;
    }
}
