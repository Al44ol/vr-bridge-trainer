// ============================================================
// TugWorkController.cs — Упражнение на своём буксире в сцене (запрос 19.4б-2)
//
// Висит на ShipRoot рядом с ShipNavigation, MooringController, TowingController и OwnTugDrive (ставится сам при
// загрузке сцены — Setup не нужен). Работает, когда у сценария есть задание буксира (ScenarioDefinition.tugWork,
// сценарий 19-03): Core TugWorkSystem (19.4б-1) — судно-клиент (Model45m, 3 СС) с автоматикой «лоцман», причал
// клиента, контакт буксир — клиент (HullToHull), задания и команды лоцмана.
//   • Загрузка сценария (ScenarioLoaded): система создаётся, клиент ставится лагом к причалу (виден на брифинге),
//     регистрируется целью «CLIENT» (ExerciseTargets: targets.csv, карты; оценка C2 его отбрасывает — WorkVessels),
//     строится вид ClientShipView (объект «Client» в корне сцены).
//   • «Начать» (ExerciseStarted): клиент и задания — заново; свой буксир ставит ScenarioRunner из начальных условий
//     (DemoTugWorkScenario.Fill посчитал их той же TugWorkSystem.TugStart), поэтому Place(own, moveOwn: false).
//   • Шаг (ShipNavigation.BeforeStep, до шага модели своего буксира; −89, как TowingController): только во время
//     упражнения, на своём буксире, в модели 3 СС — ветер из TowingController (на свой буксир его даёт он сам),
//     TugWorkSystem.Apply(свой буксир, dt, время упражнения); события → шина (PilotOrder, TugWorkStage, касания,
//     навалы и концы клиента) и консоль. Финиш (TugWorkDone) отмечает ScenarioRunner по Finished.
//   • Запись: mooring.csv в 19-03 — по клиенту + столбцы своего буксира (TugWorkSystem.Sample, ExerciseRecorder).
//   • Сценарий без задания буксира или свободное плавание — клиент и вид убираются.
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-89)]
public class TugWorkController : MonoBehaviour
{
    public static TugWorkController Instance { get; private set; }

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;
    [Tooltip("Клиент по умолчанию, если в сценарии не задан (иначе — модель 45 м сцены, ShipTypeSwitcher.bridgeParameters)")]
    public ShipParameters defaultClient;
    [Tooltip("Высота клюзов клиента над водой, м (концы клиента в сцене)")]
    public float clientFairleadHeightM = 4.5f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private string state = "нет упражнения";

    /// <summary>Система упражнения (null — сценарий без задания буксира).</summary>
    public TugWorkSystem Work { get; private set; }
    public ScenarioDefinition Scenario { get; private set; }
    /// <summary>Номер причала клиента в акватории (−1 — упражнения нет).</summary>
    public int ClientBerthIndex => Work != null && Scenario != null ? Scenario.tugWork.berthIndex : -1;
    /// <summary>Положение клиента на прошлом шаге — интерполяция вида (как у TugView).</summary>
    public double PrevEast { get; private set; }
    public double PrevNorth { get; private set; }
    public double PrevPsi { get; private set; }

    private ClientShipView view;
    private GameObject viewObject;
    private bool warned;

    private static ScenarioRunner Runner => ScenarioRunner.Instance;

    // Сцена без компонента — поставить на объект навигации (ShipRoot)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<TugWorkController>() != null) return;
        ShipNavigation nav = FindAnyObjectByType<ShipNavigation>();
        if (nav == null || FindAnyObjectByType<ScenarioRunner>() == null) return;   // не сцена тренажёра
        nav.gameObject.AddComponent<TugWorkController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Работа буксира] В сцене несколько TugWorkController — используется первый.", this);
        else Instance = this;
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Clear();
    }

    void OnEnable()
    {
        if (navigation != null) navigation.BeforeStep += OnBeforeStep;
        ExerciseEvents.Raised += OnExerciseEvent;
        if (Runner != null) Runner.StateChanged += OnRunnerState;
    }

    void OnDisable()
    {
        if (navigation != null) navigation.BeforeStep -= OnBeforeStep;
        ExerciseEvents.Raised -= OnExerciseEvent;
        if (Runner != null) Runner.StateChanged -= OnRunnerState;
    }

    void Start()
    {
        // Runner мог появиться позже OnEnable (порядок Awake)
        if (Runner != null) { Runner.StateChanged -= OnRunnerState; Runner.StateChanged += OnRunnerState; }
    }

    // ─────────── Сценарий ───────────

    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ScenarioLoaded) Build(Runner != null ? Runner.Current : null);
        else if (e.Type == ExerciseEventType.ExerciseStarted && Work != null)
        {
            Work.Place(navigation != null ? navigation.Model : null, moveOwn: false);
            RememberClient();
            warned = false;
            Debug.Log($"[Работа буксира] Начало: судно-клиент лагом в {ScenarioText.Num(Work.Task.clientGapM)} м от причала, " +
                      $"буксир у {(Work.WorkSide > 0 ? "правого" : "левого")} борта — {ScenarioText.Num(Work.BowDistance(navigation.Model))} м до корпуса.", this);
        }
    }

    // Свободное плавание (Idle) — клиента нет
    private void OnRunnerState()
    {
        if (Work != null && Runner != null && (Runner.CurrentPhase == ScenarioRunner.Phase.Idle || Runner.Current != Scenario)) Clear();
    }

    private void Build(ScenarioDefinition s)
    {
        Clear();
        if (s == null || !s.IsTugWork) return;
        BerthArea berth = s.tugWork.BerthIn(s.waterArea);
        ShipParameters client = s.tugWorkClient != null ? s.tugWorkClient : defaultClient;
        if (client == null && ShipTypeSwitcher.Instance != null) client = ShipTypeSwitcher.Instance.bridgeParameters;   // модель 45 м сцены
        ShipParameters own = navigation != null ? navigation.parameters : null;
        if (berth == null || client == null || own == null)
        {
            Debug.LogWarning($"[Работа буксира] «{s.title}»: нет причала № {s.tugWork.berthIndex}, судна-клиента или параметров своего буксира — упражнение без клиента.", this);
            return;
        }
        OwnTugDrive drive = OwnTugDrive.Instance;
        TugSpec spec = drive != null && drive.Drive != null ? drive.Drive.Spec
            : TugSpec.For(own, drive != null ? drive.bollardPullTonnes : TugSpec.DefaultBollardPullTonnes);
        Scenario = s;
        Work = new TugWorkSystem(s.tugWork, client, berth, spec);
        Work.Client.Mooring.FairleadHeightM = clientFairleadHeightM;
        Work.Place(navigation.Model, moveOwn: false);
        RememberClient();
        ExerciseTargets.Register(Work.Client);
        viewObject = new GameObject("Client");
        view = viewObject.AddComponent<ClientShipView>();
        view.Init(this);
        UpdateInspector();
        Debug.Log($"[Работа буксира] «{s.title}»: судно-клиент {client.name} (L {client.length:F0} м) у причала «{berth.name}», " +
                  $"рабочий борт — {(Work.WorkSide > 0 ? "правый" : "левый")}; задания: {TugWorkSystem.StageCount}.", this);
    }

    private void Clear()
    {
        if (Work != null) ExerciseTargets.Unregister(Work.Client);
        Work = null;
        Scenario = null;
        if (viewObject != null) Destroy(viewObject);
        viewObject = null;
        view = null;
        state = "нет упражнения";
    }

    private void RememberClient()
    {
        ShipDynamicsModel c = Work.Client.Model;
        PrevEast = c.East; PrevNorth = c.North; PrevPsi = c.Psi;
    }

    // ─────────── Шаг ───────────

    private void OnBeforeStep(ShipDynamicsModel m, double dt)
    {
        if (Work == null || Runner == null || Runner.CurrentPhase != ScenarioRunner.Phase.Running || Runner.Current != Scenario) return;
        if (!m.Uses3Dof || !ShipTypeSwitcher.OnTug)
        {
            if (!warned) Debug.LogWarning("[Работа буксира] Своё судно не буксир или не модель 3 СС — клиент стоит, задания не считаются.", this);
            warned = true;
            return;
        }
        RememberClient();
        TowingController tc = TowingController.Instance;
        Work.Wind = tc != null ? tc.Wind : new Wind(0.0, 0.0);
        Work.Apply(m, dt, Runner.ExerciseTime);
        foreach (ExerciseEvent e in Work.Events) Publish(e);
        Work.Events.Clear();
        UpdateInspector();
    }

    private void Publish(ExerciseEvent e)
    {
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Работа буксира] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }

    private void UpdateInspector()
    {
        if (Work == null) { state = "нет упражнения"; return; }
        state = $"«{TugWorkText.Order(Work.Order, Work.WorkSide)}», заданий {Work.DoneCount}/{TugWorkSystem.StageCount}, " +
                $"зазор клиента {ScenarioText.Num(Work.Client.GapM, 1)} м, упор {ScenarioText.Num(Work.Contact.FenderForce / 1000.0)} кН";
    }

    // ─────────── Запись, окно инструктора ───────────

    /// <summary>Строка mooring.csv в 19-03: клиент у причала + свой буксир (TugWorkSystem.Sample).</summary>
    public MooringSample Sample(double t, BridgeStation station)
    {
        OwnTugDrive d = OwnTugDrive.Instance;
        TowingController tc = TowingController.Instance;
        return Work.Sample(t, ClientBerthIndex, d != null ? d.Drive : null, tc != null ? tc.Wind : new Wind(0.0, 0.0), station);
    }

    /// <summary>Строка окна инструктора (пусто — упражнения нет).</summary>
    public string InstructorLine()
    {
        if (Work == null) return "";
        int hull = Work.HullContacts.Count;
        return $"Работа буксира: «{TugWorkText.Order(Work.Order, Work.WorkSide)}» · заданий {Work.DoneCount}/{TugWorkSystem.StageCount} · " +
               $"зазор клиента {ScenarioText.Num(Work.Client.GapM, 1)} м · упор {ScenarioText.Num(Work.Contact.FenderForce / 1000.0)} кН · " +
               $"касаний {Work.Contacts.Count}" + (hull > 0 ? $", корпусом {hull}" : "");
    }
}
