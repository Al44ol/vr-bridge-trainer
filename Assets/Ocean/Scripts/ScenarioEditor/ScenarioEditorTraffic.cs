// ============================================================
// ScenarioEditorTraffic.cs — Другие суда в режиме «Сценарий» (запрос 21.5а; часть ScenarioEditorApp, partial-класс)
//
// Инструмент «Другое судно»: щелчок по пустой карте — цель в эту точку (курс — на старт своего судна, 10 уз);
// у выделенной цели с движением «по маршруту» щелчок добавляет точку её маршрута (новую цель — после Esc).
// В любом инструменте: цель — щелчком выделить и тянуть («точка» — место; «пеленг и дистанция» — пересчёт
// от старта своего судна; «встреча» — место считается само); ручка на носу — курс; ромб на пути своего судна
// («встреча») — TCPA; квадраты — точки маршрута цели. Delete — точка маршрута цели (если выбрана) или цель.
// На карте у каждой цели — пунктиры своего судна и цели до положений в момент TCPA и красный отрезок CPA
// с подписью: встреча без манёвра (TrafficEditing.Meet — тот же расчёт, что в тренажёре).
// Справа — группа «Другое судно»: имя и id, вид и модель, способ задания (поля по способу: E/N, пеленг/дистанция
// или CPA/TCPA/борт), курс, скорость, CPA_доп, движение, огни, сведения о встрече.
// ============================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class ScenarioEditorApp
{
    private int selTraffic = -1, hotTraffic = -1, hotTPoint = -1, selTPoint = -1;
    private bool hotTHeading, hotMeet, dragTraffic, dragTHeading, dragMeet, dragTPoint;
    private RectTransform trafficGroup;
    private TMP_InputField tNameField, tIdField, tAField, tBField, tCourseField, tSpeedField, tReqField;
    private TextMeshProUGUI tPlaceLabel, tInfo;

    private TrafficShipDefinition SelT => S != null && TrafficEditing.Valid(S.traffic, selTraffic) ? S.traffic[selTraffic] : null;
    private bool TrafficBusy => dragTraffic || dragTHeading || dragMeet || dragTPoint;

    // ─────────── Свойства ───────────
    private float BuildTrafficProps(RectTransform p, float w)
    {
        RectTransform g = trafficGroup = Group(p, "TrafficProps");
        float y = -Pad, half = (w - MonitorUi.Gap) * 0.5f, third = (w - 2f * MonitorUi.Gap) / 3f;
        Header(g, "Другое судно", ref y);
        TextMeshProUGUI hint = MonitorUi.Text(g, "Тянуть — место · ручка на носу — курс · ромб на своём пути — TCPA · Delete — удалить · Esc — снять",
                                              14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 40f);
        y -= 42f;
        Label(g, "Имя · идентификатор (запись, пусто — T№)", ref y, w);
        tNameField = MonitorUi.Field(g, Pad, y, w - 120f - MonitorUi.Gap, 34f, "имя", false, TMP_InputField.ContentType.Standard,
                                     v => EditTraffic(d => { string n = (v ?? "").Trim(); if (n.Length == 0) return false; d.name = n; return true; }));
        tIdField = MonitorUi.Field(g, Pad + w - 120f, y, 120f, 34f, "T№", false, TMP_InputField.ContentType.Standard,
                                   v => EditTraffic(d =>
                                   {
                                       List<string> before = TriggerEditing.TargetIds(S);
                                       d.id = (v ?? "").Trim();
                                       TriggerEditing.RemapTargets(S, before, TriggerEditing.TargetIds(S));   // 21.6б: ссылки триггеров — за целью
                                       return true;
                                   }));
        y -= 34f + MonitorUi.Gap;
        Label(g, "Вид · модель (размеры и манёвренность)", ref y, w);
        float x = Pad;
        ui.Button(g, ref x, y, half, "", MonitorUi.NeutralColor, () => CycleTrafficType(1),
                  () => SelT == null ? "" : "Вид: " + ScenarioText.VesselType(SelT.type));
        ui.Button(g, ref x, y, half, "", MonitorUi.NeutralColor, () => CycleTrafficModel(1),
                  () => SelT == null ? "" : TrafficModelTitle(SelT));
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        Label(g, "Начальная позиция", ref y, w);
        Cycler(g, ref y, w, () => CyclePlacement(-1), () => CyclePlacement(1), () => SelT == null ? "" : PlacementTitle(SelT.placement));
        tPlaceLabel = MonitorUi.Text(g, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(tPlaceLabel.rectTransform, Pad, y, w, 22f);
        y -= 22f;
        tAField = MonitorUi.Field(g, Pad, y, third, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditPlaceValue(v, true));
        tBField = MonitorUi.Field(g, Pad + third + MonitorUi.Gap, y, third, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditPlaceValue(v, false));
        x = Pad + 2f * (third + MonitorUi.Gap);
        ui.Button(g, ref x, y, third, "", MonitorUi.NeutralColor, CycleSide,
                  () => SelT != null && SelT.placement == TrafficPlacement.MeetingPoint ? SideShort(SelT.meetingSide) : "—", null,
                  () => SelT != null && SelT.placement == TrafficPlacement.MeetingPoint, 34f, 15f);
        y -= 34f + MonitorUi.Gap;
        Label(g, "Курс, ° · скорость, уз · CPA_доп, мили (0 — 0,5)", ref y, w);
        tCourseField = MonitorUi.Field(g, Pad, y, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                       v => EditTrafficNumber(v, d => true, (t, d) => t.headingDeg = (float)(((d % 360.0) + 360.0) % 360.0)));
        tSpeedField = MonitorUi.Field(g, Pad + third + MonitorUi.Gap, y, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                      v => EditTrafficNumber(v, d => d >= 0.0 && d <= 40.0, (t, d) => t.speedKnots = (float)d));
        tReqField = MonitorUi.Field(g, Pad + 2f * (third + MonitorUi.Gap), y, third, 34f, "", false, TMP_InputField.ContentType.Standard,
                                    v => EditTrafficNumber(v, d => d >= 0.0 && d <= 5.0, (t, d) => t.requiredCpaNm = (float)d));
        y -= 34f + MonitorUi.Gap;
        Label(g, "Движение · огни (МППСС)", ref y, w);
        x = Pad;
        ui.Button(g, ref x, y, half, "", MonitorUi.NeutralColor, ToggleTrafficMotion,
                  () => SelT == null ? "" : SelT.motion == TrafficMotion.Route ? $"По маршруту ({SelT.route?.Count ?? 0} т.)" : "Постоянный курс",
                  () => SelT != null && SelT.motion == TrafficMotion.Route ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        ui.Button(g, ref x, y, half, "", MonitorUi.NeutralColor, CycleTrafficLights, () => SelT == null ? "" : LightsShort(SelT.lights));
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        tInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(tInfo.rectTransform, Pad, y, w, 88f);
        y -= 92f;
        x = Pad;
        ui.Button(g, ref x, y, half, "Очистить маршрут", MonitorUi.NeutralColor,
                  () => EditTraffic(d => { if (d.route == null || d.route.Count == 0) return false; d.route.Clear(); selTPoint = -1; return true; }),
                  null, null, () => SelT != null && SelT.route != null && SelT.route.Count > 0);
        ui.Button(g, ref x, y, half, "Удалить судно", MonitorUi.StopColor, DeleteTraffic);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        return y;
    }

    private static string PlacementTitle(TrafficPlacement p)
    {
        switch (p)
        {
            case TrafficPlacement.RelativeToOwnShip: return "Пеленг и дистанция от своего";
            case TrafficPlacement.MeetingPoint: return "Встреча: CPA, TCPA, борт";
            default: return "Точка (восток, север)";
        }
    }

    private static string SideShort(MeetingSide s)
    {
        switch (s)
        {
            case MeetingSide.Port: return "Левый борт";
            case MeetingSide.Ahead: return "По носу";
            case MeetingSide.Astern: return "По корме";
            default: return "Правый борт";
        }
    }

    private static string LightsShort(VesselLightStatus l)
    {
        switch (l)
        {
            case VesselLightStatus.AtAnchor: return "Огни: на якоре (задел)";
            case VesselLightStatus.NotUnderCommand: return "Огни: НУС (задел)";
            case VesselLightStatus.RestrictedManoeuvrability: return "Огни: ОВМ (задел)";
            case VesselLightStatus.ConstrainedByDraught: return "Огни: осадка (задел)";
            case VesselLightStatus.Aground: return "Огни: на мели (задел)";
            default: return "Огни: на ходу (пр. 23)";
        }
    }

    // Параметры цели для размеров на карте: заданные или по виду, как в сцене (TrafficController: сухогруз и танкер —
    // Mariner, малое судно — модель 45 м)
    private static ShipParameters TrafficParams(TrafficShipDefinition d)
    {
        if (d.parameters != null) return d.parameters;
        string n = d.type == TrafficVesselType.Small ? "ShipParameters_Model45m" : "ShipParameters_Mariner";
        return Catalog.Ships.Find(x => x != null && x.name == n);
    }

    private static string TrafficModelTitle(TrafficShipDefinition d)
    {
        if (d.parameters != null) return ShipLabel(d.parameters);
        ShipParameters p = TrafficParams(d);
        return p != null ? $"по виду ({ShipLabel(p)})" : "по виду";
    }

    private void FillTrafficFields()
    {
        TrafficShipDefinition d = SelT;
        if (d == null || tNameField == null) return;
        Set(tNameField, d.name);
        Set(tIdField, d.id);
        switch (d.placement)
        {
            case TrafficPlacement.RelativeToOwnShip:
                tPlaceLabel.text = "Пеленг со своего, ° · дистанция, мили";
                Set(tAField, MonitorUi.Num(d.bearingDeg, 0));
                Set(tBField, MonitorUi.Num(d.distanceNm, 2));
                break;
            case TrafficPlacement.MeetingPoint:
                tPlaceLabel.text = "CPA, мили · TCPA, мин · борт своего судна";
                Set(tAField, MonitorUi.Num(d.cpaNm, 2));
                Set(tBField, MonitorUi.Num(d.tcpaMinutes, 1));
                break;
            default:
                tPlaceLabel.text = "Положение, м: восток · север";
                Set(tAField, MonitorUi.Num(d.position.East, 0));
                Set(tBField, MonitorUi.Num(d.position.North, 0));
                break;
        }
        Set(tCourseField, MonitorUi.Num(d.headingDeg, 0));
        Set(tSpeedField, MonitorUi.Num(d.speedKnots, 1));
        Set(tReqField, MonitorUi.Num(d.requiredCpaNm, 2));
        tCourseField.interactable = !CourseByRoute(d);
        tInfo.text = TrafficInfo(d);
    }

    // По маршруту (не «встреча») курс на старте считается сам — на первую точку
    private static bool CourseByRoute(TrafficShipDefinition d) =>
        d.motion == TrafficMotion.Route && d.route != null && d.route.Count > 0 && d.placement != TrafficPlacement.MeetingPoint;

    private string TrafficInfo(TrafficShipDefinition d)
    {
        OwnShipInitialState own = S.initial;
        TrafficEncounter e = TrafficEditing.Meet(d, own);
        var b = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(e.Start.Problem)) { b.AppendLine($"<color=#FF7A7A>{e.Start.Problem}</color>"); }
        b.AppendLine($"На старте: пеленг {MonitorUi.Num(e.BearingDeg, 0)}°, {MonitorUi.Num(e.RangeNm, 2)} мили · курс {MonitorUi.Num(e.Start.HeadingDeg, 0)}°" +
                     (CourseByRoute(d) ? " (на 1-ю точку)" : ""));
        if (e.Closing)
        {
            double req = d.requiredCpaNm > 0f ? d.requiredCpaNm : TrafficEditing.DefaultRequiredCpaNm;
            string side = TrafficEditing.SideAt(own, e.TargetAtCpa - e.OwnAtCpa) == MeetingSide.Starboard ? "справа" : "слева";
            string col = e.CpaNm < req ? "#FF7A7A" : "#6FE08A";
            b.AppendLine($"Без манёвра: <color={col}>CPA {MonitorUi.Num(e.CpaNm, 2)} мили</color> через {MonitorUi.Num(e.TcpaMin, 1)} мин, {side} (CPA_доп {MonitorUi.Num(req, 2)})");
        }
        else b.AppendLine("<color=#FFC85A>Суда расходятся — встречи без манёвра нет</color>");
        if (d.motion == TrafficMotion.Route)
            b.Append($"<color=#9AA6B2>Маршрут: щелчок по карте («Другое судно») — точка в конец; квадрат — тянуть / Delete</color>");
        return b.ToString();
    }

    // ─────────── Правка ───────────
    private void EditTraffic(Func<TrafficShipDefinition, bool> change)
    {
        TrafficShipDefinition d = SelT;
        if (d == null) return;
        EditScenario(() => { if (change(d)) sdoc.Commit(); });
    }

    private void EditTrafficNumber(string text, Func<double, bool> ok, Action<TrafficShipDefinition, double> set)
    {
        if (MonitorUi.TryNumber(text, out double v) && ok(v)) EditTraffic(d => { set(d, v); return true; });
        else FillScenarioFields();
    }

    // Поля A и B — по способу задания: E/N, пеленг/дистанция, CPA/TCPA
    private void EditPlaceValue(string text, bool first)
    {
        TrafficShipDefinition d = SelT;
        if (d == null) return;
        if (!MonitorUi.TryNumber(text, out double v)) { FillScenarioFields(); return; }
        switch (d.placement)
        {
            case TrafficPlacement.RelativeToOwnShip:
                if (first) EditTraffic(t => { t.bearingDeg = (float)(((v % 360.0) + 360.0) % 360.0); return true; });
                else if (v > 0.0 && v <= 50.0) EditTraffic(t => { t.distanceNm = (float)v; return true; });
                else FillScenarioFields();
                break;
            case TrafficPlacement.MeetingPoint:
                if (first && v >= 0.0 && v <= 10.0) EditTraffic(t => { t.cpaNm = (float)v; return true; });
                else if (!first && v > 0.0 && v <= 180.0) EditTraffic(t => { t.tcpaMinutes = (float)v; return true; });
                else FillScenarioFields();
                break;
            default:
                EditTraffic(t => { t.position = first ? new WorldPosition(v, t.position.North) : new WorldPosition(t.position.East, v); return true; });
                break;
        }
    }

    private void CycleTrafficType(int dir) => EditTraffic(d => { d.type = Next(d.type, dir); return true; });
    private void CycleSide() => EditTraffic(d => { d.meetingSide = Next(d.meetingSide, 1); return true; });
    private void CycleTrafficLights() => EditTraffic(d => { d.lights = Next(d.lights, 1); return true; });
    private void ToggleTrafficMotion() =>
        EditTraffic(d => { d.motion = d.motion == TrafficMotion.Route ? TrafficMotion.ConstantCourse : TrafficMotion.Route; selTPoint = -1; return true; });

    private void CyclePlacement(int dir)
    {
        if (SelT == null) return;
        EditTraffic(d => { TrafficEditing.SetPlacement(d, S.initial, Next(d.placement, dir)); return true; });
    }

    // «по виду» → суда каталога → «по виду»
    private void CycleTrafficModel(int dir)
    {
        var list = new List<ShipParameters> { null };
        foreach (ShipParameters p in Catalog.Ships) if (p != null) list.Add(p);
        EditTraffic(d =>
        {
            int k = d.parameters == null ? 0 : list.FindIndex(x => x != null && x.name == d.parameters.name);
            d.parameters = list[((k < 0 ? 0 : k) + dir + list.Count) % list.Count];
            return true;
        });
    }

    private static T Next<T>(T v, int dir) where T : Enum
    {
        var all = (T[])Enum.GetValues(typeof(T));
        int k = Array.IndexOf(all, v);
        return all[((k < 0 ? 0 : k) + dir + all.Length) % all.Length];
    }

    private void DeleteTraffic()
    {
        if (S == null) return;
        List<string> before = TriggerEditing.TargetIds(S);
        int removed = selTraffic;
        if (!TrafficEditing.Remove(S.traffic, selTraffic)) return;
        // 21.6б: номера целей сдвинулись (у целей без id — «T№» по номеру): ссылки триггеров — за целью, на удалённую — «(удалено)»
        List<string> after = TriggerEditing.TargetIds(S);
        after.Insert(removed, null);
        TriggerEditing.RemapTargets(S, before, after);
        selTraffic = selTPoint = hotTraffic = hotTPoint = -1;
        ShowPropsGroup();
        EditScenario(() => sdoc.Commit());
    }

    private void DeleteTrafficPoint()
    {
        TrafficShipDefinition d = SelT;
        if (d == null || !TrafficEditing.RemovePoint(d, selTPoint)) return;
        selTPoint = hotTPoint = -1;
        EditScenario(() => sdoc.Commit());
    }

    private void SelectTraffic(int i)
    {
        if (S == null || !TrafficEditing.Valid(S.traffic, i)) i = -1;
        if (i == selTraffic && (i < 0 || !selStart && selPoint < 0)) return;
        EndFieldEdit();
        selTraffic = i;
        selTPoint = -1;
        if (i >= 0) { selStart = false; selPoint = -1; }
        ShowPropsGroup();
        FillScenarioFields();
        Refresh();
    }

    private void ResetTrafficState()
    {
        selTraffic = hotTraffic = hotTPoint = selTPoint = -1;
        hotTHeading = hotMeet = dragTraffic = dragTHeading = dragMeet = dragTPoint = false;
    }

    // ─────────── Мышь ───────────
    private WorldPosition TrafficHeadingHandle(TrafficShipDefinition d, TrafficStart st)
    {
        ShipParameters p = TrafficParams(d);
        double len = p != null ? p.length : 100.0;
        double meters = Math.Max(45.0 * viewport.MetersPerPixel, 0.5 * len + 20.0 * viewport.MetersPerPixel);
        return RouteEditing.HeadingHandle(st.Position, st.HeadingDeg, meters);
    }

    /// <summary>Ручки выделенной цели, затем суда — под курсором; true — что-то найдено.</summary>
    private bool TrafficHit(double px, double py)
    {
        hotTraffic = hotTPoint = -1;
        hotTHeading = hotMeet = false;
        if (S?.traffic == null) return false;
        TrafficShipDefinition d = SelT;
        if (d != null)
        {
            if (!CourseByRoute(d))
            {
                viewport.ToPixel(TrafficHeadingHandle(d, TrafficPlanner.Resolve(d, S.initial)), out double hx, out double hy);
                if ((hx - px) * (hx - px) + (hy - py) * (hy - py) <= 10.0 * 10.0) return hotTHeading = true;
            }
            if (TrafficEditing.HitMeet(d, S.initial, viewport, px, py)) return hotMeet = true;
            hotTPoint = TrafficEditing.HitPoint(d, viewport, px, py);
            if (hotTPoint >= 0) return true;
        }
        hotTraffic = TrafficEditing.HitShip(S.traffic, S.initial, viewport, px, py);
        return hotTraffic >= 0;
    }

    private bool TrafficPress()
    {
        if (hotTHeading) { dragTHeading = true; sMoved = true; return true; }
        if (hotMeet) { dragMeet = true; sMoved = true; return true; }
        if (hotTPoint >= 0) { selTPoint = hotTPoint; dragTPoint = true; return true; }
        if (hotTraffic >= 0) { SelectTraffic(hotTraffic); dragTraffic = true; return true; }
        return false;
    }

    // Щелчок по пустой карте инструментом «Другое судно»: точка маршрута выделенной цели или новая цель
    private void TrafficClickEmpty(WorldPosition snapped)
    {
        TrafficShipDefinition d = SelT;
        if (d != null && d.motion == TrafficMotion.Route)
        {
            selTPoint = TrafficEditing.AddPoint(d, snapped);
            dragTPoint = true;
        }
        else
        {
            SelectTraffic(TrafficEditing.Add(S.traffic, snapped, S.initial));
            dragTraffic = true;
        }
        sMoved = true;
        FillScenarioFields();
    }

    private void TrafficDrag(WorldPosition world, WorldPosition snapped, Vector2 local)
    {
        TrafficShipDefinition d = SelT;
        if (d == null) return;
        if ((dragTraffic || dragTPoint) && !sMoved && (local - pressLocal).magnitude > 3f) sMoved = true;
        if (dragTraffic && sMoved && (TrafficEditing.StartOf(d, S.initial) - snapped).Magnitude > 0.01 && TrafficEditing.MoveStart(d, S.initial, snapped))
            FillScenarioFields();
        if (dragTPoint && sMoved && d.route != null && selTPoint >= 0 && selTPoint < d.route.Count && !d.route[selTPoint].Equals(snapped))
        {
            d.route[selTPoint] = snapped;
            FillScenarioFields();
        }
        if (dragTHeading)
        {
            float h = RouteEditing.HeadingTo(TrafficEditing.StartOf(d, S.initial), world, snap ? 5.0 : 1.0);
            if (h != d.headingDeg) { d.headingDeg = h; FillScenarioFields(); }
        }
        if (dragMeet)
        {
            float t = TrafficEditing.TcpaFrom(S.initial, world, snap ? TrafficEditing.TcpaStepMin : 0.1);
            if (t > 0f && t != d.tcpaMinutes) { d.tcpaMinutes = t; FillScenarioFields(); }
        }
    }

    /// <summary>Отпустили кнопку: конец перетаскивания цели; true — было что отпускать.</summary>
    private bool TrafficRelease()
    {
        if (!TrafficBusy) return false;
        dragTraffic = dragTHeading = dragMeet = dragTPoint = false;
        return true;
    }

    // ─────────── Карта и подписи ───────────
    private void TrafficOverlay()
    {
        overlay.Traffic.Clear();
        ScenarioDefinition s = S;
        if (s?.traffic == null) return;
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            if (d == null) continue;
            TrafficEncounter e = TrafficEditing.Meet(d, s.initial);
            ShipParameters p = TrafficParams(d);
            bool sel = i == selTraffic;
            overlay.Traffic.Add(new EditorMapOverlay.TrafficMark
            {
                Pos = e.Start.Position,
                Heading = (float)e.Start.HeadingDeg,
                Length = p != null ? p.length : 100f,
                Beam = p != null ? p.beam : 16f,
                Selected = sel,
                Hot = i == hotTraffic || sel && dragTraffic,
                ShowHeading = sel && !CourseByRoute(d),   // по маршруту курс считается сам — ручки нет
                HeadingHandle = sel ? TrafficHeadingHandle(d, e.Start) : default,
                HeadingHot = sel && (hotTHeading || dragTHeading),
                ShowMeetHandle = sel && d.placement == TrafficPlacement.MeetingPoint && s.initial.speedKnots > 0.1f,
                MeetHandle = sel ? TrafficEditing.MeetHandle(d, s.initial) : default,
                MeetHot = sel && (hotMeet || dragMeet),
                Closing = e.Closing,
                OwnAtCpa = e.OwnAtCpa,
                TargetAtCpa = e.TargetAtCpa,
                Path = e.Start.Path,
                Points = d.motion == TrafficMotion.Route ? d.route : null,
                HotPoint = sel ? (dragTPoint ? selTPoint : hotTPoint) : -1,
                SelPoint = sel ? selTPoint : -1
            });
        }
    }

    /// <summary>Подписи целей (имя) и встреч («CPA 0,42 · 11,8 мин») — в пул подписей с номера used.</summary>
    private int TrafficLabels(Rect rc, int used)
    {
        ScenarioDefinition s = S;
        if (s?.traffic == null || s.traffic.Count > 30) return used;
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            if (d == null) continue;
            TrafficEncounter e = TrafficEditing.Meet(d, s.initial);
            bool sel = i == selTraffic;
            used = PutLabel(rc, used, overlay.Px(e.Start.Position, rc) + new Vector2(14f, -14f), d.name,
                            sel ? EditorMapOverlay.HandleHot : EditorMapOverlay.TrafficColor);
            if (!e.Closing) continue;
            Vector2 mid = 0.5f * (overlay.Px(e.OwnAtCpa, rc) + overlay.Px(e.TargetAtCpa, rc));
            used = PutLabel(rc, used, mid + new Vector2(8f, 12f), $"CPA {MonitorUi.Num(e.CpaNm, 2)} · {MonitorUi.Num(e.TcpaMin, 1)} мин",
                            sel ? EditorMapOverlay.CpaColor : new Color(EditorMapOverlay.CpaColor.r, EditorMapOverlay.CpaColor.g, EditorMapOverlay.CpaColor.b, 0.75f));
        }
        return used;
    }

    private string TrafficStatus()
    {
        ScenarioDefinition s = S;
        int i = dragTraffic || dragTHeading || dragMeet || dragTPoint ? selTraffic : hotTraffic >= 0 ? hotTraffic : hotTHeading || hotMeet || hotTPoint >= 0 ? selTraffic : -1;
        if (s == null || !TrafficEditing.Valid(s.traffic, i)) return "";
        TrafficShipDefinition d = s.traffic[i];
        TrafficEncounter e = TrafficEditing.Meet(d, s.initial);
        string m = e.Closing ? $"CPA {MonitorUi.Num(e.CpaNm, 2)} мили через {MonitorUi.Num(e.TcpaMin, 1)} мин" : "встречи нет";
        string what = hotMeet || dragMeet ? " — тянуть вдоль своего пути: TCPA"
                    : hotTHeading || dragTHeading ? $" — курс {MonitorUi.Num(d.headingDeg, 0)}°"
                    : d.placement == TrafficPlacement.MeetingPoint && (hotTraffic >= 0 || dragTraffic) ? " — место считается само (ромб — TCPA, ручка — курс)" : "";
        return $" · судно «{d.name}»: {m}{what}";
    }
}
