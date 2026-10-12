// ============================================================
// ShipTypeSwitcher.cs — Тип своего судна: модель 45 м (мостик) / быстроходный катер (запрос 15)
//
// Висит на Ship. Меню A → «Судно »» → «Тип судна: < модель 45 м / катер >» (вне упражнения).
// Катер:
//   • параметры модели движения — ShipParameters_FastBoat (ShipPresets); сценарии при загрузке
//     заменяются вариантом «-FB» (ScenarioVariants: копия, начальная скорость пересчитана);
//   • вид — корпус катера (FastBoat, BoatMeshes); от мостика остаётся только рулевая стойка
//     со всеми органами управления (они не копируются — навигация, запись и оценка работают
//     с теми же объектами). Скрываются только рендеры (hideOnBoat) — коллайдеры палубы
//     остаются, игрок с гравитацией XR не проваливается; лампы рубки и укрытие от дождя
//     (deactivateOnBoat) выключаются — на открытой палубе дождь падает;
//   • высота — судно вместе с игроком опускается (ShipMotion.SetHullFrame): палуба ≈ 1,2 м
//     над водой вместо ≈ 5 м у мостика; центр качки — мидель катера на ватерлинии;
//   • контуры корпуса для шейдера воды (вода внутри не рисуется, след, бурун) — коробки катера;
//   • отсветы своих огней — на места огней катера (мачта, борта).
// Списки и положения заполняет Tools/VR Bridge/Setup Fast Boat (их можно править в инспекторе).
// Исходное состояние каждого рендера и объекта запоминается при запуске и возвращается при
// обратном переключении — то, что было выключено в сцене, выключенным и остаётся.
// ============================================================
using System;
using UnityEngine;

public enum ShipType { Model45m, FastBoat }

public class ShipTypeSwitcher : MonoBehaviour
{
    public static ShipTypeSwitcher Instance { get; private set; }

    [Header("Параметры судов")]
    [Tooltip("Модель 45 м (мостик). Пусто — параметры ShipNavigation при запуске")]
    public ShipParameters bridgeParameters;
    public ShipParameters boatParameters;

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;
    public ShipMotion motion;
    public ShipWaterInteraction water;
    public ScenarioRunner runner;

    [Header("Мостик (модель 45 м)")]
    public Transform bridgeHullBox;
    public Transform bridgeBowBox;
    [Tooltip("Центр качки мостика (ShipMotion.pivotLocal в сцене)")]
    public Vector3 bridgePivot = new Vector3(0f, -5f, 0f);

    [Header("Катер")]
    public GameObject boatRoot;
    public Transform boatHullBox;
    public Transform boatBowBox;
    [Tooltip("Центр качки катера в системе Ship: мидель катера на ватерлинии")]
    public Vector3 boatPivot;
    [Tooltip("Сдвиг судна по вертикали в режиме катера, м (< 0 — вниз)")]
    public float boatVerticalOffset;
    [Tooltip("Высота палубы катера над водой, м (справочно — задаёт Setup Fast Boat)")]
    public float boatDeckHeight = 1.2f;

    [Header("Что скрыть на катере")]
    [Tooltip("Рендеры мостика, кроме рулевой стойки (выключаются только рендеры, коллайдеры остаются)")]
    public Renderer[] hideOnBoat = new Renderer[0];
    [Tooltip("Объекты, выключаемые на катере: лампы рубки, укрытие от дождя")]
    public GameObject[] deactivateOnBoat = new GameObject[0];
    [Tooltip("Справочно: части модели мостика, оставленные как рулевая стойка")]
    public Renderer[] keptConsole = new Renderer[0];

    [Header("Отсветы своих огней")]
    public Light[] ownLightGlows = new Light[0];
    [Tooltip("Места отсветов на катере (в системе родителя источника), по порядку ownLightGlows")]
    public Vector3[] boatGlowPositions = new Vector3[0];

    public ShipType Type { get; private set; } = ShipType.Model45m;
    public event Action TypeChanged;

    /// <summary>Параметры модели движения выбранного типа.</summary>
    public ShipParameters ActiveParameters => Type == ShipType.FastBoat && boatParameters != null ? boatParameters : bridgeParameters;
    /// <summary>Переключать можно вне упражнения (в брифинге сценарий перезагружается).</summary>
    public bool CanSwitch => runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Running;
    public bool BoatReady => boatParameters != null && boatRoot != null && boatHullBox != null;

    public const string BoatIdSuffix = "-FB";
    public const string BoatTitleSuffix = " (катер)";

    private bool[] hideWasEnabled = new bool[0];
    private bool[] deactivateWasActive = new bool[0];
    private Vector3[] glowBridgePositions = new Vector3[0];
    private BridgeMenuCommand menuCommand;

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Судно] В сцене несколько ShipTypeSwitcher — используется первый.", this);
        else Instance = this;

        if (navigation == null) navigation = GetComponentInParent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (motion == null) motion = GetComponent<ShipMotion>();
        if (water == null) water = FindAnyObjectByType<ShipWaterInteraction>();
        if (runner == null) runner = FindAnyObjectByType<ScenarioRunner>();
        if (bridgeParameters == null && navigation != null) bridgeParameters = navigation.parameters;
        if (motion != null) bridgePivot = motion.pivotLocal;   // центр качки мостика — как настроен в сцене
        if (water != null)
        {
            if (bridgeHullBox == null) bridgeHullBox = water.hull;
            if (bridgeBowBox == null) bridgeBowBox = water.bowTip;
        }

        // Исходное состояние сцены — чтобы вернуть его при обратном переключении
        hideWasEnabled = new bool[hideOnBoat.Length];
        for (int i = 0; i < hideOnBoat.Length; i++) hideWasEnabled[i] = hideOnBoat[i] != null && hideOnBoat[i].enabled;
        deactivateWasActive = new bool[deactivateOnBoat.Length];
        for (int i = 0; i < deactivateOnBoat.Length; i++) deactivateWasActive[i] = deactivateOnBoat[i] != null && deactivateOnBoat[i].activeSelf;
        glowBridgePositions = new Vector3[ownLightGlows.Length];
        for (int i = 0; i < ownLightGlows.Length; i++)
            if (ownLightGlows[i] != null) glowBridgePositions[i] = ownLightGlows[i].transform.localPosition;

        if (boatRoot != null) boatRoot.SetActive(false);   // при запуске — модель 45 м, как до запроса 15
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder("Судно", 30);
        menuCommand = BridgeMenu.RegisterStepper("Судно", () => $"Тип судна: {TypeTitle(Type)}",
            () => Step(-1), () => Step(+1), () => CanSwitch && BoatReady);
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(menuCommand);
        menuCommand = null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static string TypeTitle(ShipType t) => t == ShipType.FastBoat ? "катер" : "модель 45 м";

    private void Step(int dir)
    {
        int n = Enum.GetValues(typeof(ShipType)).Length;
        SetType((ShipType)((((int)Type + dir) % n + n) % n));
    }

    /// <summary>Сменить тип судна: вид, высоту, контуры, параметры; в брифинге — перезагрузить сценарий.</summary>
    public void SetType(ShipType t)
    {
        if (t == Type) return;
        if (!CanSwitch)
        {
            Debug.LogWarning("[Судно] Тип судна меняется только вне упражнения.");
            return;
        }
        if (t == ShipType.FastBoat && !BoatReady)
        {
            Debug.LogWarning("[Судно] Катер не настроен — запустите Tools/VR Bridge/Setup Fast Boat и сохраните сцену.");
            return;
        }
        Type = t;
        ApplyView();

        if (runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Briefing && runner.Current != null)
            runner.Load(ScenarioVariants.SourceOf(runner.Current));   // Load подставит вариант для нового типа
        else if (navigation != null && ActiveParameters != null)
            navigation.parameters = ActiveParameters;

        ShipParameters p = ActiveParameters;
        Debug.Log($"[Судно] Тип судна: {TypeTitle(Type)}" +
                  (p != null ? $" ({p.name}: L {p.length:F0} м, {p.serviceSpeedKnots:F0} уз)" : ""));
        TypeChanged?.Invoke();
    }

    /// <summary>Сценарий для выбранного типа судна: на катере — вариант «-FB», иначе — исходный.</summary>
    public ScenarioDefinition VariantFor(ScenarioDefinition s)
    {
        ScenarioDefinition src = ScenarioVariants.SourceOf(s);
        if (Type != ShipType.FastBoat || boatParameters == null) return src;
        return ScenarioVariants.WithShip(src, boatParameters, bridgeParameters, BoatIdSuffix, BoatTitleSuffix,
            "Вариант на быстроходном катере (тестовое судно, L 18 м, до 35 уз): маршрут и цели прежние, " +
            "начальная скорость пересчитана. Модель катера — для отладки сценариев, не для оценки.");
    }

    private void ApplyView()
    {
        bool boat = Type == ShipType.FastBoat;
        for (int i = 0; i < hideOnBoat.Length; i++)
            if (hideOnBoat[i] != null) hideOnBoat[i].enabled = !boat && hideWasEnabled[i];
        for (int i = 0; i < deactivateOnBoat.Length; i++)
            if (deactivateOnBoat[i] != null) deactivateOnBoat[i].SetActive(!boat && deactivateWasActive[i]);
        if (boatRoot != null) boatRoot.SetActive(boat);

        if (water != null)
        {
            water.hull = boat ? boatHullBox : bridgeHullBox;
            water.bowTip = boat ? boatBowBox : bridgeBowBox;
        }
        if (motion != null) motion.SetHullFrame(boat ? boatPivot : bridgePivot, boat ? boatVerticalOffset : 0f);

        for (int i = 0; i < ownLightGlows.Length; i++)
        {
            if (ownLightGlows[i] == null) continue;
            ownLightGlows[i].transform.localPosition = boat && i < boatGlowPositions.Length ? boatGlowPositions[i] : glowBridgePositions[i];
        }
    }
}
