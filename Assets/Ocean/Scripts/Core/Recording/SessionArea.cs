// ============================================================
// SessionArea.cs — Снимок учебной акватории в записи сессии (C3)
//
// До C3 в session.json было только имя ассета акватории. Схема траектории
// разбора (буи, мели, суша, фарватеры) должна строиться по самой записи:
// ассет могут поправить после эксперимента, а разбор старой сессии должен
// показывать акваторию такой, какой её видел обучающийся. Поэтому в начале
// сессии в session.json (scenario.area) пишется снимок; для сессий до C3
// снимка нет — редактор подставляет ассет по имени (From(WaterArea)).
// Классы — простые данные (без ScriptableObject): снимок можно читать
// в фоновом потоке (отчёт в шлеме строится в фоне).
// ============================================================
using System;
using System.Collections.Generic;

[Serializable]
public class SessionAid
{
    public string name = "";
    /// <summary>Тип знака (AidType).</summary>
    public string type = "";
    public double x_e_m, y_n_m;

    public AidType Type => Enum.TryParse(type, out AidType t) ? t : AidType.SafeWater;
    public WorldPosition Position => new WorldPosition(x_e_m, y_n_m);
}

[Serializable]
public class SessionShoal
{
    public string name = "";
    public double depth_m;
    public List<SessionPoint> points = new List<SessionPoint>();
}

[Serializable]
public class SessionLand
{
    public string name = "";
    public double height_m;
    public List<SessionPoint> points = new List<SessionPoint>();
}

[Serializable]
public class SessionFairway
{
    public string name = "";
    public double width_m;
    public List<SessionPoint> axis = new List<SessionPoint>();
}

[Serializable]
public class SessionArea
{
    public string name = "";
    public List<SessionAid> aids = new List<SessionAid>();
    public List<SessionShoal> shoals = new List<SessionShoal>();
    public List<SessionLand> land = new List<SessionLand>();
    public List<SessionFairway> fairways = new List<SessionFairway>();

    public bool IsEmpty => (aids == null || aids.Count == 0) && (shoals == null || shoals.Count == 0) &&
                           (land == null || land.Count == 0) && (fairways == null || fairways.Count == 0);

    public static SessionArea From(WaterArea a)
    {
        var r = new SessionArea();
        if (a == null) return r;
        r.name = a.name;
        if (a.aids != null)
            foreach (AidToNavigation x in a.aids)
                if (x != null) r.aids.Add(new SessionAid { name = x.name, type = x.type.ToString(), x_e_m = R(x.position.East), y_n_m = R(x.position.North) });
        if (a.shoals != null)
            foreach (ShoalArea x in a.shoals)
                if (x != null) r.shoals.Add(new SessionShoal { name = x.name, depth_m = R(x.depth), points = Points(x.vertices) });
        if (a.land != null)
            foreach (LandArea x in a.land)
                if (x != null) r.land.Add(new SessionLand { name = x.name, height_m = R(x.height), points = Points(x.vertices) });
        if (a.fairways != null)
            foreach (FairwayLine x in a.fairways)
                if (x != null) r.fairways.Add(new SessionFairway { name = x.name, width_m = R(x.width), axis = Points(x.axis) });
        return r;
    }

    public static List<WorldPosition> ToWorld(List<SessionPoint> pts)
    {
        var list = new List<WorldPosition>();
        if (pts != null) foreach (SessionPoint p in pts) list.Add(new WorldPosition(p.x_e_m, p.y_n_m));
        return list;
    }

    private static List<SessionPoint> Points(List<WorldPosition> v)
    {
        var list = new List<SessionPoint>();
        if (v != null) foreach (WorldPosition p in v) list.Add(new SessionPoint { x_e_m = R(p.East), y_n_m = R(p.North) });
        return list;
    }

    private static double R(double v) => Math.Round(v, 3);
}
