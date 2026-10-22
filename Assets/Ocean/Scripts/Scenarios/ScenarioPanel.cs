// ============================================================
// ScenarioPanel.cs — VR-панель сценария и команды меню мостика (B2)
//
// Без клавиатуры (решение пользователя в B1): всё управление сценарием —
// через эту панель и меню на кнопке A (группа «Сценарий», вверху меню).
// Режимы панели:
//   • выбор — список сценариев библиотеки ScenarioRunner и «Свободное плавание»;
//   • брифинг — название, текст, условия (погода, начальные условия, лимит,
//     условия завершения), кнопки «К упражнению» / «Начать сразу» / «К выбору»;
//   • 21.5в: пауза перед началом — табличка «ПАУЗА» («Начать», «Брифинг»): после брифинга упражнение по умолчанию
//     стоит (фаза Briefing), пока не нажата «Начать»; табличка на 0,3 м ниже места брифинга;
//   • результаты — предварительный итог (ExerciseSummary; полноценно — C3),
//     «Перезапустить» / «К выбору» / «Закрыть».
// C1: на брифинге — ID обучающегося (обезличенный код) и цифровая
// VR-клавиатура «Ввести ID» → ExerciseRecorder.TraineeId; на результатах —
// папка записанной сессии.
// Панель появляется перед игроком и закреплена в системе судна (объект —
// дочерний к Ship), как BridgeMenu. Строится в коде, сцену не правит.
// B3: при запуске панель выбора встаёт в фиксированную точку startAnchor (объект
// ScenarioPanelAnchor, дочерний к Ship; двигается мышью в Scene View), а не туда,
// куда смотрит камера в момент загрузки; из меню — по-прежнему перед игроком.
// На результатах провал (мель, столкновение) выделен красным (ExerciseFailureNotice).
// C2: на результатах (в том числе при досрочном завершении) — оценка: балл, зачёт/незачёт и
// список критериев (ExerciseAssessmentMonitor). Меню «Параметры» (Y) → «Текущий балл» — промежуточная
// оценка во время упражнения и опасные цели (CPA/TCPA).
// C3: результаты — разбор на трёх вкладках (высота панели ограничена):
//   «Итог» — причина завершения, оценка и почему незачёт, баллы групп, сближения,
//     нарушения с пояснениями и правилами МППСС (DebriefText);
//   «Критерии» — таблица «значение / порог / результат» по страницам;
//   «Схема» — траектория вида сверху по записанной сессии (DebriefMapGraphic):
//     весь маршрут и окна сближений, переключатель «< … >». Схема и report.html
//     строятся в фоне после закрытия записи (ExerciseRecorder.LastDebrief) —
//     до готовности «Схема строится…», затем панель обновляется сама.
// Запрос 17: вкладка «Графики» — курс, скорость, XTE, руль по времени по записи
//   (DebriefPlotGraphic; те же ряды, что в report.html — DebriefPlots), по одному
//   графику на всё поле схемы, листание «< курс / скорость / XTE / руль >».
// Команды меню: «Выбор сценария…», «Начать упражнение», «Брифинг»/«Результаты»,
// «Перезапустить сценарий» (инстр.), «Завершить досрочно» (инстр.); недоступные
// по фазе — скрыты. Отладочные команды B1 остаются в группе «Отладка» (внизу).
// 17.10.2026 (по проверке 19-03): над панелью — ручка «переместить» (PanelDragHandle: взять лучом или рукой,
// панель идёт за контроллером, отпустили — остаётся); повторный вызов разбора из меню (A «Результаты»,
// Y «Оценка (итог)») открывает ту же вкладку и страницу (ReopenResults).
// К1 (22.10.2026): брифинг — на трёх панелях дугой вокруг головы: в центре — название (32 px), текст брифинга (24 px;
// длинный — страницами «<  Назад / Вперёд  >»), ID и кнопки; слева — «Судно и задание» (ExerciseTasks.BriefingShip), справа — «Условия
// и оценка» (BriefingConditions, обязательные критерии — BriefingMandatoryText), 22 px. Боковые панели — дочерние
// к холсту (ручка «переместить» двигает все три), повёрнуты к голове; угол — по ширине панелей и расстоянию до головы.
// По проверке 22.10.2026: три панели одной ширины (680 px), верхние края на одной линии, верхняя часть одной высоты
// (по самой высокой из трёх); ниже неё у центральной — страницы, ID и кнопки.
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ScenarioPanel : MonoBehaviour
{
    private enum Mode { Hidden, Selection, Briefing, Results, Keypad, CurrentScore, Ready }
    private enum DebriefTab { Summary, Criteria, Map, Plots }

    public ScenarioRunner runner;
    [Tooltip("Запись упражнения (C1): ID обучающегося, папка сессии. Пусто — найти")]
    public ExerciseRecorder recorder;
    [Tooltip("Оценка (C2): итог и текущий балл. Пусто — найти")]
    public ExerciseAssessmentMonitor assessment;
    [Tooltip("Наибольшая длина ID обучающегося, цифр")]
    public int traineeIdMaxLength = 8;

    [Header("Запуск")]
    [Tooltip("При входе в Play Mode показать выбор сценария")]
    public bool showSelectionOnStart = true;
    [Tooltip("Задержка показа, с (трекинг шлема должен успеть встать)")]
    public float startDelay = 1.5f;
    [Tooltip("Точка панели выбора при запуске (B3). Пусто — перед игроком")]
    public Transform startAnchor;

    [Header("Положение панели")]
    public float distanceFromHead = 0.95f;
    public float downOffset = 0.12f;
    [Tooltip("Масштаб холста, м на пиксель")]
    public float canvasScale = 0.001f;

    [Header("Брифинг на трёх панелях (К1)")]
    [Tooltip("Слева — судно и задание, справа — условия и оценка (false — всё на одной панели, как до К1)")]
    public bool briefingSidePanels = true;
    [Tooltip("Зазор между панелями по углу, градусы")]
    public float sidePanelGapDeg = 2f;
    [Tooltip("Шрифт: название, текст брифинга, боковые панели, px (до К1 — 25 / 18 / 16)")]
    public float briefingTitleSize = 32f, briefingTextSize = 24f, briefingSideTextSize = 22f;
    [Tooltip("Наибольшая высота текста брифинга на странице, px (длиннее — страницы «<» «>»)")]
    public float briefingPageHeight = 900f;

    [Header("Разбор (C3)")]
    [Tooltip("Строк таблицы критериев на странице")]
    [Range(4, 20)] public int criteriaPerPage = 10;
    [Tooltip("Высота схемы траектории, пикселей холста (ширина — вся панель)")]
    public float mapHeight = 500f;
    [Tooltip("Нарушений на вкладке «Итог» (остальные — на вкладке «Критерии»)")]
    [Range(1, 20)] public int maxViolationsOnSummary = 6;
    [Tooltip("Ширины столбцов таблицы критериев (доли): отметка, критерий, значение, порог")]
    public float[] criteriaColumns = { 0.05f, 0.6f, 0.17f, 0.18f };

    private const string MenuGroup = "Сценарий";
    private const float Width = 680f, Pad = 22f, ButtonHeight = 48f, Gap = 8f;
    private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.96f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    private static readonly Color StartColor = new Color(0.16f, 0.55f, 0.3f);
    private static readonly Color NeutralColor = new Color(0.33f, 0.35f, 0.4f);
    private static readonly Color TitleColor = new Color(0.85f, 0.92f, 1f);
    private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.75f);
    private static readonly Color WarnColor = new Color(1f, 0.78f, 0.35f);
    private static readonly Color ActiveTabColor = new Color(0.3f, 0.52f, 0.8f);

    private Mode mode = Mode.Hidden;
    private GameObject panel;
    private Canvas canvas;
    private RectTransform background;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private ScenarioRunner subscribed;
    private string keypadBuffer = "";
    private bool placeAtAnchor;
    private DebriefTab tab;
    private int criteriaPage, mapView, plotView;
    private bool waitingDebrief;
    // 17.10.2026: ручка «переместить» над панелью; разбор, для которого запомнены вкладка и страницы (повторный вызов)
    private PanelDragHandle dragHandle;
    private ExerciseSummary debriefFor;
    // 21.5в: табличка «ПАУЗА» опущена относительно места брифинга (не заслонять вид вперёд)
    private bool readyLowered;
    private const float ReadyDropM = 0.3f;
    // К1: боковые панели брифинга и страница текста брифинга
    private readonly List<GameObject> sidePanels = new List<GameObject>();
    private int briefingPage;
    private ScenarioDefinition briefingFor;

    public bool IsOpen => canvas != null && canvas.enabled;

    private ScenarioRunner.Phase Phase => runner != null ? runner.CurrentPhase : ScenarioRunner.Phase.Idle;

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder(MenuGroup, -100);
        BridgeMenu.SetGroupExpanded(MenuGroup, true);   // D1: команды сценария — на главной странице меню, остальное — в подменю
        BridgeMenu.SetGroupOrder("Отладка", 100);
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Выбор сценария…", ShowSelection,
            () => runner != null && Phase != ScenarioRunner.Phase.Running, hideWhenUnavailable: true).OpensWindow());   // D1: открывает панель — меню закрывается
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Начать упражнение", () => runner.StartExercise(),
            () => Phase == ScenarioRunner.Phase.Briefing, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup,
            () => Phase == ScenarioRunner.Phase.Finished ? "Результаты" : "Брифинг",
            () => { if (Phase == ScenarioRunner.Phase.Finished) ReopenResults(); else ShowBriefing(); },
            () => runner != null && runner.Current != null && Phase != ScenarioRunner.Phase.Idle, hideWhenUnavailable: true).OpensWindow());
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Перезапустить сценарий", () => runner.Restart(),
            () => runner != null && runner.Current != null && Phase != ScenarioRunner.Phase.Idle,
            instructorOnly: true, hideWhenUnavailable: true).OpensWindow());   // брифинг
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Завершить досрочно", () => runner.Abort(),
            () => Phase == ScenarioRunner.Phase.Running, instructorOnly: true, hideWhenUnavailable: true).OpensWindow());   // разбор
        menuCommands.Add(BridgeMenu.Register(MenuGroup, () => Phase == ScenarioRunner.Phase.Finished ? "Оценка (итог)" : "Текущий балл",
            () => { if (Phase == ScenarioRunner.Phase.Finished) ReopenResults(); else ShowCurrentScore(); },
            () => assessment != null && assessment.assess && (Phase == ScenarioRunner.Phase.Running || Phase == ScenarioRunner.Phase.Finished),
            hideWhenUnavailable: true).OpensWindow().InMenu(BridgeMenu.SettingsMenuId));
        Subscribe();
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
        if (subscribed != null) subscribed.StateChanged -= OnStateChanged;
        subscribed = null;
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (recorder == null) recorder = ExerciseRecorder.Instance != null ? ExerciseRecorder.Instance : FindAnyObjectByType<ExerciseRecorder>();
        if (assessment == null) assessment = ExerciseAssessmentMonitor.Instance != null ? ExerciseAssessmentMonitor.Instance : FindAnyObjectByType<ExerciseAssessmentMonitor>();
        Subscribe();
        BuildCanvas();
        if (showSelectionOnStart) StartCoroutine(ShowSelectionDelayed());
    }

    private void Subscribe()
    {
        if (runner == null || subscribed == runner) return;
        if (subscribed != null) subscribed.StateChanged -= OnStateChanged;
        runner.StateChanged += OnStateChanged;
        subscribed = runner;
    }

    private IEnumerator ShowSelectionDelayed()
    {
        yield return new WaitForSecondsRealtime(startDelay);
        if (Phase == ScenarioRunner.Phase.Idle && !IsOpen)
        {
            placeAtAnchor = startAnchor != null;
            ShowSelection();
            placeAtAnchor = false;
        }
    }

    private void OnStateChanged()
    {
        switch (Phase)
        {
            case ScenarioRunner.Phase.Briefing: ShowBriefing(); break;
            case ScenarioRunner.Phase.Finished: ShowResults(); break;
            default: Hide(); break;
        }
    }

    // ─────────── Режимы ───────────
    [ContextMenu("Показать выбор сценария")]
    public void ShowSelection()
    {
        if (runner == null) return;
        BeginContent(Mode.Selection);
        float y = Top;
        Title("Выбор упражнения", ref y);
        Text("Выберите сценарий. После выбора судно встанет в начальную точку и откроется брифинг.", 17f, MutedColor, ref y);
        y -= Gap;

        if (runner.scenarios.Count == 0)
            Text("В библиотеке нет сценариев (Tools/VR Bridge/Setup B2 Scenarios).", 18f, MutedColor, ref y);
        foreach (ScenarioDefinition s in runner.scenarios)
        {
            if (s == null) continue;
            ScenarioDefinition sc = s;
            string limit = s.timeLimitSeconds > 0 ? $" · {ScenarioText.Num(s.timeLimitSeconds / 60.0)} мин" : "";
            string label = $"<b>{s.id}</b>  {s.title}\n<size=75%><color=#C8D2DC>{ScenarioText.Conditions(s.weather, s.timeOfDay)}{limit} · {(s.IsMooring ? "швартовка" : $"маршрут {ScenarioText.Num(s.RouteLength / 1000.0, 1)} км")}{(s.IsFromFile ? " · из файла" : "")}</color></size>";
            ButtonRow(ref y, 66f, (label, ButtonColor, () => runner.Load(sc)));
        }
        y -= Gap;
        ButtonRow(ref y, ButtonHeight,
            ("Свободное плавание", NeutralColor, () => { runner.FreeSail(); Hide(); }),
            ("Закрыть", NeutralColor, Hide));
        EndContent(y);
    }

    public void ShowBriefing()
    {
        ScenarioDefinition s = runner != null ? runner.Current : null;
        if (s == null) return;
        if (IsOpen && mode == Mode.Ready && readyLowered) { panel.transform.position += panel.transform.up * ReadyDropM; readyLowered = false; }
        if (mode != Mode.Briefing || briefingFor != s) briefingPage = 0;   // К1: другой сценарий или вход в брифинг — с первой страницы
        briefingFor = s;
        BeginContent(Mode.Briefing);
        float y = Top;
        if (!briefingSidePanels)
        {
            Title($"{s.id}. {s.title}", ref y);
            Text(s.briefing, 18f, Color.white, ref y);
            y -= Gap;
            Text(ExerciseTasks.BriefingFacts(s), 16f, MutedColor, ref y);   // тот же текст — вкладка «Задание» планшета (D2)
            y -= Gap;
        }
        else
        {
            Title($"{s.id}. {s.title}", ref y, briefingTitleSize);
            List<string> pages = BriefingPages(s.briefing, Width - 2f * Pad, briefingTextSize);
            briefingPage = Mathf.Clamp(briefingPage, 0, pages.Count - 1);
            Text(pages[briefingPage], briefingTextSize, Color.white, ref y);

            // По проверке 22.10.2026: три панели одной ширины, верхние края на одной линии, верхняя часть — одной высоты
            // (по самой высокой); ниже неё у центральной — страницы, ID и кнопки
            leftSide = SidePanel("BriefingLeft", "Судно и задание", ExerciseTasks.BriefingShip(s), out float yLeft);
            rightSide = SidePanel("BriefingRight", "Условия и оценка",
                ExerciseTasks.BriefingConditions(s) + "\n\n" + ExerciseTasks.BriefingMandatoryText(s), out float yRight);
            y = Mathf.Min(y, Mathf.Min(yLeft, yRight));
            upperHeight = -y + Pad;
            y -= Pad;   // низ боковых панелей — на уровне верха кнопок страниц / строки ID
            if (pages.Count > 1)   // стрелок ◀ ▶ в шрифте LiberationSans нет — «<» «>»
                ButtonRow(ref y, ButtonHeight,
                    ("<  Назад", NeutralColor, () => { briefingPage = Mathf.Max(0, briefingPage - 1); ShowBriefing(); }),
                    ($"Стр. {briefingPage + 1} из {pages.Count}", NeutralColor, null),
                    ("Вперёд  >", NeutralColor, () => { briefingPage = Mathf.Min(pages.Count - 1, briefingPage + 1); ShowBriefing(); }));
            y -= Gap;
        }

        if (Phase == ScenarioRunner.Phase.Briefing)
        {
            TraineeRow(ref y);
            Text("«К упражнению» — пауза: судно и цели стоят, время не идёт; осмотритесь, возьмите управление и нажмите «Начать» на табличке «ПАУЗА» " +
                 "(или меню A «Начать упражнение»). Начальные условия будут поставлены заново.", 15f, MutedColor, ref y);   // 21.5в
            ButtonRow(ref y, ButtonHeight + 6f,
                ("К упражнению", StartColor, ShowReady),
                ("Начать сразу", ButtonColor, () => runner.StartExercise()),
                ("К выбору", NeutralColor, ShowSelection));
        }
        else
            ButtonRow(ref y, ButtonHeight, ("Закрыть", NeutralColor, Hide));
        float height = EndContent(y);
        if (briefingSidePanels)
        {
            PlaceSide(leftSide, -1, upperHeight, height);
            PlaceSide(rightSide, +1, upperHeight, height);
        }
    }

    private RectTransform leftSide, rightSide;
    private float upperHeight;

    // ─────────── К1: брифинг на трёх панелях ───────────

    /// <summary>Текст брифинга по страницам: абзацы подряд, пока высота не больше briefingPageHeight (длинный абзац — своя страница).</summary>
    private List<string> BriefingPages(string text, float width, float size)
    {
        var pages = new List<string>();
        text = text ?? "";
        if (TextHeight(text, width, size) <= briefingPageHeight) { pages.Add(text); return pages; }
        var page = new StringBuilder();
        foreach (string para in text.Split('\n'))
        {
            string tryText = page.Length == 0 ? para : page + "\n" + para;
            if (page.Length > 0 && TextHeight(tryText, width, size) > briefingPageHeight)
            {
                pages.Add(page.ToString());
                page.Clear();
                page.Append(para);
            }
            else
            {
                page.Clear();
                page.Append(tryText);
            }
        }
        if (page.Length > 0 || pages.Count == 0) pages.Add(page.ToString());
        return pages;
    }

    private TextMeshProUGUI measure;

    private float TextHeight(string text, float width, float size)
    {
        if (measure == null)
        {
            // Невидимый образец для замера (пустой текст: GetPreferredValues не меняет показанное)
            var go = new GameObject("Measure", typeof(RectTransform));
            go.transform.SetParent(panel.transform, false);
            measure = go.AddComponent<TextMeshProUGUI>();
            measure.textWrappingMode = TextWrappingModes.Normal;
            measure.raycastTarget = false;
            measure.color = new Color(1f, 1f, 1f, 0f);
            measure.text = "";
        }
        measure.fontSize = size;
        return measure.GetPreferredValues(text, width, 0f).y;
    }

    /// <summary>Боковая панель брифинга шириной с центральную: заголовок и текст; yBottom — низ текста (как y центральной панели).</summary>
    private RectTransform SidePanel(string name, string title, string text, out float yBottom)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panel.transform, false);
        sidePanels.Add(go);
        var bg = go.AddComponent<Image>();
        bg.color = PanelColor;
        bg.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 1f);

        float w = Width - 2f * Pad;
        float y = -Pad;
        TextIn(rt, w, title, briefingSideTextSize + 4f, TitleColor, ref y, FontStyles.Bold);
        y -= 4f;
        TextIn(rt, w, text, briefingSideTextSize, Color.white, ref y, FontStyles.Normal);
        yBottom = y;
        return rt;
    }

    /// <summary>
    /// Боковая панель на дуге вокруг головы: side −1 — слева, +1 — справа; радиус — расстояние от головы до центральной
    /// панели (0,7–1,6 м), повёрнута к голове; высота — верхняя часть центральной панели, верхний край — на одной линии с ней
    /// (centreHeight — полная высота центральной, она отцентрована в точке холста).
    /// </summary>
    private void PlaceSide(RectTransform rt, int side, float height, float centreHeight)
    {
        if (rt == null) return;
        rt.sizeDelta = new Vector2(Width, height);

        // Голова в системе холста (пиксели); дуга — в плоскости холста по горизонтали
        Vector3 head = new Vector3(0f, 0f, -distanceFromHead / canvasScale);
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 h = panel.transform.InverseTransformPoint(cam.transform.position);
            float r = new Vector2(h.x, h.z).magnitude * canvasScale;
            if (h.z < 0f && r >= 0.7f && r <= 1.6f) head = new Vector3(h.x, 0f, h.z);
        }
        Vector3 toCentre = -head;
        float radius = toCentre.magnitude;
        float angle = 2f * Mathf.Atan2(0.5f * Width, radius) * Mathf.Rad2Deg + sidePanelGapDeg;
        Quaternion turn = Quaternion.Euler(0f, side * angle, 0f);
        Vector3 centre = head + turn * toCentre;
        rt.localPosition = centre + Vector3.up * (centreHeight * 0.5f);   // pivot сверху: верхний край — как у центральной
        rt.localRotation = Quaternion.LookRotation(centre - head, Vector3.up);
    }

    private void ClearSidePanels()
    {
        foreach (GameObject g in sidePanels) if (g != null) Destroy(g);
        sidePanels.Clear();
    }

    // ─────────── Разбор (C3): итог, критерии, схема ───────────
    /// <summary>Разбор упражнения с первой вкладки («Итог»).</summary>
    /// <summary>
    /// 21.5в: пауза перед началом — компактная табличка «ПАУЗА» вместо брифинга (фаза Briefing: модель стоит, время не идёт;
    /// волны и качка идут). «Начать» — runner.StartExercise, «Брифинг» — обратно к тексту. Табличка — на 0,3 м ниже места брифинга.
    /// </summary>
    public void ShowReady()
    {
        ScenarioDefinition s = runner != null ? runner.Current : null;
        if (s == null || Phase != ScenarioRunner.Phase.Briefing) return;
        bool lower = IsOpen && mode == Mode.Briefing && !readyLowered;
        BeginContent(Mode.Ready);
        float y = Top;
        Title("ПАУЗА — упражнение не начато", ref y);
        Text($"{s.id}. {s.title}", 18f, Color.white, ref y);
        Text("Судно и цели стоят, время не идёт. Осмотритесь, возьмите управление и нажмите «Начать».", 16f, MutedColor, ref y);
        y -= Gap;
        ButtonRow(ref y, 72f, ("Начать", StartColor, () => runner.StartExercise()), ("Брифинг", NeutralColor, ShowBriefing));
        EndContent(y);
        if (lower) { panel.transform.position -= panel.transform.up * ReadyDropM; readyLowered = true; }
        Debug.Log($"[Сценарий] «{s.title}» — пауза перед началом (табличка «Начать»).");
    }

    public void ShowResults()
    {
        tab = DebriefTab.Summary;
        criteriaPage = 0;
        mapView = 0;
        plotView = 0;
        debriefFor = runner != null ? runner.LastSummary : null;
        ShowDebrief();
    }

    /// <summary>17.10.2026: повторный вызов разбора из меню — та же вкладка и страницы, что при закрытии (для нового упражнения — «Итог»).</summary>
    public void ReopenResults()
    {
        if (runner == null || runner.LastSummary == null || runner.LastSummary != debriefFor) ShowResults();
        else ShowDebrief();
    }

    private void ShowDebrief()
    {
        ExerciseSummary r = runner != null ? runner.LastSummary : null;
        if (r == null) return;
        BeginContent(Mode.Results);
        waitingDebrief = false;
        float y = Top;
        Title($"Разбор упражнения — {r.ScenarioId}", ref y);
        AssessmentResult a = assessment != null ? assessment.LastResult : null;
        ButtonRow(ref y, ButtonHeight,
            Tab("Итог", DebriefTab.Summary),
            Tab("Критерии", DebriefTab.Criteria),
            Tab("Схема", DebriefTab.Map),
            Tab("Графики", DebriefTab.Plots));
        y -= 4f;
        switch (tab)
        {
            case DebriefTab.Criteria: CriteriaTab(a, ref y); break;
            case DebriefTab.Map: MapTab(ref y); break;
            case DebriefTab.Plots: PlotsTab(ref y); break;
            default: SummaryTab(r, a, ref y); break;
        }
        y -= Gap;
        ButtonRow(ref y, ButtonHeight,
            ("Перезапустить", StartColor, () => runner.Restart()),
            ("К выбору", NeutralColor, ShowSelection),
            ("Закрыть", NeutralColor, Hide));
        EndContent(y);
    }

    private (string, Color, UnityAction) Tab(string label, DebriefTab t) =>
        (label, tab == t ? ActiveTabColor : NeutralColor, () => { tab = t; ShowDebrief(); });

    private void SummaryTab(ExerciseSummary r, AssessmentResult a, ref float y)
    {
        Text(r.Title, 17f, MutedColor, ref y);
        Text(r.ToPanelTextShort(ExerciseFailureNotice.Enabled), 18f, Color.white, ref y);
        // Перед 19.2 (п. B): режим тренажёра — был ли режим обучения / инструктора (из журнала упражнения)
        string mode = r.Modes != null ? r.Modes.Line() : "";
        if (mode.Length > 0)
            Text(r.Modes.UsedInstructor ? $"<color=#FFC85A>{mode}</color>" : r.Modes.UsedTraining ? $"<color=#FFE08A>{mode}</color>" : $"<color=#9AA6B2>{mode}</color>",
                 16f, Color.white, ref y);
        if (a == null)
        {
            Text(r.ToPanelText(ExerciseFailureNotice.Enabled), 18f, Color.white, ref y);
        }
        else
        {
            y -= Gap;
            string color = a.Failed ? "#FF5A4A" : a.Passed ? "#6FE08A" : a.NotAssessed ? "#9AA6B2" : "#FFC85A";
            var b = new StringBuilder($"<b><color={color}>{a.Headline()}</color></b>");
            string reason = DebriefText.VerdictReason(a);
            if (reason.Length > 0) b.Append($"\n<color={color}>{char.ToUpperInvariant(reason[0])}{reason.Substring(1)}</color>");
            string groups = DebriefText.GroupsLine(a);
            if (groups.Length > 0) b.Append($"\n<size=85%><color=#9AA6B2>{groups}</color></size>");
            Text(b.ToString(), 20f, Color.white, ref y);
            if (a.Encounters.Count > 0)
            {
                var e = new StringBuilder();
                foreach (EncounterSummary s in a.Encounters) e.AppendLine(DebriefText.EncounterLine(s));
                Text(e.ToString().TrimEnd(), 16f, MutedColor, ref y);
            }
            y -= Gap;
            List<DebriefViolation> v = DebriefText.Violations(a);
            if (v.Count == 0) Text("<color=#6FE08A>Нарушений нет</color>", 18f, Color.white, ref y);
            else
            {
                var t = new StringBuilder("<b>Нарушения и замечания</b>");
                int shown = Mathf.Min(v.Count, maxViolationsOnSummary);
                for (int i = 0; i < shown; i++)
                    t.Append($"\n<color={(v[i].Severe ? "#FF7A6A" : "#FFC85A")}>•</color> {v[i].Text}");
                if (v.Count > shown) t.Append($"\n<color=#9AA6B2>… ещё {v.Count - shown} — на вкладке «Критерии»</color>");
                Text(t.ToString(), 16f, Color.white, ref y);
            }
        }
        if (recorder != null && !string.IsNullOrEmpty(recorder.LastSessionFolder))
        {
            DebriefBuild d = CurrentDebrief();
            bool pending = DebriefPending(d);
            string report = pending ? " · отчёт строится…" : d == null ? "" : string.IsNullOrEmpty(d.Error) ? $" · отчёт: {SessionFormat.ReportFile}" : " · отчёт не построен";
            if (pending) waitingDebrief = true;
            Text($"Запись: {System.IO.Path.GetFileName(recorder.LastSessionFolder)}{report}", 15f, MutedColor, ref y);
        }
    }

    private void CriteriaTab(AssessmentResult a, ref float y)
    {
        if (a == null) { Text("Оценка не выполнялась.", 17f, MutedColor, ref y); return; }
        List<CriterionRow> rows = DebriefText.Rows(a);
        int pages = DebriefText.PageCount(rows.Count, criteriaPerPage);
        criteriaPage = Mathf.Clamp(criteriaPage, 0, pages - 1);
        TableRow(ref y, 15f, FontStyles.Bold, ("", MutedColor), ("Критерий (объект)", MutedColor), ("Значение", MutedColor), ("Порог", MutedColor));
        foreach (CriterionRow c in DebriefText.Page(rows, criteriaPage, criteriaPerPage))
        {
            Color col = c.State == CriterionState.Passed ? Color.white : c.State == CriterionState.FailedMandatory ? new Color(1f, 0.45f, 0.38f) :
                        c.State == CriterionState.Failed ? WarnColor : MutedColor;
            Color mark = c.State == CriterionState.Passed ? new Color(0.44f, 0.88f, 0.54f) : col;
            TableRow(ref y, 16f, FontStyles.Normal, (c.Mark, mark), ($"{c.Code}  {c.TitleWithSubject}", col), (c.Value, col), (c.Threshold, col));
        }
        Text("«+» — выполнен, «×» — не выполнен (красный — обязательный), «–» — не оценивался", 14f, MutedColor, ref y);
        if (pages > 1)
            ButtonRow(ref y, ButtonHeight,
                ("<", ButtonColor, () => { criteriaPage = (criteriaPage + pages - 1) % pages; ShowDebrief(); }),
                ($"стр. {criteriaPage + 1} / {pages}", NeutralColor, () => { criteriaPage = (criteriaPage + 1) % pages; ShowDebrief(); }),
                (">", ButtonColor, () => { criteriaPage = (criteriaPage + 1) % pages; ShowDebrief(); }));
    }

    private void MapTab(ref float y)
    {
        DebriefBuild d = CurrentDebrief();
        if (d == null || !d.Done || d.Map == null)
        {
            bool pending = DebriefPending(d);
            if (pending) waitingDebrief = true;
            string why = pending ? "Схема строится…"
                : d == null ? "Схема строится по записи упражнения — запись или разбор выключены (ExerciseRecorder)."
                : $"Схема не построена: {d.Error}";
            Text(why, 17f, MutedColor, ref y);
            return;
        }
        DebriefMapData map = d.Map;
        mapView = Mathf.Clamp(mapView, 0, map.Views.Count - 1);
        float w = Width - 2f * Pad;
        var go = new GameObject("DebriefMap", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(background, false);
        RectTransform rt = Top0(go, w, mapHeight, y);
        var g = go.AddComponent<DebriefMapGraphic>();
        g.SetView(map, mapView);
        foreach (DebriefMapGraphic.Label l in g.Labels()) MapLabel(rt, l);
        y -= mapHeight + 6f;

        // Легенда
        var leg = new StringBuilder($"<color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.OwnColor)}>——</color> своё судно");
        for (int i = 0; i < map.TargetNames.Count; i++)
            leg.Append($"   <color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.TargetColor(i))}>——</color> {map.TargetNames[i]}");
        if (map.HasRoute) leg.Append($"   <color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.RouteColor)}>——</color> маршрут и коридор");
        if (map.Views[mapView].ShowHulls)   // 19.2б-3б: вид «У причала»
            leg.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.HullColor)}>——</color> контур судна через {ScenarioText.Num(DebriefMap.HullOutlineStepS)} с · " +
                       $"<color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.FailureColor)}>●</color> навал / <color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.BadColor)}>●</color> удар о причал");
        leg.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.OkColor)}>●</color>/<color=#{ColorUtility.ToHtmlStringRGB(DebriefMapGraphic.BadColor)}>●</color> наименьшая дистанция (допустимо / меньше допустимой) · ○ начало манёвра · " +
                   $"точки — каждые {ScenarioText.Num(map.Views[mapView].TickStepS / 60.0)} мин · сетка {ScenarioText.Num(g.GridStepM)} м");
        Text(leg.ToString(), 14f, MutedColor, ref y);
        if (map.Views.Count > 1)
        {
            int n = map.Views.Count;
            ButtonRow(ref y, ButtonHeight,
                ("<", ButtonColor, () => { mapView = (mapView + n - 1) % n; ShowDebrief(); }),
                (map.Views[mapView].Key == "all" ? "Весь маршрут" : map.Views[mapView].Key == "berth" ? "У причала" : $"Сближение с {map.Views[mapView].Key}", NeutralColor, () => { mapView = (mapView + 1) % n; ShowDebrief(); }),
                (">", ButtonColor, () => { mapView = (mapView + 1) % n; ShowDebrief(); }));
        }
    }

    // Запрос 17: графики по записи — один на всё поле схемы, листание
    private void PlotsTab(ref float y)
    {
        DebriefBuild d = CurrentDebrief();
        if (d == null || !d.Done || d.Plots == null)
        {
            bool pending = DebriefPending(d);
            if (pending) waitingDebrief = true;
            string why = pending ? "Графики строятся…"
                : d == null ? "Графики строятся по записи упражнения — запись или разбор выключены (ExerciseRecorder)."
                : $"Графики не построены: {d.Error}";
            Text(why, 17f, MutedColor, ref y);
            return;
        }
        List<DebriefPlot> plots = d.Plots;
        if (plots.Count == 0) { Text("Графиков нет: в записи меньше двух строк траектории.", 17f, MutedColor, ref y); return; }
        int n = plots.Count;
        plotView = Mathf.Clamp(plotView, 0, n - 1);
        DebriefPlot p = plots[plotView];
        float w = Width - 2f * Pad;
        var go = new GameObject("DebriefPlot", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(background, false);
        RectTransform rt = Top0(go, w, mapHeight, y);
        var g = go.AddComponent<DebriefPlotGraphic>();
        g.SetPlot(p);
        foreach (DebriefPlotGraphic.Label l in g.Labels()) PlotLabel(rt, l);
        y -= mapHeight + 6f;

        // Легенда: ряды (сплошная / штрих) и виды отметок, которые есть на графике
        var leg = new StringBuilder();
        for (int i = 0; i < p.Series.Count; i++)
        {
            if (i > 0) leg.Append("   ");
            leg.Append($"<color=#{ColorUtility.ToHtmlStringRGB(DebriefPlotGraphic.SeriesColor(p, i))}>{(p.Series[i].Dash == SvgPlot.Dash.Solid ? "——" : "– –")}</color> {p.Series[i].Label}");
        }
        var kinds = new List<string>();
        void Kind(PlotMarkKind k, string text)
        {
            if (p.Marks.Exists(m => m.Kind == k)) kinds.Add($"<color=#{ColorUtility.ToHtmlStringRGB(DebriefPlotGraphic.MarkColor(k))}>¦</color> {text}");
        }
        Kind(PlotMarkKind.Manoeuvre, "начало манёвра (цель)");
        Kind(PlotMarkKind.Accident, p.Key.StartsWith("berth") ? "удар о кромку" : "авария");
        Kind(PlotMarkKind.Failure, "вводная");
        Kind(PlotMarkKind.Pause, "пауза");
        Kind(PlotMarkKind.Berthing, "навал (скорость, уз) / ошвартован");
        if (kinds.Count > 0) leg.Append("\n").Append(string.Join(" · ", kinds));
        Text(leg.ToString(), 14f, MutedColor, ref y);
        ButtonRow(ref y, ButtonHeight,
            ("<", ButtonColor, () => { plotView = (plotView + n - 1) % n; ShowDebrief(); }),
            ($"{p.ShortTitle} · {plotView + 1} / {n}", NeutralColor, () => { plotView = (plotView + 1) % n; ShowDebrief(); }),
            (">", ButtonColor, () => { plotView = (plotView + 1) % n; ShowDebrief(); }));
    }

    private void PlotLabel(RectTransform parent, DebriefPlotGraphic.Label l)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = l.Text;
        tmp.fontSize = l.Size;
        tmp.color = l.Color;
        tmp.alignment = l.Pivot.x < 0.25f ? TextAlignmentOptions.Left : l.Pivot.x > 0.75f ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);   // начало координат графики — её точка привязки (верх, центр)
        rt.pivot = l.Pivot;
        rt.sizeDelta = new Vector2(l.Rotation != 0f ? 360f : 200f, l.Size + 6f);
        rt.anchoredPosition = l.Local;
        rt.localRotation = Quaternion.Euler(0f, 0f, l.Rotation);
    }

    /// <summary>Разбор текущего упражнения (не предыдущего: запись может закрыться позже показа панели).</summary>
    private DebriefBuild CurrentDebrief()
    {
        if (recorder == null || recorder.LastDebrief == null) return null;
        DebriefBuild d = recorder.LastDebrief;
        return d.Folder == recorder.LastSessionFolder ? d : null;
    }

    /// <summary>Разбор ещё будет: запись не закрыта или фоновое построение идёт.</summary>
    private bool DebriefPending(DebriefBuild d) =>
        d != null ? !d.Done : recorder != null && recorder.IsRecording && recorder.buildDebrief;

    void Update()
    {
        // Схема и отчёт строятся в фоне после закрытия записи — показать, когда готовы
        if (!waitingDebrief || mode != Mode.Results || !IsOpen) return;
        if (DebriefPending(CurrentDebrief())) return;
        ShowDebrief();
    }

    private void MapLabel(RectTransform parent, DebriefMapGraphic.Label l)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = l.Text;
        tmp.fontSize = 14f;
        tmp.color = l.Color;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);   // начало координат графики — её точка привязки (верх, центр)
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(180f, 20f);
        rt.anchoredPosition = l.Local;
    }

    /// <summary>Строка таблицы: столбцы по ширинам criteriaColumns (доли), высота — по самому высокому.</summary>
    private void TableRow(ref float y, float size, FontStyles style, params (string text, Color color)[] cells)
    {
        float w = Width - 2f * Pad;
        float x = -w * 0.5f, h = 0f;
        for (int i = 0; i < cells.Length; i++)
        {
            float cw = w * criteriaColumns[Mathf.Min(i, criteriaColumns.Length - 1)];
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(background, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size; tmp.color = cells[i].color; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            tmp.text = cells[i].text;
            float ch = Mathf.Ceil(tmp.GetPreferredValues(cells[i].text, cw - 6f, 0f).y) + 2f;
            RectTransform rt = Top0(go, cw - 6f, ch, y);
            rt.anchoredPosition = new Vector2(x + cw * 0.5f, y);
            h = Mathf.Max(h, ch);
            x += cw;
        }
        y -= h + 3f;
    }

    // ─────────── Текущий балл (C2, меню «Параметры» на Y) ───────────
    /// <summary>Промежуточная оценка во время упражнения: балл, оценённые критерии, цели (CPA/TCPA).</summary>
    public void ShowCurrentScore()
    {
        if (assessment == null) return;
        if (Phase == ScenarioRunner.Phase.Finished) { ShowResults(); return; }
        AssessmentResult a = assessment.Current();
        BeginContent(Mode.CurrentScore);
        float y = Top;
        Title($"Текущая оценка — t = {ScenarioText.Clock(runner != null ? runner.ExerciseTime : 0)}", ref y);
        if (a == null)
            Text("Оценка начнётся с началом упражнения.", 17f, MutedColor, ref y);
        else
        {
            var targets = new StringBuilder();
            // C4: в сценарии «цели не показывать» (туман) CPA/TCPA целей здесь тоже не видны
            bool hideTargets = runner != null && runner.Current != null && runner.Current.hideTargetsOnPlotter;
            foreach (ExerciseAssessmentMonitor.LiveTarget t in hideTargets ? new List<ExerciseAssessmentMonitor.LiveTarget>() : assessment.LiveTargets)
            {
                bool closing = t.TcpaMin > 0;
                targets.AppendLine($"{t.Id}: D {ScenarioText.Num(t.RangeNm, 2)} мили, пеленг {ScenarioText.Num(t.BearingDeg)}°" +
                                   (closing ? $", CPA {ScenarioText.Num(t.CpaNm, 2)} мили, TCPA {ScenarioText.Num(t.TcpaMin, 1)} мин" : ", расходимся"));
            }
            if (targets.Length > 0) Text(targets.ToString().TrimEnd(), 16f, MutedColor, ref y);
            Text(a.ToPanelText(), 17f, Color.white, ref y);
            Text("Предварительно: сближения оцениваются после того, как цель пройдёт; маршрут — по пройденной части.", 14f, MutedColor, ref y);
        }
        y -= Gap;
        ButtonRow(ref y, ButtonHeight,
            ("Обновить", ButtonColor, ShowCurrentScore),
            ("Закрыть", NeutralColor, Hide));
        EndContent(y);
    }

    // ─────────── ID обучающегося (C1) ───────────
    private void TraineeRow(ref float y)
    {
        if (recorder == null || !recorder.isActiveAndEnabled || !recorder.record)
        {
            Text("Запись упражнения выключена.", 16f, MutedColor, ref y);
            return;
        }
        string id = recorder.TraineeId;
        Text(string.IsNullOrEmpty(id)
                ? "<b>Обучающийся:</b> ID не введён — запись как «noid»"
                : $"<b>Обучающийся:</b> {id}", 18f, string.IsNullOrEmpty(id) ? WarnColor : Color.white, ref y);
        ButtonRow(ref y, ButtonHeight, ("Ввести ID обучающегося", ButtonColor, ShowKeypad));
    }

    /// <summary>Цифровая VR-клавиатура ID обучающегося (вводит инструктор, без ФИО).</summary>
    public void ShowKeypad()
    {
        if (recorder == null) return;
        if (mode != Mode.Keypad) keypadBuffer = DigitsOnly(recorder.TraineeId);
        BeginContent(Mode.Keypad);
        float y = Top;
        Title("ID обучающегося", ref y);
        Text("Обезличенный код из цифр — без фамилии. Попадает в имя папки записи и в session.json.", 16f, MutedColor, ref y);
        Text(string.IsNullOrEmpty(keypadBuffer) ? "—" : keypadBuffer, 40f, Color.white, ref y, FontStyles.Bold);
        y -= Gap;
        for (int row = 0; row < 3; row++)
            ButtonRow(ref y, 58f, Key(row * 3 + 1), Key(row * 3 + 2), Key(row * 3 + 3));
        ButtonRow(ref y, 58f,
            ("← Стереть", NeutralColor, () => { if (keypadBuffer.Length > 0) keypadBuffer = keypadBuffer.Substring(0, keypadBuffer.Length - 1); ShowKeypad(); }),
            Key(0),
            ("Очистить", NeutralColor, () => { keypadBuffer = ""; ShowKeypad(); }));
        y -= Gap;
        ButtonRow(ref y, ButtonHeight,
            ("Готово", StartColor, () => { recorder.TraineeId = keypadBuffer; AfterKeypad(); }),
            ("Отмена", NeutralColor, AfterKeypad));
        EndContent(y);
    }

    private (string, Color, UnityAction) Key(int digit)
    {
        string d = digit.ToString();
        return (d, ButtonColor, () =>
        {
            if (keypadBuffer.Length < Mathf.Max(1, traineeIdMaxLength)) keypadBuffer += d;
            ShowKeypad();
        });
    }

    private void AfterKeypad()
    {
        if (Phase == ScenarioRunner.Phase.Briefing) ShowBriefing();
        else ShowSelection();
    }

    private static string DigitsOnly(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder();
        foreach (char c in s) if (c >= '0' && c <= '9') b.Append(c);
        return b.ToString();
    }

    public void Hide()
    {
        mode = Mode.Hidden;
        if (canvas != null) canvas.enabled = false;
        if (dragHandle != null) dragHandle.gameObject.SetActive(false);
    }

    // ─────────── Построение панели ───────────
    private float Top => 0f;

    private void BuildCanvas()
    {
        if (canvas != null) return;
        panel = new GameObject("ScenarioPanelCanvas", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        panel.transform.localScale = Vector3.one * canvasScale;
        panel.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 100;

        var raycasterType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (raycasterType != null) panel.AddComponent(raycasterType);
        else panel.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(panel.transform, false);
        bg.AddComponent<Image>().color = PanelColor;
        background = bg.GetComponent<RectTransform>();
        background.pivot = new Vector2(0.5f, 1f);   // элементы — сверху вниз от y = 0
        dragHandle = PanelDragHandle.Create(panel.transform, Width * 0.6f);
        dragHandle.gameObject.SetActive(false);
        canvas.enabled = false;
    }

    private void BeginContent(Mode newMode)
    {
        BuildCanvas();
        for (int i = background.childCount - 1; i >= 0; i--)
            Destroy(background.GetChild(i).gameObject);
        ClearSidePanels();   // К1: боковые панели — только у брифинга (ShowBriefing ставит их заново)
        bool wasOpen = IsOpen;
        mode = newMode;
        if (!wasOpen)
        {
            readyLowered = false;
            if (placeAtAnchor && startAnchor != null) panel.transform.SetPositionAndRotation(startAnchor.position, startAnchor.rotation);
            else PositionInFrontOfPlayer();
        }
    }

    private float EndContent(float y)
    {
        float height = -y + Pad;
        background.sizeDelta = new Vector2(Width, height);
        background.anchoredPosition = new Vector2(0f, height * 0.5f);   // центр панели — в точке объекта
        canvas.enabled = true;
        dragHandle.PlaceAbove(height * 0.5f);
        dragHandle.gameObject.SetActive(true);
        return height;
    }

    private void Title(string text, ref float y, float size = 25f)
    {
        y -= Pad;
        Text(text, size, TitleColor, ref y, FontStyles.Bold);
        y -= 4f;
    }

    private void Text(string text, float size, Color color, ref float y, FontStyles style = FontStyles.Normal) =>
        TextIn(background, Width - 2f * Pad, text, size, color, ref y, style);

    // Текст шириной w в родителе с якорем по верхнему краю (центральная панель или боковая — К1)
    private void TextIn(RectTransform parent, float w, string text, float size, Color color, ref float y, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        tmp.text = text;
        float h = Mathf.Ceil(tmp.GetPreferredValues(text, w, 0f).y) + 4f;
        RectTransform rt = Top0(go, w, h, y);
        rt.anchoredPosition = new Vector2(0f, y);
        y -= h + 4f;
    }

    private void ButtonRow(ref float y, float height, params (string label, Color color, UnityAction action)[] buttons)
    {
        float w = Width - 2f * Pad;
        float bw = (w - Gap * (buttons.Length - 1)) / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(background, false);
            var img = go.AddComponent<Image>();
            img.color = buttons[i].color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UnityAction a = buttons[i].action;
            btn.onClick.AddListener(() =>
            {
                try { a?.Invoke(); }
                catch (Exception e) { Debug.LogException(e, this); }
            });
            RectTransform rt = Top0(go, bw, height, y);
            rt.anchoredPosition = new Vector2(-w * 0.5f + bw * 0.5f + i * (bw + Gap), y);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = buttons[i].label;
            tmp.fontSize = 19f; tmp.color = Color.white;
            tmp.alignment = buttons.Length == 1 && height > ButtonHeight + 10f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(bw - 24f, height);
        }
        y -= height + Gap;
    }

    // Элемент с якорем по верхнему краю (pivot сверху): x — от центра, y — сверху вниз
    private RectTransform Top0(GameObject go, float w, float h, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(0f, y);
        return rt;
    }

    // Точка панели при запуске — видна в Scene View при выделении ScenarioPanel
    void OnDrawGizmosSelected()
    {
        if (startAnchor == null) return;
        Gizmos.color = new Color(0.3f, 0.7f, 1f);
        Gizmos.matrix = Matrix4x4.TRS(startAnchor.position, startAnchor.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(Width * canvasScale, 0.5f, 0.01f));
        Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.3f);
    }

    private void PositionInFrontOfPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null || panel == null) return;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = cam.transform.up;
        fwd.y = 0f;
        fwd.Normalize();
        panel.transform.position = cam.transform.position + fwd * distanceFromHead + Vector3.down * downOffset;
        Vector3 toPanel = panel.transform.position - cam.transform.position;
        panel.transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
    }
}
