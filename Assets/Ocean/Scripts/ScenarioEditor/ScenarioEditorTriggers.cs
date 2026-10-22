// ============================================================
// ScenarioEditorTriggers.cs — Лист «Триггеры» и круги триггеров на карте (запрос 21.6б; часть ScenarioEditorApp, partial-класс)
//
// Лист (кнопка «Триггеры… (N)» в свойствах сценария): слева — список «Тр1 · условие → действия» (× — есть ошибка),
// «Добавить», «Копия», «Удалить»; справа — выбранный триггер: название, условие < > и его поля (время «мм:сс»; круг —
// восток, север, радиус; точка маршрута / цель / этап / другой триггер — < >; дистанция, мили), задержка; действия —
// «Действие 1 из N» < >, «Добавить» / «Удалить», вид < > и поля вида (цель < >, курс и скорость — пусто: не менять;
// погода < >; видимость; ветер; текст сообщения). Внизу — замечания «Проверки» по этому триггеру.
// Карта (режим «Сценарий», инструмент любой): у триггеров «в круге» — пунктирная окружность с подписью «Тр2»;
// щелчок по центру или окружности — выбрать (ручки: центр — тянуть с привязкой, квадрат на краю — радиус шагом 10 м).
// Каждая правка — шаг отмены. Core — TriggerEditing.
// ============================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class ScenarioEditorApp
{
    private const int TrigRows = 12;
    private RectTransform trigSheet;
    private int selTrigger = -1, selAction, trigListTop;
    private int mapTrigger = -1, hotTrigCenter = -1, hotTrigRing = -1;
    private bool hotTrigEdge, dragTrigCenter, dragTrigEdge;
    private readonly RectTransform[] trigRowButtons = new RectTransform[TrigRows];
    private readonly string[] trigRowText = new string[TrigRows];
    private RectTransform trCondChoice, trCondTime, trCondCircle, trCondRange, taChoice, taFields, taTextRow, trEditor;
    private TMP_InputField trTitleField, trTimeField, trEField, trNField, trRField, trRangeField, trDelayField, taAField, taBField, taTextField;
    private TextMeshProUGUI trHeader, trCondChoiceLabel, taChoiceLabel, taFieldsLabel, trInfo, trListInfo;

    private ScenarioTrigger SelTr => S != null && TriggerEditing.Valid(S.triggers, selTrigger) ? S.triggers[selTrigger] : null;
    private TriggerAction SelAct => SelTr != null && SelTr.actions != null && selAction >= 0 && selAction < SelTr.actions.Count ? SelTr.actions[selAction] : null;
    private bool TriggerBusy => dragTrigCenter || dragTrigEdge;
    private int TriggerCount => S?.triggers != null ? S.triggers.Count : 0;

    // ─────────── Построение листа ───────────
    private void BuildTriggerSheet()
    {
        RectTransform p = trigSheet = Sheet("TriggerSheet", "Триггеры «если — то»", out float y0);
        float listW = 520f, x1 = 20f, x2 = 20f + listW + 40f, colW = SheetWidth - x2 - 20f;
        float half = (colW - MonitorUi.Gap) * 0.5f, third = (colW - 2f * MonitorUi.Gap) / 3f;

        // Список
        float y = y0;
        SHeader(p, "Триггеры сценария (каждый — один раз за упражнение)", x1, ref y);
        for (int i = 0; i < TrigRows; i++)
        {
            int row = i;
            float x = x1;
            trigRowButtons[i] = ui.Button(p, ref x, y, listW, "", MonitorUi.NeutralColor, () => SelectTriggerRow(row), () => trigRowText[row] ?? "",
                                          () => trigListTop + row == selTrigger ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, 34f, 14f);
            trigRowButtons[i].GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            y -= 34f + MonitorUi.Gap;
        }
        float xs = x1;
        ui.Button(p, ref xs, y, 48f, "<", MonitorUi.NeutralColor, () => ScrollTriggers(-TrigRows), null, null, () => trigListTop > 0);
        trListInfo = MonitorUi.Text(p, "", 14f, MonitorUi.MutedColor, TextAlignmentOptions.Center);
        MonitorUi.TopLeft(trListInfo.rectTransform, xs, y, listW - 2f * (48f + MonitorUi.Gap), MonitorUi.RowHeight);
        xs = x1 + listW - 48f;
        ui.Button(p, ref xs, y, 48f, ">", MonitorUi.NeutralColor, () => ScrollTriggers(TrigRows), null, null, () => trigListTop + TrigRows < TriggerCount);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        float tl = (listW - 2f * MonitorUi.Gap) / 3f;
        xs = x1;
        ui.Button(p, ref xs, y, tl, "Добавить", MonitorUi.StartColor, AddTrigger, null, null, () => S != null);
        ui.Button(p, ref xs, y, tl, "Копия", MonitorUi.NeutralColor, DuplicateTrigger, null, null, () => SelTr != null);
        ui.Button(p, ref xs, y, tl, "Удалить", MonitorUi.StopColor, DeleteTrigger, null, null, () => SelTr != null);
        y -= MonitorUi.RowHeight + 2f * MonitorUi.Gap;
        TextMeshProUGUI help = MonitorUi.Text(p,
            "Условие → задержка → действия. Сложное — цепочкой: «Сработал другой триггер» + задержка.\n" +
            "Круг «Своё судно в круге» — на карте: центр и квадрат на краю (радиус) — тянуть.\n" +
            "Действия идут теми же путями, что вводные инструктора; в тренажёре — список и «Сработать» в окне инструктора.",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, x1, y, listW, 110f);

        // Выбранный триггер
        RectTransform e = trEditor = SubPanel(p, "TriggerEditor", x2, y0, colW, SheetHeight + y0 - 10f);
        y = 0f;
        trHeader = MonitorUi.Text(e, "", 18f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        trHeader.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(trHeader.rectTransform, 0f, y, colW, 26f);
        y -= 30f;
        trTitleField = SField(e, "Название (для инструктора и журнала; можно пусто)", 0f, ref y, colW,
                              v => EditTrigger(t => { string n = (v ?? "").Trim(); if (n == (t.title ?? "")) return false; t.title = n; return true; }));
        SLabel(e, "Условие", 0f, ref y, colW);
        SCycler(e, 0f, ref y, colW, () => CycleCondition(-1), () => CycleCondition(1),
                () => SelTr == null ? "" : TriggerText.ConditionTitle(SelTr.condition));

        // Поля условия: строка выбора (точка / цель / этап / триггер) и строка чисел (время / круг / дистанция)
        float yRow = y;
        trCondChoice = SubPanel(e, "CondChoice", 0f, yRow, colW, 22f + MonitorUi.RowHeight);
        float yy = 0f;
        trCondChoiceLabel = MonitorUi.Text(trCondChoice, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(trCondChoiceLabel.rectTransform, 0f, yy, colW, 22f);
        yy -= 22f;
        SCycler(trCondChoice, 0f, ref yy, colW, () => CycleCondChoice(-1), () => CycleCondChoice(1), CondChoiceTitle);
        y -= 22f + MonitorUi.RowHeight + MonitorUi.Gap;
        float yNums = y;
        trCondTime = SubPanel(e, "CondTime", 0f, yNums, colW, 56f);
        yy = 0f;
        trTimeField = SField(trCondTime, "Время от «Начать», мм:сс (или число минут)", 0f, ref yy, half,
                             v => EditTriggerClock(v, (t, s) => t.timeSeconds = (float)s));
        trCondCircle = SubPanel(e, "CondCircle", 0f, yNums, colW, 56f);
        yy = 0f;
        SLabel(trCondCircle, "Центр круга: восток, м · север, м · радиус, м (на карте — тянуть)", 0f, ref yy, colW);
        trEField = MonitorUi.Field(trCondCircle, 0f, -22f, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                   v => EditTriggerNumber(v, d => true, (t, d) => t.center = new WorldPosition(d, t.center.North)));
        trNField = MonitorUi.Field(trCondCircle, third + MonitorUi.Gap, -22f, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                   v => EditTriggerNumber(v, d => true, (t, d) => t.center = new WorldPosition(t.center.East, d)));
        trRField = MonitorUi.Field(trCondCircle, 2f * (third + MonitorUi.Gap), -22f, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                   v => EditTriggerNumber(v, d => d >= TriggerEditing.MinRadiusM && d <= 20000.0, (t, d) => t.radiusM = (float)d));
        trCondRange = SubPanel(e, "CondRange", 0f, yNums, colW, 56f);
        yy = 0f;
        trRangeField = SField(trCondRange, "Дистанция до цели меньше, мили", 0f, ref yy, half,
                              v => EditTriggerNumber(v, d => d > 0.0 && d <= 30.0, (t, d) => t.rangeNm = (float)d));
        y -= 56f + MonitorUi.Gap;
        trDelayField = SField(e, "Задержка после условия, с (0 — сразу)", 0f, ref y, half,
                              v => EditTriggerNumber(v, d => d >= 0.0 && d <= 7200.0, (t, d) => t.delaySeconds = (float)d));
        y -= 2f * MonitorUi.Gap;

        // Действия
        SHeader(e, "Действия (по порядку)", 0f, ref y);
        float xa = 0f, wsel = colW - 2f * (140f + MonitorUi.Gap);
        ui.Button(e, ref xa, y, 48f, "<", MonitorUi.NeutralColor, () => StepAction(-1), null, null, () => SelTr != null && SelTr.actions.Count > 1);
        ui.Button(e, ref xa, y, wsel - 2f * (48f + MonitorUi.Gap), "", MonitorUi.NeutralColor, () => StepAction(1), ActionPagerTitle);
        ui.Button(e, ref xa, y, 48f, ">", MonitorUi.NeutralColor, () => StepAction(1), null, null, () => SelTr != null && SelTr.actions.Count > 1);
        ui.Button(e, ref xa, y, 140f, "Добавить", MonitorUi.StartColor, AddAction, null, null, () => SelTr != null);
        ui.Button(e, ref xa, y, 140f, "Удалить", MonitorUi.StopColor, DeleteAction, null, null, () => SelAct != null);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        SLabel(e, "Вид действия", 0f, ref y, colW);
        SCycler(e, 0f, ref y, colW, () => CycleActionKind(-1), () => CycleActionKind(1),
                () => SelAct == null ? "нет действий — «Добавить»" : TriggerText.ActionTitle(SelAct.kind));
        float yAct = y;
        taChoice = SubPanel(e, "ActChoice", 0f, yAct, colW, 22f + MonitorUi.RowHeight);
        yy = 0f;
        taChoiceLabel = MonitorUi.Text(taChoice, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(taChoiceLabel.rectTransform, 0f, yy, colW, 22f);
        yy -= 22f;
        SCycler(taChoice, 0f, ref yy, colW, () => CycleActChoice(-1), () => CycleActChoice(1), ActChoiceTitle);
        float yActNums = yAct - (22f + MonitorUi.RowHeight + MonitorUi.Gap);
        taFields = SubPanel(e, "ActFields", 0f, yActNums, colW, 56f);
        taFieldsLabel = MonitorUi.Text(taFields, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(taFieldsLabel.rectTransform, 0f, 0f, colW, 22f);
        taAField = MonitorUi.Field(taFields, 0f, -22f, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditActionValue(v, true));
        taBField = MonitorUi.Field(taFields, half + MonitorUi.Gap, -22f, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditActionValue(v, false));
        taTextRow = SubPanel(e, "ActText", 0f, yAct, colW, 22f + 120f);
        yy = 0f;
        SLabel(taTextRow, "Текст сообщения (табличка в шлеме и строка в «Событиях»)", 0f, ref yy, colW);
        taTextField = MonitorUi.Field(taTextRow, 0f, yy, colW, 120f, "сообщение обучающемуся", true, TMP_InputField.ContentType.Standard,
                                      v => EditAction(a => { string n = v ?? ""; if (n == (a.text ?? "")) return false; a.text = n; return true; }), 16f);
        y = yAct - (22f + 120f + 2f * MonitorUi.Gap);
        trInfo = MonitorUi.Text(e, "", 14f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(trInfo.rectTransform, 0f, y, colW, Mathf.Max(60f, SheetHeight + y0 - 10f + y));
    }

    // Прозрачная группа элементов (показать / скрыть целиком)
    private static RectTransform SubPanel(RectTransform p, string name, float x, float y, float w, float h)
    {
        RectTransform r = MonitorUi.Panel(p, name, new Color(0f, 0f, 0f, 0f));
        MonitorUi.TopLeft(r, x, y, w, h);
        return r;
    }

    // ─────────── Заполнить ───────────
    private void FillTriggerSheet()
    {
        ScenarioDefinition s = S;
        if (s == null || trigSheet == null || !Active(trigSheet)) return;   // «Проверка» — только при открытом листе
        int n = TriggerCount;
        if (selTrigger >= n) selTrigger = n - 1;
        if (selTrigger < 0 && n > 0) selTrigger = 0;
        if (selTrigger >= 0 && (selTrigger < trigListTop || selTrigger >= trigListTop + TrigRows)) trigListTop = selTrigger / TrigRows * TrigRows;
        if (trigListTop >= n) trigListTop = Math.Max(0, (n - 1) / TrigRows * TrigRows);

        AreaCheckResult check = ScenarioChecks.Check(s, Catalog);
        for (int i = 0; i < TrigRows; i++)
        {
            int k = trigListTop + i;
            bool on = k < n && s.triggers[k] != null;
            trigRowButtons[i].gameObject.SetActive(on);
            if (!on) { trigRowText[i] = ""; continue; }
            ScenarioTrigger t = s.triggers[k];
            bool err = check.Errors.Exists(x => About(x, t)) || check.Errors.Exists(x => x.StartsWith("Триггеры:", StringComparison.Ordinal) && x.Contains(t.id));
            trigRowText[i] = (err ? "<color=#FF7A7A>×</color> " : "") + TriggerText.Line(t, s);
        }
        trListInfo.text = n == 0 ? "триггеров нет — «Добавить»" : n > TrigRows ? $"{trigListTop + 1}–{Math.Min(n, trigListTop + TrigRows)} из {n}" : $"всего {n}";

        ScenarioTrigger tr = SelTr;
        trEditor.gameObject.SetActive(tr != null);
        if (tr == null) return;
        if (tr.actions == null) tr.actions = new List<TriggerAction>();
        selAction = Mathf.Clamp(selAction, 0, Math.Max(0, tr.actions.Count - 1));
        trHeader.text = $"Триггер {tr.id}";
        Set(trTitleField, tr.title ?? "");
        Set(trTimeField, ScenarioText.Clock(tr.timeSeconds));
        Set(trEField, MonitorUi.Num(tr.center.East, 0));
        Set(trNField, MonitorUi.Num(tr.center.North, 0));
        Set(trRField, MonitorUi.Num(tr.radiusM, 0));
        Set(trRangeField, MonitorUi.Num(tr.rangeNm, 2));
        Set(trDelayField, MonitorUi.Num(tr.delaySeconds, 0));
        TriggerCondition c = tr.condition;
        trCondChoice.gameObject.SetActive(c == TriggerCondition.Waypoint || c == TriggerCondition.TargetRange || c == TriggerCondition.TaskStage || c == TriggerCondition.AfterTrigger);
        trCondChoiceLabel.text = c == TriggerCondition.Waypoint ? "Точка маршрута (первая — старт, её не «проходят»)"
                               : c == TriggerCondition.TargetRange ? "Цель"
                               : c == TriggerCondition.TaskStage ? "Этап задания (швартовка или работа буксира — лист «Задание…»)"
                               : "Сработал триггер";
        trCondTime.gameObject.SetActive(c == TriggerCondition.Time);
        trCondCircle.gameObject.SetActive(c == TriggerCondition.EnterCircle);
        trCondRange.gameObject.SetActive(c == TriggerCondition.TargetRange);

        TriggerAction a = SelAct;
        TriggerActionKind kind = a != null ? a.kind : TriggerActionKind.Message;
        bool choice = a != null && (kind == TriggerActionKind.TargetOrder || kind == TriggerActionKind.Weather);
        bool fields = a != null && (kind == TriggerActionKind.TargetOrder || kind == TriggerActionKind.Visibility || kind == TriggerActionKind.Wind);
        taChoice.gameObject.SetActive(choice);
        taChoiceLabel.text = kind == TriggerActionKind.TargetOrder ? "Цель" : "Погода (пресет; переход плавный)";
        taFields.gameObject.SetActive(fields);
        taBField.gameObject.SetActive(kind != TriggerActionKind.Visibility);
        // Поля чисел под строкой выбора; у видимости и ветра выбора нет — сразу под видом действия
        RectTransform fr = taFields;
        float yChoice = taChoice.anchoredPosition.y;
        fr.anchoredPosition = new Vector2(0f, choice ? yChoice - (22f + MonitorUi.RowHeight + MonitorUi.Gap) : yChoice);
        taTextRow.gameObject.SetActive(a != null && kind == TriggerActionKind.Message);
        if (a != null)
        {
            switch (kind)
            {
                case TriggerActionKind.TargetOrder:
                    taFieldsLabel.text = "Курс, ° · скорость, уз (пусто — не менять; 0 уз — стоп)";
                    Set(taAField, a.ChangesCourse ? MonitorUi.Num(a.courseDeg, 0) : "");
                    Set(taBField, a.ChangesSpeed ? MonitorUi.Num(a.speedKnots, 1) : "");
                    Placeholder(taAField, "не менять");
                    Placeholder(taBField, "не менять");
                    break;
                case TriggerActionKind.Visibility:
                    taFieldsLabel.text = "Видимость, мили (0 — по погоде)";
                    Set(taAField, MonitorUi.Num(a.visibilityNm, 2));
                    Placeholder(taAField, "");
                    break;
                case TriggerActionKind.Wind:
                    taFieldsLabel.text = "Ветер: сила, уз · откуда, ° (только в модели 3 СС)";
                    Set(taAField, MonitorUi.Num(a.windKnots, 0));
                    Set(taBField, MonitorUi.Num(a.windFromDeg, 0));
                    Placeholder(taAField, "");
                    Placeholder(taBField, "");
                    break;
            }
            Set(taTextField, a.text ?? "");
        }

        var b = new System.Text.StringBuilder();
        b.AppendLine($"<color=#9AA6B2>{TriggerText.Line(tr, s)}</color>");
        foreach (string x in check.Errors) if (About(x, tr) || x.StartsWith("Триггеры:", StringComparison.Ordinal) && x.Contains(tr.id)) b.AppendLine($"<color=#FF7A7A>× {x}</color>");
        foreach (string x in check.Warnings) if (About(x, tr)) b.AppendLine($"<color=#FFC85A>! {x}</color>");
        trInfo.text = b.ToString().TrimEnd();
    }

    private static bool About(string problem, ScenarioTrigger t) => problem.StartsWith($"Триггер {t.id}:", StringComparison.Ordinal);

    private static void Placeholder(TMP_InputField f, string text)
    {
        if (f.placeholder is TextMeshProUGUI ph) ph.text = text;
    }

    // ─────────── Правка ───────────
    private void EditTrigger(Func<ScenarioTrigger, bool> change)
    {
        ScenarioTrigger t = SelTr;
        if (t == null) return;
        EditScenario(() => { if (change(t)) sdoc.Commit(); });
    }

    private void EditTriggerNumber(string text, Func<double, bool> ok, Action<ScenarioTrigger, double> set)
    {
        if (MonitorUi.TryNumber(text, out double v) && ok(v)) EditTrigger(t => { set(t, v); return true; });
        else FillScenarioFields();
    }

    private void EditTriggerClock(string text, Action<ScenarioTrigger, double> set)
    {
        if (TriggerEditing.ParseClock(text, out double s) && s <= 6.0 * 3600.0) EditTrigger(t => { set(t, s); return true; });
        else FillScenarioFields();
    }

    private void EditAction(Func<TriggerAction, bool> change)
    {
        TriggerAction a = SelAct;
        if (a == null) return;
        EditScenario(() => { if (change(a)) sdoc.Commit(); });
    }

    // Поля чисел действия: цель — курс / скорость (пусто — не менять), видимость, ветер — сила / откуда
    private void EditActionValue(string text, bool first)
    {
        TriggerAction a = SelAct;
        if (a == null) return;
        string v = (text ?? "").Trim();
        bool empty = v.Length == 0;
        double d = 0.0;
        if (!empty && !MonitorUi.TryNumber(v, out d)) { FillScenarioFields(); return; }
        switch (a.kind)
        {
            case TriggerActionKind.TargetOrder:
                if (first && (empty || d >= 0.0)) EditAction(x => { x.courseDeg = empty ? -1f : (float)(((d % 360.0) + 360.0) % 360.0); return true; });
                else if (!first && (empty || d >= 0.0 && d <= 40.0)) EditAction(x => { x.speedKnots = empty ? -1f : (float)d; return true; });
                else FillScenarioFields();
                break;
            case TriggerActionKind.Visibility:
                if (!empty && d >= 0.0 && d <= 30.0) EditAction(x => { x.visibilityNm = (float)d; return true; });
                else FillScenarioFields();
                break;
            case TriggerActionKind.Wind:
                if (empty) FillScenarioFields();
                else if (first && d >= 0.0 && d <= 80.0) EditAction(x => { x.windKnots = (float)d; return true; });
                else if (!first) EditAction(x => { x.windFromDeg = (float)(((d % 360.0) + 360.0) % 360.0); return true; });
                else FillScenarioFields();
                break;
        }
    }

    private void SelectTriggerRow(int row)
    {
        int k = trigListTop + row;
        if (!TriggerEditing.Valid(S?.triggers, k)) return;
        EndFieldEdit();
        selTrigger = k;
        selAction = 0;
        mapTrigger = S.triggers[k].condition == TriggerCondition.EnterCircle ? k : -1;
        FillScenarioFields();
    }

    private void ScrollTriggers(int d)
    {
        trigListTop = Mathf.Clamp(trigListTop + d, 0, Math.Max(0, TriggerCount - 1) / TrigRows * TrigRows);
        FillScenarioFields();
    }

    private void AddTrigger()
    {
        if (S == null) return;
        EndFieldEdit();
        int i = -1;
        EditScenario(() => { i = TriggerEditing.Add(S); selTrigger = i; selAction = 0; sdoc.Commit(); });
        Debug.Log($"[Редактор] Триггер {S.triggers[i].id} добавлен.");
    }

    private void DuplicateTrigger()
    {
        if (SelTr == null) return;
        EndFieldEdit();
        EditScenario(() => { int i = TriggerEditing.Duplicate(S, selTrigger); if (i >= 0) { selTrigger = i; sdoc.Commit(); } });
    }

    private void DeleteTrigger()
    {
        if (SelTr == null) return;
        EndFieldEdit();
        string id = SelTr.id;
        bool referenced = S.triggers.Exists(t => t != null && t.condition == TriggerCondition.AfterTrigger && t.afterTrigger == id);
        void Do() => EditScenario(() => { if (TriggerEditing.Remove(S, selTrigger)) { mapTrigger = -1; sdoc.Commit(); } });
        if (!referenced) { Do(); return; }
        Dialog("Удалить триггер?", $"На {id} ссылаются другие триггеры («сработал другой триггер») — у них будет ошибка «Проверки». Удалить (отменить — Ctrl+Z)?", null, true,
               ("Удалить", MonitorUi.StopColor, () => { CloseDialog(); Do(); }), ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    private void CycleCondition(int dir)
    {
        if (SelTr == null) return;
        EditTrigger(t => { TriggerEditing.SetCondition(S, t, Next(t.condition, dir)); return true; });
        mapTrigger = SelTr != null && SelTr.condition == TriggerCondition.EnterCircle ? selTrigger : -1;
    }

    private string CondChoiceTitle()
    {
        ScenarioTrigger t = SelTr;
        if (t == null) return "";
        switch (t.condition)
        {
            case TriggerCondition.Waypoint: return string.IsNullOrEmpty(t.waypoint) ? "в маршруте нет точек" : t.waypoint;
            case TriggerCondition.TargetRange: return string.IsNullOrEmpty(t.target) ? "в сценарии нет судов" : TargetTitle(t.target);
            case TriggerCondition.TaskStage: return S.IsMooring || S.IsTugWork ? $"{t.stage}. {TriggerText.StageTitle(S, t.stage)}" : "нет задания (швартовка / работа буксира)";
            case TriggerCondition.AfterTrigger:
                ScenarioTrigger o = S.triggers.Find(x => x != null && x.id == t.afterTrigger);
                return o != null ? o.Label : string.IsNullOrEmpty(t.afterTrigger) ? "других триггеров нет" : t.afterTrigger + " (нет)";
            default: return "";
        }
    }

    private string TargetTitle(string id)
    {
        if (S?.traffic != null)
            for (int i = 0; i < S.traffic.Count; i++)
                if (S.traffic[i] != null && S.traffic[i].TargetId(i) == id) return $"{id} «{S.traffic[i].name}»";
        return id + " (нет такой цели)";
    }

    private void CycleCondChoice(int dir)
    {
        ScenarioTrigger t = SelTr;
        if (t == null) return;
        switch (t.condition)
        {
            case TriggerCondition.Waypoint: EditTrigger(x => { x.waypoint = TriggerEditing.Cycle(TriggerEditing.WaypointChoices(S), x.waypoint, dir); return true; }); break;
            case TriggerCondition.TargetRange: EditTrigger(x => { x.target = TriggerEditing.Cycle(TriggerEditing.TargetChoices(S), x.target, dir); return true; }); break;
            case TriggerCondition.TaskStage:
                if (S.IsMooring || S.IsTugWork) EditTrigger(x => { x.stage = ((x.stage - 1 + dir) % 4 + 4) % 4 + 1; return true; });
                break;
            case TriggerCondition.AfterTrigger: EditTrigger(x => { x.afterTrigger = TriggerEditing.Cycle(TriggerEditing.TriggerChoices(S, x), x.afterTrigger, dir); return true; }); break;
        }
    }

    private string ActionPagerTitle()
    {
        ScenarioTrigger t = SelTr;
        if (t == null || t.actions == null || t.actions.Count == 0) return "действий нет";
        return $"Действие {selAction + 1} из {t.actions.Count}: {TriggerText.Action(SelAct)}";
    }

    private void StepAction(int dir)
    {
        ScenarioTrigger t = SelTr;
        if (t == null || t.actions.Count == 0) return;
        EndFieldEdit();
        selAction = ((selAction + dir) % t.actions.Count + t.actions.Count) % t.actions.Count;
        FillScenarioFields();
    }

    private void AddAction()
    {
        if (SelTr == null) return;
        EndFieldEdit();
        EditTrigger(t => { selAction = TriggerEditing.AddAction(S, t); return true; });
    }

    private void DeleteAction()
    {
        if (SelAct == null) return;
        EndFieldEdit();
        EditTrigger(t => { if (!TriggerEditing.RemoveAction(t, selAction)) return false; selAction = Math.Max(0, selAction - 1); return true; });
    }

    private void CycleActionKind(int dir)
    {
        if (SelAct == null) return;
        EditAction(a => { TriggerEditing.SetActionKind(S, a, Next(a.kind, dir)); return true; });
    }

    private string ActChoiceTitle()
    {
        TriggerAction a = SelAct;
        if (a == null) return "";
        if (a.kind == TriggerActionKind.Weather) return ScenarioText.Weather(a.weather);
        return string.IsNullOrEmpty(a.target) ? "в сценарии нет судов" : TargetTitle(a.target);
    }

    private void CycleActChoice(int dir)
    {
        TriggerAction a = SelAct;
        if (a == null) return;
        if (a.kind == TriggerActionKind.Weather) EditAction(x => { x.weather = Next(x.weather, dir); return true; });
        else if (a.kind == TriggerActionKind.TargetOrder) EditAction(x => { x.target = TriggerEditing.Cycle(TriggerEditing.TargetChoices(S), x.target, dir); return true; });
    }

    private void ResetTriggerState()
    {
        mapTrigger = hotTrigCenter = hotTrigRing = -1;
        hotTrigEdge = dragTrigCenter = dragTrigEdge = false;
        if (!TriggerEditing.Valid(S?.triggers, selTrigger)) { selTrigger = -1; selAction = 0; }
    }

    // ─────────── Карта: круги ───────────
    private void ClearTriggerHot()
    {
        hotTrigCenter = hotTrigRing = -1;
        hotTrigEdge = false;
    }

    /// <summary>Ручки выделенного круга (край, центр) — поверх маршрута; true — под курсором.</summary>
    private bool TriggerHandleHit(double px, double py)
    {
        if (!TriggerEditing.Valid(S?.triggers, mapTrigger)) return false;
        if (TriggerEditing.HitEdge(S, mapTrigger, viewport, px, py)) return hotTrigEdge = true;
        viewport.ToPixel(S.triggers[mapTrigger].center, out double cx, out double cy);
        if ((cx - px) * (cx - px) + (cy - py) * (cy - py) > TriggerEditing.CenterHitPx * TriggerEditing.CenterHitPx) return false;
        hotTrigCenter = mapTrigger;
        return true;
    }

    /// <summary>Центры и окружности остальных кругов — под маршрутом (точки и участки важнее); true — под курсором.</summary>
    private bool TriggerShapeHit(double px, double py)
    {
        if (S?.triggers == null) return false;
        hotTrigCenter = TriggerEditing.HitCenter(S, viewport, px, py);
        if (hotTrigCenter >= 0) return true;
        hotTrigRing = TriggerEditing.HitRing(S, viewport, px, py);
        return hotTrigRing >= 0;
    }

    private bool TriggerPress()
    {
        if (hotTrigEdge) { dragTrigEdge = true; sMoved = true; return true; }
        int i = hotTrigCenter >= 0 ? hotTrigCenter : hotTrigRing;
        if (i < 0) return false;
        SelectStart(false);
        SelectPoint(-1);
        SelectTraffic(-1);
        mapTrigger = selTrigger = i;
        selAction = 0;
        if (hotTrigCenter >= 0) dragTrigCenter = true;   // по окружности — только выбрать
        FillScenarioFields();
        return true;
    }

    private void TriggerDrag(WorldPosition world, WorldPosition snapped, Vector2 local)
    {
        if (!TriggerEditing.Valid(S?.triggers, mapTrigger)) return;
        ScenarioTrigger t = S.triggers[mapTrigger];
        if (dragTrigCenter && !sMoved && (local - pressLocal).magnitude > 3f) sMoved = true;
        if (dragTrigCenter && sMoved && !t.center.Equals(snapped)) { t.center = snapped; FillScenarioFields(); }
        if (dragTrigEdge)
        {
            float r = TriggerEditing.RadiusFrom(t, world, snap ? TriggerEditing.RadiusStep : 1.0);
            if (r != t.radiusM) { t.radiusM = r; FillScenarioFields(); }
        }
    }

    /// <summary>Отпустили кнопку: конец перетаскивания круга; true — было что отпускать.</summary>
    private bool TriggerRelease()
    {
        if (!TriggerBusy) return false;
        dragTrigCenter = dragTrigEdge = false;
        return true;
    }

    private void TriggerOverlay()
    {
        overlay.TriggerCircles.Clear();
        ScenarioDefinition s = S;
        if (s?.triggers == null) return;
        for (int i = 0; i < s.triggers.Count; i++)
        {
            ScenarioTrigger t = s.triggers[i];
            if (t == null || t.condition != TriggerCondition.EnterCircle || t.radiusM <= 0f) continue;
            bool sel = i == mapTrigger;
            overlay.TriggerCircles.Add(new EditorMapOverlay.TriggerCircleMark
            {
                Center = t.center, Radius = t.radiusM, Edge = TriggerEditing.EdgeHandle(t), Selected = sel,
                CenterHot = i == hotTrigCenter || sel && dragTrigCenter, EdgeHot = sel && (hotTrigEdge || dragTrigEdge), RingHot = i == hotTrigRing
            });
        }
    }

    /// <summary>Подписи кругов («Тр2») — над окружностью, в пул подписей с номера used.</summary>
    private int TriggerLabels(Rect rc, int used)
    {
        ScenarioDefinition s = S;
        if (s?.triggers == null || s.triggers.Count > 40) return used;
        for (int i = 0; i < s.triggers.Count; i++)
        {
            ScenarioTrigger t = s.triggers[i];
            if (t == null || t.condition != TriggerCondition.EnterCircle || t.radiusM <= 0f) continue;
            Vector2 top = overlay.Px(new WorldPosition(t.center.East, t.center.North + t.radiusM), rc);
            used = PutLabel(rc, used, top + new Vector2(6f, 10f), t.id, i == mapTrigger ? EditorMapOverlay.HandleHot : EditorMapOverlay.TriggerColor);
        }
        return used;
    }

    private string TriggerStatus()
    {
        int i = TriggerBusy || hotTrigEdge ? mapTrigger : hotTrigCenter >= 0 ? hotTrigCenter : hotTrigRing;
        if (!TriggerEditing.Valid(S?.triggers, i)) return "";
        ScenarioTrigger t = S.triggers[i];
        string what = dragTrigEdge || hotTrigEdge ? $" — радиус {MonitorUi.Num(t.radiusM, 0)} м (тянуть)"
                    : dragTrigCenter ? " — центр (тянуть)"
                    : " — щелчок: выбрать (центр — тянуть, квадрат на краю — радиус); правка — «Триггеры…»";
        return $" · триггер {TriggerText.Line(t, S)}{what}";
    }
}
