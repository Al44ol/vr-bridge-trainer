// ============================================================
// InstructorWindow.cs — Интерфейс окна инструктора (D1)
//
// Screen Space Overlay (только монитор, в шлем не попадает), строится в коде,
// сцену не правит. Опорное разрешение 1920 × 1080 (CanvasScaler, Expand — окно помещается целиком).
//   • Верхняя строка: выбор сценария «< … >» и «Загрузить», ID обучающегося
//     (поле ввода → ExerciseRecorder.TraineeId), «Начать», «Пауза / Продолжить»,
//     «Перезапуск», «Завершить», «Свободное плавание»; справа — фаза и время.
//   • Слева — карта (InstructorMapGraphic поверх DebriefMapGraphic): колесо мыши —
//     масштаб к курсору, перетаскивание — сдвиг; «За судном» (следование с
//     перецентровкой, MapViewport), «Весь маршрут», «+», «−»; масштаб и сетка внизу;
//     справа внизу — вид из шлема (PiP: кнопка здесь или меню Y «Монитор инструктора»).
//     D2: «Планшет: все цели / обнаруженные» — что видит обучающийся на планшете (NavPlotter);
//     суда и следы — общий класс LiveMapTracks (с планшетом).
//     19.2 (A): над PiP — [−] [+] (480 / 720 / 960 / на всё поле карты) и «10 / 20 Гц»;
//     «Шлем на весь экран» — режим зеркала; возврат — кнопка «« Окно инструктора» поверх
//     зеркала, Esc или правый клик мыши (InstructorStation).
//     19.2 (B): в верхней строке — «Режим: экзамен / обучение / инструктор» (TrainerModeSwitch, по кругу).
//   • Справа — состояние своего судна и маршрута; таблица целей (дистанция, пеленг,
//     курсовой угол, CPA, TCPA, ситуация по МППСС из оценки C2 — инструктор видит
//     цели и при флаге сценария «цели не показывать»); вводные (отказ ГД, отказ руля,
//     квитирование, оповещение, видимость), погода и время суток; события шины
//     (нарушения и вводные выделены цветом); текущий балл по кнопке.
// Обновление — uiRateHz (5 Гц, реальное время); события — по мере прихода.
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InstructorWindow : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Сцена (пусто — найти)")]
    public ScenarioRunner runner;
    public ExerciseRecorder recorder;
    public ExerciseAssessmentMonitor assessment;
    public InstructorInputs inputs;
    public WeatherController weather;
    public GroundingMonitor grounding;

    [Header("Окно")]
    [Tooltip("Строк в списке событий")]
    [Range(5, 30)] public int eventLines = 14;
    [Tooltip("След судов на карте: точка не чаще, чем через … м")]
    public float trailMinMeters = 10f;
    [Tooltip("Наибольшее число точек следа одного судна")]
    public int trailMaxPoints = 4000;
    [Tooltip("Масштаб при следовании за судном без маршрута, м на пиксель")]
    public float followMetersPerPixel = 3f;

    private const float TopBar = 60f, ColumnWidth = 760f, Pad = 10f, Gap = 6f, RowHeight = 40f;
    private static readonly Color BarColor = new Color(0.08f, 0.1f, 0.13f, 1f);
    private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.97f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    private static readonly Color StartColor = new Color(0.16f, 0.55f, 0.3f);
    private static readonly Color StopColor = new Color(0.6f, 0.18f, 0.15f);
    private static readonly Color WarnColor = new Color(0.62f, 0.42f, 0.08f);
    private static readonly Color NeutralColor = new Color(0.33f, 0.35f, 0.4f);
    private static readonly Color ActiveColor = new Color(0.3f, 0.52f, 0.8f);
    private static readonly Color TitleColor = new Color(0.85f, 0.92f, 1f);
    private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.75f);
    private static readonly float[] VisibilityStepsNm = { 0f, 4f, 2f, 1f, 0.5f, 0.25f };

    private InstructorStation station;
    private ShipNavigation navigation;
    private Canvas canvas;
    private RectTransform root, column, bar;
    private float nextRefresh;
    private int scenarioIndex;

    // Карта
    private RectTransform mapRect, mapControls;
    private DebriefMapGraphic mapStatic;
    private InstructorMapGraphic mapLive;
    private DebriefMapData mapData;
    private readonly MapViewport viewport = new MapViewport();
    private int builtVersion = -1;
    private WaterArea builtArea;
    private ScenarioDefinition builtScenario;
    private bool builtOnce;
    private TextMeshProUGUI scaleText;
    private readonly List<TextMeshProUGUI> shipLabels = new List<TextMeshProUGUI>();
    private readonly LiveMapTracks tracks = new LiveMapTracks();   // суда и следы (общий класс с планшетом D2)
    private NavPlotter plotter;
    private bool dragging;
    private RawImage pipImage;
    private RectTransform pipPanel;          // [−] [+] размер, частота — над PiP
    private TextMeshProUGUI pipSizeText;
    private int pipStep;                     // шаг размера PiP (PipWidths)
    private Vector2 pipBase;                 // шаг 0: размер на холсте
    private Vector2Int pipBaseTexture;       // шаг 0: текстура (размер InstructorPip по умолчанию)
    private RectTransform mirrorBack;        // в режиме зеркала: «« Окно инструктора»

    // Тексты и кнопки
    private TextMeshProUGUI statusText, stateText, targetsText, eventsText, scoreText, mirrorHint, scenarioLabel;
    private TMP_InputField idField;
    private readonly List<(TextMeshProUGUI label, Image image, Func<string> text, Func<Color> color, Func<bool> enabled)> dynamicButtons =
        new List<(TextMeshProUGUI, Image, Func<string>, Func<Color>, Func<bool>)>();
    private readonly List<string> events = new List<string>();
    private bool eventsDirty = true;

    public void Init(InstructorStation owner)
    {
        station = owner;
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (recorder == null) recorder = ExerciseRecorder.Instance != null ? ExerciseRecorder.Instance : FindAnyObjectByType<ExerciseRecorder>();
        if (assessment == null) assessment = ExerciseAssessmentMonitor.Instance != null ? ExerciseAssessmentMonitor.Instance : FindAnyObjectByType<ExerciseAssessmentMonitor>();
        if (inputs == null) inputs = InstructorInputs.Instance != null ? InstructorInputs.Instance : FindAnyObjectByType<InstructorInputs>();
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
        navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (grounding == null && navigation != null) grounding = navigation.GetComponent<GroundingMonitor>();
        plotter = FindAnyObjectByType<NavPlotter>();   // D2 (нет — кнопка «Планшет» неактивна)
        Build();
    }

    void OnEnable() => ExerciseEvents.Raised += OnExerciseEvent;
    void OnDisable() => ExerciseEvents.Raised -= OnExerciseEvent;

    public void SetVisible(bool on)
    {
        if (root == null) return;
        foreach (Transform t in root) t.gameObject.SetActive(on);
        mirrorHint.gameObject.SetActive(!on);
        mirrorBack.gameObject.SetActive(!on);
        nextRefresh = 0f;
    }

    private bool Visible => station != null && station.Mode == InstructorStation.DisplayMode.Instructor;

    void Update()
    {
        if (canvas == null || !Visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, station.uiRateHz);
        RefreshMap();
        RefreshTexts();
        foreach (var b in dynamicButtons)
        {
            b.label.text = b.text();
            bool enabled = b.enabled == null || b.enabled();
            Color c = b.color != null ? b.color() : ButtonColor;
            b.image.color = enabled ? c : new Color(0.2f, 0.22f, 0.25f);
        }
        RefreshPip();
    }

    // ─────────── Вид из шлема (PiP): размер и частота ───────────
    // Шаг 0 — прежние 480 × 270 (текстура — размер по умолчанию InstructorPip, как в замере табл. 13.2);
    // шаги 1, 2 — ширина 720 и 960 ед. холста; последний — на всё поле карты (под строкой кнопок).
    // Текстура на шагах ≥ 1 — по пикселям экрана (размер на холсте × scaleFactor).
    private static readonly float[] PipWidths = { 0f, 720f, 960f, float.MaxValue };
    private static readonly float[] PipRates = { 10f, 20f };

    private Vector2 PipDisplaySize()
    {
        float aspect = pipBase.x / Mathf.Max(1f, pipBase.y);
        if (pipStep == 0) return pipBase;
        // Наибольший: поле карты без отступов, строки кнопок карты и строки кнопок PiP
        float maxW = mapRect.rect.width - 16f;
        float maxH = mapRect.rect.height - 16f - (RowHeight + 8f) - (RowHeight + 4f);
        float w = Mathf.Min(PipWidths[pipStep], maxW, maxH * aspect);
        w = Mathf.Max(w, pipBase.x);
        return new Vector2(Mathf.Round(w), Mathf.Round(w / aspect));
    }

    private void RefreshPip()
    {
        if (pipImage == null) return;
        InstructorPip p = station.Pip;
        bool pip = p != null && p.Active;
        pipImage.gameObject.SetActive(pip);
        pipPanel.gameObject.SetActive(pip);
        if (!pip) return;
        Vector2 size = PipDisplaySize();
        if (pipImage.rectTransform.sizeDelta != size)
        {
            pipImage.rectTransform.sizeDelta = size;
            pipPanel.anchoredPosition = new Vector2(-8f, 8f + size.y + 4f);
            float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            if (pipStep == 0) p.SetResolution(pipBaseTexture.x, pipBaseTexture.y);
            else p.SetResolution(Mathf.RoundToInt(size.x * k), Mathf.RoundToInt(size.y * k));
            Debug.Log($"[Инструктор] Вид из шлема (PiP): {size.x:F0}×{size.y:F0} на экране, текстура {p.width}×{p.height}, {p.rateHz:F0} Гц.");
        }
        pipImage.texture = p.Texture;
        pipSizeText.text = $"{p.width}×{p.height} · {pipStep + 1}/{PipWidths.Length}";
    }

    private void StepPip(int delta)
    {
        // Шаг, который на этом мониторе не меняет размер (поле карты узкое), пропускается
        Vector2 current = PipDisplaySize();
        int saved = pipStep, step = pipStep;
        while (step + delta >= 0 && step + delta < PipWidths.Length)
        {
            step += delta;
            pipStep = step;
            bool changed = PipDisplaySize() != current;
            pipStep = saved;
            if (changed) break;
        }
        pipStep = step;
    }

    private void TogglePipRate()
    {
        InstructorPip p = station.Pip;
        if (p == null) return;
        int i = Array.IndexOf(PipRates, p.rateHz);
        p.rateHz = PipRates[(i + 1) % PipRates.Length];
        Debug.Log($"[Инструктор] Вид из шлема (PiP): {p.rateHz:F0} Гц.");
    }

    // ─────────── Карта ───────────
    private void RefreshMap()
    {
        if (navigation == null || navigation.Model == null) return;
        viewport.SetSize(mapRect.rect.width, mapRect.rect.height);

        // Неподвижный слой: акватория и маршрут сценария (новый сценарий — новая схема)
        WaterArea area = runner != null && runner.waterArea != null ? runner.waterArea.area : null;
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        if (!builtOnce || area != builtArea || sc != builtScenario)
        {
            builtOnce = true;
            builtArea = area;
            builtScenario = sc;
            mapData = DebriefMap.BuildStatic(area != null ? SessionArea.From(area) : null, sc != null ? SessionScenario.From(sc) : null);
            if (sc != null && sc.route != null && sc.route.Count >= 2) FitRoute();
            else { viewport.MetersPerPixel = followMetersPerPixel; viewport.FollowShip(navigation.PositionEastNorth); }
            mapStatic.SetView(mapData, 0);
            mapLive.SetView(mapData, 0);
            builtVersion = -1;   // новой схеме — границы текущего окна
        }

        // Суда: своё и цели (все — инструктор видит всё); след — по мере движения
        tracks.TrailMinMeters = trailMinMeters;
        tracks.TrailMaxPoints = trailMaxPoints;
        tracks.Update(navigation);
        tracks.Fill(mapLive);
        FillMooringLines();

        viewport.Track(tracks.Own.Position);
        if (viewport.Version != builtVersion)
        {
            builtVersion = viewport.Version;
            mapData.Views[0].Bounds = viewport.Bounds;
            mapStatic.SetVerticesDirty();
        }
        mapLive.SetVerticesDirty();
        Labels();
        scaleText.text = $"1 пикс. = {ScenarioText.Num(viewport.MetersPerPixel, viewport.MetersPerPixel < 10 ? 1 : 0)} м · сетка {ScenarioText.Num(mapStatic.GridStepM)} м" +
                         (viewport.Follow ? " · за судном" : "") + " · колесо — масштаб, перетаскивание — сдвиг";
    }

    // 19.2б-3б: поданные концы — от клюза к кнехту, цвет по натяжению (как MooringLinesView в «обучении»):
    // T < 0,3·MBL — зелёный, < 0,5·MBL — жёлтый, ≥ 0,5·MBL (перегрузка) — красный. Только окно инструктора.
    private static readonly Color LineSlack = new Color(0.25f, 0.85f, 0.35f), LineLoaded = new Color(1f, 0.82f, 0.2f), LineOverload = new Color(0.95f, 0.2f, 0.15f);

    private void FillMooringLines()
    {
        mapLive.MooringLines.Clear();
        // 19.3б: буксирный трос — от клюза к гаку буксира, цвет по натяжению
        TowingSystem ts = TowingController.Instance != null ? TowingController.Instance.Towing : null;
        if (ts != null && ts.Line.Fast && navigation.Model != null)
        {
            double share = ts.Line.Mbl > 0 ? ts.Line.Tension / ts.Line.Mbl : 0.0;
            mapLive.MooringLines.Add(new InstructorMapGraphic.Line
            {
                From = ts.TowPoint(navigation.Model), To = ts.Tug.Bow,
                Color = share >= MooringLine.OverloadFraction ? LineOverload : share >= 0.3 ? LineLoaded : LineSlack
            });
        }
        MooringController mc = MooringController.Instance;
        BerthArea berth = mc != null ? mc.ActiveBerth : null;
        if (berth == null || navigation.Model == null) return;
        foreach (MooringLine l in mc.System.Lines)
        {
            if (l.State != MooringLineState.Fast) continue;
            double share = l.Mbl > 0 ? l.Tension / l.Mbl : 0.0;
            mapLive.MooringLines.Add(new InstructorMapGraphic.Line
            {
                From = mc.System.Fairlead(navigation.Model, l), To = berth.BollardWorld(l.Bollard),
                Color = share >= MooringLine.OverloadFraction ? LineOverload : share >= 0.3 ? LineLoaded : LineSlack
            });
        }
    }

    private void Labels()
    {
        int i = 0;
        foreach (InstructorMapGraphic.Ship s in mapLive.Ships)
        {
            if (i >= shipLabels.Count) shipLabels.Add(MakeText(mapRect, "", 15f, Color.white, TextAlignmentOptions.Left));
            TextMeshProUGUI l = shipLabels[i++];
            bool show = mapLive.InView(s.Position);
            l.gameObject.SetActive(show);
            if (!show) continue;
            l.text = s.Own ? "своё" : s.Id;
            l.color = s.Color;
            var rt = l.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(120f, 22f);
            rt.anchoredPosition = mapLive.LocalOf(s.Position) + new Vector2(12f, 10f);
        }
        for (; i < shipLabels.Count; i++) shipLabels[i].gameObject.SetActive(false);
    }

    private void FitRoute()
    {
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        MapRect r = MapRect.Empty;
        if (sc != null && sc.route != null) foreach (RouteWaypoint w in sc.route) r.Include(w.position);
        if (navigation != null && navigation.Model != null) r.Include(navigation.PositionEastNorth);
        foreach (ITrackedTarget t in ExerciseTargets.All) r.Include(t.Position);
        if (r.IsEmpty) return;
        viewport.Fit(r.Padded(0.02, 1000));
    }

    private void Zoom(float factor, Vector2 local)
    {
        Rect rr = mapRect.rect;
        viewport.ZoomAt(local.x - rr.xMin, local.y - rr.yMin, factor);
        nextRefresh = 0f;
    }

    public void OnScroll(PointerEventData e)
    {
        if (!Visible || !RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, e.position, null, out Vector2 local) || !mapRect.rect.Contains(local)) return;
        if (Mathf.Abs(e.scrollDelta.y) < 1e-3f) return;
        Zoom(e.scrollDelta.y > 0 ? 1.25f : 0.8f, local);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        GameObject press = e.pointerPressRaycast.gameObject;
        dragging = Visible && press != null && press.transform == mapRect;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging) return;
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        viewport.Pan(e.delta.x / k, e.delta.y / k);
        nextRefresh = 0f;
    }

    public void OnEndDrag(PointerEventData e) => dragging = false;

    // ─────────── Тексты ───────────
    private void RefreshTexts()
    {
        ScenarioRunner.Phase phase = runner != null ? runner.CurrentPhase : ScenarioRunner.Phase.Idle;
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        string phaseText = phase == ScenarioRunner.Phase.Running ? (runner.IsPaused ? "<color=#7FB2FF>ПАУЗА</color>" : "<color=#6FE08A>УПРАЖНЕНИЕ</color>")
                         : phase == ScenarioRunner.Phase.Briefing ? "<color=#FFC85A>БРИФИНГ · ПАУЗА ДО «НАЧАТЬ»</color>"   // 21.5в
                         : phase == ScenarioRunner.Phase.Finished ? "ЗАВЕРШЕНО" : "СВОБОДНОЕ ПЛАВАНИЕ";
        statusText.text = $"{phaseText}  <b>{(phase == ScenarioRunner.Phase.Idle ? "" : ScenarioText.Clock(runner.ExerciseTime))}</b>" +
                          (sc != null && phase != ScenarioRunner.Phase.Idle ? $"\n<size=75%><color=#9AA6B2>{sc.id} «{sc.title}»" +
                           (recorder != null && recorder.IsRecording ? " · запись" : "") + "</color></size>" : "");
        if (scenarioLabel != null)
        {
            List<ScenarioDefinition> list = runner != null ? runner.scenarios : null;
            if (list == null || list.Count == 0) scenarioLabel.text = "нет сценариев";
            else
            {
                scenarioIndex = Mathf.Clamp(scenarioIndex, 0, list.Count - 1);
                ScenarioDefinition s = list[scenarioIndex];
                scenarioLabel.text = s != null ? $"<b>{s.id}</b> {s.title}" + (s.IsFromFile ? " <size=80%><color=#9AA6B2>· файл</color></size>" : "") : "—";
            }
        }
        if (idField != null && !idField.isFocused && recorder != null && idField.text != recorder.TraineeId) idField.text = recorder.TraineeId;

        stateText.text = StateText();
        targetsText.text = TargetsText(sc);
        if (eventsDirty)
        {
            eventsDirty = false;
            var b = new StringBuilder();
            for (int i = Math.Max(0, events.Count - eventLines); i < events.Count; i++) b.AppendLine(events[i]);
            eventsText.text = b.Length > 0 ? b.ToString().TrimEnd() : "<color=#9AA6B2>событий пока нет</color>";
        }
        if (phase == ScenarioRunner.Phase.Finished && assessment != null && assessment.LastResult != null && scoreText.text.Length == 0)
            ShowScore();
    }

    private string StateText()
    {
        if (navigation == null || navigation.Model == null) return "нет навигации";
        var b = new StringBuilder();
        float hdg = Mathf.Repeat(navigation.Heading, 360f);
        b.AppendLine($"Курс <b>{hdg:000.0}°</b>   скорость <b>{ScenarioText.Num(navigation.SpeedKnots, 1)} уз</b>   поворот {navigation.RateOfTurn:+0;−0;0} °/мин");
        b.AppendLine($"Руль: заданный {ScenarioText.Rudder(navigation.RudderOrdered)}, фактический {ScenarioText.Rudder(navigation.RudderActual)}   телеграф «{TelegraphTitle()}»");
        RouteTracker route = runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Running ? runner.Route : null;
        if (route != null && route.Valid && !route.Finished)
        {
            int next = Mathf.Min(route.Leg + 1, route.WaypointCount - 1);
            double dist = Math.Max(0.0, route.LegLength - route.Atd);
            double u = Math.Abs(navigation.SpeedMs);
            string eta = u > 0.2 ? ScenarioText.Clock(dist / u) : "—";
            string xte = route.OutsideCorridor ? $"<color=#FF7A6A>XTE {route.Xte:+0;−0;0} м — вне коридора (доп. {route.XteLimit:F0} м)</color>"
                                               : $"XTE {route.Xte:+0;−0;0} м (доп. {route.XteLimit:F0} м)";
            b.AppendLine($"{xte}   участок {route.Leg + 1}, курс {route.LegCourseDeg:000}°");
            b.AppendLine($"До точки {next} «{route.Route[next].name}»: {ScenarioText.Num(dist / 1852.0, 2)} мили, {eta}   пройдено {route.PassedCount} из {route.WaypointCount - 1}");
        }
        if (grounding != null)
        {
            string depth = float.IsNaN(grounding.DepthUnderShip) ? "—" : $"{ScenarioText.Num(grounding.DepthUnderShip)} м, под килем {ScenarioText.Num(grounding.DepthUnderShip - grounding.Draft, 1)} м";
            string alarm = grounding.State == GroundingState.Grounded ? "  <color=#FF5A4A><b>НА МЕЛИ</b></color>"
                         : grounding.State == GroundingState.Warning ? "  <color=#FF7A6A>опасная глубина рядом</color>" : "";
            b.AppendLine($"Глубина {depth}{alarm}");
        }
        if (weather != null)
        {
            string vis = weather.VisibilityOverrideM > 0 ? $"{ScenarioText.Num(weather.VisibilityOverrideM / 1852.0, 2)} мили (задана)" : $"{ScenarioText.Num(weather.VisibilityM / 1852.0, 1)} мили (по погоде)";
            b.AppendLine($"{ScenarioText.WeatherTitle(weather.CurrentPresetId)}, {ScenarioText.TimeOfDayTitle(weather.CurrentTimeOfDay).ToLowerInvariant()}, видимость {vis}");
        }
        // 19.3б: ветер (действует только в 3 СС) и буксир
        TowingController towing = TowingController.Instance;
        if (towing != null)
        {
            Wind w = towing.Wind;
            if (!w.Calm)
                b.AppendLine($"Ветер {ScenarioText.Wind(w.SpeedMs / ShipParameters.KnotsToMs, w.FromDeg)}" + (navigation.Uses3Dof ? "" : " <color=#9AA6B2>(не действует: модель Номото)</color>"));
            if (towing.TugPresent) b.AppendLine(towing.InstructorLine());
        }
        // 19.4б-2: упражнение на своём буксире — команда лоцмана, задания, зазор клиента, упор, касания
        TugWorkController work = TugWorkController.Instance;
        if (work != null && work.Work != null) b.AppendLine(work.InstructorLine());
        // 19.2б-3: сценарий швартовки — выполненные задания и строка причала
        MooringTaskTracker mt = runner != null && runner.CurrentPhase != ScenarioRunner.Phase.Idle ? runner.MooringTask : null;
        if (mt != null)
        {
            var done = new StringBuilder();
            for (int i = 1; i <= MooringTaskTracker.StageCount; i++) done.Append(mt.Done((MooringStage)i) ? " +" : " –");
            string hold = !mt.Moored && mt.HoldTime > 0.0 ? $", ошвартован через {ScenarioText.Num(Math.Max(0.0, mt.Task.mooredHoldS - mt.HoldTime))} с" : "";
            b.AppendLine($"Швартовка: заданий {mt.DoneCount} из {MooringTaskTracker.StageCount} ({done.ToString().Trim()}){hold}");
            BerthReadout br = BerthReadout.Compute(runner.Current.mooring.BerthIn(runner.Current.waterArea), navigation.parameters, navigation.Model);
            if (br.Valid) b.AppendLine(br.Format().Replace("\n", "   "));
        }
        if (inputs != null && (inputs.EngineFailed || inputs.SteeringFailed))
            b.AppendLine($"<color=#FFC85A><b>Вводные: {(inputs.EngineFailed ? "отказ ГД " : "")}{(inputs.SteeringFailed ? "отказ руля" : "")}</b>" +
                         $"{(inputs.announceFailures ? "" : " (обучающийся не оповещён)")}</color>");
        return b.ToString().TrimEnd();
    }

    private string TelegraphTitle()
    {
        EngineTelegraph tel = navigation != null ? navigation.telegraph : null;
        double sf = tel != null ? tel.SpeedFactor : navigation.Model.SpeedFactor;
        return ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(sf));
    }

    private string TargetsText(ScenarioDefinition sc)
    {
        if (ExerciseTargets.All.Count == 0) return "<color=#9AA6B2>других судов нет</color>";
        var b = new StringBuilder("<color=#9AA6B2>Цель<pos=11%>D, мили<pos=24%>П, °<pos=33%>КУ<pos=45%>CPA, мили<pos=60%>TCPA, мин<pos=74%>Ситуация</color>\n");
        WorldPosition p = navigation.PositionEastNorth;
        ExerciseAssessor a = assessment != null && assessment.Live.Running ? assessment.Live.Assessor : null;
        int i = 0;
        foreach (ITrackedTarget t in ExerciseTargets.All)
        {
            CpaResult c = CpaCalculator.FromCourses(p, navigation.Heading, navigation.SpeedKnots, t.Position, t.HeadingDeg, t.SpeedKnots);
            double q = CpaCalculator.RelativeBearing(c.BearingDeg, navigation.Heading);
            if (WorkVessels.Is(t.TargetId))
            {
                // 19.3б: свой буксир, 19.4б-2: судно-клиент — без CPA (состояние — строкой в «Состоянии судна»)
                string tc = ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.TargetColor(i++));
                b.AppendLine($"<color=#{tc}>{t.TargetId}</color><pos=11%>{ScenarioText.Num(c.Range / 1852.0, 2)}<pos=24%>{c.BearingDeg:000}<pos=33%>{ScenarioText.RelativeBearing(q)}<pos=45%>{(t is TugTarget ? "свой буксир" : "судно-клиент")}");
                continue;
            }
            bool closing = c.Tcpa > 0;
            string situation = "—";
            bool danger = false;
            if (a != null)
                foreach (TargetTrack tr in a.Targets)
                    if (tr.Info.Id == t.TargetId)
                    {
                        if (tr.Encounter != null) { situation = EncounterClassifier.Title(tr.Encounter.Situation); danger = true; }
                        else situation = tr.Detected ? (tr.Seen || !a.RestrictedVisibility ? "обнаружена" : "обнаружена, не видна") : "не обнаружена";
                    }
            string col = ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.TargetColor(i++));
            string line = $"<color=#{col}>{t.TargetId}</color><pos=11%>{ScenarioText.Num(c.Range / 1852.0, 2)}<pos=24%>{c.BearingDeg:000}<pos=33%>{ScenarioText.RelativeBearing(q)}" +
                          (closing ? $"<pos=45%>{ScenarioText.Num(c.Cpa / 1852.0, 2)}<pos=60%>{ScenarioText.Num(c.Tcpa / 60.0, 1)}" : "<pos=45%>расходимся") +
                          $"<pos=74%>{(danger ? $"<color=#FFC85A>{situation}</color>" : situation)}";
            b.AppendLine(line);
        }
        if (sc != null && sc.hideTargetsOnPlotter) b.Append("<size=85%><color=#9AA6B2>У обучающегося CPA/TCPA целей скрыты (сценарий)</color></size>");
        return b.ToString().TrimEnd();
    }

    private void ShowScore()
    {
        if (assessment == null) { scoreText.text = "Оценка выключена."; return; }
        AssessmentResult r = assessment.Current();
        if (r == null) { scoreText.text = "<color=#9AA6B2>Оценка начнётся с началом упражнения.</color>"; return; }
        var b = new StringBuilder($"<b>{r.Headline()}</b>");
        string groups = DebriefText.GroupsLine(r);
        if (groups.Length > 0) b.Append($"\n<size=85%><color=#9AA6B2>{groups}</color></size>");
        List<DebriefViolation> v = DebriefText.Violations(r);
        for (int i = 0; i < Math.Min(3, v.Count); i++) b.Append($"\n<size=85%><color={(v[i].Severe ? "#FF7A6A" : "#FFC85A")}>•</color> {v[i].Text}</size>");
        if (v.Count > 3) b.Append($"\n<size=85%><color=#9AA6B2>… ещё {v.Count - 3}</color></size>");
        scoreText.text = b.ToString();
    }

    // ─────────── События ───────────
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ExerciseStarted || e.Type == ExerciseEventType.ScenarioLoaded)
        {
            if (e.Type == ExerciseEventType.ScenarioLoaded) events.Clear();
            if (scoreText != null) scoreText.text = "";
            tracks.ClearTrails();
            if (mapLive != null) mapLive.Failures.Clear();
        }
        if (e.Type == ExerciseEventType.EngineFailure || e.Type == ExerciseEventType.SteeringFailure)
            mapLive?.Failures.Add(new InstructorMapGraphic.Marker { Position = e.Position, Label = DebriefMap.FailureLabel(e.Type) });
        if (!Shown(e)) return;
        events.Add($"<color=#9AA6B2>{ScenarioText.Clock(e.Time)}</color>  <color={EventColor(e.Type)}>{e.Describe()}</color>");
        if (events.Count > 200) events.RemoveRange(0, events.Count - 200);
        eventsDirty = true;
    }

    private static bool Shown(in ExerciseEvent e)
    {
        switch (e.Type)
        {
            case ExerciseEventType.HelmStarted:
            case ExerciseEventType.HornStarted:
                return false;   // пара к HelmEnded / HornEnded
            case ExerciseEventType.ScenarioLoaded:
            case ExerciseEventType.EngineFailure:
            case ExerciseEventType.EngineRestored:
            case ExerciseEventType.SteeringFailure:
            case ExerciseEventType.SteeringRestored:
            case ExerciseEventType.Grounded:
            case ExerciseEventType.Collision:
            case ExerciseEventType.TrainerModeChanged:   // перед 19.2 (п. B)
                return true;    // и вне упражнения
            default:
                return e.InExercise;
        }
    }

    private static string EventColor(ExerciseEventType t)
    {
        switch (t)
        {
            case ExerciseEventType.Grounded:
            case ExerciseEventType.Collision:
            case ExerciseEventType.CorridorExit:
            case ExerciseEventType.ShoalWarningStarted:
                return "#FF7A6A";
            case ExerciseEventType.EngineFailure:
            case ExerciseEventType.SteeringFailure:
            case ExerciseEventType.EncounterStarted:
            case ExerciseEventType.PlotterRevealTargets:   // D2: обучающемуся показаны все цели
            case ExerciseEventType.TrainerModeChanged:     // перед 19.2 (п. B): режим тренажёра
                return "#FFC85A";
            case ExerciseEventType.EngineRestored:
            case ExerciseEventType.SteeringRestored:
            case ExerciseEventType.ExercisePaused:
            case ExerciseEventType.ExerciseResumed:
            case ExerciseEventType.TargetDetected:
            case ExerciseEventType.TargetSoundSignal:
                return "#9CC8FF";
            case ExerciseEventType.ExerciseStarted:
            case ExerciseEventType.ExerciseEnded:
                return "#6FE08A";
            default:
                return "#E6EDF3";
        }
    }

    // ─────────── Команды ───────────
    private void LoadSelected()
    {
        if (runner == null || runner.scenarios.Count == 0) return;
        ScenarioDefinition s = runner.scenarios[Mathf.Clamp(scenarioIndex, 0, runner.scenarios.Count - 1)];
        if (s != null) runner.Load(s);
    }

    // 21.1: «Обновить файлы» — сценарии из «Мои сценарии» заново; итог и проблемы по файлам — в «События»
    private void ReloadFiles()
    {
        ScenarioFiles files = ScenarioFiles.Instance;
        if (files == null) return;
        files.Reload();
        ScenarioLibraryReport rep = files.LastReport;
        if (rep == null) return;
        string clock = $"<color=#9AA6B2>{files.LastLoad:HH:mm:ss}</color>  ";
        events.Add(clock + $"<color={(rep.ErrorFiles > 0 ? "#FFC85A" : "#6FE08A")}>{rep.Summary()}</color>");
        int shown = 0;
        foreach (ScenarioLoadResult f in rep.Files)
        {
            if (f.Errors.Count == 0 && f.Warnings.Count == 0) continue;
            if (++shown > 6) { events.Add(clock + $"<color=#9AA6B2>… подробно — {ScenarioFolder.ReportFile}</color>"); break; }
            string first = f.Errors.Count > 0 ? f.Errors[0] : f.Warnings[0];
            int more = f.Errors.Count + f.Warnings.Count - 1;
            events.Add(clock + $"<color={(f.Errors.Count > 0 ? "#FF7A7A" : "#FFC85A")}>{(f.Errors.Count > 0 ? "не загружен" : "предупреждение")}: " +
                       $"{System.IO.Path.GetFileName(f.File)} — {first}{(more > 0 ? $" (и ещё {more})" : "")}</color>");
        }
        eventsDirty = true;
        int n = runner != null ? runner.scenarios.Count : 0;
        scenarioIndex = n > 0 ? Mathf.Clamp(scenarioIndex, 0, n - 1) : 0;
        nextRefresh = 0f;
    }

    /// <summary>21.4б: показать сценарий в выборе (после «Пройти в тренажёре» из редактора).</summary>
    public void ShowScenario(ScenarioDefinition s)
    {
        int i = runner != null ? runner.scenarios.IndexOf(s) : -1;
        if (i < 0) return;
        scenarioIndex = i;
        nextRefresh = 0f;
    }

    private void StepScenario(int dir)
    {
        int n = runner != null ? runner.scenarios.Count : 0;
        if (n == 0) return;
        scenarioIndex = ((scenarioIndex + dir) % n + n) % n;
        nextRefresh = 0f;
    }

    private void StepVisibility(int dir)
    {
        if (weather == null || inputs == null) return;
        int i = 0;
        float nm = weather.VisibilityOverrideM / 1852f;
        for (int k = 0; k < VisibilityStepsNm.Length; k++)
            if (Mathf.Abs(VisibilityStepsNm[k] - nm) < Mathf.Abs(VisibilityStepsNm[i] - nm)) i = k;
        i = Mathf.Clamp(i + dir, 0, VisibilityStepsNm.Length - 1);
        inputs.SetVisibility(VisibilityStepsNm[i] * 1852f);
    }

    private string VisibilityTitle() =>
        weather == null ? "Видимость: —" :
        weather.VisibilityOverrideM > 0 ? $"Видимость: {ScenarioText.Num(weather.VisibilityOverrideM / 1852f, 2)} мили" : "Видимость: по погоде";

    private ScenarioRunner.Phase Phase => runner != null ? runner.CurrentPhase : ScenarioRunner.Phase.Idle;

    // ─────────── Построение ───────────
    private void Build()
    {
        if (canvas != null) return;
        var go = new GameObject("InstructorCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);   // события мыши всплывают до этого компонента (масштаб, сдвиг карты)
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // Expand: холст не меньше 1920 × 1080 по обеим осям — при узком или высоком Game View окно
        // уменьшается целиком, а не обрезается справа (первая проверка D1: столбец справа не помещался)
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        root = go.GetComponent<RectTransform>();

        BuildBar();
        BuildMap();
        BuildColumn();

        // Режим зеркала (вид шлема на весь монитор): кнопка возврата и подсказка поверх зеркала
        float bx = 12f;
        mirrorBack = Button(root, ref bx, -10f, 230f, "«  Окно инструктора", ButtonColor,
                            () => station.SetMode(InstructorStation.DisplayMode.Instructor));
        mirrorHint = MakeText(root, "Вид из шлема. Окно инструктора — кнопка слева, Esc или правый клик мыши; в шлеме — меню Y → «Монитор инструктора»",
                              17f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.MidlineLeft);
        Place(mirrorHint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(bx + 6f, -10f), new Vector2(1100f, RowHeight), new Vector2(0f, 1f));
    }

    private void BuildBar()
    {
        bar = Panel(root, "TopBar", BarColor);
        Place(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, TopBar), new Vector2(0.5f, 1f));
        float x = Pad, y = -(TopBar - RowHeight) * 0.5f;
        Button(bar, ref x, y, 40f, "<", NeutralColor, () => StepScenario(-1));
        scenarioLabel = MakeText(bar, "", 17f, Color.white, TextAlignmentOptions.Left);
        Place(scenarioLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(240f, RowHeight), new Vector2(0f, 1f));
        x += 240f + Gap;   // было 300 — место под кнопку режима (перед 19.2, п. B)
        Button(bar, ref x, y, 40f, ">", NeutralColor, () => StepScenario(+1));
        Button(bar, ref x, y, 120f, "Загрузить", ButtonColor, LoadSelected, null, null, () => Phase != ScenarioRunner.Phase.Running);

        var idLabel = MakeText(bar, "ID:", 17f, MutedColor, TextAlignmentOptions.Right);
        Place(idLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(34f, RowHeight), new Vector2(0f, 1f));
        x += 34f + Gap;
        idField = InputField(bar, new Vector2(x, y), new Vector2(130f, RowHeight));
        x += 130f + 2f * Gap;

        Button(bar, ref x, y, 110f, "Начать", StartColor, () => runner.StartExercise(), null, null, () => Phase == ScenarioRunner.Phase.Briefing);
        Button(bar, ref x, y, 130f, "", ButtonColor, () => runner.TogglePause(),
               () => runner != null && runner.IsPaused ? "Продолжить" : "Пауза", () => runner != null && runner.IsPaused ? ActiveColor : ButtonColor,
               () => Phase == ScenarioRunner.Phase.Running);
        Button(bar, ref x, y, 130f, "Перезапуск", NeutralColor, () => runner.Restart(), null, null, () => runner != null && runner.Current != null && Phase != ScenarioRunner.Phase.Idle);
        Button(bar, ref x, y, 130f, "Завершить", StopColor, () => runner.Abort(), null, null, () => Phase == ScenarioRunner.Phase.Running);
        Button(bar, ref x, y, 190f, "Свободное плавание", NeutralColor, () => runner.FreeSail(), null, null, () => runner != null);
        // Перед 19.2 (п. B): режим тренажёра по кругу экзамен → обучение → инструктор (в журнал упражнения)
        Button(bar, ref x, y, 150f, "", NeutralColor, () => TrainerModeSwitch.Instance.Cycle(byInstructor: true),
               () => $"Режим: {TrainerModes.Title(TrainerModes.Current)}",
               () => TrainerModes.Current == TrainerMode.Exam ? StartColor : TrainerModes.Current == TrainerMode.Instructor ? WarnColor : ButtonColor,
               () => TrainerModeSwitch.Instance != null);

        statusText = MakeText(bar, "", 20f, Color.white, TextAlignmentOptions.Right);
        Place(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -4f), new Vector2(380f, TopBar - 6f), new Vector2(1f, 1f));
    }

    private void BuildMap()
    {
        var holder = new GameObject("Map", typeof(RectTransform), typeof(CanvasRenderer));
        holder.transform.SetParent(root, false);
        mapRect = holder.GetComponent<RectTransform>();
        mapRect.anchorMin = Vector2.zero;
        mapRect.anchorMax = Vector2.one;
        mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.offsetMin = new Vector2(Pad, Pad);
        mapRect.offsetMax = new Vector2(-(ColumnWidth + Pad), -(TopBar + Pad));
        holder.AddComponent<RectMask2D>();
        var hit = holder.AddComponent<Image>();   // ловит мышь (колесо, перетаскивание); прозрачный
        hit.color = new Color(0f, 0f, 0f, 0f);

        mapStatic = Layer<DebriefMapGraphic>("MapStatic");
        mapLive = Layer<InstructorMapGraphic>("MapLive");

        mapControls = Panel(mapRect, "MapControls", new Color(0f, 0f, 0f, 0f));
        Place(mapControls, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -8f), new Vector2(1130f, RowHeight), new Vector2(0f, 1f));
        float x = 0f;
        Button(mapControls, ref x, 0f, 44f, "+", NeutralColor, () => Zoom(1.5f, mapRect.rect.center));
        Button(mapControls, ref x, 0f, 44f, "−", NeutralColor, () => Zoom(1f / 1.5f, mapRect.rect.center));
        Button(mapControls, ref x, 0f, 140f, "За судном", NeutralColor, () =>
        {
            if (navigation != null && navigation.Model != null) viewport.FollowShip(navigation.PositionEastNorth);
            nextRefresh = 0f;
        }, null, () => viewport.Follow ? ActiveColor : NeutralColor);
        Button(mapControls, ref x, 0f, 160f, "Весь маршрут", NeutralColor, () => { FitRoute(); nextRefresh = 0f; },
               null, null, () => runner != null && runner.Current != null);
        Button(mapControls, ref x, 0f, 130f, "Вид из шлема", NeutralColor, () => station.TogglePip(),
               null, () => station.Pip != null && station.Pip.Active ? ActiveColor : NeutralColor);
        // D2: режим «все цели» на планшете обучающегося (пишется в журнал упражнения)
        Button(mapControls, ref x, 0f, 210f, "", NeutralColor, () => plotter.SetRevealAll(!plotter.RevealAll),
               () => plotter != null && plotter.RevealAll ? "Планшет: все цели" : "Планшет: обнаруженные",
               () => plotter != null && plotter.RevealAll ? WarnColor : NeutralColor, () => plotter != null);
        // 19.2 (A): вид шлема на весь монитор (зеркало XR — полная частота, без лишней отрисовки)
        Button(mapControls, ref x, 0f, 190f, "Шлем на весь экран", NeutralColor,
               () => station.SetMode(InstructorStation.DisplayMode.Mirror));
        // 21.1: сценарии из папки «Мои сценарии» — загрузить заново (итог и ошибки — в «События»)
        Button(mapControls, ref x, 0f, 155f, "Обновить файлы", NeutralColor, ReloadFiles, null,
               () => ScenarioFiles.Instance != null && ScenarioFiles.Instance.LastReport != null && ScenarioFiles.Instance.LastReport.ErrorFiles > 0 ? WarnColor : NeutralColor,
               () => ScenarioFiles.Instance != null);

        scaleText = MakeText(mapRect, "", 14f, MutedColor, TextAlignmentOptions.BottomLeft);
        Place(scaleText.rectTransform, Vector2.zero, Vector2.zero, new Vector2(10f, 8f), new Vector2(900f, 24f), Vector2.zero);

        var pip = new GameObject("Pip", typeof(RectTransform));
        pip.transform.SetParent(mapRect, false);
        pipImage = pip.AddComponent<RawImage>();
        pipImage.raycastTarget = false;
        pipBaseTexture = new Vector2Int(station.Pip != null ? station.Pip.width : 480, station.Pip != null ? station.Pip.height : 270);
        pipBase = new Vector2(pipBaseTexture.x, pipBaseTexture.y);
        Place(pipImage.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-8f, 8f), pipBase, new Vector2(1f, 0f));
        pip.SetActive(false);

        // 19.2 (A): над PiP — размер [−] [+] и частота обновления
        pipPanel = Panel(mapRect, "PipControls", new Color(0f, 0f, 0f, 0.55f));
        Place(pipPanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-8f, 8f + pipBase.y + 4f),
              new Vector2(44f * 2f + 170f + 90f + 3f * Gap, RowHeight), new Vector2(1f, 0f));
        float px = 0f;
        Button(pipPanel, ref px, 0f, 44f, "−", NeutralColor, () => StepPip(-1));
        Button(pipPanel, ref px, 0f, 44f, "+", NeutralColor, () => StepPip(+1));
        pipSizeText = MakeText(pipPanel, "", 15f, MutedColor, TextAlignmentOptions.Center);
        Place(pipSizeText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(px, 0f), new Vector2(170f, RowHeight), new Vector2(0f, 1f));
        px += 170f + Gap;
        Button(pipPanel, ref px, 0f, 90f, "", NeutralColor, TogglePipRate,
               () => station.Pip != null ? $"{station.Pip.rateHz:F0} Гц" : "—");
        pipPanel.gameObject.SetActive(false);
    }

    private T Layer<T>(string name) where T : DebriefMapGraphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(mapRect, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        T g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private void BuildColumn()
    {
        column = Panel(root, "Column", PanelColor);
        column.anchorMin = new Vector2(1f, 0f);
        column.anchorMax = new Vector2(1f, 1f);
        column.pivot = new Vector2(1f, 1f);
        column.sizeDelta = new Vector2(ColumnWidth, -TopBar);
        column.anchoredPosition = new Vector2(0f, -TopBar);
        float w = ColumnWidth - 2f * Pad;
        float y = -Pad;

        Header("Своё судно и маршрут", ref y);
        stateText = Block(ref y, 168f, 17f);
        Header("Цели", ref y);
        targetsText = Block(ref y, 126f, 16f);

        Header("Вводные и обстановка", ref y);
        float x = Pad;
        float third = (w - 2f * Gap) / 3f;
        Button(column, ref x, y, third, "", WarnColor, () => inputs.SetEngineFailure(!inputs.EngineFailed),
               () => inputs != null && inputs.EngineFailed ? "Устранить отказ ГД" : "Отказ главного двигателя",
               () => inputs != null && inputs.EngineFailed ? StopColor : WarnColor, () => inputs != null && (inputs.EngineFailed || inputs.CanApply));
        Button(column, ref x, y, third, "", WarnColor, () => inputs.SetSteeringFailure(!inputs.SteeringFailed),
               () => inputs != null && inputs.SteeringFailed ? "Устранить отказ руля" : "Отказ рулевого устройства",
               () => inputs != null && inputs.SteeringFailed ? StopColor : WarnColor, () => inputs != null && (inputs.SteeringFailed || inputs.CanApply));
        Button(column, ref x, y, third, "Квитировать", NeutralColor, () => inputs.Acknowledge(), null, null, () => inputs != null && inputs.AlarmSounding);
        y -= RowHeight + Gap;

        x = Pad;
        Button(column, ref x, y, 44f, "<", NeutralColor, () => StepVisibility(-1));
        Button(column, ref x, y, w * 0.5f - 2f * (44f + Gap), "", NeutralColor, null, VisibilityTitle);
        Button(column, ref x, y, 44f, ">", NeutralColor, () => StepVisibility(+1));
        Button(column, ref x, y, w - (x - Pad), "", NeutralColor, () => inputs.SetAnnounce(!inputs.announceFailures),
               () => inputs != null && inputs.announceFailures ? "Оповещение об отказах: вкл" : "Оповещение об отказах: выкл",
               () => inputs != null && inputs.announceFailures ? ActiveColor : NeutralColor, () => inputs != null);
        y -= RowHeight + Gap;

        x = Pad;
        float quarter = (w - 3f * Gap) / 4f;
        foreach (WeatherPresetId id in new[] { WeatherPresetId.Calm, WeatherPresetId.Moderate, WeatherPresetId.Storm, WeatherPresetId.Night })
        {
            WeatherPresetId p = id;
            Button(column, ref x, y, quarter, ScenarioText.WeatherTitle(p), NeutralColor, () => weather.SetWeather(p), null,
                   () => weather != null && weather.CurrentPresetId == p ? ActiveColor : NeutralColor, () => weather != null);
        }
        y -= RowHeight + Gap;
        x = Pad;
        foreach (TimeOfDay t in new[] { TimeOfDay.Day, TimeOfDay.Twilight, TimeOfDay.Night })
        {
            TimeOfDay tod = t;
            Button(column, ref x, y, third, ScenarioText.TimeOfDayTitle(tod), NeutralColor, () => weather.SetTimeOfDay(tod), null,
                   () => weather != null && weather.CurrentTimeOfDay == tod ? ActiveColor : NeutralColor, () => weather != null);
        }
        y -= RowHeight + Gap;

        Header("События", ref y);
        eventsText = Block(ref y, 262f, 15f);
        x = Pad;
        Button(column, ref x, y, 200f, "Текущий балл", ButtonColor, ShowScore, null, null, () => assessment != null && Phase != ScenarioRunner.Phase.Idle);
        // 21.2а: редактор сценариев и карт — не во время упражнения (тренажёр выгружается, обратно — заново)
        float ex = Pad + w - 220f;
        Button(column, ref ex, y, 220f, "Редактор сценариев", NeutralColor, TrainerScenes.OpenEditor, null, null,
               () => Phase != ScenarioRunner.Phase.Running);
        scoreText = MakeText(column, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        Place(scoreText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w - (x - Pad) - 226f, 110f), new Vector2(0f, 1f));
    }

    // ─────────── Элементы ───────────
    private void Header(string text, ref float y)
    {
        TextMeshProUGUI t = MakeText(column, text, 18f, TitleColor, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Bold;
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(ColumnWidth - 2f * Pad, 26f), new Vector2(0f, 1f));
        y -= 26f;
    }

    private TextMeshProUGUI Block(ref float y, float height, float size)
    {
        TextMeshProUGUI t = MakeText(column, "", size, Color.white, TextAlignmentOptions.TopLeft);
        t.overflowMode = TextOverflowModes.Truncate;
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(ColumnWidth - 2f * Pad, height), new Vector2(0f, 1f));
        y -= height + Gap;
        return t;
    }

    private static RectTransform Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = color.a > 0f;
        return go.GetComponent<RectTransform>();
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
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

    /// <summary>Кнопка в строке (x — от левого края родителя, растёт; y — от верха). text/color/enabled — обновляются с окном. Возвращает кнопку.</summary>
    private RectTransform Button(RectTransform parent, ref float x, float y, float width, string label, Color color, UnityAction action,
                        Func<string> text = null, Func<Color> dynColor = null, Func<bool> enabled = null)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (action != null)
            btn.onClick.AddListener(() =>
            {
                if (enabled != null && !enabled()) return;
                try { action(); }
                catch (Exception e) { Debug.LogException(e, this); }
                nextRefresh = 0f;
            });
        else btn.interactable = false;
        Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, RowHeight), new Vector2(0f, 1f));
        TextMeshProUGUI t = MakeText(go.transform, label, 16f, Color.white, TextAlignmentOptions.Center);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 0f);
        trt.offsetMax = new Vector2(-6f, 0f);
        if (text != null || dynColor != null || enabled != null)
            dynamicButtons.Add((t, img, text ?? (() => label), dynColor ?? (() => color), enabled));
        x += width + Gap;
        return go.GetComponent<RectTransform>();
    }

    private TMP_InputField InputField(RectTransform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("TraineeId", typeof(RectTransform));
        go.SetActive(false);   // поле включается, когда назначены текст и область (иначе TMP_InputField ругается в OnEnable)
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.14f, 0.16f, 0.2f);
        Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, new Vector2(0f, 1f));

        var area = new GameObject("TextArea", typeof(RectTransform));
        area.transform.SetParent(go.transform, false);
        area.AddComponent<RectMask2D>();
        RectTransform art = area.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(8f, 4f);
        art.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI placeholder = MakeText(area.transform, "цифры", 17f, new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Left);
        TextMeshProUGUI text = MakeText(area.transform, "", 17f, Color.white, TextAlignmentOptions.Left);
        foreach (TextMeshProUGUI t in new[] { placeholder, text })
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = art;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = img;
        field.contentType = TMP_InputField.ContentType.IntegerNumber;
        field.characterLimit = 8;
        field.onSelect.AddListener(_ => InstructorStation.TextInputFocused = true);
        field.onDeselect.AddListener(_ => InstructorStation.TextInputFocused = false);
        field.onEndEdit.AddListener(v =>
        {
            InstructorStation.TextInputFocused = false;
            if (recorder != null) recorder.TraineeId = v;
        });
        go.SetActive(true);
        return field;
    }
}
