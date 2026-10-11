// ============================================================
// ScenarioPanel.cs — VR-панель сценария и команды меню мостика (B2)
//
// Без клавиатуры (решение пользователя в B1): всё управление сценарием —
// через эту панель и меню на кнопке A (группа «Сценарий», вверху меню).
// Режимы панели:
//   • выбор — список сценариев библиотеки ScenarioRunner и «Свободное плавание»;
//   • брифинг — название, текст, условия (погода, начальные условия, лимит,
//     условия завершения), кнопки «Начать» / «К выбору» / «Закрыть»;
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
// Команды меню: «Выбор сценария…», «Начать упражнение», «Брифинг»/«Результаты»,
// «Перезапустить сценарий» (инстр.), «Завершить досрочно» (инстр.); недоступные
// по фазе — скрыты. Отладочные команды B1 остаются в группе «Отладка» (внизу).
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
    private enum Mode { Hidden, Selection, Briefing, Results, Keypad }

    public ScenarioRunner runner;
    [Tooltip("Запись упражнения (C1): ID обучающегося, папка сессии. Пусто — найти")]
    public ExerciseRecorder recorder;
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

    private const string MenuGroup = "Сценарий";
    private const float Width = 680f, Pad = 22f, ButtonHeight = 48f, Gap = 8f;
    private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.96f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    private static readonly Color StartColor = new Color(0.16f, 0.55f, 0.3f);
    private static readonly Color NeutralColor = new Color(0.33f, 0.35f, 0.4f);
    private static readonly Color TitleColor = new Color(0.85f, 0.92f, 1f);
    private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.75f);
    private static readonly Color WarnColor = new Color(1f, 0.78f, 0.35f);

    private Mode mode = Mode.Hidden;
    private GameObject panel;
    private Canvas canvas;
    private RectTransform background;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private ScenarioRunner subscribed;
    private string keypadBuffer = "";
    private bool placeAtAnchor;

    public bool IsOpen => canvas != null && canvas.enabled;

    private ScenarioRunner.Phase Phase => runner != null ? runner.CurrentPhase : ScenarioRunner.Phase.Idle;

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder(MenuGroup, -100);
        BridgeMenu.SetGroupOrder("Отладка", 100);
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Выбор сценария…", ShowSelection,
            () => runner != null && Phase != ScenarioRunner.Phase.Running, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Начать упражнение", () => runner.StartExercise(),
            () => Phase == ScenarioRunner.Phase.Briefing, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup,
            () => Phase == ScenarioRunner.Phase.Finished ? "Результаты" : "Брифинг",
            () => { if (Phase == ScenarioRunner.Phase.Finished) ShowResults(); else ShowBriefing(); },
            () => runner != null && runner.Current != null && Phase != ScenarioRunner.Phase.Idle, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Перезапустить сценарий", () => runner.Restart(),
            () => runner != null && runner.Current != null && Phase != ScenarioRunner.Phase.Idle,
            instructorOnly: true, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, "Завершить досрочно", () => runner.Abort(),
            () => Phase == ScenarioRunner.Phase.Running, instructorOnly: true, hideWhenUnavailable: true));
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
            string label = $"<b>{s.id}</b>  {s.title}\n<size=75%><color=#C8D2DC>{ScenarioText.Conditions(s.weather, s.timeOfDay)}{limit} · маршрут {ScenarioText.Num(s.RouteLength / 1000.0, 1)} км</color></size>";
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
        BeginContent(Mode.Briefing);
        float y = Top;
        Title($"{s.id}. {s.title}", ref y);
        Text(s.briefing, 18f, Color.white, ref y);
        y -= Gap;
        Text(Conditions(s), 16f, MutedColor, ref y);
        y -= Gap;

        if (Phase == ScenarioRunner.Phase.Briefing)
        {
            TraineeRow(ref y);
            Text("Судно стоит, пока не нажата «Начать». Начальные условия будут поставлены заново.", 15f, MutedColor, ref y);
            ButtonRow(ref y, ButtonHeight + 6f,
                ("Начать", StartColor, () => runner.StartExercise()),
                ("К выбору", NeutralColor, ShowSelection),
                ("Закрыть", NeutralColor, Hide));
        }
        else
            ButtonRow(ref y, ButtonHeight, ("Закрыть", NeutralColor, Hide));
        EndContent(y);
    }

    public void ShowResults()
    {
        ExerciseSummary r = runner != null ? runner.LastSummary : null;
        if (r == null) return;
        BeginContent(Mode.Results);
        float y = Top;
        Title($"Упражнение завершено — {r.ScenarioId}", ref y);
        Text(r.Title, 17f, MutedColor, ref y);
        y -= Gap;
        Text(r.ToPanelText(ExerciseFailureNotice.Enabled), 19f, Color.white, ref y);
        if (recorder != null && !string.IsNullOrEmpty(recorder.LastSessionFolder))
            Text($"Запись: {System.IO.Path.GetFileName(recorder.LastSessionFolder)}", 15f, MutedColor, ref y);
        y -= Gap;
        ButtonRow(ref y, ButtonHeight,
            ("Перезапустить", StartColor, () => runner.Restart()),
            ("К выбору", NeutralColor, ShowSelection),
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
    }

    private static string Conditions(ScenarioDefinition s)
    {
        var b = new StringBuilder();
        b.AppendLine("<b>Условия</b>");
        b.AppendLine($"Время суток: {ScenarioText.TimeOfDayTitle(s.timeOfDay)}; погода: {ScenarioText.Weather(s.weather)}");
        OwnShipInitialState i = s.initial;
        if (i != null)
            b.AppendLine($"Начало: курс {i.headingDeg:000}°, {ScenarioText.Num(i.speedKnots, 1)} уз, телеграф «{ScenarioText.Telegraph(i.telegraph)}», руль {ScenarioText.Num(i.rudderDeg)}°");
        b.AppendLine($"Путевых точек: {Math.Max(0, s.route.Count - 1)}, маршрут {ScenarioText.Num(s.RouteLength / 1852.0, 1)} мили");
        if (s.traffic != null && s.traffic.Count > 0) b.AppendLine($"Другие суда в районе: {s.traffic.Count}");
        b.AppendLine(s.timeLimitSeconds > 0 ? $"Лимит времени: {ScenarioText.Clock(s.timeLimitSeconds)}" : "Без лимита времени");
        b.Append($"Завершение: {ScenarioText.EndConditions(s)}");
        return b.ToString();
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
        canvas.enabled = false;
    }

    private void BeginContent(Mode newMode)
    {
        BuildCanvas();
        for (int i = background.childCount - 1; i >= 0; i--)
            Destroy(background.GetChild(i).gameObject);
        bool wasOpen = IsOpen;
        mode = newMode;
        if (!wasOpen)
        {
            if (placeAtAnchor && startAnchor != null) panel.transform.SetPositionAndRotation(startAnchor.position, startAnchor.rotation);
            else PositionInFrontOfPlayer();
        }
    }

    private void EndContent(float y)
    {
        float height = -y + Pad;
        background.sizeDelta = new Vector2(Width, height);
        background.anchoredPosition = new Vector2(0f, height * 0.5f);   // центр панели — в точке объекта
        canvas.enabled = true;
    }

    private void Title(string text, ref float y)
    {
        y -= Pad;
        Text(text, 25f, TitleColor, ref y, FontStyles.Bold);
        y -= 4f;
    }

    private void Text(string text, float size, Color color, ref float y, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(background, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        tmp.text = text;
        float w = Width - 2f * Pad;
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
