// ============================================================
// MooringController.cs — Причал, кранцы, концы и подруливающее в сцене (запрос 19.2б)
//
// Висит на ShipRoot рядом с ShipNavigation. На каждом шаге модели (ShipNavigation.BeforeStep,
// до model.Step) вызывает MooringSystem.Apply (Core, 19.2а): подруливающее — всегда, причал —
// ближайший из WaterArea.berths в пределах activationRangeM (кранцы, удар о кромку, концы).
// Силы действуют только в модели 3 СС (решение 19.2): в модели Номото шаг пропускается, поданные
// концы отдаются (смена модели — только вне упражнения). На катере (ShipTypeSwitcher) — выключено.
//
// События MooringSystem → шина упражнения (MooringEventBus; единицы — ExerciseEvents.cs) и консоль.
// Рычаг подруливающего (ThrusterLever, пульт) — источник команды; меню «Управление» (кнопка B)
// ставит его через SetThrusterIndex. Смена положения — событие ThrusterChanged.
// Операция «Швартовка» (меню «Управление» на кнопке B; 16.10.2026 вынесена из «Отладки» — решение
// пользователя): «Судно к причалу: лагом» и «… подход» — в любом режиме тренажёра, только в свободном
// плавании (не в упражнении и не на паузе брифинга); модель 3 СС ставится сама.
// Концы (19.2б-2): подменю «Концы» меню B — выбор конца, состояние (в «экзамене» — словами, без кН), команды
// LineOrder (подать / выбирать / травить / держать / на кнехты / отдать) → MooringSystem.Order, событие LineCommand;
// вид концов — MooringLinesView (добавляется сам).
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-89)]
public class MooringController : MonoBehaviour
{
    public static MooringController Instance { get; private set; }

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;
    [Tooltip("Акватория с причалами; пусто — из WaterAreaView")]
    public WaterArea area;
    public ThrusterLever thrusterLever;
    public GroundingMonitor grounding;

    [Header("Причал")]
    [Tooltip("Причал считается (кранцы, кромка, концы), если до кромки не дальше, м")]
    public float activationRangeM = 600f;
    [Tooltip("Высота клюзов над водой, м (MooringSystem.FairleadHeightM; подогнать по виду в сцене)")]
    public float fairleadHeightM = 2.5f;

    [Header("Операция «Судно к причалу» (меню B → «Швартовка»)")]
    [Tooltip("Лагом: зазор борта до кромки, м")]
    public float alongsideGapM = 30f;
    [Tooltip("Подход: мидель за approachDistanceM до середины причала, борт в approachGapM от кромки")]
    public float approachDistanceM = 300f;
    public float approachGapM = 60f;
    public float approachSpeedKnots = 3f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private string berthName = "";
    [SerializeField] private float berthDistanceM = float.NaN;
    [SerializeField] private float fenderForceKn;
    [SerializeField] private float thrusterActualPercent;
    [SerializeField] private bool inContact;

    private MooringSystem system;
    private int berthIndex = -1;
    private int ownThrusterIndex = ThrusterOrders.ZeroIndex;   // без рычага на пульте (Setup не выполнен)
    private double simTime;
    private bool silentLever;
    private readonly System.Collections.Generic.List<BridgeMenuCommand> commands = new System.Collections.Generic.List<BridgeMenuCommand>();

    /// <summary>Core-модель (null до первого шага).</summary>
    public MooringSystem System => system;
    /// <summary>Номер причала в WaterArea.berths, силы которого считаются (−1 — нет рядом).</summary>
    public int ActiveBerthIndex => system != null && system.Berth != null && Operating ? berthIndex : -1;
    public BerthArea ActiveBerth => ActiveBerthIndex >= 0 ? system.Berth : null;
    public WaterArea Area
    {
        get
        {
            if (area == null)
            {
                var view = FindAnyObjectByType<WaterAreaView>();
                if (view != null) area = view.area;
            }
            return area;
        }
    }
    /// <summary>Швартовка работает: модель 3 СС и не катер.</summary>
    public bool Operating => navigation != null && navigation.Model != null && navigation.Uses3Dof && !OnFastBoat;
    private static bool OnFastBoat => ShipTypeSwitcher.Instance != null && ShipTypeSwitcher.Instance.Type == ShipType.FastBoat;

    /// <summary>Положение подруливающего (ThrusterOrders): рычаг на пульте или своё, если рычага нет.</summary>
    public int ThrusterIndex => thrusterLever != null ? thrusterLever.Index : ownThrusterIndex;
    /// <summary>Фактическая тяга подруливающего, доля −1 … 1 (раскрутка τ = 3 с).</summary>
    public double ThrusterActual => system != null && system.Thruster != null ? system.Thruster.Actual : 0.0;

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Швартовка] В сцене несколько MooringController — используется первый.", this);
        else Instance = this;
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (grounding == null) grounding = FindAnyObjectByType<GroundingMonitor>();
        if (thrusterLever == null) thrusterLever = FindAnyObjectByType<ThrusterLever>();
        if (GetComponent<MooringLinesView>() == null) gameObject.AddComponent<MooringLinesView>();   // 19.2б-2: вид концов (Setup не нужен)
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (navigation != null)
        {
            navigation.BeforeStep += OnBeforeStep;
            navigation.MotionModelChanged += OnMotionModelChanged;
        }
        if (thrusterLever != null) thrusterLever.Changed += OnLeverChanged;
        ExerciseEvents.Raised += OnExerciseEvent;

        commands.Add(BridgeMenu.Register(ShipControlOrders.BerthGroup, () => $"Судно к причалу: лагом, {alongsideGapM:0} м",
            () => ToBerth(false), CanTeleport, hideWhenUnavailable: true).InMenu(ShipControlOrders.MenuId));
        commands.Add(BridgeMenu.Register(ShipControlOrders.BerthGroup, () => $"Судно к причалу: подход {approachDistanceM:0} м, {approachSpeedKnots:0.#} уз",
            () => ToBerth(true), CanTeleport, hideWhenUnavailable: true).InMenu(ShipControlOrders.MenuId));
        BridgeMenu.SetGroupExpanded(ShipControlOrders.BerthGroup, true);
        BridgeMenu.SetGroupOrder(ShipControlOrders.BerthGroup, 20);
        RegisterLineCommands();
    }

    void OnDisable()
    {
        if (navigation != null)
        {
            navigation.BeforeStep -= OnBeforeStep;
            navigation.MotionModelChanged -= OnMotionModelChanged;
        }
        if (thrusterLever != null) thrusterLever.Changed -= OnLeverChanged;
        ExerciseEvents.Raised -= OnExerciseEvent;
        foreach (BridgeMenuCommand c in commands) BridgeMenu.Unregister(c);
        commands.Clear();
    }

    // ─────────── Шаг ───────────

    private void OnBeforeStep(ShipDynamicsModel m, double dt)
    {
        simTime += dt;
        if (system == null || system.Parameters != navigation.parameters)
        {
            system = new MooringSystem(null, navigation.parameters) { FairleadHeightM = fairleadHeightM };
            Debug.Log($"[Швартовка] {navigation.parameters.name}: подруливающее {system.Thruster.MaxThrust / 1000.0:F1} кН в {system.Thruster.X:F1} м от миделя, " +
                      $"MBL концов {MooringLine.MblFor(navigation.parameters) / 1000.0:F0} кН.", this);
        }
        if (!m.Uses3Dof || OnFastBoat)
        {
            UpdateInspector(null);
            return;
        }

        SelectBerth(new WorldPosition(m.East, m.North));
        system.FairleadHeightM = fairleadHeightM;
        system.Thruster.Command = ThrusterOrders.Command(ThrusterIndex);
        system.Apply(m, dt, simTime);

        if (system.Events.Count > 0)
        {
            foreach (MooringEvent me in system.Events) Publish(MooringEventBus.ToExerciseEvent(me));
            system.Events.Clear();
        }
        UpdateInspector(m);
    }

    // Ближайший причал в пределах activationRangeM; при смене причала поданные концы отдаются
    private void SelectBerth(WorldPosition p)
    {
        WaterArea a = Area;
        int i = a != null ? a.NearestBerth(p, out double d) : -1;
        BerthArea berth = null;
        if (i >= 0 && a.berths[i].DistanceTo(p) <= activationRangeM) berth = a.berths[i];
        else i = -1;
        if (berth == system.Berth) return;
        bool hadLines = false;
        foreach (MooringLine l in system.Lines) hadLines |= l.State == MooringLineState.Fast;
        if (hadLines) system.ResetState();
        system.Berth = berth;
        berthIndex = i;
        Debug.Log(berth != null
            ? $"[Швартовка] Рядом причал «{berth.name}» ({berth.Length:F0} м, кранцы D {berth.fenderOuterDiameterM:F2} м через ≤ {berth.fenderSpacingM:0.#} м, курс кромки {berth.BearingDeg:F0}°)."
            : "[Швартовка] Причал далеко — считается только подруливающее.", this);
    }

    private void UpdateInspector(ShipDynamicsModel m)
    {
        BerthArea b = system != null ? system.Berth : null;
        berthName = b != null ? b.name : "";
        berthDistanceM = b != null && m != null ? (float)b.DistanceTo(new WorldPosition(m.East, m.North)) : float.NaN;
        fenderForceKn = system != null ? (float)(system.TotalFenderForce / 1000.0) : 0f;
        thrusterActualPercent = (float)(ThrusterActual * 100.0);
        inContact = system != null && system.InContact;
    }

    // ─────────── Подруливающее ───────────

    /// <summary>Поставить положение подруливающего (меню «Управление»): рычаг на пульте доворачивается сам.</summary>
    public void SetThrusterIndex(int index)
    {
        index = ThrusterOrders.Clamp(index);
        if (thrusterLever != null) { thrusterLever.SetIndex(index); return; }
        int old = ownThrusterIndex;
        if (old == index) return;
        ownThrusterIndex = index;
        OnLeverChanged(old, index, true);
    }

    private void OnLeverChanged(int oldIndex, int newIndex, bool fromCode)
    {
        if (silentLever) return;
        Publish(new ExerciseEvent(ExerciseEventType.ThrusterChanged)
        {
            Index = newIndex, Value = ThrusterOrders.Command(newIndex) * 100.0, Value2 = ThrusterOrders.Command(oldIndex) * 100.0,
            Subject = fromCode ? "меню" : "рычаг"
        });
    }

    // ─────────── Сброс ───────────

    /// <summary>Концы на борт, подруливающее 0, эпизоды навала — сначала (загрузка сценария, перестановка судна).</summary>
    public void ResetMooring()
    {
        system?.ResetState();
        silentLever = true;
        try
        {
            if (thrusterLever != null) thrusterLever.SetIndex(ThrusterOrders.ZeroIndex);
            ownThrusterIndex = ThrusterOrders.ZeroIndex;
        }
        finally { silentLever = false; }
    }

    private void OnMotionModelChanged(bool to3Dof)
    {
        if (!to3Dof && system != null)
        {
            system.ResetState();
            Debug.Log("[Швартовка] Модель Номото: силы причала и концов не действуют, концы отданы.", this);
        }
    }

    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ScenarioLoaded) ResetMooring();
    }

    // ─────────── Операция «Судно к причалу» ───────────

    private bool OutsideExercise => ScenarioRunner.Instance == null || ScenarioRunner.Instance.CurrentPhase != ScenarioRunner.Phase.Running;

    // Свободное плавание: не в упражнении и не на паузе (брифинг сценария — судно стоит, ShipNavigation.Paused)
    private bool CanTeleport() =>
        navigation != null && navigation.Model != null && OutsideExercise && !navigation.Paused && !OnFastBoat && Area != null && Area.berths.Count > 0;

    private void ToBerth(bool approach)
    {
        if (!CanTeleport()) return;
        WorldPosition ship = navigation.PositionEastNorth;
        int i = Area.NearestBerth(ship, out _);
        if (i < 0) return;
        BerthArea b = Area.berths[i];
        navigation.SetMotionModel(ShipNavigation.MotionChoice.Manoeuvring3Dof);
        float beam = navigation.parameters.beam;
        double t = 0.5 * b.Length - (approach ? approachDistanceM : 0.0);
        WorldPosition p = b.AlongsidePosition(t, approach ? approachGapM : alongsideGapM, beam, out double hdg);
        double knots = approach ? approachSpeedKnots : 0.0;
        navigation.TeleportTo(p.East, p.North, hdg, knots, 0.0);
        navigation.SetRudderOrder(0f);
        if (navigation.telegraph != null)
            navigation.telegraph.SetSpeedFactor(approach ? (float)(knots / Mathf.Max(1f, navigation.parameters.serviceSpeedKnots)) : 0f);
        if (grounding != null) grounding.ResetState();
        ResetMooring();
        Debug.Log($"[Швартовка] Судно к причалу «{b.name}»: {(approach ? $"подход, {approachDistanceM:0} м до середины, борт в {approachGapM:0} м от кромки, {knots:0.#} уз" : $"лагом, борт в {alongsideGapM:0} м от кромки")}, " +
                  $"курс {hdg:F0}°, причал по левому борту, модель 3 СС." +
                  (approach ? "" : " Машина «Стоп»: руль без хода не действует — «толчок» машиной вперёд с рулём или подруливающее."), this);
    }

    // ─────────── Концы (19.2б-2): подменю «Концы» меню B ───────────
    // Выбор конца переключателем, строка состояния (в «экзамене» — без кН, словами экипажа), команды
    // выбранному концу (LineOrder) и «Все поданные: держать». Каждая команда — событие LineCommand.

    private int selectedLine;

    /// <summary>Выбранный в меню конец (номер в MooringSystem.Lines).</summary>
    public int SelectedLine => selectedLine;

    private MooringLine Selected => system != null && selectedLine >= 0 && selectedLine < system.Lines.Count ? system.Lines[selectedLine] : null;

    private void RegisterLineCommands()
    {
        string g = ShipControlOrders.LinesGroup, menu = ShipControlOrders.MenuId;
        int count = global::System.Enum.GetValues(typeof(MooringLineKind)).Length;
        commands.Add(BridgeMenu.RegisterStepper(g, () => $"Конец: {MooringLine.Title((MooringLineKind)selectedLine)}",
            () => selectedLine = (selectedLine + count - 1) % count, () => selectedLine = (selectedLine + 1) % count).InMenu(menu));
        commands.Add(BridgeMenu.Register(g, () => $"<size=85%>{(Operating ? LineOrders.State(Selected, TrainerModes.HintsAllowedNow) : "концы — только в модели 3 СС")}</size>",
            null, () => false).InMenu(menu));
        foreach (LineOrder o in (LineOrder[])global::System.Enum.GetValues(typeof(LineOrder)))
        {
            LineOrder order = o;
            commands.Add(BridgeMenu.Register(g, () => Capital(LineOrders.Title(order)), () => OrderSelected(order), () => CanOrder(order)).InMenu(menu));
        }
        commands.Add(BridgeMenu.Register(g, "Все поданные: держать", HoldAll, () => AnyFast).InMenu(menu));
        BridgeMenu.SetGroupOrder(g, 30);
    }

    private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    private bool AnyFast
    {
        get
        {
            if (!Operating || system == null) return false;
            foreach (MooringLine l in system.Lines) if (l.State == MooringLineState.Fast) return true;
            return false;
        }
    }

    private bool CanOrder(LineOrder o)
    {
        MooringLine l = Selected;
        if (!Operating || l == null || navigation.Paused) return false;
        if (o == LineOrder.Give) return ActiveBerth != null && l.State != MooringLineState.Fast;
        return l.State == MooringLineState.Fast;
    }

    private void OrderSelected(LineOrder o) => OrderLine(selectedLine, o);

    /// <summary>Команда концу (меню; в части 3 — сценарий): в модель, событие LineCommand в шину.</summary>
    public bool OrderLine(int index, LineOrder o)
    {
        if (system == null || navigation == null || navigation.Model == null) return false;
        bool ok = system.Order(index, o, navigation.Model, simTime, out string reason);
        string title = index >= 0 && index < system.Lines.Count ? system.Lines[index].Title() : "—";
        Publish(new ExerciseEvent(ExerciseEventType.LineCommand)
        {
            Index = index, Value = (int)o, Value2 = ok ? 1 : 0, Subject = ok ? title : $"{title}: {reason}"
        });
        // Событие подачи / отдачи (LineMadeFast / LineReleased) — сразу, а не на следующем шаге
        if (system.Events.Count > 0)
        {
            foreach (MooringEvent me in system.Events) Publish(MooringEventBus.ToExerciseEvent(me));
            system.Events.Clear();
        }
        return ok;
    }

    private void HoldAll()
    {
        if (!AnyFast) return;
        for (int i = 0; i < system.Lines.Count; i++)
            if (system.Lines[i].State == MooringLineState.Fast && !system.Lines[i].OnBitts && system.Lines[i].Winch != WinchCommand.Hold)   // на кнехтах — не трогать
                OrderLine(i, LineOrder.Hold);
    }

    private void Publish(ExerciseEvent e)
    {
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Швартовка] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }
}
