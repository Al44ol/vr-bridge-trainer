// ============================================================
// ScenarioEditorSheets.cs — Листы сценария поверх карты и «Пройти в тренажёре» (запрос 21.4б;
// часть ScenarioEditorApp, partial-класс)
//
// Правая колонка занята свойствами, поэтому три раздела сценария — листы на месте карты (кнопки
// «Условия…», «Критерии…», «Брифинг…» в свойствах сценария; Esc или «Готово» — закрыть):
//   • «Условия и завершение» — погода, время суток, видимость (мили; в файле — метры), безопасная скорость,
//     дальность обнаружения, цели на планшете, ветер (сила, откуда, сектор случайности; «Поставить 3 СС»),
//     лимит времени (мин), галочки условий завершения;
//   • «Критерии оценки» — базовый набор < >, 50 критериев по группам: «вкл» / «обяз» (только отличия от набора
//     пишутся в сценарий — CriteriaEditing), пороги и вес — серым, не правятся; «Как в наборе»;
//   • «Брифинг» — многострочное поле и «Заготовка» (ScenarioChecks.Briefing; написанный текст — с вопросом).
//   • 21.5б — «Задание» (швартовка, буксир по вызову, работа буксира) — ScenarioEditorTask.cs; 21.5а — листы непрозрачные.
// Каждая правка — шаг отмены (Ctrl+Z работает и с открытым листом).
// «Пройти в тренажёре» (палитра режима «Сценарий»): ошибки проверки — не запускается; иначе сценарий
// сохраняется, тренажёр загружается заново, ScenarioFiles находит файл и открывает сценарий на брифинге
// (TrainerScenes.PlayOnLoad). «Редактор сценариев» в окне инструктора — обратно к этому же сценарию
// (TrainerScenes.LastScenarioFile).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public partial class ScenarioEditorApp
{
    private RectTransform condSheet, critSheet, briefSheet;
    private TMP_InputField cVisField, cSafeField, cDetField, cWindField, cWindFromField, cSectorField, cLimitField, briefField;
    private TextMeshProUGUI cInfo, critInfo;
    private readonly List<(string code, TextMeshProUGUI text)> critRows = new List<(string, TextMeshProUGUI)>();
    private AssessmentSettings critEff, critBase;

    private bool SheetOpen => Active(condSheet) || Active(critSheet) || Active(briefSheet) || Active(taskSheet);
    private static bool Active(RectTransform r) => r != null && r.gameObject.activeSelf;

    // ─────────── Построение ───────────
    private RectTransform Sheet(string name, string title, out float y)
    {
        RectTransform s = MonitorUi.Panel(root, name, MonitorUi.SheetColor);   // 21.5а: непрозрачный — карта и подписи не просвечивают
        MonitorUi.Stretch(s, PaletteWidth + Pad, StatusBar + Pad, PropsWidth + Pad, TopBar + Pad);
        TextMeshProUGUI t = MonitorUi.Text(s, title, 22f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(t.rectTransform, 20f, -14f, 900f, 32f);
        float x = SheetWidth - 20f - 160f;
        ui.Button(s, ref x, -10f, 160f, "Готово", MonitorUi.StartColor, CloseSheets);
        s.gameObject.SetActive(false);
        y = -60f;
        return s;
    }

    private static float SheetWidth => 1920f - PaletteWidth - PropsWidth - 2f * Pad;
    private static float SheetHeight => 1080f - TopBar - StatusBar - 2f * Pad;

    private void BuildSheets()
    {
        BuildConditionsSheet();
        BuildCriteriaSheet();
        BuildBriefingSheet();
        BuildTaskSheet();   // 21.5б
    }

    private static void SLabel(RectTransform p, string text, float x, ref float y, float w)
    {
        TextMeshProUGUI t = MonitorUi.Text(p, text, 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(t.rectTransform, x, y, w, 22f);
        y -= 22f;
    }

    private static void SHeader(RectTransform p, string text, float x, ref float y)
    {
        TextMeshProUGUI t = MonitorUi.Text(p, text, 18f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(t.rectTransform, x, y, 560f, 26f);
        y -= 30f;
    }

    private TMP_InputField SField(RectTransform p, string label, float x, ref float y, float w, UnityEngine.Events.UnityAction<string> onEnd)
    {
        SLabel(p, label, x, ref y, w);
        TMP_InputField f = MonitorUi.Field(p, x, y, w, 34f, "", false, TMP_InputField.ContentType.Standard, onEnd);
        y -= 34f + MonitorUi.Gap;
        return f;
    }

    private void SCycler(RectTransform p, float x, ref float y, float w, Action prev, Action next, Func<string> text)
    {
        float xx = x;
        ui.Button(p, ref xx, y, 48f, "<", MonitorUi.NeutralColor, () => prev());
        ui.Button(p, ref xx, y, w - 2f * 48f - 2f * MonitorUi.Gap, "", MonitorUi.NeutralColor, () => next(), text);
        ui.Button(p, ref xx, y, 48f, ">", MonitorUi.NeutralColor, () => next());
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
    }

    private void BuildConditionsSheet()
    {
        RectTransform p = condSheet = Sheet("ConditionsSheet", "Условия и завершение", out float y0);
        float colW = 520f, x1 = 20f, x2 = 20f + colW + 60f;
        float y = y0;
        SHeader(p, "Условия плавания", x1, ref y);
        SLabel(p, "Погода (волнение, видимость по умолчанию)", x1, ref y, colW);
        SCycler(p, x1, ref y, colW, () => CycleWeather(-1), () => CycleWeather(1), () => S == null ? "" : ScenarioText.Weather(S.weather));
        SLabel(p, "Время суток", x1, ref y, colW);
        SCycler(p, x1, ref y, colW, () => CycleTimeOfDay(-1), () => CycleTimeOfDay(1),
                () => S == null ? "" : char.ToUpper(ScenarioText.TimeOfDayTitle(S.timeOfDay)[0]) + ScenarioText.TimeOfDayTitle(S.timeOfDay).Substring(1));
        cVisField = SField(p, "Видимость сценария, мили (0 — по погоде; C4)", x1, ref y, colW,
                           v => EditScenarioNumber(v, d => d >= 0.0 && d <= 30.0, d => S.visibilityMeters = (float)Math.Round(d * 1852.0)));
        cSafeField = SField(p, "Безопасная скорость, уз (0 — не оценивается; F3, F4)", x1, ref y, colW,
                            v => EditScenarioNumber(v, d => d >= 0.0 && d <= 40.0, d => S.safeSpeedKnots = (float)d));
        cDetField = SField(p, "Дальность обнаружения целей, мили (0 — по видимости и огням)", x1, ref y, colW,
                           v => EditScenarioNumber(v, d => d >= 0.0 && d <= 30.0, d => S.detectionRangeNm = (float)d));
        float x = x1;
        ui.Button(p, ref x, y, colW, "", MonitorUi.NeutralColor, () => EditScenario(() => { S.hideTargetsOnPlotter = !S.hideTargetsOnPlotter; sdoc.Commit(); }),
                  () => S != null && S.hideTargetsOnPlotter ? "Цели на планшете: скрыты (только глазами и на слух)" : "Цели на планшете: показываются",
                  () => S != null && S.hideTargetsOnPlotter ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;

        y = y0;
        SHeader(p, "Ветер (действует только в модели 3 СС)", x2, ref y);
        float half = (colW - MonitorUi.Gap) * 0.5f;
        SLabel(p, "Сила, уз · откуда, ° (0 = N)", x2, ref y, colW);
        cWindField = MonitorUi.Field(p, x2, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                     v => EditScenarioNumber(v, d => d >= 0.0 && d <= 80.0, d => S.windKnots = (float)d));
        cWindFromField = MonitorUi.Field(p, x2 + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                         v => EditScenarioNumber(v, d => true, d => S.windFromDeg = (float)(((d % 360.0) + 360.0) % 360.0)));
        y -= 34f + MonitorUi.Gap;
        cSectorField = SField(p, "Случайное направление: сектор, ° (0 — как задано, 360 — любое)", x2, ref y, colW,
                              v => EditScenarioNumber(v, d => d >= 0.0 && d <= 360.0, d => S.windRandomSectorDeg = (float)d));
        x = x2;
        ui.Button(p, ref x, y, colW, "Поставить модель движения 3 СС", MonitorUi.WarnColor,
                  () => EditScenario(() => { S.motionModel = ScenarioMotionModel.Manoeuvring3Dof; sdoc.Commit(); }), null, null,
                  () => S != null && S.windKnots > 0f && S.motionModel != ScenarioMotionModel.Manoeuvring3Dof);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;

        SHeader(p, "Лимит и завершение", x2, ref y);
        cLimitField = SField(p, "Лимит времени, мин (0 — без лимита)", x2, ref y, colW,
                             v => EditScenarioNumber(v, d => d >= 0.0 && d <= 600.0, d => S.timeLimitSeconds = (float)Math.Round(d * 60.0)));
        SLabel(p, "Упражнение завершается (досрочно — инструктор, всегда):", x2, ref y, colW);
        var ends = new (ScenarioEndConditions f, string t)[]
        {
            (ScenarioEndConditions.Arrival, "прибытие в последнюю точку"), (ScenarioEndConditions.Grounding, "посадка на мель"),
            (ScenarioEndConditions.Collision, "столкновение"), (ScenarioEndConditions.TimeLimit, "истечение времени"),
            (ScenarioEndConditions.Moored, "ошвартован (швартовка)"), (ScenarioEndConditions.TugWorkDone, "задание буксира выполнено")
        };
        for (int i = 0; i < ends.Length; i++)
        {
            var e = ends[i];
            if (i % 2 == 0) x = x2;
            ui.Button(p, ref x, y, half, "", MonitorUi.NeutralColor, () => EditScenario(() => { S.endConditions ^= e.f; sdoc.Commit(); }),
                      () => (S != null && S.Ends(e.f) ? "+ " : "") + e.t,
                      () => S != null && S.Ends(e.f) ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, MonitorUi.RowHeight, 14f);
            if (i % 2 == 1) y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
        y -= 2f * MonitorUi.Gap;
        cInfo = MonitorUi.Text(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(cInfo.rectTransform, x1, -SheetHeight + 150f, SheetWidth - 40f, 130f);
    }

    private void BuildCriteriaSheet()
    {
        RectTransform p = critSheet = Sheet("CriteriaSheet", "Критерии оценки", out float y);
        float x = 20f;
        SLabel(p, "Набор оценки (пороги и веса — методика, не правятся; в сценарий пишутся только отличия галочек)", x, ref y, 1100f);
        SCycler(p, x, ref y, 520f, () => CycleCriteriaSet(-1), () => CycleCriteriaSet(1), () => S == null ? "" : S.assessment != null ? S.assessment.name : "по умолчанию");
        float xr = 20f + 520f + 20f;
        ui.Button(p, ref xr, y + MonitorUi.RowHeight + MonitorUi.Gap, 200f, "Как в наборе", MonitorUi.NeutralColor,
                  () => EditScenario(() => { if (CriteriaEditing.Reset(S)) sdoc.Commit(); }), null, null,
                  () => S != null && S.criteriaSwitches != null && S.criteriaSwitches.Count > 0);
        critInfo = MonitorUi.Text(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(critInfo.rectTransform, xr + 210f, y + MonitorUi.RowHeight + MonitorUi.Gap, 420f, 40f);

        // Две колонки: группы по порядку, строка — код и название, «вкл», «обяз»
        const float rowH = 26f, btnW = 62f;
        float colW = (SheetWidth - 60f) * 0.5f, top = y - 4f, bottom = -SheetHeight + 16f;
        int col = 0;
        float cx = 20f, cy = top;
        foreach (AssessmentGroup g in (AssessmentGroup[])Enum.GetValues(typeof(AssessmentGroup)))
        {
            var items = new List<CriterionInfo>();
            foreach (CriterionInfo c in AssessmentCatalog.All) if (c.Group == g) items.Add(c);
            if (items.Count == 0) continue;
            if (cy - rowH * (items.Count + 1) < bottom && col == 0) { col = 1; cx = 20f + colW + 20f; cy = top; }
            TextMeshProUGUI h = MonitorUi.Text(p, CriteriaEditing.GroupTitle(g), 16f, MonitorUi.TitleColor, TextAlignmentOptions.BottomLeft);
            h.fontStyle = FontStyles.Bold;
            MonitorUi.TopLeft(h.rectTransform, cx, cy, colW, rowH);
            cy -= rowH;
            foreach (CriterionInfo c in items)
            {
                string code = c.Code;
                TextMeshProUGUI t = MonitorUi.Text(p, "", 14f, Color.white, TextAlignmentOptions.Left);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                MonitorUi.TopLeft(t.rectTransform, cx, cy, colW - 2f * btnW - 2f * MonitorUi.Gap, rowH);
                critRows.Add((code, t));
                float bx = cx + colW - 2f * btnW - MonitorUi.Gap;
                ui.Button(p, ref bx, cy - 1f, btnW, "", MonitorUi.NeutralColor, () => EditScenario(() => { if (CriteriaEditing.ToggleEnabled(S, code)) sdoc.Commit(); }),
                          () => CritOn(code) ? "вкл" : "выкл", () => CritOn(code) ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, rowH - 2f, 13f);
                ui.Button(p, ref bx, cy - 1f, btnW, "", MonitorUi.NeutralColor, () => EditScenario(() => { if (CriteriaEditing.ToggleMandatory(S, code)) sdoc.Commit(); }),
                          () => CritOn(code) && CritMan(code) ? "обяз" : "—", () => CritOn(code) && CritMan(code) ? MonitorUi.WarnColor : MonitorUi.NeutralColor,
                          () => CritOn(code), rowH - 2f, 13f);
                cy -= rowH;
            }
            cy -= 6f;
        }
    }

    private bool CritOn(string code) => critEff != null && critEff.Get(code).enabled;
    private bool CritMan(string code) => critEff != null && critEff.Get(code).mandatory;

    private void BuildBriefingSheet()
    {
        RectTransform p = briefSheet = Sheet("BriefingSheet", "Брифинг", out float y);
        SLabel(p, "Текст видят обучающийся (панель сценария в шлеме) и инструктор перед началом упражнения", 20f, ref y, 1100f);
        float h = SheetHeight + y - 70f;
        briefField = MonitorUi.Field(p, 20f, y, SheetWidth - 40f, h, "задача, условия, порядок действий…", true, TMP_InputField.ContentType.Standard,
                                     v => { if (S != null && (v ?? "") != (S.briefing ?? "")) EditScenario(() => { S.briefing = v ?? ""; sdoc.Commit(); }); }, 17f);
        float x = 20f;
        ui.Button(p, ref x, y - h - 10f, 300f, "Заготовка по сценарию", MonitorUi.ButtonColor, BriefingTemplate);
    }

    // ─────────── Открыть, закрыть, заполнить ───────────
    private void OpenSheet(RectTransform s)
    {
        if (sdoc == null) return;
        EndFieldEdit();
        foreach (RectTransform r in new[] { condSheet, critSheet, briefSheet, taskSheet }) if (r != null) r.gameObject.SetActive(r == s);
        FillSheets();
    }

    private void CloseSheets()
    {
        EndFieldEdit();   // незаконченный ввод — в сценарий
        foreach (RectTransform r in new[] { condSheet, critSheet, briefSheet, taskSheet }) if (r != null) r.gameObject.SetActive(false);
        Refresh();
    }

    private void FillSheets()
    {
        ScenarioDefinition s = S;
        if (s == null || condSheet == null) return;
        Set(cVisField, MonitorUi.Num(s.visibilityMeters / 1852.0, 2));
        Set(cSafeField, MonitorUi.Num(s.safeSpeedKnots, 1));
        Set(cDetField, MonitorUi.Num(s.detectionRangeNm, 1));
        Set(cWindField, MonitorUi.Num(s.windKnots, 0));
        Set(cWindFromField, MonitorUi.Num(s.windFromDeg, 0));
        Set(cSectorField, MonitorUi.Num(s.windRandomSectorDeg, 0));
        Set(cLimitField, MonitorUi.Num(s.timeLimitSeconds / 60.0, 0));
        Set(briefField, s.briefing ?? "");
        var b = new System.Text.StringBuilder();
        b.AppendLine($"{ScenarioText.Conditions(s.weather, s.timeOfDay)}" +
                     (s.visibilityMeters > 0f ? $"; видимость сценария {MonitorUi.Num(s.visibilityMeters, 0)} м ({MonitorUi.Num(s.visibilityMeters / 1852.0, 2)} мили)" : ""));
        b.AppendLine(s.windKnots > 0f ? $"Ветер {ScenarioText.Wind(s.windKnots, s.windFromDeg)}" +
                                         (s.windRandomSectorDeg > 0f ? $", направление случайно в секторе {MonitorUi.Num(s.windRandomSectorDeg, 0)}°" : "") +
                                         $" · модель движения: {MotionTitle(s.motionModel)}" : "Ветра нет.");
        b.Append($"Лимит: {(s.timeLimitSeconds > 0f ? MonitorUi.Num(s.timeLimitSeconds / 60.0, 0) + " мин" : "нет")} · завершается: {ScenarioText.EndConditions(s)}");
        cInfo.text = b.ToString();

        critEff = s.EffectiveAssessment();
        critBase = AssessmentSettings.From(s.assessment);
        foreach ((string code, TextMeshProUGUI t) in critRows)
        {
            CriterionInfo info = AssessmentCatalog.Find(code);
            CriterionSetting e = critEff.Get(code);
            bool diff = CriteriaEditing.Differs(s, code);
            string col = diff ? "#FFC85A" : e.enabled ? "#E6EDF3" : "#7A8590";
            t.text = $"<color={col}><b>{code}</b> {info?.Title}</color>  <color=#7A8590><size=85%>зач. {MonitorUi.Num(e.pass, 2)} · вес {MonitorUi.Num(e.weight, 0)}</size></color>";
        }
        (int en, int man) = CriteriaEditing.Count(s);
        int nd = s.criteriaSwitches != null ? s.criteriaSwitches.Count : 0;
        critInfo.text = $"Включено {en} из {AssessmentCatalog.All.Count}, обязательных {man}\n" +
                        (nd > 0 ? $"<color=#FFC85A>Отличий от набора: {nd}</color>" : "<color=#9AA6B2>Как в наборе</color>");
        FillTaskSheet();   // 21.5б
    }

    // ─────────── Правка ───────────
    private void CycleWeather(int d)
    {
        if (S == null) return;
        var all = (WeatherPresetId[])Enum.GetValues(typeof(WeatherPresetId));
        int k = Array.IndexOf(all, S.weather);
        EditScenario(() => { S.weather = all[((k < 0 ? 0 : k) + d + all.Length) % all.Length]; sdoc.Commit(); });
    }

    private void CycleTimeOfDay(int d)
    {
        if (S == null) return;
        var all = (TimeOfDay[])Enum.GetValues(typeof(TimeOfDay));
        int k = Array.IndexOf(all, S.timeOfDay);
        EditScenario(() => { S.timeOfDay = all[((k < 0 ? 0 : k) + d + all.Length) % all.Length]; sdoc.Commit(); });
    }

    private void CycleCriteriaSet(int d)
    {
        if (S == null) return;
        var list = new List<AssessmentCriteria>(Catalog.Criteria);
        if (S.assessment != null && !list.Exists(x => x.name == S.assessment.name)) list.Add(S.assessment);
        if (list.Count == 0) return;
        int k = S.assessment != null ? list.FindIndex(x => x.name == S.assessment.name) : -1;
        AssessmentCriteria next = list[((k < 0 ? 0 : k) + d + list.Count) % list.Count];
        EditScenario(() => { CriteriaEditing.SetBase(S, next); sdoc.Commit(); });
    }

    private void BriefingTemplate()
    {
        if (S == null) return;
        EndFieldEdit();
        string text = ScenarioChecks.Briefing(S);
        if (string.IsNullOrWhiteSpace(S.briefing)) { EditScenario(() => { S.briefing = text; sdoc.Commit(); }); return; }
        Dialog("Заменить брифинг?", "Брифинг уже написан. Заменить его заготовкой по сценарию (отменить — Ctrl+Z)?", null, true,
               ("Заменить", MonitorUi.WarnColor, () => { CloseDialog(); EditScenario(() => { S.briefing = text; sdoc.Commit(); }); }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // ─────────── Пройти в тренажёре ───────────
    private void PlayInTrainer()
    {
        if (sdoc == null) return;
        EndFieldEdit();
        AreaCheckResult c = ScenarioChecks.Check(S, Catalog);
        if (c.Errors.Count > 0)
        {
            Dialog("Сценарий не запускается", "Исправьте ошибки проверки:\n" + string.Join("\n", c.Errors.GetRange(0, Math.Min(4, c.Errors.Count))), null, true,
                   ("Понятно", MonitorUi.NeutralColor, CloseDialog));
            return;
        }
        if (sdoc.Dirty || sdoc.FilePath.Length == 0 || !File.Exists(sdoc.FilePath))
        {
            AreaSaveStatus st = sdoc.Save(Root, false, DateTime.Now);
            if (st == AreaSaveStatus.NameTaken)
            {
                Dialog("Имя занято", sdoc.LastMessage + " Заменить его этим сценарием и пройти? Прежний вариант уйдёт в «История».", null, true,
                       ("Заменить и пройти", MonitorUi.WarnColor, () => { CloseDialog(); if (sdoc.Save(Root, true, DateTime.Now) == AreaSaveStatus.Saved) GoPlay(); else PlayInTrainer(); }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
                return;
            }
            if (st != AreaSaveStatus.Saved)
            {
                Dialog("Не сохранено", sdoc.LastMessage, null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog));
                return;
            }
            Debug.Log("[Редактор] " + sdoc.LastMessage);
        }
        GoPlay();
    }

    private void GoPlay()
    {
        lastScenarioDraftText = "";
        TrainerScenes.PlayOnLoad = sdoc.FilePath;
        TrainerScenes.LastScenarioFile = sdoc.FilePath;
        Debug.Log($"[Редактор] Пройти в тренажёре: «{S.id} {S.title}» ({Path.GetFileName(sdoc.FilePath)}).");
        GuardedArea(TrainerScenes.OpenTraining);   // несохранённая акватория — спросить
    }

    void OnDestroy()
    {
        // Обратно в редактор — к последнему сценарию (окно инструктора, «Редактор сценариев»)
        if (mode == Mode.Scenario && sdoc != null && sdoc.FilePath.Length > 0) TrainerScenes.LastScenarioFile = sdoc.FilePath;
    }
}
