// ============================================================
// ScenarioEditorScenario.cs — Слой сценария в окне редактора (запрос 21.4а; часть ScenarioEditorApp, partial-класс)
//
// Режим «Сценарий» (переключатель вверху палитры): акватория сценария — фон (не правится; правка — в режиме
// «Акватория»), поверх — старт своего судна и маршрут с коридором XTE. Свой документ (ScenarioDocument) и своя отмена.
//   • Новый: «с нуля» на встроенной или своей акватории; «на основе» встроенного (копия, новый id «МОЙ-NN»);
//     свои — из «Мои сценарии».
//   • Инструменты: «Выбор / сдвиг»; «Старт» — щелчок ставит судно (тянуть — место); «Маршрут» — щелчок —
//     точка в конец. В любом инструменте: точку и судно — тянуть; круг на середине участка — вставить точку;
//     ромб на кромке коридора — тянуть поперёк (XTE участка, шаг 10 м; виден, когда кромка дальше 12 пикс.);
//     у выделенного судна — ручка курса на носу (шаг 1°, с привязкой 5°). Delete — удалить точку; Esc — «Выбор» /
//     снять выделение.
//   • Справа: свойства сценария (id, название, акватория, судно — модель 45 м / свой буксир, модель движения,
//     блоки из основы), старта (место, курс, скорость, телеграф, руль) или точки (имя, место, XTE, узкость, участки).
//   • «Проверка» — ScenarioDefinition.Validate + замечания редактора (RouteEditing.Check).
// Условия, критерии, брифинг, «Пройти в тренажёре» — ScenarioEditorSheets.cs (21.4б); другие суда — ScenarioEditorTraffic.cs (21.5а);
// швартовка, буксир по вызову, работа буксира — лист «Задание…», ScenarioEditorTask.cs (21.5б).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class ScenarioEditorApp
{
    private enum Mode { Area, Scenario }
    private enum STool { Select, Start, Route, Traffic }

    private Mode mode = Mode.Area;
    private STool stool = STool.Select;
    private ScenarioDocument sdoc;
    private List<WaterArea> folderAreas = new List<WaterArea>();
    private List<ScenarioDefinition> folderScenarios = new List<ScenarioDefinition>();
    private bool selStart, hotStart, hotHeading;
    private int selPoint = -1, hotPoint = -1, hotLegMid = -1, hotXte = -1;
    private bool dragStart, dragHeading, dragPoint, dragXte, sMoved;
    private int dragLeg = -1;
    private float nextScenarioDraft;
    private string lastScenarioDraftText = "";

    private RectTransform areaPalette, scenarioPalette, scenarioGroup, startGroup, pointGroup;
    private TMP_InputField sIdField, sTitleField, stEField, stNField, stHeadField, stSpeedField, stRudderField;
    private TMP_InputField ptNameField, ptEField, ptNField, ptXteField;
    private TextMeshProUGUI sInfo, ptInfo;

    private ScenarioDefinition S => sdoc?.Scenario;
    private bool HasDoc => mode == Mode.Scenario ? sdoc != null : doc != null;
    private bool ActiveDirty => mode == Mode.Scenario ? sdoc != null && sdoc.Dirty : doc != null && doc.Dirty;
    private EditHistory ActiveHistory => mode == Mode.Scenario ? sdoc?.History : doc?.History;
    private WaterArea ActiveArea => mode == Mode.Scenario ? S?.waterArea : doc?.Area;
    private RouteWaypoint SelWp => S != null && RouteEditing.Valid(S.route, selPoint) ? S.route[selPoint] : null;

    // ─────────── Палитра ───────────
    private void BuildModeSwitch(RectTransform p, ref float y)
    {
        float w = PaletteWidth - 2f * Pad, half = (w - MonitorUi.Gap) * 0.5f, x = Pad;
        ui.Button(p, ref x, y, half, "Акватория", MonitorUi.NeutralColor, () => SetMode(Mode.Area), null,
                  () => mode == Mode.Area ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, MonitorUi.RowHeight, 15f);
        ui.Button(p, ref x, y, half, "Сценарий", MonitorUi.NeutralColor, () => SetMode(Mode.Scenario), null,
                  () => mode == Mode.Scenario ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, MonitorUi.RowHeight, 15f);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
    }

    private void BuildScenarioPalette(RectTransform panel, float y0)
    {
        RectTransform p = scenarioPalette = Group(panel, "ScenarioPalette");
        float y = y0, w = PaletteWidth - 2f * Pad;
        Header(p, "Инструменты", ref y);
        foreach ((STool t, string title) in new[] { (STool.Select, "Выбор / сдвиг"), (STool.Start, "Старт судна"), (STool.Route, "Маршрут"), (STool.Traffic, "Другое судно") })
        {
            STool tt = t;
            float x = Pad;
            ui.Button(p, ref x, y, w, title, MonitorUi.NeutralColor, () => SetSTool(tt), null,
                      () => stool == tt ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => tt == STool.Select || sdoc != null);
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
        y -= 2f * MonitorUi.Gap;
        // 21.4б: проверить, сохранить и открыть сценарий в тренажёре на брифинге
        float xp = Pad;
        ui.Button(p, ref xp, y, w, "Пройти в тренажёре", MonitorUi.StartColor, PlayInTrainer, null, null, () => sdoc != null);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        TextMeshProUGUI help = MonitorUi.Text(p,
            "Колесо — масштаб; правая кнопка — сдвиг\n" +
            "Старт: щелчок — поставить судно\n" +
            "Маршрут: щелчок — точка в конец\n" +
            "Точку, судно — тянуть\n" +
            "Круг на участке — вставить точку\n" +
            "Ромб на кромке — XTE участка\n" +
            "Ручка на носу судна — курс\n" +
            "Другое судно: щелчок — судно;\n  у выделенного «по маршруту» — точка\n" +
            "Ромб на своём пути — TCPA встречи\n" +
            "Delete — удалить точку / судно\nEsc — «Выбор» / снять\nCtrl+Z / Ctrl+Y, Ctrl+S",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, Pad, y, w, 330f);
        y -= 338f;
        TextMeshProUGUI later = MonitorUi.Text(p,
            "Условия, критерии, брифинг, задание (швартовка, буксир) — кнопки в свойствах сценария.",
            13f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(later.rectTransform, Pad, y, w, 80f);
    }

    // ─────────── Свойства ───────────
    /// <summary>Группы свойств сценария, старта и точки; возвращает нижний край самой длинной.</summary>
    private float BuildScenarioProps(RectTransform p, float w)
    {
        float half = (w - MonitorUi.Gap) * 0.5f;
        // Сценарий
        RectTransform g = scenarioGroup = Group(p, "ScenarioProps");
        float y = -Pad;
        Header(g, "Сценарий", ref y);
        Label(g, "Идентификатор · название", ref y, w);
        sIdField = MonitorUi.Field(g, Pad, y, 120f, 34f, "МОЙ-01", false, TMP_InputField.ContentType.Standard, EditScenarioId);
        sTitleField = MonitorUi.Field(g, Pad + 120f + MonitorUi.Gap, y, w - 120f - MonitorUi.Gap, 34f, "название", false,
                                      TMP_InputField.ContentType.Standard, EditScenarioTitle);
        y -= 34f + MonitorUi.Gap;
        Label(g, "Акватория (фон; правка — в режиме «Акватория»)", ref y, w);
        Cycler(g, ref y, w, () => CycleArea(-1), () => CycleArea(1), () => S?.waterArea != null ? AreaTitle(S.waterArea) : "—");
        Label(g, "Своё судно (катер — переключатель в тренажёре)", ref y, w);
        float x = Pad;
        ui.Button(g, ref x, y, w, "", MonitorUi.NeutralColor, ToggleOwnShip, () => S == null ? "" : OwnShipTitle(S));
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        Label(g, "Модель движения", ref y, w);
        Cycler(g, ref y, w, () => CycleMotion(-1), () => CycleMotion(1), () => S == null ? "" : MotionTitle(S.motionModel));
        x = Pad;
        ui.Button(g, ref x, y, w, "", MonitorUi.ButtonColor, () => SelectStart(true),
                  () => S == null ? "" : $"Старт: {MonitorUi.Num(S.initial.headingDeg, 0)}°, {MonitorUi.Num(S.initial.speedKnots, 1)} уз — свойства");
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        // 21.4б: листы поверх карты
        float third = (w - 2f * MonitorUi.Gap) / 3f;
        x = Pad;
        ui.Button(g, ref x, y, third, "Условия…", MonitorUi.ButtonColor, () => OpenSheet(condSheet), null, null, null, MonitorUi.RowHeight, 15f);
        ui.Button(g, ref x, y, third, "Критерии…", MonitorUi.ButtonColor, () => OpenSheet(critSheet), null, null, null, MonitorUi.RowHeight, 15f);
        ui.Button(g, ref x, y, third, "", MonitorUi.ButtonColor, () => OpenSheet(briefSheet),
                  () => S != null && string.IsNullOrWhiteSpace(S.briefing) ? "Брифинг… (пусто)" : "Брифинг…", null, null, MonitorUi.RowHeight, 15f);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        x = Pad;   // 21.5б: швартовка, буксир по вызову, работа буксира
        ui.Button(g, ref x, y, w, "", MonitorUi.ButtonColor, () => OpenSheet(taskSheet), () => S == null ? "" : "Задание… (" + TaskTitle(S) + ")",
                  null, null, MonitorUi.RowHeight, 15f);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        sInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(sInfo.rectTransform, Pad, y, w, 110f);
        y -= 114f;
        float yScenario = y;

        // Старт
        g = startGroup = Group(p, "StartProps");
        y = -Pad;
        Header(g, "Старт своего судна", ref y);
        TextMeshProUGUI hint = MonitorUi.Text(g, "Тянуть судно — место; ручку на носу — курс · Esc — к свойствам сценария", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 40f);
        y -= 42f;
        Label(g, "Положение, м: восток … север", ref y, w);
        stEField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditStartPos(v, true));
        stNField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditStartPos(v, false));
        y -= 34f + MonitorUi.Gap;
        Label(g, "Курс, ° · скорость, уз", ref y, w);
        stHeadField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                      v => EditScenarioNumber(v, d => true, d => S.initial.headingDeg = (float)(((d % 360.0) + 360.0) % 360.0)));
        stSpeedField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                       v => EditScenarioNumber(v, d => d >= 0.0 && d <= 60.0, d => S.initial.speedKnots = (float)d));
        y -= 34f + MonitorUi.Gap;
        Label(g, "Телеграф", ref y, w);
        Cycler(g, ref y, w, () => CycleTelegraph(-1), () => CycleTelegraph(1), () => S == null ? "" : ScenarioText.Telegraph(S.initial.telegraph));
        Label(g, "Руль, ° (> 0 — правый борт)", ref y, w);
        stRudderField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                        v => EditScenarioNumber(v, d => Math.Abs(d) <= 35.0, d => S.initial.rudderDeg = (float)d));
        y -= 34f + 3f * MonitorUi.Gap;
        float yStart = y;
        float yTraffic = BuildTrafficProps(p, w);   // 21.5а: другое судно

        // Точка маршрута
        g = pointGroup = Group(p, "PointProps");
        y = -Pad;
        Header(g, "Точка маршрута", ref y);
        hint = MonitorUi.Text(g, "Delete — удалить точку · Esc — снять выделение", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 20f);
        y -= 24f;
        ptNameField = Row(g, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard,
                          v => EditPoint(wp => { string n = (v ?? "").Trim(); if (n.Length == 0) return false; wp.name = n; return true; }));
        Label(g, "Положение, м: восток … север", ref y, w);
        ptEField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditPointPos(v, true));
        ptNField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditPointPos(v, false));
        y -= 34f + MonitorUi.Gap;
        Label(g, "XTE участка к точке, м · узкость (правило 9)", ref y, w);
        ptXteField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                     v => { if (MonitorUi.TryNumber(v, out double d) && d > 0.0) EditPoint(wp => { wp.xteLimit = (float)d; return true; }); else FillScenarioFields(); });
        x = Pad + half + MonitorUi.Gap;
        ui.Button(g, ref x, y, half, "", MonitorUi.NeutralColor, () => EditPoint(wp => { wp.narrowChannel = !wp.narrowChannel; return true; }),
                  () => SelWp != null && SelWp.narrowChannel ? "Узкость: да" : "Узкость: нет",
                  () => SelWp != null && SelWp.narrowChannel ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => selPoint > 0, 34f);
        y -= 34f + MonitorUi.Gap;
        ptInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(ptInfo.rectTransform, Pad, y, w, 66f);
        y -= 70f;
        x = Pad;
        ui.Button(g, ref x, y, w, "Удалить точку", MonitorUi.StopColor, DeletePoint);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        return Mathf.Min(Mathf.Min(yScenario, yTraffic), Mathf.Min(yStart, y));
    }

    // «<» · значение (щелчок — дальше) · «>»
    private void Cycler(RectTransform g, ref float y, float w, Action prev, Action next, Func<string> text)
    {
        float x = Pad;
        ui.Button(g, ref x, y, 48f, "<", MonitorUi.NeutralColor, () => prev());
        ui.Button(g, ref x, y, w - 2f * 48f - 2f * MonitorUi.Gap, "", MonitorUi.NeutralColor, () => next(), text);
        ui.Button(g, ref x, y, 48f, ">", MonitorUi.NeutralColor, () => next());
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
    }

    private static string TaskTitle(ScenarioDefinition s)
    {
        var parts = new List<string>();
        if (s.IsMooring) parts.Add("швартовка");
        if (s.tugAvailable) parts.Add("буксир по вызову");
        if (s.IsTugWork) parts.Add("работа буксира");
        return parts.Count > 0 ? string.Join(", ", parts) : "только маршрут";
    }

    private static string AreaTitle(WaterArea a) => a.name + (TrainerScenes.Catalog != null && TrainerScenes.Catalog.IsBuiltIn(a) ? " · встроенная" : " · своя");

    private static string OwnShipTitle(ScenarioDefinition s) =>
        (s.ownShipType == ScenarioShipType.Tug ? "Свой буксир" : "Модель 45 м") + (s.shipParameters != null ? $" ({ShipLabel(s.shipParameters)})" : "");

    private static string MotionTitle(ScenarioMotionModel m)
    {
        switch (m)
        {
            case ScenarioMotionModel.Nomoto: return "Номото (курс)";
            case ScenarioMotionModel.Manoeuvring3Dof: return "3 СС (малые скорости, швартовка)";
            default: return "Как выбрано в тренажёре";
        }
    }

    private void ShowScenarioGroups()
    {
        bool sc = mode == Mode.Scenario && sdoc != null;
        if (scenarioGroup != null) scenarioGroup.gameObject.SetActive(sc && !selStart && SelWp == null && SelT == null);
        if (trafficGroup != null) trafficGroup.gameObject.SetActive(sc && !selStart && SelWp == null && SelT != null);   // 21.5а
        if (startGroup != null) startGroup.gameObject.SetActive(sc && selStart);
        if (pointGroup != null) pointGroup.gameObject.SetActive(sc && !selStart && SelWp != null);
    }

    private void FillScenarioFields()
    {
        ScenarioDefinition s = S;
        if (s == null) return;
        FillSheets();   // 21.4б: открытый лист — тоже
        Set(sIdField, s.id);
        Set(sTitleField, s.title);
        Set(stEField, MonitorUi.Num(s.initial.position.East, 0));
        Set(stNField, MonitorUi.Num(s.initial.position.North, 0));
        Set(stHeadField, MonitorUi.Num(s.initial.headingDeg, 0));
        Set(stSpeedField, MonitorUi.Num(s.initial.speedKnots, 1));
        Set(stRudderField, MonitorUi.Num(s.initial.rudderDeg, 0));
        sInfo.text = ScenarioInfo(s);
        FillTrafficFields();   // 21.5а
        RouteWaypoint wp = SelWp;
        if (wp == null) return;
        Set(ptNameField, wp.name);
        Set(ptEField, MonitorUi.Num(wp.position.East, 0));
        Set(ptNField, MonitorUi.Num(wp.position.North, 0));
        Set(ptXteField, selPoint > 0 ? MonitorUi.Num(wp.xteLimit, 0) : "");
        if (ptXteField.placeholder is TextMeshProUGUI ph) ph.text = selPoint > 0 ? "" : "не нужно";
        ptXteField.interactable = selPoint > 0;
        var b = new System.Text.StringBuilder();
        if (selPoint > 0) b.AppendLine(LegLine(s.route, selPoint - 1, "к точке"));
        else b.AppendLine("Первая точка — начало маршрута (XTE не нужен).");
        if (selPoint + 1 < s.route.Count) b.Append(LegLine(s.route, selPoint, "дальше"));
        ptInfo.text = b.ToString();
    }

    private static string LegLine(List<RouteWaypoint> r, int leg, string what)
    {
        double len = RouteEditing.LegLength(r, leg);
        return $"{what}: «{r[leg].name}» → «{r[leg + 1].name}» {MonitorUi.Num(RouteEditing.LegCourse(r, leg), 0)}°, " +
               $"{MonitorUi.Num(len / 1852.0, 2)} мили, XTE {MonitorUi.Num(r[leg + 1].xteLimit, 0)} м";
    }

    // Маршрут и блоки задания (из основы — как есть)
    private string ScenarioInfo(ScenarioDefinition s)
    {
        var b = new System.Text.StringBuilder();
        b.AppendLine($"Маршрут: точек {s.route.Count}, {MonitorUi.Num(s.RouteLength / 1852.0, 2)} мили");
        int ships = s.traffic != null ? s.traffic.Count : 0;
        b.AppendLine(ships > 0 ? $"Других судов {ships} (щелчок по судну — свойства)" : "Других судов нет (инструмент «Другое судно»)");
        b.Append($"<color=#9AA6B2>{ScenarioText.Conditions(s.weather, s.timeOfDay)}; лимит {MonitorUi.Num(s.timeLimitSeconds / 60.0, 0)} мин · критериев: {CriteriaEditing.Count(s).enabled}, обязательных {CriteriaEditing.Count(s).mandatory}</color>");
        return b.ToString();
    }

    // ─────────── Правка ───────────
    private void EditScenario(Action change)
    {
        if (sdoc == null) return;
        change();
        FillScenarioFields();
        Refresh();
    }

    private void EditScenarioNumber(string text, Func<double, bool> ok, Action<double> set)
    {
        if (sdoc == null) return;
        if (MonitorUi.TryNumber(text, out double v) && ok(v)) EditScenario(() => { set(v); sdoc.Commit(); });
        else FillScenarioFields();
    }

    private void EditScenarioId(string text)
    {
        string v = (text ?? "").Trim();
        if (sdoc == null || v.Length == 0) { FillScenarioFields(); return; }
        EditScenario(() => { S.id = v; S.name = v; sdoc.Commit(); });
    }

    private void EditScenarioTitle(string text)
    {
        string v = (text ?? "").Trim();
        if (sdoc == null || v.Length == 0) { FillScenarioFields(); return; }
        EditScenario(() => { S.title = v; sdoc.Commit(); });
    }

    private void EditStartPos(string text, bool east)
    {
        if (!MonitorUi.TryNumber(text, out double v)) { FillScenarioFields(); return; }
        EditScenario(() =>
        {
            WorldPosition p = S.initial.position;
            S.initial.position = east ? new WorldPosition(v, p.North) : new WorldPosition(p.East, v);
            sdoc.Commit();
        });
    }

    private void EditPoint(Func<RouteWaypoint, bool> change)
    {
        RouteWaypoint wp = SelWp;
        if (wp == null) return;
        EditScenario(() => { if (change(wp)) sdoc.Commit(); });
    }

    private void EditPointPos(string text, bool east)
    {
        if (!MonitorUi.TryNumber(text, out double v)) { FillScenarioFields(); return; }
        EditPoint(wp => { wp.position = east ? new WorldPosition(v, wp.position.North) : new WorldPosition(wp.position.East, v); return true; });
    }

    private void CycleTelegraph(int d)
    {
        if (S == null) return;
        var all = (TelegraphOrder[])Enum.GetValues(typeof(TelegraphOrder));
        int k = Array.IndexOf(all, S.initial.telegraph);
        EditScenario(() => { S.initial.telegraph = all[((k < 0 ? 0 : k) + d + all.Length) % all.Length]; sdoc.Commit(); });
    }

    private void CycleMotion(int d)
    {
        if (S == null) return;
        var all = (ScenarioMotionModel[])Enum.GetValues(typeof(ScenarioMotionModel));
        int k = Array.IndexOf(all, S.motionModel);
        EditScenario(() => { S.motionModel = all[((k < 0 ? 0 : k) + d + all.Length) % all.Length]; sdoc.Commit(); });
    }

    private void ToggleOwnShip()
    {
        if (S == null) return;
        bool tug = S.ownShipType != ScenarioShipType.Tug;
        ShipParameters p = tug ? TrainerScenes.TugShip : TrainerScenes.BridgeShip;
        EditScenario(() => sdoc.SetOwnShip(tug ? ScenarioShipType.Tug : ScenarioShipType.Default, p));
    }

    /// <summary>Акватории на выбор: встроенные, свои из папки, вписанная в сценарий (если её нет в папке).</summary>
    private List<WaterArea> AreaChoices()
    {
        var list = new List<WaterArea>(Catalog.Areas);
        foreach (WaterArea a in folderAreas) if (a != null && !list.Exists(x => x.name == a.name)) list.Add(a);
        WaterArea cur = S?.waterArea;
        if (cur != null && !list.Exists(x => x.name == cur.name)) list.Add(cur);
        return list;
    }

    private void CycleArea(int d)
    {
        if (S == null) return;
        LoadFolder();   // своя акватория могла быть только что сохранена в режиме «Акватория»
        List<WaterArea> list = AreaChoices();
        if (list.Count == 0) return;
        int k = S.waterArea != null ? list.FindIndex(x => x.name == S.waterArea.name) : -1;
        WaterArea next = list[((k < 0 ? 0 : k) + d + list.Count) % list.Count];
        EditScenario(() => { S.waterArea = next; if (S.IsTugWork) TaskEditing.PlaceTugWork(S); sdoc.Commit(); });   // 21.5б: расстановка — по новому причалу
        mapDirty = true;
        FitArea();
        Debug.Log($"[Редактор] Сценарий «{S.id}»: акватория «{next.name}».");
    }

    private void DeletePoint()
    {
        if (S == null || !RouteEditing.Remove(S.route, selPoint)) return;
        selPoint = -1;
        hotPoint = -1;
        ShowPropsGroup();
        EditScenario(() => sdoc.Commit());
    }

    // ─────────── Выделение, инструменты, режим ───────────
    private void SelectStart(bool on)
    {
        if (on == selStart && (!on || selPoint < 0 && selTraffic < 0)) return;
        EndFieldEdit();
        selStart = on;
        if (on) { selPoint = -1; selTraffic = selTPoint = -1; }
        ShowPropsGroup();
        FillScenarioFields();
        Refresh();
    }

    private void SelectPoint(int i)
    {
        if (S == null || !RouteEditing.Valid(S.route, i)) i = -1;
        if (i == selPoint && (i < 0 || !selStart && selTraffic < 0)) return;
        EndFieldEdit();
        selPoint = i;
        if (i >= 0) { selStart = false; selTraffic = selTPoint = -1; }
        ShowPropsGroup();
        FillScenarioFields();
        Refresh();
    }

    private void SetSTool(STool t)
    {
        stool = t;
        dragStart = dragHeading = dragPoint = dragXte = false;
        TrafficRelease();
        Refresh();
    }

    private void SetMode(Mode m)
    {
        if (m == mode) return;
        CloseSheets();   // 21.4б: листы — только сценария
        EndFieldEdit();
        mode = m;
        SetTool(Tool.Select);
        SetSTool(STool.Select);
        selStart = false;
        selPoint = -1;
        ResetTrafficState();   // 21.5а
        mapDirty = true;
        mapData = null;
        if (areaPalette != null) areaPalette.gameObject.SetActive(m == Mode.Area);
        if (scenarioPalette != null) scenarioPalette.gameObject.SetActive(m == Mode.Scenario);
        ShowPropsGroup();
        FitArea();
        FillFields();
        FillScenarioFields();
        Refresh();
        if (m == Mode.Scenario && sdoc == null) OpenScenarioDialog(false);
        else if (m == Mode.Area && doc == null) OpenDialog(false);
    }

    private void SetScenarioDocument(ScenarioDocument d)
    {
        sdoc = d;
        lastScenarioDraftText = "";
        nextScenarioDraft = Time.unscaledTime + (float)AreaDocument.DraftPeriodS;
        selStart = false;
        selPoint = -1;
        dragStart = dragHeading = dragPoint = dragXte = false;
        ResetTrafficState();   // 21.5а
        if (mode != Mode.Scenario) SetMode(Mode.Scenario);
        mapDirty = true;
        mapData = null;
        ShowPropsGroup();
        FitArea();
        FillScenarioFields();
        Refresh();
        Debug.Log($"[Редактор] Сценарий «{d.Scenario.id} {d.Scenario.title}» ({d.Origin}), акватория «{d.Scenario.waterArea?.name}».");
    }

    /// <summary>Свои акватории и сценарии из папки (для выбора акватории, id и «Открыть»).</summary>
    private void LoadFolder()
    {
        try
        {
            ScenarioLibraryReport rep = ScenarioFolder.Load(Root, Catalog);
            folderAreas = rep.Areas;
            folderScenarios = rep.Scenarios;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Редактор] Папка {Root} не читается: {e.Message}");
            folderAreas = new List<WaterArea>();
            folderScenarios = new List<ScenarioDefinition>();
        }
    }

    private IEnumerable<string> TakenIds()
    {
        foreach (ScenarioDefinition s in Catalog.Scenarios) if (s != null) yield return s.id;
        foreach (ScenarioDefinition s in folderScenarios) if (s != null) yield return s.id;
        if (S != null) yield return S.id;
    }

    // ─────────── Открыть, новый, сохранить ───────────
    private void OpenScenarioDialog(bool startup)
    {
        LoadFolder();
        var buttons = new List<(string, Color, Action)>
        {
            ("Новый «с нуля» (выбрать акваторию)", MonitorUi.StartColor, () => { CloseDialog(); NewScenarioDialog(); }),
            ("На основе встроенного…", MonitorUi.ButtonColor, () => { CloseDialog(); BasedOnDialog(); })
        };
        var own = new List<ScenarioDefinition>(folderScenarios);
        own.Sort((a, b) => string.Compare(Path.GetFileName(a.sourceFile), Path.GetFileName(b.sourceFile), StringComparison.OrdinalIgnoreCase));
        int shown = 0;
        foreach (ScenarioDefinition s in own)
        {
            if (++shown > 8) break;
            string path = s.sourceFile;
            buttons.Add(($"Мой: {Path.GetFileNameWithoutExtension(path)}", MonitorUi.NeutralColor, () =>
            {
                var errors = new List<string>();
                ScenarioDocument d = ScenarioDocument.Open(path, Catalog, folderAreas, errors);
                CloseDialog();
                if (d != null) SetScenarioDocument(d);
                else Dialog("Не открылся", string.Join("\n", errors), null, true, ("Понятно", MonitorUi.NeutralColor, () => { CloseDialog(); OpenScenarioDialog(sdoc == null); }));
            }));
        }
        buttons.Add(("К акваториям", MonitorUi.NeutralColor, () => { CloseDialog(); SetMode(Mode.Area); }));
        if (!startup || sdoc != null) buttons.Add(("Отмена", MonitorUi.NeutralColor, CloseDialog));
        else buttons.Add(("В тренажёр", MonitorUi.NeutralColor, TrainerScenes.OpenTraining));
        string more = own.Count > 8 ? $"\nСвоих сценариев {own.Count} — показаны первые 8 (по имени файла)." : "";
        Dialog("Открыть сценарий", $"Папка: {Path.Combine(Root, ScenarioFolder.ScenariosDir)}{more}", null, false, buttons.ToArray());
    }

    private void NewScenarioDialog()
    {
        List<WaterArea> areas = new List<WaterArea>(Catalog.Areas);
        foreach (WaterArea a in folderAreas) if (a != null && !areas.Exists(x => x.name == a.name)) areas.Add(a);
        var buttons = new List<(string, Color, Action)>();
        foreach (WaterArea a in areas)
        {
            if (buttons.Count >= 8) break;
            WaterArea area = a;
            buttons.Add(($"{AreaTitle(a)}", MonitorUi.ButtonColor, () =>
            {
                CloseDialog();
                string id = ScenarioDocument.NextId(TakenIds());
                Dialog("Новый сценарий", $"Акватория «{area.name}». Название сценария (идентификатор — {id}, его можно сменить):", "Новый сценарий", true,
                       ("Создать", MonitorUi.StartColor, () =>
                       {
                           string t = DialogInput();
                           CloseDialog();
                           SetScenarioDocument(ScenarioDocument.New(area, TrainerScenes.BridgeShip, id, t, Catalog, folderAreas));
                       }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
            }));
        }
        buttons.Add(("Отмена", MonitorUi.NeutralColor, () => { CloseDialog(); if (sdoc == null) OpenScenarioDialog(true); }));
        Dialog("Новый сценарий: акватория", "Своя акватория вписывается в файл сценария (файл самодостаточен).", null, false, buttons.ToArray());
    }

    private void BasedOnDialog()
    {
        var buttons = new List<(string, Color, Action)>();
        foreach (ScenarioDefinition s in Catalog.Scenarios)
        {
            if (s == null || buttons.Count >= 8) continue;
            ScenarioDefinition src = s;
            buttons.Add(($"{s.id} {s.title}", MonitorUi.ButtonColor, () =>
            {
                CloseDialog();
                try { SetScenarioDocument(ScenarioDocument.BasedOn(src, ScenarioDocument.NextId(TakenIds()), Catalog, folderAreas)); }
                catch (Exception e) { Dialog("Не получилось", e.Message, null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog)); }
            }));
        }
        buttons.Add(("Отмена", MonitorUi.NeutralColor, () => { CloseDialog(); if (sdoc == null) OpenScenarioDialog(true); }));
        Dialog("На основе встроенного", "Встроенный сценарий не меняется — правится копия с новым идентификатором (суда, швартовка, буксир — как в основе).",
               null, false, buttons.ToArray());
    }

    private void SaveScenario(bool overwrite, bool keepPrevious = false)
    {
        if (sdoc == null) return;
        EndFieldEdit();
        AreaSaveStatus st = sdoc.Save(Root, overwrite, DateTime.Now, keepPrevious);
        switch (st)
        {
            case AreaSaveStatus.Saved:
                lastScenarioDraftText = "";
                Debug.Log("[Редактор] " + sdoc.LastMessage);
                break;
            case AreaSaveStatus.NameTaken:
                Dialog("Имя занято", sdoc.LastMessage + " Заменить его этим сценарием? Прежний вариант уйдёт в «История».", null, true,
                       ("Заменить", MonitorUi.WarnColor, () => { CloseDialog(); SaveScenario(true, keepPrevious); }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
                break;
            default:
                Dialog("Не сохранено", sdoc.LastMessage, null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog));
                break;
        }
        Refresh();
    }

    private void SaveScenarioAsDialog()
    {
        if (sdoc == null) return;
        string id = ScenarioDocument.NextId(TakenIds());
        Dialog("Сохранить как", $"Новый идентификатор сценария (прежний файл останется). Свободный — {id}:", id, true,
               ("Сохранить", MonitorUi.ButtonColor, () =>
               {
                   string n = (DialogInput() ?? "").Trim();
                   CloseDialog();
                   if (n.Length == 0) return;
                   S.id = n;
                   S.name = n;
                   sdoc.Commit();
                   FillScenarioFields();
                   SaveScenario(false, keepPrevious: true);
               }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // Черновик и восстановление после сбоя (при запуске — после черновика акватории)
    private void ScenarioDraftOrOpen(bool openIfNone)
    {
        LoadFolder();
        ScenarioDocument d = ScenarioDocument.ReadDraft(Root, Catalog, folderAreas, out DateTime when);
        if (d != null)
            Dialog("Несохранённый черновик",
                   $"После прошлого сеанса остался черновик сценария «{d.Scenario.id} {d.Scenario.title}» от {when:dd.MM HH:mm} ({d.Origin}).",
                   null, false,
                   ("Восстановить", MonitorUi.StartColor, () => { CloseDialog(); SetScenarioDocument(d); }),
                   ("Удалить черновик", MonitorUi.StopColor, () => { ScenarioDocument.DeleteDraft(Root); CloseDialog(); if (openIfNone) OpenDialog(true); }));
        else if (openIfNone && File.Exists(TrainerScenes.LastScenarioFile ?? ""))
        {
            // 21.4б: из тренажёра обратно — к последнему сценарию («Пройти в тренажёре», «Редактор сценариев»)
            var errors = new List<string>();
            ScenarioDocument last = ScenarioDocument.Open(TrainerScenes.LastScenarioFile, Catalog, folderAreas, errors);
            if (last != null) SetScenarioDocument(last);
            else OpenDialog(true);
        }
        else if (openIfNone) OpenDialog(true);
    }

    private void WriteScenarioDraft()
    {
        nextScenarioDraft = Time.unscaledTime + (float)AreaDocument.DraftPeriodS;
        try
        {
            sdoc.WriteDraft(Root);
            lastScenarioDraftText = $"черновик {DateTime.Now:HH:mm}";
        }
        catch (Exception e) { lastScenarioDraftText = "черновик не записан: " + e.Message; }
    }

    private void UndoScenario()
    {
        bool st = selStart;
        int sp = selPoint, tr = selTraffic;
        if (sdoc == null || !sdoc.Undo()) return;
        AfterScenarioRestore(st, sp, tr);
    }

    private void RedoScenario()
    {
        bool st = selStart;
        int sp = selPoint, tr = selTraffic;
        if (sdoc == null || !sdoc.Redo()) return;
        AfterScenarioRestore(st, sp, tr);
    }

    private void AfterScenarioRestore(bool st, int sp, int tr)
    {
        selStart = st;
        selPoint = RouteEditing.Valid(S.route, sp) ? sp : -1;
        dragStart = dragHeading = dragPoint = dragXte = false;
        ResetTrafficState();   // 21.5а: выделенная цель — по номеру, если осталась
        selTraffic = TrafficEditing.Valid(S.traffic, tr) ? tr : -1;
        mapDirty = true;   // акватория могла смениться
        ShowPropsGroup();
        FillScenarioFields();
        Refresh();
    }

    // ─────────── Мышь ───────────
    private void ScenarioMouse()
    {
        Mouse m = Mouse.current;
        if (m == null || S == null) { overlay.ShowCursor = false; return; }
        Vector2 screen = m.position.ReadValue();
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screen, null, out Vector2 local) && mapRect.rect.Contains(local);
        Rect rr = mapRect.rect;
        double px = local.x - rr.xMin, py = local.y - rr.yMin;
        WorldPosition world = viewport.ToWorld(px, py);
        WorldPosition snapped = snap ? MapGrid.Snap(world, GridStep) : world;
        List<RouteWaypoint> r = S.route;

        float wheel = m.scroll.ReadValue().y;
        if (inside && Mathf.Abs(wheel) > 0.01f) viewport.ZoomAt(px, py, wheel > 0f ? 1.25 : 0.8);

        bool busy = dragStart || dragHeading || dragPoint || dragXte || TrafficBusy;
        if (!busy)
        {
            hotHeading = false; hotPoint = hotXte = hotLegMid = -1; hotStart = false;
            bool tHit = inside && TrafficHit(px, py);   // 21.5а: ручки выделенной цели и суда — поверх маршрута
            if (inside && !tHit)
            {
                if (selStart)
                {
                    viewport.ToPixel(HeadingHandlePos(), out double hx, out double hy);
                    hotHeading = (hx - px) * (hx - px) + (hy - py) * (hy - py) <= 10.0 * 10.0;
                }
                // Старт часто стоит на первой точке: в «Старте» судно — поверх точек, иначе точки — поверх судна
                bool startFirst = stool == STool.Start;
                if (!hotHeading && startFirst) hotStart = RouteEditing.HitStart(S.initial, viewport, px, py);
                if (!hotHeading && !hotStart) hotPoint = RouteEditing.HitPoint(r, viewport, px, py);
                if (!hotHeading && !hotStart && hotPoint < 0) hotXte = RouteEditing.HitXte(r, viewport, px, py);
                if (!hotHeading && !startFirst && hotPoint < 0 && hotXte < 0) hotStart = RouteEditing.HitStart(S.initial, viewport, px, py);
                if (!hotHeading && hotPoint < 0 && hotXte < 0 && !hotStart) hotLegMid = RouteEditing.HitLegMid(r, viewport, px, py);
            }
        }
        if (inside && (m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) { panning = true; lastLocal = local; }
        if (inside && m.leftButton.wasPressedThisFrame)
        {
            sMoved = false;
            pressLocal = local;
            if (TrafficPress()) { }   // 21.5а
            else if (hotHeading) { dragHeading = true; sMoved = true; }
            else if (hotPoint >= 0) { SelectPoint(hotPoint); dragPoint = true; }
            else if (hotXte >= 0) { SelectPoint(hotXte + 1); dragXte = true; dragLeg = hotXte; sMoved = true; }
            else if (hotStart) { SelectStart(true); dragStart = true; }
            else if (hotLegMid >= 0)
            {
                int i = RouteEditing.Insert(r, hotLegMid, snapped);
                SelectPoint(i);
                dragPoint = true;
                sMoved = true;
            }
            else if (stool == STool.Start)
            {
                S.initial.position = snapped;
                SelectStart(true);
                dragStart = true;
                sMoved = true;
            }
            else if (stool == STool.Route)
            {
                int i = RouteEditing.Add(r, snapped);
                SelectPoint(i);
                dragPoint = true;
                sMoved = true;
            }
            else if (stool == STool.Traffic) TrafficClickEmpty(snapped);   // 21.5а
            else
            {
                SelectStart(false);
                SelectPoint(-1);
                SelectTraffic(-1);
                panning = true;
                lastLocal = local;
            }
        }
        if (panning)
        {
            Vector2 d = local - lastLocal;
            lastLocal = local;
            viewport.Pan(d.x, d.y);
        }
        if ((dragPoint || dragStart) && !sMoved && (local - pressLocal).magnitude > 3f) sMoved = true;
        if (dragPoint && sMoved && SelWp != null && !SelWp.position.Equals(snapped)) { RouteEditing.Move(r, selPoint, snapped); FillScenarioFields(); }
        if (dragStart && sMoved && !S.initial.position.Equals(snapped)) { S.initial.position = snapped; FillScenarioFields(); }
        if (dragHeading)
        {
            float h = RouteEditing.HeadingTo(S.initial.position, world, snap ? 5.0 : 1.0);
            if (h != S.initial.headingDeg) { S.initial.headingDeg = h; FillScenarioFields(); }
        }
        if (dragXte && RouteEditing.Valid(r, dragLeg + 1))
        {
            float x = RouteEditing.XteFrom(r, dragLeg, world, snap ? RouteEditing.XteStep : 1.0);
            if (x != r[dragLeg + 1].xteLimit) { r[dragLeg + 1].xteLimit = x; FillScenarioFields(); }
        }
        TrafficDrag(world, snapped, local);   // 21.5а
        bool anyHeld = m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed;
        if (!anyHeld)
        {
            bool tRel = TrafficRelease();
            if (dragStart || dragHeading || dragPoint || dragXte || tRel)
            {
                dragStart = dragHeading = dragPoint = dragXte = false;
                dragLeg = -1;
                if (sdoc.Commit()) { FillScenarioFields(); Refresh(); }
            }
            panning = false;
        }

        bool tHot = hotTraffic >= 0 || hotTPoint >= 0 || hotTHeading || hotMeet;
        overlay.ShowCursor = inside && (stool != STool.Select && hotPoint < 0 && !hotStart && !tHot || dragPoint && sMoved || dragStart && sMoved ||
                                        (dragTraffic || dragTPoint) && sMoved);
        overlay.Cursor = snapped;
        cursorWorld = world;
        cursorInside = inside;
        overlay.SetVerticesDirty();
    }

    // Ручка курса: впереди носа (не ближе 45 пикс. от середины судна)
    private WorldPosition HeadingHandlePos()
    {
        double len = S.shipParameters != null ? S.shipParameters.length : 45.0;
        double meters = Math.Max(45.0 * viewport.MetersPerPixel, 0.5 * len + 20.0 * viewport.MetersPerPixel);
        return RouteEditing.HeadingHandle(S.initial.position, S.initial.headingDeg, meters);
    }

    private void DeleteScenarioSelected()
    {
        if (SelWp != null) DeletePoint();
        else if (SelT != null) { if (selTPoint >= 0) DeleteTrafficPoint(); else DeleteTraffic(); }   // 21.5а
    }

    // ─────────── Карта, подписи, заголовок, строка состояния ───────────
    private void ScenarioRedraw()
    {
        viewport.SetSize(Mathf.Max(1f, mapRect.rect.width), Mathf.Max(1f, mapRect.rect.height));
        WaterArea a = S?.waterArea;
        if (mapDirty || mapData == null)
        {
            mapData = a != null ? DebriefMap.BuildStatic(SessionArea.From(a), null) : new DebriefMapData();   // знаки — картой (не правятся)
            if (mapData.Views.Count == 0) mapData.Views.Add(new DebriefMapView { Key = "live", Title = "", Bounds = viewport.Bounds, TickStepS = 300 });
            mapStatic.SetView(mapData, 0);
            mapDirty = false;
            builtVersion = -1;
        }
        overlay.GridStep = GridStep;
        overlay.ShowBounds = a != null;
        if (a != null) { overlay.BoundsMin = a.boundsMin; overlay.BoundsMax = a.boundsMax; }
        overlay.ShowHandles = false;
        overlay.HotHandle = -1;
        overlay.Aids.Clear();
        overlay.SelectedAid = overlay.HotAid = -1;
        overlay.Shape.Clear();
        overlay.HoverShape.Clear();
        overlay.Draft.Clear();
        overlay.ShapeHandles = false;
        overlay.Berths.Clear();
        overlay.Fairways.Clear();
        if (a != null) foreach (FairwayLine f in a.fairways) if (f != null) overlay.Fairways.Add((f.axis, f.width));

        ScenarioDefinition s = S;
        overlay.ScenarioLayer = s != null;
        overlay.Route = s?.route;
        overlay.SelectedPoint = selPoint;
        overlay.HotPoint = dragPoint ? selPoint : hotPoint;
        overlay.HotLegMid = hotLegMid;
        overlay.HotXte = dragXte ? dragLeg : hotXte;
        if (s != null)
        {
            overlay.StartPos = s.initial.position;
            overlay.StartHeading = s.initial.headingDeg;
            overlay.ShipLength = s.shipParameters != null ? s.shipParameters.length : 45f;
            overlay.ShipBeam = s.shipParameters != null ? s.shipParameters.beam : 10f;
            overlay.StartSelected = selStart;
            overlay.StartHot = hotStart || dragStart;
            overlay.HeadingHandle = HeadingHandlePos();
            overlay.HeadingHot = hotHeading || dragHeading;
        }
        TrafficOverlay();   // 21.5а
        TaskOverlay();      // 21.5б
        overlay.SetVerticesDirty();
        PointLabels();
        if (viewport.Version != builtVersion)
        {
            builtVersion = viewport.Version;
            if (mapData.Views.Count > 0) mapData.Views[0].Bounds = viewport.Bounds;
            mapStatic.SetVerticesDirty();
            overlay.SetVerticesDirty();
        }
    }

    // Имена точек маршрута, 21.5а — имена целей и подписи встреч (пул подписей — общий со знаками).
    // Под открытым листом подписей нет (21.5а: сквозь лист их не должно быть видно)
    private void PointLabels()
    {
        Rect rc = overlay.rectTransform.rect;
        int used = 0;
        List<RouteWaypoint> r = S != null ? S.route : null;
        bool show = r != null && r.Count <= 60 && !SheetOpen;
        for (int i = 0; show && i < r.Count; i++)
        {
            if (r[i] == null) continue;
            used = PutLabel(rc, used, overlay.Px(r[i].position, rc) + new Vector2(12f, 10f), r[i].name,
                            i == selPoint ? EditorMapOverlay.HandleHot : EditorMapOverlay.RouteColor);
        }
        if (!SheetOpen) used = TrafficLabels(rc, used);
        for (int i = used; i < aidLabels.Count; i++)
            if (aidLabels[i].gameObject.activeSelf) aidLabels[i].gameObject.SetActive(false);
    }

    // Подпись из пула в точку at (левый край, середина по высоте); вне карты — не ставится
    private int PutLabel(Rect rc, int used, Vector2 at, string text, Color color)
    {
        if (!rc.Contains(at)) return used;
        if (used >= aidLabels.Count)
        {
            TextMeshProUGUI t = MonitorUi.Text(overlay.transform, "", 14f, Color.white, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            MonitorUi.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 20f), new Vector2(0f, 0.5f));
            aidLabels.Add(t);
        }
        TextMeshProUGUI l = aidLabels[used++];
        if (l.text != text) l.text = text;
        l.color = color;
        l.rectTransform.anchoredPosition = at;
        if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        return used;
    }

    private void ScenarioRefresh()
    {
        ScenarioDefinition s = S;
        if (s == null) { titleText.text = "<color=#9AA6B2>сценарий не открыт</color>"; checkText.text = ""; return; }
        string file = sdoc.FilePath.Length > 0 ? Path.GetFileName(sdoc.FilePath) : "не сохранён";
        titleText.text = $"Сценарий «<b>{s.id} {s.title}</b>»{(sdoc.Dirty ? " <color=#FFC85A>*</color>" : "")}  <size=80%><color=#9AA6B2>{file} · {sdoc.Origin}</color></size>";
        var b = new System.Text.StringBuilder();
        // 21.4б: ошибки (×) — «Пройти в тренажёре» не запустит; предупреждения (!) — можно
        AreaCheckResult c = ScenarioChecks.Check(s, Catalog);
        foreach (string p in c.Errors) b.AppendLine($"<color=#FF7A7A>× {p}</color>");
        foreach (string p in c.Warnings) b.AppendLine($"<color=#FFC85A>! {p}</color>");
        if (b.Length == 0) b.Append("<color=#6FE08A>Замечаний нет.</color>");
        if (!string.IsNullOrEmpty(sdoc.LastMessage)) b.Append($"\n\n<color=#9AA6B2>{sdoc.LastMessage}</color>");
        checkText.text = b.ToString();
        if (sInfo != null) sInfo.text = ScenarioInfo(s);
    }

    private void ScenarioStatus()
    {
        ScenarioDefinition s = S;
        if (s == null) { statusText.text = $"Папка: {Path.Combine(Root, ScenarioFolder.ScenariosDir)}"; return; }
        WaterArea a = s.waterArea;
        string pos = cursorInside
            ? $"E {MonitorUi.Num(cursorWorld.East, 0)} м · N {MonitorUi.Num(cursorWorld.North, 0)} м" + (a != null ? $" · глубина {MonitorUi.Num(a.DepthAt(cursorWorld), 1)} м" : "")
            : "курсор вне карты";
        List<RouteWaypoint> r = s.route;
        int hp = dragPoint ? selPoint : hotPoint;
        if (cursorInside && RouteEditing.Valid(r, hp)) pos += $" · точка «{r[hp].name}»";
        int leg = dragXte ? dragLeg : hotXte >= 0 ? hotXte : hotLegMid;
        if (cursorInside && leg >= 0 && RouteEditing.Valid(r, leg + 1))
            pos += $" · участок {MonitorUi.Num(RouteEditing.LegCourse(r, leg), 0)}°, {MonitorUi.Num(RouteEditing.LegLength(r, leg) / 1852.0, 2)} мили, XTE {MonitorUi.Num(r[leg + 1].xteLimit, 0)} м" +
                   (hotLegMid >= 0 && !dragXte ? " — щелчок: вставить точку" : "");
        if (dragHeading || hotHeading) pos += $" · курс старта {MonitorUi.Num(s.initial.headingDeg, 0)}°";
        else if (cursorInside && (hotStart || dragStart)) pos += $" · старт: {MonitorUi.Num(s.initial.headingDeg, 0)}°, {MonitorUi.Num(s.initial.speedKnots, 1)} уз";
        if (cursorInside) pos += TrafficStatus();   // 21.5а
        string saved = sdoc.SavedAt.HasValue ? $"сохранено {sdoc.SavedAt.Value:HH:mm}" : sdoc.Dirty ? "не сохранено" : "";
        statusText.text = $"{pos} · 1 пикс. = {MonitorUi.Num(viewport.MetersPerPixel, viewport.MetersPerPixel < 10 ? 1 : 0)} м · сетка {MapGrid.StepTitle(GridStep)}" +
                          $"{(snap ? " (привязка)" : "")} · {saved}{(lastScenarioDraftText.Length > 0 ? " · " + lastScenarioDraftText : "")}";
    }
}
