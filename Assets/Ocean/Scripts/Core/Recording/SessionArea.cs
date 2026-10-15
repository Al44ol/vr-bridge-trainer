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
// 19.2б-3б: в снимок входят причалы (SessionBerth) — пирс, кранцы, кнехты на схеме.
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

/// <summary>
/// 19.2б-3б: причал в снимке акватории — те же поля, что BerthArea (кромка A→B, вода справа; кранцы,
/// кнехты, вид), и рассчитанные положения кранцев и кнехтов вдоль кромки (для анализа без кода проекта).
/// </summary>
[Serializable]
public class SessionBerth
{
    public string name = "";
    public double a_x_e_m, a_y_n_m, b_x_e_m, b_y_n_m;
    public double quay_height_m, depth_m;
    public double fender_outer_diameter_m, fender_rated_deflection_m, fender_rated_reaction_kn, fender_length_m;
    public double fender_spacing_m, fender_end_margin_m, fender_friction;
    public double bollard_spacing_m, bollard_end_margin_m, bollard_setback_m;
    public double structure_width_m;
    /// <summary>Кранцы и кнехты: расстояние от A вдоль кромки, м.</summary>
    public List<double> fender_t_m = new List<double>();
    public List<double> bollard_t_m = new List<double>();

    public static SessionBerth From(BerthArea b)
    {
        var r = new SessionBerth
        {
            name = b.name ?? "",
            a_x_e_m = R(b.a.East), a_y_n_m = R(b.a.North), b_x_e_m = R(b.b.East), b_y_n_m = R(b.b.North),
            quay_height_m = R(b.quayHeightM), depth_m = R(b.depthM),
            fender_outer_diameter_m = R(b.fenderOuterDiameterM), fender_rated_deflection_m = R(b.fenderRatedDeflectionM),
            fender_rated_reaction_kn = R(b.fenderRatedReactionKn), fender_length_m = R(b.fenderLengthM),
            fender_spacing_m = R(b.fenderSpacingM), fender_end_margin_m = R(b.fenderEndMarginM), fender_friction = R(b.fenderFriction),
            bollard_spacing_m = R(b.bollardSpacingM), bollard_end_margin_m = R(b.bollardEndMarginM), bollard_setback_m = R(b.bollardSetbackM),
            structure_width_m = R(b.structureWidthM)
        };
        foreach (double t in b.FenderPositions()) r.fender_t_m.Add(R(t));
        foreach (double t in b.BollardPositions()) r.bollard_t_m.Add(R(t));
        return r;
    }

    /// <summary>Причал для расчётов (кранцы и кнехты расставляются заново по тем же правилам).</summary>
    public BerthArea ToBerth() => new BerthArea
    {
        name = name ?? "",
        a = new WorldPosition(a_x_e_m, a_y_n_m), b = new WorldPosition(b_x_e_m, b_y_n_m),
        quayHeightM = (float)quay_height_m, depthM = (float)depth_m,
        fenderOuterDiameterM = (float)fender_outer_diameter_m, fenderRatedDeflectionM = (float)fender_rated_deflection_m,
        fenderRatedReactionKn = (float)fender_rated_reaction_kn, fenderLengthM = (float)fender_length_m,
        fenderSpacingM = (float)fender_spacing_m, fenderEndMarginM = (float)fender_end_margin_m, fenderFriction = (float)fender_friction,
        bollardSpacingM = (float)bollard_spacing_m, bollardEndMarginM = (float)bollard_end_margin_m, bollardSetbackM = (float)bollard_setback_m,
        structureWidthM = (float)structure_width_m
    };

    private static double R(double v) => Math.Round(v, 3);
}

[Serializable]
public class SessionArea
{
    public string name = "";
    public List<SessionAid> aids = new List<SessionAid>();
    public List<SessionShoal> shoals = new List<SessionShoal>();
    public List<SessionLand> land = new List<SessionLand>();
    public List<SessionFairway> fairways = new List<SessionFairway>();
    /// <summary>19.2б-3б: причалы (у записей до 19.2б-3б поля нет — список пуст).</summary>
    public List<SessionBerth> berths = new List<SessionBerth>();

    public bool IsEmpty => (aids == null || aids.Count == 0) && (shoals == null || shoals.Count == 0) &&
                           (land == null || land.Count == 0) && (fairways == null || fairways.Count == 0) &&
                           (berths == null || berths.Count == 0);

    /// <summary>Причал i (null — нет такого).</summary>
    public BerthArea Berth(int i) => berths != null && i >= 0 && i < berths.Count && berths[i] != null ? berths[i].ToBerth() : null;

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
        if (a.berths != null)
            foreach (BerthArea x in a.berths)
                r.berths.Add(x != null ? SessionBerth.From(x) : new SessionBerth());   // номера — как в WaterArea.berths (пустой — кромка 0 м, не рисуется)
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
