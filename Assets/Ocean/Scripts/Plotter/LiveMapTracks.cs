// ============================================================
// LiveMapTracks.cs — Суда и их следы для живой карты (D1, D2)
//
// Общий для окна инструктора (InstructorWindow) и навигационного планшета
// (NavPlotter): своё судно и цели из реестра ExerciseTargets — положение, курс,
// скорость, размеры и след (точка не чаще, чем через TrailMinMeters, не больше
// TrailMaxPoints; скачок больше 500 м — перестановка судна, след заново).
// Цвет цели — по её порядку в реестре (у окна и планшета одинаковый).
// Фильтр include (планшет): цель, которую не показывать, убирается вместе со
// следом — после обнаружения след начинается с момента обнаружения.
// Вынесено из InstructorWindow (D2); поведение окна инструктора прежнее.
// ============================================================
using System;
using System.Collections.Generic;

public sealed class LiveMapTracks
{
    public float TrailMinMeters = 10f;
    public int TrailMaxPoints = 4000;

    public readonly InstructorMapGraphic.Ship Own = new InstructorMapGraphic.Ship { Id = "своё", Own = true, Color = DebriefMapGraphic.OwnColor };
    private readonly Dictionary<string, InstructorMapGraphic.Ship> ships = new Dictionary<string, InstructorMapGraphic.Ship>();
    private readonly HashSet<string> seen = new HashSet<string>();
    private readonly List<string> gone = new List<string>();

    public IEnumerable<InstructorMapGraphic.Ship> Targets => ships.Values;

    /// <summary>Обновить суда по навигации и реестру целей; include — показывать ли цель (null — все).</summary>
    public void Update(ShipNavigation navigation, Func<ITrackedTarget, bool> include = null)
    {
        if (navigation == null || navigation.Model == null) return;
        Own.Position = navigation.PositionEastNorth;
        Own.HeadingDeg = navigation.Heading;
        Own.SpeedKnots = navigation.SpeedKnots;
        if (navigation.parameters != null) { Own.LengthM = navigation.parameters.length; Own.BeamM = navigation.parameters.beam; Own.ParallelL = navigation.parameters.hullParallelForwardL; }
        AddTrail(Own);

        seen.Clear();
        int series = 0;
        foreach (ITrackedTarget t in ExerciseTargets.All)
        {
            int color = series++;
            if (include != null && !include(t)) continue;
            if (!ships.TryGetValue(t.TargetId, out InstructorMapGraphic.Ship s))
                ships[t.TargetId] = s = new InstructorMapGraphic.Ship { Id = t.TargetId };
            s.Color = DebriefMapGraphic.TargetColor(color);
            s.Position = t.Position;
            s.HeadingDeg = t.HeadingDeg;
            s.SpeedKnots = t.SpeedKnots;
            if (t is TrafficVessel v && v.Model.Parameters != null) { s.LengthM = v.Model.Parameters.length; s.BeamM = v.Model.Parameters.beam; }
            else if (t is TugTarget g) { s.LengthM = g.LengthM; s.BeamM = g.BeamM; }   // 19.3б
            else if (t is ClientShip c) { s.LengthM = c.LengthM; s.BeamM = c.BeamM; }  // 19.4б-2
            AddTrail(s);
            seen.Add(t.TargetId);
        }
        gone.Clear();
        foreach (string id in ships.Keys) if (!seen.Contains(id)) gone.Add(id);
        foreach (string id in gone) ships.Remove(id);
    }

    /// <summary>Суда — в подвижный слой карты (своё — поверх целей).</summary>
    public void Fill(InstructorMapGraphic g)
    {
        g.Ships.Clear();
        foreach (InstructorMapGraphic.Ship s in ships.Values) g.Ships.Add(s);
        g.Ships.Add(Own);
    }

    public void ClearTrails()
    {
        Own.Trail.Clear();
        foreach (InstructorMapGraphic.Ship s in ships.Values) s.Trail.Clear();
    }

    private void AddTrail(InstructorMapGraphic.Ship s)
    {
        List<WorldPosition> tr = s.Trail;
        if (tr.Count > 0)
        {
            WorldPosition last = tr[tr.Count - 1];
            double de = s.Position.East - last.East, dn = s.Position.North - last.North;
            double d2 = de * de + dn * dn;
            if (d2 < TrailMinMeters * TrailMinMeters) return;
            if (d2 > 500.0 * 500.0) tr.Clear();   // перестановка судна (начальные условия) — след заново
        }
        tr.Add(s.Position);
        if (tr.Count > TrailMaxPoints) tr.RemoveRange(0, tr.Count - TrailMaxPoints);
    }
}
