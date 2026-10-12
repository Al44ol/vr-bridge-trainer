// ============================================================
// NavPlotter.cs — Навигационный планшет в левой руке (D2)
//
// Не ECDIS: учебная электронная схема вида сверху. World Space Canvas (свой,
// корневой) строится в коде и крепится к левому контроллеру XR Rig
// (leftController, ставит Setup D2 Plotter); 1 единица холста = 0,5 мм,
// планшет 600 × 400 ед. = 0,30 × 0,20 м. Показать / скрыть — кнопка X левого
// контроллера и меню Y («Общее»); пока левая рука держит штурвал, телеграф или
// тифон — скрыт сам. Кнопки — лучом или касанием правой руки (вибрация при
// наведении и нажатии — штатная XRI, своей нет: D3, решение пользователя).
//   • Карта (слева, 400 × 400): та же техника и те же слои, что у окна инструктора
//     (D1): неподвижный — DebriefMapGraphic + DebriefMap.BuildStatic (суша, мели,
//     знаки, ось фарватера, маршрут и коридор), подвижный — InstructorMapGraphic
//     (своё судно и цели: силуэт, вектор vectorMinutes, след; LiveMapTracks).
//     Обновление uiRateHz (5 Гц); неподвижный слой — только при смене окна
//     (MapViewport.Version: шкала, перецентровка, режим).
//   • Шкала дальности 0,25…8 мили (от центра окна до верхнего края), «−» / «+».
//   • «Север вверх» — судно ходит по окну, перецентровка за центральными 60 %;
//     «Курс вверх» — слой повёрнут на +ψ вокруг судна (RectTransform, без перестройки
//     сетки), судно на headUpShipOffset полувысоты ниже центра; слой больше окна
//     (PlotterMath.Layout), окно обрезает RectMask2D. Подписи не поворачиваются.
//   • «Маршрут» — весь маршрут в окне (север вверх), «За судном» — снова шкала.
//   • Данные справа: курс, скорость; в упражнении — XTE с допуском, участок,
//     следующая точка (пеленг, дистанция, время); ближайшая опасная цель
//     (D, CPA, TCPA; CPA меньше допустимой — красным).
//   • Цели (PlotterMath.ShowTarget): в брифинге и упражнении — только обнаруженные
//     (оценка C2), при флаге сценария «цели не показывать» (C4) — никаких; в свободном
//     плавании и после упражнения — все. Режим инструктора / теста «все цели»
//     (окно инструктора, меню A «Отладка») — видно всё, надпись на планшете,
//     событие PlotterRevealTargets в журнал упражнения.
//   • Ночь: шторка «Подсветка приборов» (BridgeLighting.RegisterInstrumentPanel).
// Скрытый планшет ничего не обновляет.
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class NavPlotter : MonoBehaviour
{
    public static NavPlotter Instance { get; private set; }

    [Header("Сцена (пусто — найти)")]
    public ScenarioRunner runner;
    public ShipNavigation navigation;
    public ExerciseAssessmentMonitor assessment;
    public BridgeLighting lighting;
    [Tooltip("Левый контроллер XR Rig — к нему крепится планшет (Setup D2 Plotter)")]
    public Transform leftController;

    [Header("Планшет в руке")]
    [Tooltip("Показан при запуске")]
    public bool visibleOnStart;
    [Tooltip("Кнопка X левого контроллера — показать / скрыть")]
    public bool controllerButton = true;
    [Tooltip("Центр планшета относительно контроллера, м (x — вправо, y — вверх, z — вперёд)")]
    public Vector3 handOffset = new Vector3(0f, 0.14f, 0.05f);   // нижний край выше луча левого контроллера
    [Tooltip("Поворачивать к глазам (иначе — наклон handEuler относительно контроллера)")]
    public bool faceHead = true;
    [Tooltip("Наклон относительно контроллера, ° (если faceHead выключен)")]
    public Vector3 handEuler = new Vector3(35f, 0f, 0f);
    [Tooltip("Скрывать, пока левая рука держит орган управления (штурвал, телеграф, тифон)")]
    public bool hideWhileGrabbing = true;
    [Tooltip("Метров на единицу холста (0,0005 — планшет 0,30 × 0,20 м)")]
    public float canvasScale = 0.0005f;

    [Header("Карта")]
    [Tooltip("Обновление карты и данных, раз в секунду (реальное время)")]
    public float uiRateHz = 5f;
    [Tooltip("Шкала дальности: номер в 0,25 / 0,5 / 1 / 2 / 4 / 8 мили")]
    public int rangeIndex = PlotterMath.DefaultRangeIndex;
    [Tooltip("Курс вверх (иначе север вверх)")]
    public bool headUp;
    [Tooltip("«Курс вверх»: судно ниже центра окна на долю полувысоты")]
    [Range(0f, 0.6f)] public float headUpShipOffset = 0.3f;
    [Tooltip("След: точка не чаще, чем через … м")]
    public float trailMinMeters = 10f;
    public int trailMaxPoints = 1500;
    [Tooltip("Длина вектора скорости, мин")]
    public float vectorMinutes = 3f;
    [Tooltip("Наименьшая длина силуэта судна, ед. холста (0,5 мм)")]
    public float minHullPx = 12f;

    [Header("Режим инструктора")]
    [Tooltip("Все цели (в том числе не обнаруженные и скрытые флагом сценария); переключение — окно инструктора или меню A «Отладка»")]
    [SerializeField] private bool revealAll;

    public bool RevealAll => revealAll;
    public bool Shown => wantShown && !autoHidden;

    private const float Width = 600f, Height = 400f, MapSize = 400f, Pad = 8f, Gap = 6f, ButtonH = 46f;
    private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.96f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    private static readonly Color ActiveColor = new Color(0.3f, 0.52f, 0.8f);
    private static readonly Color NeutralColor = new Color(0.3f, 0.33f, 0.38f);
    private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.75f);
    private static readonly Color RevealColor = new Color(0.75f, 0.1f, 0.08f, 0.9f);

    private GameObject panel;
    private Canvas canvas;
    private Behaviour raycaster;
    private RectTransform root, window, content, labelsRoot, column;
    private DebriefMapGraphic mapStatic;
    private InstructorMapGraphic mapLive;
    private DebriefMapData mapData;
    private readonly MapViewport viewport = new MapViewport();
    private readonly LiveMapTracks tracks = new LiveMapTracks();
    private int builtVersion = -1;
    private WaterArea builtArea;
    private ScenarioDefinition builtScenario;
    private bool builtOnce, routeMode;
    private TextMeshProUGUI rangeText, dataText, northText, revealBanner;
    private readonly List<TextMeshProUGUI> shipLabels = new List<TextMeshProUGUI>();
    private readonly List<(TextMeshProUGUI text, WorldPosition pos)> staticLabels = new List<(TextMeshProUGUI, WorldPosition)>();
    private readonly List<(TextMeshProUGUI label, Image image, Func<string> text, Func<Color> color)> dynamicButtons =
        new List<(TextMeshProUGUI, Image, Func<string>, Func<Color>)>();
    private readonly HashSet<string> detected = new HashSet<string>();
    private readonly List<CpaResult> cpas = new List<CpaResult>();
    private readonly List<ITrackedTarget> cpaTargets = new List<ITrackedTarget>();
    private IXRSelectInteractor[] leftInteractors = new IXRSelectInteractor[0];
    private InputAction xButton;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private bool wantShown, autoHidden;
    private float nextRefresh, nextText;
    private float rotationDeg;
    private Func<ITrackedTarget, bool> include;

    void Awake()
    {
        Instance = this;
        include = ShowTargetNow;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (panel != null) Destroy(panel);
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (navigation == null) navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (assessment == null) assessment = ExerciseAssessmentMonitor.Instance != null ? ExerciseAssessmentMonitor.Instance : FindAnyObjectByType<ExerciseAssessmentMonitor>();
        if (lighting == null) lighting = BridgeLighting.Instance != null ? BridgeLighting.Instance : FindAnyObjectByType<BridgeLighting>();
        if (leftController == null) leftController = FindLeftController();
        if (leftController == null) Debug.LogWarning("[Планшет] Левый контроллер не найден — планшет перед игроком без руки (Tools/VR Bridge/Setup D2 Plotter).", this);
        else leftInteractors = leftController.GetComponentsInChildren<IXRSelectInteractor>(true);
        Build();
        SetShown(visibleOnStart);
    }

    void OnEnable()
    {
        ExerciseEvents.Raised += OnExerciseEvent;
        if (controllerButton)
        {
            // X на левом контроллере = primaryButton левого (до D1 — панель роста, HeightAdjust; теперь свободна)
            xButton = new InputAction("NavPlotter", InputActionType.Button);
            xButton.AddBinding("<XRController>{LeftHand}/primaryButton");
            xButton.performed += _ => Toggle();
            xButton.Enable();
        }
        BridgeMenu.SetGroupOrder("Общее", -95);
        BridgeMenu.SetGroupExpanded("Общее", true);
        menuCommands.Add(BridgeMenu.Register("Общее", () => wantShown ? "Навигационный планшет: скрыть (X)" : "Навигационный планшет: показать (X)", Toggle)
            .InMenu(BridgeMenu.SettingsMenuId));
        menuCommands.Add(BridgeMenu.Register("Отладка", () => revealAll ? "Планшет: все цели — вкл" : "Планшет: все цели — выкл (только обнаруженные)",
            () => SetRevealAll(!revealAll), instructorOnly: true));
    }

    void OnDisable()
    {
        ExerciseEvents.Raised -= OnExerciseEvent;
        if (xButton != null) { xButton.Disable(); xButton.Dispose(); xButton = null; }
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
    }

    // ─────────── Управление ───────────
    public void Toggle() => SetShown(!wantShown);

    public void SetShown(bool on)
    {
        wantShown = on;
        ApplyShown();
    }

    /// <summary>Режим инструктора / теста «все цели»; включение и выключение — событие в журнал упражнения.</summary>
    public void SetRevealAll(bool on)
    {
        if (revealAll == on) return;
        revealAll = on;
        PublishReveal();
        nextRefresh = 0f;
        BridgeMenu.RebuildOpen();
    }

    private void PublishReveal()
    {
        var e = new ExerciseEvent(ExerciseEventType.PlotterRevealTargets) { Value = revealAll ? 1 : 0, ByInstructor = true };
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        ExerciseEvents.Publish(e);
    }

    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ExerciseStarted || e.Type == ExerciseEventType.ScenarioLoaded)
        {
            tracks.ClearTrails();
            if (mapLive != null) mapLive.Failures.Clear();
            nextRefresh = 0f;
        }
        // Журнал упражнения начинается с «Начать»: режим «все цели», включённый раньше, — тоже в журнал
        if (e.Type == ExerciseEventType.ExerciseStarted && revealAll) PublishReveal();
    }

    private void ApplyShown()
    {
        bool shown = Shown;
        if (canvas != null) canvas.enabled = shown;
        if (raycaster != null) raycaster.enabled = shown;
        if (shown) { nextRefresh = 0f; nextText = 0f; }
    }

    // ─────────── Кадр ───────────
    void Update()
    {
        if (canvas == null) return;
        if (hideWhileGrabbing && wantShown)
        {
            bool grabbing = false;
            foreach (IXRSelectInteractor i in leftInteractors)
            {
                var b = i as Behaviour;
                if (b != null && b.isActiveAndEnabled && i.hasSelection) { grabbing = true; break; }
            }
            if (grabbing != autoHidden) { autoHidden = grabbing; ApplyShown(); }
        }
        if (!Shown || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, uiRateHz);
        try { Refresh(); }
        catch (Exception ex) { Debug.LogException(ex, this); }
        foreach (var b in dynamicButtons)
        {
            b.label.text = b.text();
            b.image.color = b.color();
        }
    }

    void LateUpdate()
    {
        if (panel == null || !Shown || !faceHead || leftController == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        // Лицом к глазам, верх — по вертикали мира (не заваливается вместе с кистью)
        Vector3 toPanel = panel.transform.position - cam.transform.position;
        if (toPanel.sqrMagnitude > 1e-6f) panel.transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
    }

    private bool ScenarioActive =>
        runner != null && runner.Current != null &&
        (runner.CurrentPhase == ScenarioRunner.Phase.Briefing || runner.CurrentPhase == ScenarioRunner.Phase.Running);

    private bool HideTargets => ScenarioActive && runner.Current.hideTargetsOnPlotter;

    private bool ShowTargetNow(ITrackedTarget t) => PlotterMath.ShowTarget(ScenarioActive, HideTargets, revealAll, detected.Contains(t.TargetId));

    private void Refresh()
    {
        if (navigation == null || navigation.Model == null) return;

        // Обнаруженные цели — из оценки в реальном времени (C2)
        detected.Clear();
        ExerciseAssessor a = assessment != null && assessment.Live.Running ? assessment.Live.Assessor : null;
        if (a != null)
            foreach (TargetTrack tr in a.Targets)
                if (tr.Detected) detected.Add(tr.Info.Id);

        // Неподвижный слой: акватория и маршрут сценария
        WaterArea area = runner != null && runner.waterArea != null ? runner.waterArea.area : null;
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        if (!builtOnce || area != builtArea || sc != builtScenario)
        {
            builtOnce = true;
            builtArea = area;
            builtScenario = sc;
            mapData = DebriefMap.BuildStatic(area != null ? SessionArea.From(area) : null, sc != null ? SessionScenario.From(sc) : null);
            mapStatic.SetView(mapData, 0);
            mapLive.SetView(mapData, 0);
            routeMode = false;
            viewport.FollowShip(navigation.PositionEastNorth);
            builtVersion = -1;
        }

        // Окно: размер слоя и масштаб по режиму
        bool rotate = headUp && !routeMode;
        PlotterLayout L = PlotterMath.Layout(MapSize, MapSize, rotate, headUpShipOffset);
        if (Mathf.Abs(content.sizeDelta.x - (float)L.ContentSide) > 0.5f) content.sizeDelta = new Vector2((float)L.ContentSide, (float)L.ContentSide);
        viewport.SetSize(L.ContentSide, L.ContentSide);
        viewport.FollowBox = L.FollowBox;
        if (!routeMode)
        {
            viewport.SetMetersPerPixel(PlotterMath.MetersPerPixel(PlotterMath.RangeNm(rangeIndex), MapSize));
            if (!viewport.Follow) viewport.FollowShip(navigation.PositionEastNorth);
        }

        // Суда: своё и показываемые цели
        tracks.TrailMinMeters = trailMinMeters;
        tracks.TrailMaxPoints = trailMaxPoints;
        tracks.Update(navigation, include);
        tracks.Fill(mapLive);
        mapLive.vectorMinutes = vectorMinutes;
        mapLive.minHullPx = minHullPx;
        if (!routeMode) viewport.Track(tracks.Own.Position);

        if (viewport.Version != builtVersion)
        {
            builtVersion = viewport.Version;
            mapData.Views[0].Bounds = viewport.Bounds;
            mapStatic.SetVerticesDirty();
            BuildStaticLabels();
        }
        mapLive.SetVerticesDirty();

        // Положение слоя: «курс вверх» — поворот на +ψ вокруг судна, судно в точке S окна
        if (rotate)
        {
            rotationDeg = Mathf.Repeat(navigation.Heading, 360f);
            double m = viewport.MetersPerPixel;
            double px = (tracks.Own.Position.East - viewport.Center.East) / m, py = (tracks.Own.Position.North - viewport.Center.North) / m;
            PlotterMath.ContentOffset(rotationDeg, px, py, L.ShipX, L.ShipY, out double ox, out double oy);
            content.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            content.anchoredPosition = new Vector2((float)ox, (float)oy);
        }
        else if (rotationDeg != 0f || content.anchoredPosition != Vector2.zero)
        {
            rotationDeg = 0f;
            content.localRotation = Quaternion.identity;
            content.anchoredPosition = Vector2.zero;
        }

        ShipLabels();
        foreach ((TextMeshProUGUI t, WorldPosition p) in staticLabels) Upright(t.rectTransform, mapLive.LocalOf(p), new Vector2(0f, 0f));
        NorthMarker();
        rangeText.text = (routeMode ? "Весь маршрут" : $"Шкала {PlotterMath.RangeTitle(PlotterMath.RangeNm(rangeIndex))}") +
                         $"  ·  {(rotate ? "курс вверх" : "север вверх")}" +
                         $"\n<size=80%><color=#9AA6B2>сетка {GridTitle(mapStatic.GridStepM)}</color></size>";
        revealBanner.transform.parent.gameObject.SetActive(revealAll);

        if (Time.unscaledTime >= nextText)
        {
            nextText = Time.unscaledTime + 0.5f;
            dataText.text = DataText(a);
        }
    }

    // ─────────── Подписи ───────────
    // Подпись в слое карты: точка local (координаты слоя) + смещение offset на экране; текст не поворачивается с картой
    private void Upright(RectTransform rt, Vector2 local, Vector2 offset)
    {
        PlotterMath.Rotate(offset.x, offset.y, -rotationDeg, out double ox, out double oy);
        rt.anchoredPosition = local + new Vector2((float)ox, (float)oy);
        rt.localRotation = Quaternion.Euler(0f, 0f, -rotationDeg);
    }

    private void ShipLabels()
    {
        int i = 0;
        foreach (InstructorMapGraphic.Ship s in mapLive.Ships)
        {
            if (s.Own) continue;
            if (i >= shipLabels.Count) shipLabels.Add(LabelText(""));
            TextMeshProUGUI l = shipLabels[i++];
            bool show = mapLive.InView(s.Position);
            l.gameObject.SetActive(show);
            if (!show) continue;
            l.text = s.Id;
            l.color = s.Color;
            Upright(l.rectTransform, mapLive.LocalOf(s.Position), new Vector2(10f, 8f));
        }
        for (; i < shipLabels.Count; i++) shipLabels[i].gameObject.SetActive(false);
    }

    // Глубины мелей и названия путевых точек — при перестройке неподвижного слоя
    private void BuildStaticLabels()
    {
        foreach ((TextMeshProUGUI t, WorldPosition _) in staticLabels) Destroy(t.gameObject);
        staticLabels.Clear();
        if (mapData == null) return;
        MapRect view = viewport.Bounds;
        foreach (MapPolygon p in mapData.Polygons)
        {
            if (p.Layer != MapLayer.Shoal || string.IsNullOrEmpty(p.Label)) continue;
            WorldPosition c = PlotterMath.LabelPoint(p.Points);
            if (!view.Contains(c)) continue;
            TextMeshProUGUI t = LabelText(p.Label);
            t.color = new Color(1f, 0.78f, 0.45f);
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            staticLabels.Add((t, c));
        }
        foreach (MapMarker m in mapData.Markers)
        {
            if (m.Layer != MapLayer.Waypoint || string.IsNullOrEmpty(m.Label) || !view.Contains(m.Position)) continue;
            TextMeshProUGUI t = LabelText(m.Label);
            t.color = DebriefMapGraphic.RouteColor;
            t.fontSize = 13f;
            t.rectTransform.pivot = new Vector2(-0.08f, 1.1f);   // справа ниже точки
            staticLabels.Add((t, m.Position));
        }
    }

    // «N» на краю окна по направлению на север (в «курсе вверх» поворачивается вместе с картой)
    private void NorthMarker()
    {
        PlotterMath.Rotate(0, 0.5 * MapSize - 16, rotationDeg, out double nx, out double ny);
        northText.rectTransform.anchoredPosition = new Vector2((float)nx, (float)ny);
    }

    private static string GridTitle(double m) => m >= 1000 ? $"{ScenarioText.Num(m / 1000.0, m % 1000 == 0 ? 0 : 1)} км" : $"{ScenarioText.Num(m)} м";

    // ─────────── Данные ───────────
    private string DataText(ExerciseAssessor a)
    {
        var b = new StringBuilder();
        b.AppendLine($"Курс <b>{Mathf.Repeat(navigation.Heading, 360f):000.0}°</b>");
        b.AppendLine($"Скорость <b>{ScenarioText.Num(navigation.SpeedKnots, 1)} уз</b>");

        RouteTracker route = runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Running ? runner.Route : null;
        if (route != null && route.Valid && !route.Finished)
        {
            string xte = $"XTE {route.Xte:+0;−0;0} м <size=85%>(доп. {route.XteLimit:F0})</size>";
            b.AppendLine(route.OutsideCorridor ? $"<color=#FF7A6A>{xte}</color>" : xte);
            b.AppendLine($"Участок {route.Leg + 1}: {route.LegCourseDeg:000}°");
            int next = Mathf.Min(route.Leg + 1, route.WaypointCount - 1);
            WaypointBearing w = PlotterMath.ToWaypoint(navigation.PositionEastNorth, route.Route[next].position, navigation.SpeedKnots);
            b.AppendLine($"<color=#9AA6B2>Точка {next}</color> {route.Route[next].name}");
            b.AppendLine($"П {w.BearingDeg:000}° · {ScenarioText.Num(w.DistanceM / 1852.0, 2)} мили");
            b.AppendLine(double.IsNaN(w.EtaS) ? "без хода" : $"через {ScenarioText.Clock(w.EtaS)}");
        }
        else if (runner != null)
            b.AppendLine($"<color=#9AA6B2>{PhaseTitle(runner.CurrentPhase)}</color>");

        // Ближайшая опасная цель из показываемых
        if (HideTargets && !revealAll)
        {
            b.Append("<size=85%><color=#9AA6B2>Цели на планшете не показываются (сценарий)</color></size>");
            return b.ToString().TrimEnd();
        }
        cpas.Clear();
        cpaTargets.Clear();
        WorldPosition own = navigation.PositionEastNorth;
        foreach (ITrackedTarget t in ExerciseTargets.All)
        {
            if (!ShowTargetNow(t)) continue;
            cpas.Add(CpaCalculator.FromCourses(own, navigation.Heading, navigation.SpeedKnots, t.Position, t.HeadingDeg, t.SpeedKnots));
            cpaTargets.Add(t);
        }
        int k = PlotterMath.NearestByCpa(cpas);
        if (k < 0)
        {
            string none = ExerciseTargets.All.Count == 0 ? "других судов нет" : cpas.Count == 0 ? "цели не обнаружены" : "опасных целей нет";
            b.Append($"<size=85%><color=#9AA6B2>{none}</color></size>");
            return b.ToString().TrimEnd();
        }
        CpaResult c = cpas[k];
        double required = RequiredCpaM(a, cpaTargets[k].TargetId);
        string cpa = $"CPA {ScenarioText.Num(c.Cpa / 1852.0, 2)} мили";
        b.AppendLine($"<color=#9AA6B2>Цель</color> <b>{cpaTargets[k].TargetId}</b>  D {ScenarioText.Num(c.Range / 1852.0, 2)}");
        b.AppendLine(c.Cpa < required ? $"<color=#FF7A6A>{cpa}</color>" : cpa);
        b.Append($"TCPA {ScenarioText.Num(c.Tcpa / 60.0, 1)} мин");
        return b.ToString();
    }

    private static double RequiredCpaM(ExerciseAssessor a, string id)
    {
        if (a != null)
            foreach (TargetTrack tr in a.Targets)
                if (tr.Info.Id == id) return a.Context.RequiredCpaM(tr.Info);
        return AssessmentSettings.Default().defaultRequiredCpaNm * 1852.0;
    }

    private static string PhaseTitle(ScenarioRunner.Phase p) =>
        p == ScenarioRunner.Phase.Briefing ? "Брифинг" : p == ScenarioRunner.Phase.Finished ? "Упражнение завершено" : p == ScenarioRunner.Phase.Running ? "Маршрут пройден" : "Свободное плавание";

    // ─────────── Кнопки ───────────
    private void StepRange(int dir)
    {
        routeMode = false;
        rangeIndex = PlotterMath.StepRange(rangeIndex, dir);
        if (navigation != null && navigation.Model != null) viewport.FollowShip(navigation.PositionEastNorth);
        nextRefresh = 0f;
    }

    private void ToggleHeadUp()
    {
        headUp = !headUp;
        routeMode = false;
        if (navigation != null && navigation.Model != null) viewport.FollowShip(navigation.PositionEastNorth);
        nextRefresh = 0f;
    }

    private void ToggleRoute()
    {
        nextRefresh = 0f;
        if (routeMode || navigation == null || navigation.Model == null)
        {
            routeMode = false;
            if (navigation != null && navigation.Model != null) viewport.FollowShip(navigation.PositionEastNorth);
            return;
        }
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        MapRect r = MapRect.Empty;
        if (sc != null && sc.route != null) foreach (RouteWaypoint w in sc.route) r.Include(w.position);
        r.Include(navigation.PositionEastNorth);
        if (r.Width < 1 && r.Height < 1) return;
        routeMode = true;
        PlotterLayout L = PlotterMath.Layout(MapSize, MapSize, false);
        viewport.SetSize(L.ContentSide, L.ContentSide);
        viewport.Fit(r.Padded(0.04, 1000), 0.0, MapSize, MapSize);
    }

    // ─────────── Построение ───────────
    private void Build()
    {
        if (panel != null) return;
        panel = new GameObject("NavPlotterCanvas", typeof(RectTransform));
        if (leftController != null)
        {
            panel.transform.SetParent(leftController, false);
            panel.transform.localPosition = handOffset;
            panel.transform.localRotation = Quaternion.Euler(handEuler);
        }
        else
        {
            panel.transform.SetParent(transform, false);
            Camera cam = Camera.main;
            if (cam != null) panel.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 0.45f + Vector3.down * 0.25f,
                                                                    Quaternion.LookRotation(cam.transform.forward, Vector3.up));
        }
        panel.transform.localScale = Vector3.one * canvasScale;
        canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 6;
        panel.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 100;
        var raycasterType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        raycaster = raycasterType != null ? (Behaviour)panel.AddComponent(raycasterType) : panel.AddComponent<GraphicRaycaster>();
        root = panel.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(Width, Height);

        Image bg = panel.AddComponent<Image>();
        bg.color = PanelColor;

        // Окно карты (обрезка) и слой (поворачивается и сдвигается целиком)
        window = MakeRect(root, "Map", new Vector2(0f, 0.5f), new Vector2(MapSize, MapSize), new Vector2(0f, 0.5f), Vector2.zero);
        window.gameObject.AddComponent<RectMask2D>();
        content = MakeRect(window, "Layer", new Vector2(0.5f, 0.5f), new Vector2(MapSize, MapSize), new Vector2(0.5f, 0.5f), Vector2.zero);
        mapStatic = Layer<DebriefMapGraphic>("MapStatic");
        mapLive = Layer<InstructorMapGraphic>("MapLive");
        labelsRoot = MakeRect(content, "Labels", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero);
        labelsRoot.anchorMin = Vector2.zero;
        labelsRoot.anchorMax = Vector2.one;
        labelsRoot.sizeDelta = Vector2.zero;

        // Надписи поверх карты (не поворачиваются)
        rangeText = Text(window, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        Place(rangeText.rectTransform, new Vector2(0f, 1f), new Vector2(6f, -4f), new Vector2(300f, 44f), new Vector2(0f, 1f));
        northText = Text(window, "N", 16f, new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center);
        northText.fontStyle = FontStyles.Bold;
        Place(northText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 22f), new Vector2(0.5f, 0.5f));
        RectTransform banner = MakeRect(window, "Reveal", new Vector2(0.5f, 0f), new Vector2(MapSize - 20f, 26f), new Vector2(0.5f, 0f), new Vector2(0f, 6f));
        banner.gameObject.AddComponent<Image>().color = RevealColor;
        banner.GetComponent<Image>().raycastTarget = false;
        revealBanner = Text(banner, "ВСЕ ЦЕЛИ (инструктор)", 15f, Color.white, TextAlignmentOptions.Center);
        revealBanner.fontStyle = FontStyles.Bold;
        Stretch(revealBanner.rectTransform);
        banner.gameObject.SetActive(false);

        // Столбец: кнопки и данные
        column = MakeRect(root, "Column", new Vector2(1f, 0.5f), new Vector2(Width - MapSize, Height), new Vector2(1f, 0.5f), Vector2.zero);
        float w = Width - MapSize - 2f * Pad, half = (w - Gap) * 0.5f, y = -Pad;
        Button(column, Pad, y, half, "−", () => StepRange(-1), null, () => rangeIndex > 0 && !routeMode ? ButtonColor : NeutralColor);
        Button(column, Pad + half + Gap, y, half, "+", () => StepRange(+1), null, () => rangeIndex < PlotterMath.RangesNm.Length - 1 || routeMode ? ButtonColor : NeutralColor);
        y -= ButtonH + Gap;
        Button(column, Pad, y, w, "", ToggleHeadUp, () => headUp ? "Курс вверх" : "Север вверх", () => headUp ? ActiveColor : ButtonColor);
        y -= ButtonH + Gap;
        Button(column, Pad, y, w, "", ToggleRoute, () => routeMode ? "За судном" : "Весь маршрут", () => routeMode ? ActiveColor : ButtonColor);
        y -= ButtonH + Gap + 2f;
        dataText = Text(column, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        dataText.overflowMode = TextOverflowModes.Truncate;
        Place(dataText.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(w, Height + y - Pad), new Vector2(0f, 1f));

        // Ночь: затемнение вместе с приборами (шторка — последней, поверх всего)
        if (lighting != null) lighting.RegisterInstrumentPanel(root);
        canvas.enabled = false;
        raycaster.enabled = false;
    }

    private T Layer<T>(string name) where T : DebriefMapGraphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(content, false);
        Stretch(go.GetComponent<RectTransform>());
        T g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private TextMeshProUGUI LabelText(string text)
    {
        TextMeshProUGUI t = Text(labelsRoot, text, 14f, Color.white, TextAlignmentOptions.Left);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(120f, 20f);
        return t;
    }

    private static RectTransform MakeRect(Transform parent, string name, Vector2 anchor, Vector2 size, Vector2 pivot, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Кнопка: x, y — от левого верхнего угла родителя; подпись и цвет обновляются с картой.</summary>
    private void Button(RectTransform parent, float x, float y, float width, string label, UnityAction action, Func<string> text, Func<Color> color)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color != null ? color() : ButtonColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogException(e, this); }
        });
        Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, ButtonH), new Vector2(0f, 1f));
        TextMeshProUGUI t = Text(go.transform, text != null ? text() : label, 18f, Color.white, TextAlignmentOptions.Center);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(t.rectTransform);
        dynamicButtons.Add((t, img, text ?? (() => label), color ?? (() => ButtonColor)));
    }

    // Левый контроллер XR Rig по имени (если Setup не задал ссылку)
    private static Transform FindLeftController()
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.name == "Left Controller") return t;
        return null;
    }
}
