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
//   • отсветы своих огней — на места огней катера (мачта, борта);
//   • гашение волн у борта (материал воды) — слабее: у мостика 25 % в полосе 8 м, с низкой палубы
//     катера это видно как ровный «срез» воды, а катер качается на полных волнах; у катера — 90 % в 2 м.
//     Материал — общий ассет: при выходе из Play Mode значения мостика возвращаются.
// Ручная правка стойки — Tools/VR Bridge/Fast Boat — оставить / скрыть выделенное (forceKeep / forceHide),
// предпросмотр — Tools/VR Bridge/Fast Boat — предпросмотр стойки (Scene View).
// Списки и положения заполняет Tools/VR Bridge/Setup Fast Boat (их можно править в инспекторе).
// Исходное состояние каждого рендера и объекта запоминается при запуске и возвращается при
// обратном переключении — то, что было выключено в сцене, выключенным и остаётся.
//
// 19.4а — третий тип «буксир» (свой ASD-буксир 22 × 8 м, ShipParameters_Tug из Resources, модель 3 СС):
//   • Setup не нужен: модель (TugMeshes, материалы катера) и коробки корпуса для воды строятся при первом
//     переключении; место — от рулевой стойки катера (Setup Fast Boat): стойка в ConsoleStation·L впереди миделя
//     буксира, ДП — как у мостика; игрок — на полу приподнятой рубки TugMeshes.FloorHeight над водой (судно опускается
//     на разницу с полом мостика, как катер); цвета — TugMaterials (по фото портового буксира);
//   • от мостика — та же рулевая стойка, что у катера (hideOnBoat, deactivateOnBoat); штурвал, телеграф и рычаг
//     подруливающего скрыты (рендеры и коллайдеры), на месте телеграфа — рукоятка джойстика (OwnTugDrive);
//   • движитель — две азимутальные колонки (OwnTugDrive, ShipNavigation.SetAzimuthDrive), меню B «Джойстик»;
//   • сценарии на буксире переключают тип на модель 45 м сами (ScenarioRunner.Load) — упражнение на буксире — 19.4б.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShipType { Model45m, FastBoat, Tug }

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
    [Tooltip("Ручная правка (Tools/VR Bridge/Fast Boat: оставить выделенное) — Setup Fast Boat всегда оставляет")]
    public Renderer[] forceKeep = new Renderer[0];
    [Tooltip("Ручная правка (Tools/VR Bridge/Fast Boat: скрыть выделенное) — Setup Fast Boat всегда скрывает")]
    public Renderer[] forceHide = new Renderer[0];

    [Header("Вода у борта на катере")]
    [Tooltip("Материал воды (пусто — ShipMotion.oceanMaterial). Гашение волн у борта подобрано под мостик " +
             "(25 % в полосе 8 м): с низкой палубы катера гладкая полоса видна как «срез», а катер качается на полных волнах")]
    public Material oceanMaterial;
    [Range(0f, 1f)] public float boatHullDampMin = 0.9f;
    [Tooltip("Ширина полосы гашения у катера, м")]
    public float boatHullDampWidth = 2f;

    [Header("Отсветы своих огней")]
    public Light[] ownLightGlows = new Light[0];
    [Tooltip("Места отсветов на катере (в системе родителя источника), по порядку ownLightGlows")]
    public Vector3[] boatGlowPositions = new Vector3[0];

    [Header("Буксир (19.4а)")]
    [Tooltip("Пусто — Resources/ShipParameters_Tug (создаётся сам при входе в Play Mode), иначе ShipPresets.FillTug")]
    public ShipParameters tugParameters;
    [Tooltip("19.4а-2: сдвиг рукоятки джойстика (и тумблера «УДЕРЖ. КУРС») от TelegraphHead в системе Ship, м. " +
             "В Play Mode подвинуть Ship/TugJoystick → Tools/VR Bridge/Tug — запомнить положение джойстика → после выхода Ctrl+S")]
    public Vector3 tugJoystickOffset;

    public ShipType Type { get; private set; } = ShipType.Model45m;
    public event Action TypeChanged;

    /// <summary>Своё судно — буксир (19.4а).</summary>
    public static bool OnTug => Instance != null && Instance.Type == ShipType.Tug;

    /// <summary>Параметры модели движения выбранного типа.</summary>
    public ShipParameters ActiveParameters =>
        Type == ShipType.FastBoat && boatParameters != null ? boatParameters
        : Type == ShipType.Tug ? TugParameters
        : bridgeParameters;
    /// <summary>Переключать можно вне упражнения (в брифинге сценарий перезагружается).</summary>
    public bool CanSwitch => runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Running;
    public bool BoatReady => boatParameters != null && boatRoot != null && boatHullBox != null;
    /// <summary>Буксир строится от стойки катера (Setup Fast Boat) и его материалов.</summary>
    public bool TugReady => BoatReady && boatRoot.GetComponent<MeshRenderer>() != null;

    /// <summary>Параметры своего буксира: поле, Resources/ShipParameters_Tug или FillTug.</summary>
    public ShipParameters TugParameters
    {
        get
        {
            if (tugParameters != null) return tugParameters;
            tugParameters = Resources.Load<ShipParameters>("ShipParameters_Tug");
            if (tugParameters == null)
            {
                tugParameters = ScriptableObject.CreateInstance<ShipParameters>();
                tugParameters.name = "ShipParameters_Tug (FillTug)";
                ShipPresets.FillTug(tugParameters);
            }
            return tugParameters;
        }
    }

    // Стойка на катере — ConsoleStation·L от транца (как SetupFastBoat.ConsoleStation)
    private const float BoatConsoleStation = 0.4f;
    private GameObject tugRoot;
    private Transform tugHullBox, tugBowBox;
    private Vector3 tugPivot;
    private float tugVerticalOffset;
    private Vector3[] tugGlowPositions = new Vector3[0];
    private readonly List<Renderer> controlRenderers = new List<Renderer>();
    private readonly List<Collider> controlColliders = new List<Collider>();
    private bool[] controlRendererWas = new bool[0], controlColliderWas = new bool[0];
    private OwnTugDrive tugDrive;

    public const string BoatIdSuffix = "-FB";
    public const string BoatTitleSuffix = " (катер)";

    private static readonly int HullDampMinId = Shader.PropertyToID("_HullDampMin");
    private static readonly int HullDampWidthId = Shader.PropertyToID("_HullDampWidth");
    private float bridgeDampMin = -1f, bridgeDampWidth = -1f;   // < 0 — не запомнены (нет материала)
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
        // 19.4а: движитель своего буксира — ставится сам рядом с навигацией
        if (navigation != null)
        {
            tugDrive = navigation.GetComponent<OwnTugDrive>();
            if (tugDrive == null) tugDrive = navigation.gameObject.AddComponent<OwnTugDrive>();
        }

        if (oceanMaterial == null && motion != null) oceanMaterial = motion.oceanMaterial;
        if (oceanMaterial != null && oceanMaterial.HasProperty(HullDampMinId) && oceanMaterial.HasProperty(HullDampWidthId))
        {
            bridgeDampMin = oceanMaterial.GetFloat(HullDampMinId);
            bridgeDampWidth = oceanMaterial.GetFloat(HullDampWidthId);
        }
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
        SetWaterDamping(false);   // материал воды — общий ассет: после Play Mode — как у мостика
    }

    private void SetWaterDamping(bool boat)
    {
        if (oceanMaterial == null || bridgeDampMin < 0f) return;
        oceanMaterial.SetFloat(HullDampMinId, boat ? boatHullDampMin : bridgeDampMin);
        oceanMaterial.SetFloat(HullDampWidthId, boat ? boatHullDampWidth : bridgeDampWidth);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static string TypeTitle(ShipType t) => t == ShipType.FastBoat ? "катер" : t == ShipType.Tug ? "буксир" : "модель 45 м";

    private void Step(int dir)
    {
        int n = Enum.GetValues(typeof(ShipType)).Length;
        ShipType t = Type;
        for (int i = 0; i < n; i++)
        {
            t = (ShipType)((((int)t + dir) % n + n) % n);
            if (t != ShipType.Tug || TugReady) break;   // буксир без стойки катера пропускается
        }
        SetType(t);
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
        if ((t == ShipType.FastBoat && !BoatReady) || (t == ShipType.Tug && !TugReady))
        {
            Debug.LogWarning("[Судно] Катер не настроен (буксир строится от его стойки) — запустите Tools/VR Bridge/Setup Fast Boat и сохраните сцену.");
            return;
        }
        if (t == ShipType.Tug)
        {
            // 19.4а: у буксира крыльев нет — к стойке
            BridgeStationSwitch st = FindAnyObjectByType<BridgeStationSwitch>();
            if (st != null && st.Current != BridgeStation.Helm) st.MoveTo(BridgeStation.Helm);
            MooringController mc = MooringController.Instance;
            if (mc != null) mc.SetThrusterIndex(ThrusterOrders.ZeroIndex);   // подруливающего у буксира нет
            EnsureTugView();
        }
        Type = t;
        ApplyView();

        if (t == ShipType.Tug && runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Briefing)
        {
            runner.FreeSail();   // 19.4а: сценариев на своём буксире пока нет — брифинг закрывается, свободное плавание
            if (navigation != null) navigation.parameters = ActiveParameters;
        }
        else if (runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Briefing && runner.Current != null)
            runner.Load(ScenarioVariants.SourceOf(runner.Current));   // Load подставит вариант для нового типа
        else if (navigation != null && ActiveParameters != null)
            navigation.parameters = ActiveParameters;

        ShipParameters p = ActiveParameters;
        Debug.Log($"[Судно] Тип судна: {TypeTitle(Type)}" +
                  (p != null ? $" ({p.name}: L {p.length:F0} м, {p.serviceSpeedKnots:F0} уз)" : "") +
                  (Type == ShipType.Tug ? " — управление джойстиком (рукоятка на месте телеграфа, меню B «Джойстик»)" : ""));
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
        bool boat = Type == ShipType.FastBoat, tug = Type == ShipType.Tug, small = boat || tug;
        for (int i = 0; i < hideOnBoat.Length; i++)
            if (hideOnBoat[i] != null) hideOnBoat[i].enabled = !small && hideWasEnabled[i];
        for (int i = 0; i < deactivateOnBoat.Length; i++)
            if (deactivateOnBoat[i] != null) deactivateOnBoat[i].SetActive(!small && deactivateWasActive[i]);
        if (boatRoot != null) boatRoot.SetActive(boat);
        if (tugRoot != null) tugRoot.SetActive(tug);
        SetControlsHidden(tug);

        if (water != null)
        {
            water.hull = boat ? boatHullBox : tug ? tugHullBox : bridgeHullBox;
            water.bowTip = boat ? boatBowBox : tug ? tugBowBox : bridgeBowBox;
        }
        if (motion != null)
            motion.SetHullFrame(boat ? boatPivot : tug ? tugPivot : bridgePivot, boat ? boatVerticalOffset : tug ? tugVerticalOffset : 0f);
        SetWaterDamping(small);

        for (int i = 0; i < ownLightGlows.Length; i++)
        {
            if (ownLightGlows[i] == null) continue;
            ownLightGlows[i].transform.localPosition =
                boat && i < boatGlowPositions.Length ? boatGlowPositions[i]
                : tug && i < tugGlowPositions.Length ? tugGlowPositions[i]
                : glowBridgePositions[i];
        }

        // 19.4а: движитель буксира — колонки по джойстику; штурвал и телеграф в модель не идут
        if (tug && navigation != null) navigation.parameters = TugParameters;
        if (navigation != null) navigation.SetAzimuthDrive(tug);
        if (tugDrive != null) tugDrive.SetActive(tug, TugParameters);
    }

    // ─────────── 19.4а: свой буксир ───────────

    // Модель и коробки корпуса — один раз, от стойки катера (как Setup Fast Boat ставит катер)
    private void EnsureTugView()
    {
        if (tugRoot != null || !TugReady) return;
        ShipParameters p = TugParameters;
        WorldOrigin origin = FindAnyObjectByType<WorldOrigin>();
        Vector3 bow = origin != null ? origin.bowLocalAxis : Vector3.back;
        bow.y = 0f;
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back;
        Vector3 stbd = Vector3.Cross(Vector3.up, bow);
        Vector3 boatOrigin = boatRoot.transform.localPosition;   // мидель катера на ватерлинии (Setup Fast Boat)
        float consoleBow = Vector3.Dot(boatOrigin, bow) - (0.5f - BoatConsoleStation) * boatParameters.length;
        float deck = TugMeshes.DeckHeight, floor = TugMeshes.FloorHeight;
        // Игрок — на полу приподнятой рубки (FloorHeight над водой); пол мостика = boatOrigin.y + boatDeckHeight (Ship)
        Vector3 tugOrigin = stbd * Vector3.Dot(boatOrigin, stbd)
                          + bow * (consoleBow - TugMeshes.ConsoleStation * p.length)
                          + Vector3.up * (boatOrigin.y + boatDeckHeight - floor);
        tugPivot = tugOrigin;
        tugVerticalOffset = boatVerticalOffset + (floor - boatDeckHeight);

        tugRoot = new GameObject("OwnTug");
        tugRoot.SetActive(false);
        Transform t = tugRoot.transform;
        t.SetParent(transform, false);
        t.localPosition = tugOrigin;
        t.localRotation = Quaternion.LookRotation(bow, Vector3.up);   // +Z модели — нос, +X — ПБ
        tugRoot.AddComponent<MeshFilter>().sharedMesh = TugMeshes.Build(p.length, p.beam, p.draft);
        tugRoot.AddComponent<MeshRenderer>().sharedMaterials = TugMaterials(boatRoot.GetComponent<MeshRenderer>().sharedMaterials);
        tugHullBox = Box(t, "HullBox", new Vector3(0f, 0.5f * (deck - p.draft), -0.08f * p.length), new Vector3(0.95f * p.beam, deck + p.draft, 0.82f * p.length));
        tugBowBox = Box(t, "BowBox", new Vector3(0f, 0.5f * (deck - p.draft), 0.38f * p.length), new Vector3(0.7f * p.beam, deck + p.draft, 0.22f * p.length));

        tugGlowPositions = new Vector3[ownLightGlows.Length];
        for (int i = 0; i < ownLightGlows.Length; i++)
        {
            Light l = ownLightGlows[i];
            if (l == null) continue;
            Vector3? model = l.name.Contains("Masthead") ? TugMeshes.MastheadLight(p.length)
                : l.name.Contains("Starboard") ? TugMeshes.SidelightFor(p.length, +1)
                : l.name.Contains("Port") ? TugMeshes.SidelightFor(p.length, -1)
                : (Vector3?)null;
            tugGlowPositions[i] = model.HasValue && l.transform.parent != null
                ? l.transform.parent.InverseTransformPoint(t.TransformPoint(model.Value))
                : glowBridgePositions[i];
        }

        CollectControls();
        Debug.Log($"[Судно] Буксир построен: мидель (Ship) {tugOrigin}, палуба {deck} м над водой (судно ниже на {-tugVerticalOffset:F2} м), " +
                  $"скрываемых органов управления: {controlRenderers.Count} рендеров, {controlColliders.Count} коллайдеров.", this);
    }

    // Материалы буксира (подмеши TugMeshes): копии материала катера с цветами, как у портового буксира на фото
    private static Material[] TugMaterials(Material[] boat)
    {
        Material template = boat != null && boat.Length > 0 ? boat[0] : null;
        var colors = new[]
        {
            new Color(0.12f, 0.42f, 0.72f),   // борт — синий
            new Color(0.55f, 0.1f, 0.08f),    // подводная часть — красная
            new Color(0.42f, 0.44f, 0.46f),   // палуба — серая
            new Color(0.06f, 0.06f, 0.07f),   // кранцы, лебёдки, кнехты — чёрные
            new Color(0.9f, 0.91f, 0.92f)     // надстройка и рубка — белые
        };
        var mats = new Material[TugMeshes.SubCount];
        for (int i = 0; i < mats.Length; i++)
        {
            Material src = boat != null && i < boat.Length && boat[i] != null ? boat[i] : template;
            mats[i] = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mats[i].name = "OwnTug_" + i;
            if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", colors[i]);
            if (mats[i].HasProperty("_Smoothness")) mats[i].SetFloat("_Smoothness", i == 3 ? 0.15f : 0.35f);
        }
        return mats;
    }

    private static Transform Box(Transform parent, string name, Vector3 center, Vector3 size)
    {
        Transform b = new GameObject(name).transform;
        b.SetParent(parent, false);
        b.localPosition = center;
        b.localScale = size;   // ShipWaterInteraction: единичный куб × масштаб
        return b;
    }

    // Штурвал (с ободом WheelGrip), телеграф (TelegraphHead), рычаг подруливающего (ThrusterHead): рендеры и коллайдеры
    private void CollectControls()
    {
        var roots = new List<Transform>();
        if (navigation != null && navigation.wheel != null) roots.Add(navigation.wheel.transform);
        Transform grip = FindDeep(transform, "WheelGrip");
        if (grip != null) roots.Add(grip);
        if (navigation != null && navigation.telegraph != null)
        {
            Transform tel = navigation.telegraph.transform;
            roots.Add(tel.parent != null && tel.parent.name == "TelegraphHead" ? tel.parent : tel);
        }
        MooringController mc = MooringController.Instance;
        if (mc != null && mc.thrusterLever != null)
        {
            Transform th = mc.thrusterLever.transform;
            while (th.parent != null && th.name != "ThrusterHead" && th.parent != transform) th = th.parent;
            roots.Add(th.name == "ThrusterHead" ? th : mc.thrusterLever.transform);
        }
        foreach (Transform r in roots)
        {
            controlRenderers.AddRange(r.GetComponentsInChildren<Renderer>(true));
            controlColliders.AddRange(r.GetComponentsInChildren<Collider>(true));
        }
        controlRendererWas = new bool[controlRenderers.Count];
        for (int i = 0; i < controlRenderers.Count; i++) controlRendererWas[i] = controlRenderers[i].enabled;
        controlColliderWas = new bool[controlColliders.Count];
        for (int i = 0; i < controlColliders.Count; i++) controlColliderWas[i] = controlColliders[i].enabled;
    }

    private void SetControlsHidden(bool hidden)
    {
        for (int i = 0; i < controlRenderers.Count; i++)
            if (controlRenderers[i] != null) controlRenderers[i].enabled = !hidden && controlRendererWas[i];
        for (int i = 0; i < controlColliders.Count; i++)
            if (controlColliders[i] != null) controlColliders[i].enabled = !hidden && controlColliderWas[i];
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }
}
