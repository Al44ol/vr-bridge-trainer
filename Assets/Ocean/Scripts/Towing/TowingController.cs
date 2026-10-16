// ============================================================
// TowingController.cs — Буксир и ветер в сцене (запрос 19.3б)
//
// Висит на ShipRoot рядом с ShipNavigation и MooringController (ставится сам при загрузке сцены — Setup не нужен).
// На каждом шаге модели (ShipNavigation.BeforeStep, до model.Step):
//   • ветер (WindLoad, Isherwood) — на своё судно; если буксир вызван — TowingSystem.Apply (Core, 19.3а):
//     ветер на судно и буксир, трос, кранец буксира, автоматика шкипера и шаг модели буксира;
//   • только в модели 3 СС и не на катере (в модели Номото буксир отпускается, ветра нет).
// Ветер: из сценария (ScenarioDefinition.windKnots / windFromDeg — при загрузке, у прежних сценариев 0) и из меню A
//   «Обстановка» → «Ветер: < 0 / 10 / 15 / 20 / 25 уз >», «Ветер откуда: < … >» (ступени 45°) — во всех режимах
//   тренажёра, как видимость (решение пользователя). Событие WindChanged; если на «Начать» ветер отличается от сценария —
//   WindChanged «на начало». Волны ветер не меняет (OceanWaves.windFromDeg — только вид).
// Буксир (меню B «Управление» → «Буксир»): вызвать к корме / к носу / к миделю — буксир ставится на место сразу
//   (подход буксира не моделируется); режим (тянуть / толкать / стоп), направление силы на судно, тяга
//   10 / 25 / 50 / 100 % — переключатели TugOrders; подать / отдать трос; отпустить буксир. В упражнении — только
//   если сценарий разрешает (tugAvailable), в свободном плавании — всегда (решение пользователя; модель 3 СС ставится сама).
//   Строка состояния — в «экзамене» словами шкипера, без кН (TugOrders.State).
// События TowingSystem → шина (TowEventBus) и консоль; буксир — цель реестра ExerciseTargets (TugTarget, id «TUG»).
// Вид — TugView (модель, качка, огни, трос), объект «Tug» в корне сцены.
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-89)]
public class TowingController : MonoBehaviour
{
    public const string Group = "Буксир";
    private const string SettingsGroup = "Обстановка";

    public static TowingController Instance { get; private set; }

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;
    [Tooltip("Параметры буксира; пусто — Resources/ShipParameters_Tug (создаётся сам при входе в Play Mode), иначе ShipPresets.FillTug")]
    public ShipParameters tugParameters;

    [Header("Буксир")]
    [Tooltip("Тяга на гаке, т (19.3а: 20 т под судно ≈ 720 т)")]
    public float bollardPullTonnes = (float)TugSpec.DefaultBollardPullTonnes;
    [Tooltip("Рабочая длина буксирного троса, м")]
    public float towLineLengthM = 30f;
    [Tooltip("Подать трос можно, если нос буксира ближе к клюзу, м")]
    public float maxGiveDistanceM = 60f;
    [Tooltip("Высота клюза троса над водой, м (вид)")]
    public float fairleadHeightM = 2.5f;

    [Header("Ветер")]
    [Tooltip("Ступени ветра в меню, уз")]
    public float[] windStepsKnots = { 0f, 10f, 15f, 20f, 25f };

    [Header("Состояние (только чтение)")]
    [SerializeField] private string state = "";
    [SerializeField] private float windKnotsNow;
    [SerializeField] private float windFromNow;

    private TowingSystem system;
    private TugTarget target;
    private TugView view;
    private Wind wind;
    private ShipWindArea shipWind;
    private ShipParameters shipWindFor;
    private double simTime;
    private double lastE = double.NaN, lastN = double.NaN;
    private ShipParameters runtimeTug;

    // Переключатели меню
    private TugMode mode = TugMode.Stop;
    private int dirIndex, powerIndex;
    private readonly System.Collections.Generic.List<BridgeMenuCommand> commands = new System.Collections.Generic.List<BridgeMenuCommand>();

    /// <summary>Буксир вызван.</summary>
    public bool TugPresent => system != null;
    /// <summary>Core-модель буксира (null — не вызван).</summary>
    public TowingSystem Towing => system;
    /// <summary>Ветер сейчас.</summary>
    public Wind Wind => wind;
    /// <summary>Положение буксира до последнего шага (интерполяция вида).</summary>
    public double PrevEast { get; private set; }
    public double PrevNorth { get; private set; }
    public double PrevPsi { get; private set; }

    private bool Operating => navigation != null && navigation.Model != null && navigation.Uses3Dof && !OnFastBoat;
    private static bool OnFastBoat => ShipTypeSwitcher.Instance != null && ShipTypeSwitcher.Instance.Type == ShipType.FastBoat;
    private static ScenarioRunner Runner => ScenarioRunner.Instance;
    private static bool InExercise => Runner != null && (Runner.CurrentPhase == ScenarioRunner.Phase.Running || Runner.CurrentPhase == ScenarioRunner.Phase.Briefing);

    // Сцена без компонента — поставить на объект навигации (ShipRoot)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<TowingController>() != null) return;
        ShipNavigation nav = FindAnyObjectByType<ShipNavigation>();
        if (nav == null || FindAnyObjectByType<ScenarioRunner>() == null) return;   // не сцена тренажёра
        nav.gameObject.AddComponent<TowingController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Буксир] В сцене несколько TowingController — используется первый.", this);
        else Instance = this;
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        MooringController mc = FindAnyObjectByType<MooringController>();
        if (mc != null) fairleadHeightM = mc.fairleadHeightM;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runtimeTug != null) Destroy(runtimeTug);
    }

    void OnEnable()
    {
        if (navigation != null)
        {
            navigation.BeforeStep += OnBeforeStep;
            navigation.MotionModelChanged += OnMotionModelChanged;
        }
        ExerciseEvents.Raised += OnExerciseEvent;
        RegisterWindCommands();
        RegisterTugCommands();
    }

    void OnDisable()
    {
        if (navigation != null)
        {
            navigation.BeforeStep -= OnBeforeStep;
            navigation.MotionModelChanged -= OnMotionModelChanged;
        }
        ExerciseEvents.Raised -= OnExerciseEvent;
        foreach (BridgeMenuCommand c in commands) BridgeMenu.Unregister(c);
        commands.Clear();
        if (system != null) Dismiss("отключено", false);
    }

    // ─────────── Шаг ───────────

    private void OnBeforeStep(ShipDynamicsModel m, double dt)
    {
        simTime += dt;
        if (!m.Uses3Dof || OnFastBoat)
        {
            if (system != null) Dismiss(OnFastBoat ? "катер" : "модель Номото");
            lastE = lastN = double.NaN;
            UpdateInspector();
            return;
        }
        // Перестановка судна (начальные условия, «Судно к причалу», снятие с мели) — буксир остался бы на месте
        if (system != null && !double.IsNaN(lastE))
        {
            double de = m.East - lastE, dn = m.North - lastN;
            if (de * de + dn * dn > 100.0 * 100.0) Dismiss("перестановка судна");
        }
        lastE = m.East; lastN = m.North;

        if (system != null)
        {
            if (system.ShipParams != navigation.parameters) { Dismiss("сменено судно"); }
            else
            {
                ShipDynamicsModel t = system.Tug.Model;
                PrevEast = t.East; PrevNorth = t.North; PrevPsi = t.Psi;
                system.Wind = wind;
                system.Apply(m, dt, simTime);
                PublishSystemEvents();
            }
        }
        if (system == null && !wind.Calm)
        {
            if (shipWind == null || shipWindFor != navigation.parameters)
            {
                shipWind = ShipWindArea.Estimate(navigation.parameters);
                shipWindFor = navigation.parameters;
            }
            WindLoad.Apply(m, shipWind, wind);
        }
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        state = system != null ? TugOrders.State(system, true) : "буксира нет";
        windKnotsNow = (float)(wind.SpeedMs / ShipParameters.KnotsToMs);
        windFromNow = (float)wind.FromDeg;
    }

    private void PublishSystemEvents()
    {
        if (system == null || system.Events.Count == 0) return;
        foreach (TowEvent te in system.Events) Publish(TowEventBus.ToExerciseEvent(te));
        system.Events.Clear();
    }

    // ─────────── Ветер ───────────

    /// <summary>Задать ветер (сценарий, меню): скорость, уз; «откуда», °. Событие WindChanged.</summary>
    public void SetWind(double knots, double fromDeg, string source)
    {
        knots = Mathf.Max(0f, (float)knots);
        fromDeg = ((fromDeg % 360.0) + 360.0) % 360.0;
        Wind w = Wind.Knots(knots, fromDeg);
        bool same = System.Math.Abs(w.SpeedMs - wind.SpeedMs) < 1e-6 && (w.Calm || System.Math.Abs(w.FromDeg - wind.FromDeg) < 1e-6);
        wind = w;
        UpdateInspector();
        if (same) return;
        // Источник — в Subject (как видимость, VisibilityChanged, — без признака «инструктор»: меню A доступно обоим)
        Publish(new ExerciseEvent(ExerciseEventType.WindChanged) { Value = knots, Value2 = fromDeg, Subject = source });
    }

    private double WindKnots => wind.SpeedMs / ShipParameters.KnotsToMs;

    // 19.3б-2 (по проверке в шлеме): «Ветер откуда: < случайно / 000° (N) … 315° (NW) >», затем «Ветер: < нет / 10 … 25 уз >».
    // «Случайно» — направление выбирается случайно при каждом выборе этого положения (и при загрузке сценария
    // со случайным направлением); выбранное число видно в строке.
    private bool windRandom;
    private readonly System.Random windRng = new System.Random();

    private void RegisterWindCommands()
    {
        commands.Add(BridgeMenu.RegisterStepper(SettingsGroup,
            () => $"Ветер откуда: {(windRandom ? "случайно · " : "")}{ScenarioText.Bearing(wind.FromDeg)} ({Compass(wind.FromDeg)})",
            () => StepWindFrom(-1), () => StepWindFrom(+1)));
        commands.Add(BridgeMenu.RegisterStepper(SettingsGroup,
            () => WindKnots < 0.05 ? "Ветер: нет" : $"Ветер: {ScenarioText.Num(WindKnots)} уз{(Operating ? "" : " <size=70%>(только 3 СС)</size>")}",
            () => StepWind(-1), () => StepWind(+1)));
    }

    // Положения: −1 — случайно, 0…7 — 000°, 045° … 315°
    private void StepWindFrom(int dir)
    {
        int cur = windRandom ? -1 : (int)System.Math.Round((((wind.FromDeg % 360.0) + 360.0) % 360.0) / 45.0) % 8;
        int next = Mathf.Clamp(cur + dir, -1, 7);
        if (next == cur && !windRandom) return;
        windRandom = next < 0;
        double from = windRandom ? System.Math.Round(windRng.NextDouble() * 360.0) % 360.0 : next * 45.0;
        if (WindKnots < 0.05) { wind = new Wind(0.0, from); UpdateInspector(); return; }   // без ветра — только направление на будущее
        SetWind(WindKnots, from, windRandom ? "меню, случайно" : "меню");
    }

    private void StepWind(int dir)
    {
        if (windStepsKnots == null || windStepsKnots.Length == 0) return;
        double k = WindKnots;
        int i = 0;
        for (int j = 1; j < windStepsKnots.Length; j++)
            if (System.Math.Abs(windStepsKnots[j] - k) < System.Math.Abs(windStepsKnots[i] - k)) i = j;
        if (dir > 0 && windStepsKnots[i] <= k + 0.05 || dir < 0 && windStepsKnots[i] >= k - 0.05) i += dir;
        i = Mathf.Clamp(i, 0, windStepsKnots.Length - 1);
        SetWind(windStepsKnots[i], wind.FromDeg, "меню");
    }

    private static string Compass(double deg)
    {
        string[] n = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return n[(int)System.Math.Round((((deg % 360.0) + 360.0) % 360.0) / 45.0) % 8];
    }

    // ─────────── Буксир: меню B ───────────

    private void RegisterTugCommands()
    {
        string g = Group, menu = ShipControlOrders.MenuId;
        commands.Add(BridgeMenu.Register(g, () => $"<size=85%>{StatusLine()}</size>", null, () => false).InMenu(menu));
        foreach (TugStation s in (TugStation[])global::System.Enum.GetValues(typeof(TugStation)))
        {
            TugStation st = s;
            commands.Add(BridgeMenu.Register(g, () => $"Вызвать буксир {TugOrders.StationTarget(st)}{(st == TugStation.Midship ? " (толкать)" : "")}",
                () => Call(st), CanCall, hideWhenUnavailable: true).InMenu(menu));
        }
        commands.Add(BridgeMenu.RegisterStepper(g, () => $"Режим: {TugOrders.ModeTitle(mode)}",
            () => StepMode(-1), () => StepMode(+1), CanCommand, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.RegisterStepper(g, () => $"Направление: {TugCommand.DirectionTitle(Direction)}",
            () => StepDirection(-1), () => StepDirection(+1), CanCommand, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.RegisterStepper(g, () => $"Тяга: {TugOrders.PowerTitle(Power)}",
            () => StepPower(-1), () => StepPower(+1), CanCommand, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.Register(g, "Подать трос", GiveLine, CanGiveLine, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.Register(g, "Отдать трос", ReleaseLine, () => CanCommand() && system.Line.Fast, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.Register(g, "Отпустить буксир", () => Dismiss("команда"), CanCommand, hideWhenUnavailable: true).InMenu(menu));
        BridgeMenu.SetGroupOrder(g, 35);
    }

    private string StatusLine()
    {
        if (system != null) return TugOrders.State(system, TrainerModes.HintsAllowedNow);
        if (OnFastBoat) return "буксир — не на катере";
        if (InExercise && !TugAllowedByScenario) return "в этом упражнении буксира нет";
        if (InExercise && !Operating) return "буксир — только в модели 3 СС";
        return "буксир не вызван";
    }

    private static bool TugAllowedByScenario => Runner == null || Runner.Current == null || Runner.Current.tugAvailable;

    /// <summary>Вызвать можно: буксира нет; в упражнении — если сценарий разрешает (и модель 3 СС), в свободном плавании — всегда.</summary>
    private bool CanCall()
    {
        if (system != null || navigation == null || navigation.Model == null || navigation.Paused || OnFastBoat) return false;
        if (InExercise) return TugAllowedByScenario && navigation.Uses3Dof;
        return true;
    }

    private bool CanCommand() => system != null && navigation != null && !navigation.Paused;

    private bool CanGiveLine()
    {
        if (!CanCommand() || system.Line.Fast || system.Geometry != TugMode.Pull) return false;
        return ChordToTug() <= maxGiveDistanceM;
    }

    private double ChordToTug()
    {
        WorldPosition f = system.TowPoint(navigation.Model), b = system.Tug.Bow;
        double de = b.East - f.East, dn = b.North - f.North;
        return System.Math.Sqrt(de * de + dn * dn);
    }

    private double[] Directions => system != null ? TugOrders.Directions(system.Station, mode == TugMode.Stop ? system.Geometry : mode) : TugOrders.Directions(TugStation.Stern, TugMode.Pull);
    private double Direction { get { double[] d = Directions; return d[Mathf.Clamp(dirIndex, 0, d.Length - 1)]; } }
    private double Power => TugOrders.PowerSteps[Mathf.Clamp(powerIndex, 0, TugOrders.PowerSteps.Length - 1)];

    /// <summary>Вызвать буксир к месту у судна (меню; в 19.3в — сценарий).</summary>
    public bool Call(TugStation station)
    {
        if (!CanCall()) return false;
        if (!navigation.Uses3Dof && !navigation.SetMotionModel(ShipNavigation.MotionChoice.Manoeuvring3Dof)) return false;
        ShipParameters tug = TugParameters();
        system = new TowingSystem(navigation.parameters, tug, TugSpec.For(tug, bollardPullTonnes));
        system.SetStation(station);
        system.SetGeometry(TugOrders.DefaultGeometry(station));
        mode = TugMode.Stop;
        powerIndex = 0;
        dirIndex = TugOrders.NearestIndex(Directions, TugOrders.DefaultDirection(station));
        system.Wind = wind;
        system.PlaceTug(navigation.Model, TugOrders.Command(mode, Direction, Power), towLineLengthM);
        system.Order(TugOrders.Command(mode, Direction, Power), simTime);
        system.Events.Clear();   // начальная команда — часть вызова (событие TugCalled)
        ShipDynamicsModel t = system.Tug.Model;
        PrevEast = t.East; PrevNorth = t.North; PrevPsi = t.Psi;
        lastE = navigation.Model.East; lastN = navigation.Model.North;

        target = new TugTarget(system.Tug);
        ExerciseTargets.Register(target);
        var go = new GameObject("Tug");
        view = go.AddComponent<TugView>();
        view.Init(this);
        Publish(new ExerciseEvent(ExerciseEventType.TugCalled) { Index = (int)station, Subject = TugOrders.StationTarget(station), Value = system.Tug.Spec.BollardPull / 1000.0 });
        return true;
    }

    /// <summary>Отпустить буксир (трос отдаётся).</summary>
    public void Dismiss(string reason, bool publish = true)
    {
        if (system == null) return;
        if (system.Line.Fast) { system.ReleaseLine(simTime); if (publish) PublishSystemEvents(); }
        if (target != null) ExerciseTargets.Unregister(target);
        if (view != null) Destroy(view.gameObject);
        target = null; view = null; system = null;
        mode = TugMode.Stop;
        if (publish) Publish(new ExerciseEvent(ExerciseEventType.TugDismissed) { Subject = reason ?? "" });
        else Debug.Log($"[Буксир] Отпущен ({reason}).", this);
        UpdateInspector();
    }

    private void StepMode(int dir)
    {
        if (!CanCommand()) return;
        int i = global::System.Array.IndexOf(TugOrders.Modes, mode);
        int n = Mathf.Clamp(i + dir, 0, TugOrders.Modes.Length - 1);
        if (n == i) return;
        double oldDir = Direction;
        TugMode was = mode == TugMode.Stop ? system.Geometry : mode;
        mode = TugOrders.Modes[n];
        TugMode now = mode == TugMode.Stop ? system.Geometry : mode;
        double[] dirs = Directions;
        if (now != was && now == TugMode.Push)
        {
            // Толкать — с того борта, у которого буксир сейчас: у ЛБ — сила на ПБ
            TowingSystem.ShipAxes(navigation.Model, system.Tug.Bow, out _, out double y);
            dirIndex = TugOrders.NearestIndex(dirs, y < 0.0 ? 90.0 : 270.0);
        }
        else dirIndex = TugOrders.NearestIndex(dirs, now != was ? TugOrders.DefaultDirection(system.Station) : oldDir);
        Issue();
    }

    private void StepDirection(int dir)
    {
        if (!CanCommand()) return;
        int n = Mathf.Clamp(dirIndex + dir, 0, Directions.Length - 1);
        if (n == dirIndex) return;
        dirIndex = n;
        Issue();
    }

    private void StepPower(int dir)
    {
        if (!CanCommand()) return;
        int n = Mathf.Clamp(powerIndex + dir, 0, TugOrders.PowerSteps.Length - 1);
        if (n == powerIndex) return;
        powerIndex = n;
        if (mode != TugMode.Stop) Issue();
    }

    private void Issue()
    {
        system.Order(TugOrders.Command(mode, Direction, Power), simTime);
        PublishSystemEvents();
    }

    private void GiveLine()
    {
        if (!CanGiveLine()) return;
        system.MakeFast(System.Math.Max(towLineLengthM, ChordToTug()), simTime);
        PublishSystemEvents();
    }

    private void ReleaseLine()
    {
        if (system == null || !system.Line.Fast) return;
        system.ReleaseLine(simTime);
        PublishSystemEvents();
    }

    private ShipParameters TugParameters()
    {
        if (tugParameters != null) return tugParameters;
        ShipParameters r = Resources.Load<ShipParameters>("ShipParameters_Tug");
        if (r != null) return tugParameters = r;
        if (runtimeTug == null)
        {
            runtimeTug = ScriptableObject.CreateInstance<ShipParameters>();
            runtimeTug.name = "ShipParameters_Tug (FillTug)";
            ShipPresets.FillTug(runtimeTug);
        }
        return runtimeTug;
    }

    // ─────────── Срез для записи (mooring.csv) ───────────

    /// <summary>Буксир и ветер в строку mooring.csv (TowingSample).</summary>
    public TowingSample Sample() => TowingSample.Build(system, wind);

    /// <summary>Строка для окна инструктора (с силами).</summary>
    public string InstructorLine() => system != null ? "Буксир " + TugOrders.State(system, true) : "";

    // ─────────── Шина ───────────

    private void OnMotionModelChanged(bool to3Dof)
    {
        if (!to3Dof && system != null) Dismiss("модель Номото");
    }

    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ScenarioLoaded)
        {
            if (system != null) Dismiss("загружен сценарий");
            ScenarioDefinition s = Runner != null ? Runner.Current : null;
            if (s != null)
            {
                windRandom = s.windRandomSectorDeg > 0f;
                double from = s.DrawWindFromDeg(windRng);   // 19.3б-2: случайное направление — заново при каждой загрузке
                SetWind(s.windKnots, from, windRandom ? "сценарий, случайно" : "сценарий");
            }
        }
        else if (e.Type == ExerciseEventType.ExerciseStarted)
        {
            // Ветер изменили на брифинге — состояние на начало в журнал упражнения
            ScenarioDefinition s = Runner != null ? Runner.Current : null;
            if (s != null && System.Math.Abs(WindKnots - s.windKnots) > 0.05 ||
                s != null && WindKnots > 0.05 && System.Math.Abs(System.Math.IEEERemainder(wind.FromDeg - s.windFromDeg, 360.0)) > 0.5)
                Publish(new ExerciseEvent(ExerciseEventType.WindChanged) { Value = WindKnots, Value2 = wind.FromDeg, Subject = "на начало" });
        }
    }

    private void Publish(ExerciseEvent e)
    {
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log((e.Type == ExerciseEventType.WindChanged ? "[Ветер] " : "[Буксир] ") + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }
}
