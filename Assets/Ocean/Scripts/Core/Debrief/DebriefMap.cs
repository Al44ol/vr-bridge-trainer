// ============================================================
// DebriefMap.cs — Схема траектории для разбора (C3)
//
// Схема вида сверху строится ОДИН раз по записанной сессии (track.csv,
// targets.csv, events.csv, session.json) и итогу оценки — как набор фигур
// в координатах восток/север. Рисуют её два исполнителя:
//   • DebriefSvg — SVG для report.html и рисунков ВКР (чёрно-белый стиль);
//   • DebriefMapGraphic — VR-панель разбора (цвет, сетка UI).
// Содержимое: суша, мели, ось фарватера, маршрут и коридор (±XTE доп. по
// участкам), знаки МАМС, траектории своего судна и целей, отметки времени,
// старт, начало манёвра по каждому сближению, наименьшая дистанция до каждой
// цели (оба судна в этот момент и отрезок между ними), посадка и столкновение.
// Виды: «весь маршрут» и окно вокруг каждого завершённого сближения
// (в масштабе маршрута 7 км расхождение на 100 м не разглядеть).
// ============================================================
using System;
using System.Collections.Generic;

public enum MapLayer
{
    Land, Shoal, Fairway, Corridor, Route, Waypoint, Aid,
    TargetTrack, OwnTrack, TimeTick, Start, Manoeuvre, MinLink, MinPoint, Accident
}

public sealed class MapLine
{
    public MapLayer Layer;
    /// <summary>Номер цели (для TargetTrack, MinLink), −1 — своё судно.</summary>
    public int Series = -1;
    public List<WorldPosition> Points = new List<WorldPosition>();
    public string Label = "";
    /// <summary>Для MinLink: подпись сближения (= DebriefMapView.Key окна этого сближения).</summary>
    public string Encounter = "";
    /// <summary>Для MinLink: дистанция не меньше допустимой.</summary>
    public bool Ok = true;
}

public sealed class MapPolygon
{
    public MapLayer Layer;
    public List<WorldPosition> Points = new List<WorldPosition>();
    public string Label = "";
}

public sealed class MapMarker
{
    public MapLayer Layer;
    public WorldPosition Position;
    public int Series = -1;
    public AidType Aid;
    /// <summary>Время упражнения, с (отметки времени, манёвры).</summary>
    public double Time = double.NaN;
    public string Label = "";
    public bool Ok = true;
}

public sealed class DebriefMapView
{
    public string Key = "", Title = "";
    public MapRect Bounds;
    /// <summary>Шаг отметок времени на траектории, с.</summary>
    public double TickStepS = 300;
}

public sealed class DebriefMapData
{
    public readonly List<MapPolygon> Polygons = new List<MapPolygon>();
    public readonly List<MapLine> Lines = new List<MapLine>();
    public readonly List<MapMarker> Markers = new List<MapMarker>();
    public readonly List<DebriefMapView> Views = new List<DebriefMapView>();
    /// <summary>Подписи целей по номеру серии: «T1 — сухогруз».</summary>
    public readonly List<string> TargetNames = new List<string>();
    public readonly List<string> TargetIds = new List<string>();
    public bool HasArea, HasRoute;
}

public static class DebriefMap
{
    /// <summary>Минимальный размер окна сближения, м.</summary>
    public const double EncounterWindowMinM = 1000;

    /// <summary>Схема по записи. result — итог оценки (null — без отметок сближений); area — акватория (null — из записи).</summary>
    public static DebriefMapData Build(RecordedSession s, AssessmentResult result, SessionArea area = null)
    {
        var d = new DebriefMapData();
        SessionScenario sc = s.Meta != null && s.Meta.scenario != null ? s.Meta.scenario : new SessionScenario();
        if (area == null || area.IsEmpty) area = sc.area;
        AddArea(d, area);
        AddRoute(d, sc);

        // Своё судно
        var own = new MapLine { Layer = MapLayer.OwnTrack, Label = "своё судно" };
        foreach (TrackSample t in s.Track) own.Points.Add(t.Position);
        if (own.Points.Count > 1) d.Lines.Add(own);
        if (s.Track.Count > 0)
        {
            d.Markers.Add(new MapMarker { Layer = MapLayer.Start, Position = s.Track[0].Position, Time = 0, Label = "старт" });
            for (double t = 60; t < s.Track[s.Track.Count - 1].Time; t += 60)
                d.Markers.Add(new MapMarker { Layer = MapLayer.TimeTick, Position = s.StateAt(t).Position, Time = t, Label = ScenarioText.Num(t / 60.0) });
        }

        // Цели: порядок — как в сценарии
        Dictionary<string, List<TargetSample>> tracks = TargetTracks(s);
        var order = new List<string>();
        foreach (SessionTraffic tr in sc.traffic) if (tracks.ContainsKey(tr.id)) order.Add(tr.id);
        foreach (string id in tracks.Keys) if (!order.Contains(id)) order.Add(id);
        foreach (string id in order)
        {
            int series = d.TargetIds.Count;
            d.TargetIds.Add(id);
            SessionTraffic info = sc.traffic.Find(x => x.id == id);
            d.TargetNames.Add(info != null && !string.IsNullOrEmpty(info.type)
                ? $"{id} — {TypeTitle(info.type)}" : id);
            var line = new MapLine { Layer = MapLayer.TargetTrack, Series = series, Label = id };
            foreach (TargetSample t in tracks[id]) line.Points.Add(new WorldPosition(t.East, t.North));
            if (line.Points.Count > 1) d.Lines.Add(line);
        }

        // Сближения: начало манёвра и наименьшая дистанция
        var manoeuvres = new Dictionary<double, MapMarker>();
        if (result != null)
            foreach (EncounterSummary e in result.Encounters)
            {
                int series = d.TargetIds.IndexOf(string.IsNullOrEmpty(e.TargetKey) ? e.TargetId : e.TargetKey);
                if (!double.IsNaN(e.ActionTime) && s.Track.Count > 0)
                {
                    double key = Math.Round(e.ActionTime, 3);
                    if (manoeuvres.TryGetValue(key, out MapMarker m)) m.Label += ", " + e.TargetId;   // одно действие — несколько сближений
                    else
                    {
                        m = new MapMarker { Layer = MapLayer.Manoeuvre, Position = s.StateAt(e.ActionTime).Position, Time = e.ActionTime, Label = e.TargetId, Series = series };
                        manoeuvres[key] = m;
                        d.Markers.Add(m);
                    }
                }
                if (!e.Complete || double.IsNaN(e.MinTime) || series < 0 || s.Track.Count == 0) continue;
                WorldPosition po = s.StateAt(e.MinTime).Position;
                if (!TryTargetAt(tracks[d.TargetIds[series]], e.MinTime, out WorldPosition pt)) continue;
                bool ok = e.Collided ? false : e.MinDistanceM >= e.RequiredCpaM - 1e-6;
                string dist = e.Collided ? "столкновение" : $"{ScenarioText.Num(e.MinDistanceM)} м";
                d.Lines.Add(new MapLine { Layer = MapLayer.MinLink, Series = series, Points = { po, pt }, Label = dist, Ok = ok, Encounter = e.TargetId });
                d.Markers.Add(new MapMarker { Layer = MapLayer.MinPoint, Position = po, Series = -1, Time = e.MinTime, Ok = ok });
                d.Markers.Add(new MapMarker { Layer = MapLayer.MinPoint, Position = pt, Series = series, Time = e.MinTime, Label = e.TargetId, Ok = ok });
            }

        // Аварии
        foreach (ExerciseEvent e in s.Events)
        {
            if (e.Type == ExerciseEventType.Grounded)
                d.Markers.Add(new MapMarker { Layer = MapLayer.Accident, Position = e.Position, Time = e.Time, Label = "мель", Ok = false });
            else if (e.Type == ExerciseEventType.Collision)
                d.Markers.Add(new MapMarker { Layer = MapLayer.Accident, Position = e.Position, Time = e.Time, Label = "столкновение", Ok = false });
        }

        AddViews(d, s, result, tracks);
        return d;
    }

    private static void AddArea(DebriefMapData d, SessionArea a)
    {
        if (a == null) return;
        d.HasArea = !a.IsEmpty;
        if (a.land != null)
            foreach (SessionLand x in a.land)
                d.Polygons.Add(new MapPolygon { Layer = MapLayer.Land, Points = SessionArea.ToWorld(x.points), Label = x.name });
        if (a.shoals != null)
            foreach (SessionShoal x in a.shoals)
                d.Polygons.Add(new MapPolygon { Layer = MapLayer.Shoal, Points = SessionArea.ToWorld(x.points), Label = $"{ScenarioText.Num(x.depth_m, 1)} м" });
        if (a.fairways != null)
            foreach (SessionFairway x in a.fairways)
                d.Lines.Add(new MapLine { Layer = MapLayer.Fairway, Points = SessionArea.ToWorld(x.axis), Label = x.name });
        if (a.aids != null)
            foreach (SessionAid x in a.aids)
                d.Markers.Add(new MapMarker { Layer = MapLayer.Aid, Position = x.Position, Aid = x.Type, Label = x.name });
    }

    private static void AddRoute(DebriefMapData d, SessionScenario sc)
    {
        if (sc.route == null || sc.route.Count < 2) return;
        d.HasRoute = true;
        var route = new MapLine { Layer = MapLayer.Route, Label = "маршрут" };
        for (int i = 0; i < sc.route.Count; i++)
        {
            SessionWaypoint w = sc.route[i];
            var p = new WorldPosition(w.x_e_m, w.y_n_m);
            route.Points.Add(p);
            d.Markers.Add(new MapMarker { Layer = MapLayer.Waypoint, Position = p, Label = w.name, Series = i });
            if (i == 0 || w.xte_limit_m <= 0) continue;
            // Коридор участка i (от точки i−1 к точке i): ±XTE доп.
            var a = new WorldPosition(sc.route[i - 1].x_e_m, sc.route[i - 1].y_n_m);
            foreach (double side in new[] { -1.0, 1.0 })
            {
                (WorldPosition oa, WorldPosition ob) = MapGeometry.Offset(a, p, side * w.xte_limit_m);
                d.Lines.Add(new MapLine { Layer = MapLayer.Corridor, Points = { oa, ob }, Series = i, Label = w.narrow_channel ? "узкость" : "" });
            }
        }
        d.Lines.Add(route);
    }

    private static void AddViews(DebriefMapData d, RecordedSession s, AssessmentResult result, Dictionary<string, List<TargetSample>> tracks)
    {
        // Весь маршрут: своё судно, маршрут, цели за время упражнения
        MapRect all = MapRect.Empty;
        foreach (TrackSample t in s.Track) all.Include(t.Position);
        if (s.Meta?.scenario?.route != null)
            foreach (SessionWaypoint w in s.Meta.scenario.route) all.Include(new WorldPosition(w.x_e_m, w.y_n_m));
        foreach (List<TargetSample> list in tracks.Values)
            foreach (TargetSample t in list) all.Include(new WorldPosition(t.East, t.North));
        d.Views.Add(new DebriefMapView { Key = "all", Title = "Весь маршрут", Bounds = all.Padded(0.05, 500), TickStepS = 300 });

        if (result == null) return;
        foreach (EncounterSummary e in result.Encounters)
        {
            if (!e.Complete || double.IsNaN(e.MinTime)) continue;
            string key = string.IsNullOrEmpty(e.TargetKey) ? e.TargetId : e.TargetKey;
            if (!tracks.TryGetValue(key, out List<TargetSample> tt)) continue;
            // Окно: от начала опасности (или манёвра, если раньше) за минуту — до 2 мин после наименьшей дистанции
            double from = Math.Max(0, Math.Min(Nz(e.OnsetTime, e.MinTime), Nz(e.ActionTime, e.MinTime)) - 60);
            double to = e.MinTime + 120;
            MapRect r = MapRect.Empty;
            foreach (TrackSample t in s.Track) if (t.Time >= from && t.Time <= to) r.Include(t.Position);
            foreach (TargetSample t in tt) if (t.Time >= from && t.Time <= to) r.Include(new WorldPosition(t.East, t.North));
            d.Views.Add(new DebriefMapView
            {
                Key = e.TargetId, Title = $"Сближение с {e.TargetId}: {EncounterClassifier.Title(e.Situation)}",
                Bounds = r.Padded(0.12, EncounterWindowMinM), TickStepS = 60
            });
        }
    }

    private static double Nz(double v, double fallback) => double.IsNaN(v) ? fallback : v;

    public static Dictionary<string, List<TargetSample>> TargetTracks(RecordedSession s)
    {
        var map = new Dictionary<string, List<TargetSample>>();
        foreach (TargetSample t in s.Targets)
        {
            if (string.IsNullOrEmpty(t.Id)) continue;
            if (!map.TryGetValue(t.Id, out var list)) map[t.Id] = list = new List<TargetSample>();
            list.Add(t);
        }
        return map;
    }

    /// <summary>Положение цели в момент t (линейно между строками targets.csv).</summary>
    public static bool TryTargetAt(List<TargetSample> list, double t, out WorldPosition p)
    {
        p = default;
        if (list == null || list.Count == 0) return false;
        if (t <= list[0].Time) { p = new WorldPosition(list[0].East, list[0].North); return true; }
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].Time < t) continue;
            TargetSample a = list[i - 1], b = list[i];
            double span = b.Time - a.Time, k = span > 1e-9 ? (t - a.Time) / span : 0;
            p = new WorldPosition(a.East + (b.East - a.East) * k, a.North + (b.North - a.North) * k);
            return true;
        }
        TargetSample last = list[list.Count - 1];
        p = new WorldPosition(last.East, last.North);
        return true;
    }

    private static string TypeTitle(string type) =>
        Enum.TryParse(type, out TrafficVesselType t) ? ScenarioText.VesselType(t) : type;
}
