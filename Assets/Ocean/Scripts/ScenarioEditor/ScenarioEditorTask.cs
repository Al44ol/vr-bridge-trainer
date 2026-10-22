// ============================================================
// ScenarioEditorTask.cs — Лист «Задание» в режиме «Сценарий» (запрос 21.5б; часть ScenarioEditorApp, partial-класс)
//
// Блоки задания, сделанные в запросе 19, — теперь правятся в редакторе (TaskEditing):
//   • «Швартовка своего судна» (19-01): вкл/выкл, причал < >, контрольное время; модель 3 СС и «ошвартован» — сами;
//   • «Буксир по вызову» (19-02): галочка tugAvailable (ветер — лист «Условия»);
//   • «Работа своего буксира» (19-03): вкл/выкл (своё судно — буксир), причал < >, клиент < >, зазор и сдвиг клиента,
//     дистанция буксира, контрольное время, «случайное событие»; старт своего буксира и линия «Старт → Борт судна»
//     пересчитываются при каждой правке (TaskEditing.PlaceTugWork — как в тренажёре).
// Пороги этапов — серым, не правятся (методика). Включили блок, а набор критериев не тот — вопрос «Поставить набор …?»,
// и кнопка «Набор по заданию» на листе. На карте — причал задания (голубая кромка) и клиент у причала (серый силуэт).
// ============================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class ScenarioEditorApp
{
    private RectTransform taskSheet;
    private TMP_InputField mTimeField, wGapField, wAlongField, wTugField, wTimeField;
    private TextMeshProUGUI mInfo, wInfo, taskInfo;

    private void BuildTaskSheet()
    {
        RectTransform p = taskSheet = Sheet("TaskSheet", "Задание: швартовка, буксир", out float y0);
        float colW = 520f, x1 = 20f, x2 = 20f + colW + 60f, half = (colW - MonitorUi.Gap) * 0.5f;
        float y = y0, x;

        // Швартовка своего судна
        SHeader(p, "Швартовка своего судна (как 19-01)", x1, ref y);
        x = x1;
        ui.Button(p, ref x, y, colW, "", MonitorUi.NeutralColor, ToggleMooring,
                  () => S != null && S.IsMooring ? "Швартовка: включена" : "Швартовка: выключена",
                  () => S != null && S.IsMooring ? MonitorUi.ActiveColor : MonitorUi.NeutralColor,
                  () => S != null && (S.IsMooring || S.ownShipType != ScenarioShipType.Tug));
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        SLabel(p, "Причал (на карте — голубая кромка)", x1, ref y, colW);
        SCycler(p, x1, ref y, colW, () => CycleTaskBerth(true, -1), () => CycleTaskBerth(true, 1),
                () => S == null ? "" : TaskEditing.BerthTitle(S.waterArea, S.mooring.berthIndex));
        mTimeField = SField(p, "Контрольное время, мин", x1, ref y, half,
                            v => EditScenarioNumber(v, d => d > 0.0 && d <= 120.0, d => S.mooring.targetTimeS = (float)Math.Round(d * 60.0)));
        mInfo = MonitorUi.Text(p, "", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(mInfo.rectTransform, x1, y, colW, 64f);
        y -= 70f;

        SHeader(p, "Буксир по вызову (как 19-02)", x1, ref y);
        x = x1;
        ui.Button(p, ref x, y, colW, "", MonitorUi.NeutralColor, () => EditScenario(() => { S.tugAvailable = !S.tugAvailable; if (S.tugAvailable) S.motionModel = ScenarioMotionModel.Manoeuvring3Dof; sdoc.Commit(); }),
                  () => S != null && S.tugAvailable ? "Буксир можно вызвать (меню B): да" : "Буксир можно вызвать (меню B): нет",
                  () => S != null && S.tugAvailable ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => S != null && !S.IsTugWork);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        TextMeshProUGUI th = MonitorUi.Text(p, "Буксир работает в модели 3 СС (ставится сама); ветер — лист «Условия». " +
                                               "Со швартовкой — набор TugBerthing (G1–G4 оцениваются, если буксир вызывали).",
                                            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(th.rectTransform, x1, y, colW, 44f);

        // Работа своего буксира
        y = y0;
        SHeader(p, "Работа своего буксира (как 19-03)", x2, ref y);
        x = x2;
        ui.Button(p, ref x, y, colW, "", MonitorUi.NeutralColor, ToggleTugWork,
                  () => S != null && S.IsTugWork ? "Работа буксира: включена (своё судно — буксир)" : "Работа буксира: выключена",
                  () => S != null && S.IsTugWork ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        SLabel(p, "Причал (на карте — голубая кромка, клиент — серый)", x2, ref y, colW);
        SCycler(p, x2, ref y, colW, () => CycleTaskBerth(false, -1), () => CycleTaskBerth(false, 1),
                () => S == null ? "" : TaskEditing.BerthTitle(S.waterArea, S.tugWork.berthIndex));
        SLabel(p, "Судно-клиент (модель из каталога)", x2, ref y, colW);
        SCycler(p, x2, ref y, colW, () => CycleClient(-1), () => CycleClient(1),
                () => S == null ? "" : S.tugWorkClient != null ? ShipLabel(S.tugWorkClient) : "не задан");
        SLabel(p, "Клиент: зазор до кромки, м · сдвиг вдоль причала, м (> 0 — к концу B)", x2, ref y, colW);
        wGapField = MonitorUi.Field(p, x2, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                    v => EditTugWork(v, d => d > 0.0 && d <= 500.0, d => S.tugWork.clientGapM = (float)d));
        wAlongField = MonitorUi.Field(p, x2 + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                      v => EditTugWork(v, d => Math.Abs(d) <= 2000.0, d => S.tugWork.clientAlongM = (float)d));
        y -= 34f + MonitorUi.Gap;
        SLabel(p, "Буксир на старте: от борта клиента, м · контрольное время, мин", x2, ref y, colW);
        wTugField = MonitorUi.Field(p, x2, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                    v => EditTugWork(v, d => d >= 5.0 && d <= 2000.0, d => S.tugWork.tugDistanceM = (float)d));
        wTimeField = MonitorUi.Field(p, x2 + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                     v => EditTugWork(v, d => d > 0.0 && d <= 120.0, d => S.tugWork.targetTimeS = (float)Math.Round(d * 60.0)));
        y -= 34f + MonitorUi.Gap;
        x = x2;
        ui.Button(p, ref x, y, colW, "", MonitorUi.NeutralColor, () => EditScenario(() => { S.tugWork.angleEvent = !S.tugWork.angleEvent; sdoc.Commit(); }),
                  () => S != null && S.tugWork.angleEvent ? "Случайное событие у причала: да (G12, G13)" : "Случайное событие у причала: нет",
                  () => S != null && S.tugWork.angleEvent ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => S != null && S.IsTugWork);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        wInfo = MonitorUi.Text(p, "", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(wInfo.rectTransform, x2, y, colW, 110f);
        y -= 116f;
        x = x2;
        ui.Button(p, ref x, y, colW, "Поставить буксир по расстановке", MonitorUi.NeutralColor,
                  () => EditScenario(() => { if (TaskEditing.PlaceTugWork(S)) sdoc.Commit(); }), null, null, () => S != null && S.IsTugWork);

        // Внизу — набор критериев по заданию
        float yb = -SheetHeight + 110f;
        taskInfo = MonitorUi.Text(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(taskInfo.rectTransform, x1, yb, SheetWidth - 40f - 330f, 60f);
        x = SheetWidth - 20f - 320f;
        ui.Button(p, ref x, yb, 320f, "", MonitorUi.ButtonColor, ApplySuggestedSet,
                  () => { string n = S != null ? TaskEditing.SuggestedSet(S) : null; return n != null ? "Набор по заданию: " + n.Replace("Assessment_", "") : "Набор по заданию"; },
                  null, () => S != null && SetDiffers());
    }

    // ─────────── Заполнить ───────────
    private void FillTaskSheet()
    {
        ScenarioDefinition s = S;
        if (s == null || taskSheet == null) return;
        Set(mTimeField, MonitorUi.Num(s.mooring.targetTimeS / 60.0, 0));
        Set(wGapField, MonitorUi.Num(s.tugWork.clientGapM, 0));
        Set(wAlongField, MonitorUi.Num(s.tugWork.clientAlongM, 0));
        Set(wTugField, MonitorUi.Num(s.tugWork.tugDistanceM, 0));
        Set(wTimeField, MonitorUi.Num(s.tugWork.targetTimeS / 60.0, 0));
        foreach (TMP_InputField f in new[] { mTimeField }) f.interactable = s.IsMooring;
        foreach (TMP_InputField f in new[] { wGapField, wAlongField, wTugField, wTimeField }) f.interactable = s.IsTugWork;

        MooringTaskDefinition m = s.mooring;
        mInfo.text = "Пороги этапов (методика, не правятся): лагом — зазор ≤ " + MonitorUi.Num(m.alongsideGapM, 0) + " м, угол ≤ " + MonitorUi.Num(m.alongsideAngleDeg, 0) +
                     "°; у кранцев — ≤ " + MonitorUi.Num(m.stopGapM, 1) + " м, ход ≤ " + MonitorUi.Num(m.stopSpeedKnots, 1) + " уз; ошвартован — ход ≤ " +
                     MonitorUi.Num(m.mooredSpeedKnots, 1) + " уз " + MonitorUi.Num(m.mooredHoldS, 0) + " с." +
                     (s.IsMooring && s.ownShipType == ScenarioShipType.Tug ? "\n<color=#FFC85A>Швартовка — для модели 45 м (своё судно сейчас буксир).</color>" : "");
        TugWorkDefinition w = s.tugWork;
        string place = "";
        if (s.IsTugWork)
        {
            TugWorkLayout l = TaskEditing.Layout(s);
            place = l.Ok ? $"Старт буксира: E {MonitorUi.Num(l.TugStart.East, 0)}, N {MonitorUi.Num(l.TugStart.North, 0)} м, курс {MonitorUi.Num(l.TugHeadingDeg, 0)}° (ставится сам).\n"
                         : $"<color=#FF7A7A>Расстановка не строится: {l.Problem}.</color>\n";
        }
        wInfo.text = place + "Пороги (методика, не правятся): касание ± " + MonitorUi.Num(w.contactZoneM, 0) + " м от миделя; у кранцев ≤ " + MonitorUi.Num(w.atFendersGapM, 1) +
                     " м; концы — при ≤ " + MonitorUi.Num(w.linesGapM, 1) + " м; закреплены — ход ≤ " + MonitorUi.Num(w.securedSpeedKnots, 1) + " уз " +
                     MonitorUi.Num(w.securedHoldS, 0) + " с; отойти ≥ " + MonitorUi.Num(w.clearDistanceM, 0) + " м; событие — при зазоре " +
                     MonitorUi.Num(w.eventGapMinM, 0) + "–" + MonitorUi.Num(w.eventGapMaxM, 0) + " м, угол " + MonitorUi.Num(w.eventAngleMinDeg, 0) + "–" + MonitorUi.Num(w.eventAngleMaxDeg, 0) + "°.";
        string sug = TaskEditing.SuggestedSet(s);
        taskInfo.text = $"Набор критериев: {(s.assessment != null ? s.assessment.name : "по умолчанию")}" +
                        (sug == null ? " · задание набор не определяет" : SetDiffers() ? $" · <color=#FFC85A>по заданию — {sug}</color>" : " · как по заданию") +
                        $"\nЗавершение: {ScenarioText.EndConditions(s)} · модель движения: {MotionTitle(s.motionModel)}";
    }

    private bool SetDiffers()
    {
        string n = TaskEditing.SuggestedSet(S);
        return n != null && (S.assessment == null || S.assessment.name != n) && TaskEditing.FindSet(Catalog, n) != null;
    }

    // ─────────── Правка ───────────
    private void ToggleMooring()
    {
        if (S == null) return;
        EditScenario(() => { TaskEditing.SetMooring(S, !S.IsMooring); sdoc.Commit(); });
        mapDirty = true;
        AskSuggestedSet();
    }

    private void ToggleTugWork()
    {
        if (S == null) return;
        bool on = !S.IsTugWork;
        ShipParameters client = TaskEditing.FindShip(Catalog, TaskEditing.ClientDefault) ?? FirstClient();
        EditScenario(() =>
        {
            TaskEditing.SetTugWork(S, on, on ? TrainerScenes.TugShip : null, client);
            sdoc.Commit();
        });
        selStart = false;
        selPoint = -1;
        ShowPropsGroup();
        if (on) AskSuggestedSet();
    }

    private ShipParameters FirstClient()
    {
        foreach (ShipParameters p in ClientChoices()) return p;
        return null;
    }

    // Клиент — суда каталога, кроме своего буксира и катера (им буксир не нужен)
    private List<ShipParameters> ClientChoices()
    {
        var list = new List<ShipParameters>();
        foreach (ShipParameters p in Catalog.Ships)
            if (p != null && p != TrainerScenes.TugShip && !p.name.Contains("Tug") && !p.name.Contains("FastBoat")) list.Add(p);
        return list;
    }

    private void CycleClient(int d)
    {
        if (S == null || !S.IsTugWork) return;
        List<ShipParameters> list = ClientChoices();
        if (list.Count == 0) return;
        int k = S.tugWorkClient != null ? list.FindIndex(x => x.name == S.tugWorkClient.name) : -1;
        ShipParameters next = list[((k < 0 ? 0 : k) + d + list.Count) % list.Count];
        EditScenario(() => { S.tugWorkClient = next; TaskEditing.PlaceTugWork(S); sdoc.Commit(); });
    }

    private void CycleTaskBerth(bool mooring, int d)
    {
        if (S == null || (mooring ? !S.IsMooring : !S.IsTugWork)) return;
        int cur = mooring ? S.mooring.berthIndex : S.tugWork.berthIndex;
        int next = TaskEditing.NextBerth(S.waterArea, cur, d);
        if (next < 0) return;
        EditScenario(() =>
        {
            if (mooring) S.mooring.berthIndex = next;
            else { S.tugWork.berthIndex = next; TaskEditing.PlaceTugWork(S); }
            sdoc.Commit();
        });
    }

    private void EditTugWork(string text, Func<double, bool> ok, Action<double> set)
    {
        if (S == null || !S.IsTugWork) { FillScenarioFields(); return; }
        EditScenarioNumber(text, ok, v => { set(v); TaskEditing.PlaceTugWork(S); });
    }

    private void AskSuggestedSet()
    {
        if (S == null || !SetDiffers()) return;
        string n = TaskEditing.SuggestedSet(S);
        Dialog("Набор критериев", $"Задание сценария оценивается набором {n}, а сейчас — {(S.assessment != null ? S.assessment.name : "по умолчанию")}. Поставить {n}? " +
                                  "Отличия галочек сохранятся; совпавшие с новым набором — уберутся.", null, true,
               ("Поставить", MonitorUi.StartColor, () => { CloseDialog(); ApplySuggestedSet(); }),
               ("Оставить", MonitorUi.NeutralColor, CloseDialog));
    }

    private void ApplySuggestedSet()
    {
        AssessmentCriteria set = TaskEditing.FindSet(Catalog, TaskEditing.SuggestedSet(S));
        if (set == null) return;
        EditScenario(() => { CriteriaEditing.SetBase(S, set); sdoc.Commit(); });
    }

    // ─────────── Карта ───────────
    /// <summary>Причал задания и клиент у причала — в слой карты.</summary>
    private void TaskOverlay()
    {
        ScenarioDefinition s = S;
        overlay.ShowTaskBerth = overlay.ShowClient = false;
        if (s == null) return;
        BerthArea b = s.IsTugWork ? TaskEditing.Berth(s.waterArea, s.tugWork.berthIndex) : s.IsMooring ? TaskEditing.Berth(s.waterArea, s.mooring.berthIndex) : null;
        if (b != null) { overlay.ShowTaskBerth = true; overlay.TaskBerthA = b.a; overlay.TaskBerthB = b.b; }
        if (!s.IsTugWork) return;
        TugWorkLayout l = TaskEditing.Layout(s);
        if (!l.Ok) return;
        overlay.ShowClient = true;
        overlay.ClientPos = l.ClientPos;
        overlay.ClientHeading = (float)l.ClientHeadingDeg;
        overlay.ClientLength = s.tugWorkClient.length;
        overlay.ClientBeam = s.tugWorkClient.beam;
    }
}
