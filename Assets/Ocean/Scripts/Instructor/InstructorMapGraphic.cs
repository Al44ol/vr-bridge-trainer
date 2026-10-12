// ============================================================
// InstructorMapGraphic.cs — Подвижный слой карты окна инструктора (D1)
//
// Карта окна инструктора — два слоя одной техники со схемой разбора C3
// (сетка UI, цвет вершин, без текстур):
//   • неподвижный — DebriefMapGraphic со схемой DebriefMap.BuildStatic (акватория,
//     мели, знаки, маршрут и коридор); перестраивается только при смене масштаба
//     или перецентровке окна (MapViewport.Version);
//   • подвижный — этот класс: след своего судна и целей, вектор скорости
//     (VectorMinutes), силуэт корпуса по курсу (в масштабе, но не меньше MinHullPx),
//     отметки вводных; перестраивается с частотой обновления окна (5 Гц).
// Окно (границы вида) у обоих слоёв общее — DebriefMapData.Views[0].Bounds.
// Подписи судов — текстом TextMeshPro (InstructorWindow, LocalOf).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InstructorMapGraphic : DebriefMapGraphic
{
    public sealed class Ship
    {
        public string Id = "";
        public WorldPosition Position;
        public double HeadingDeg, SpeedKnots, LengthM = 45, BeamM = 10;
        public Color Color = Color.white;
        public bool Own;
        public readonly List<WorldPosition> Trail = new List<WorldPosition>();
    }

    public struct Marker
    {
        public WorldPosition Position;
        public string Label;
    }

    [Tooltip("Длина вектора скорости, мин")]
    public float vectorMinutes = 3f;
    [Tooltip("Наименьшая длина силуэта судна на карте, пикселей")]
    public float minHullPx = 16f;

    public readonly List<Ship> Ships = new List<Ship>();
    public readonly List<Marker> Failures = new List<Marker>();

    /// <summary>Точка карты в координатах этой графики (для подписей).</summary>
    public Vector2 LocalOf(WorldPosition p)
    {
        Layout();
        return ToLocal(p);
    }

    public bool InView(WorldPosition p)
    {
        Layout();
        return View != null && view.Contains(p);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Layout();
        if (View == null) return;

        foreach (Ship s in Ships)
            if (s.Trail.Count > 1)
            {
                Color c = s.Color;
                c.a = 0.65f;
                Polyline(vh, s.Trail, s.Own ? 2.4f : 1.8f, c);
            }
        foreach (Ship s in Ships)
        {
            double minutes = Math.Max(0.0, vectorMinutes);
            if (minutes <= 0 || Math.Abs(s.SpeedKnots) < 0.05) continue;
            CpaCalculator.Velocity(s.HeadingDeg, s.SpeedKnots, out double ve, out double vn);
            var end = new WorldPosition(s.Position.East + ve * minutes * 60.0, s.Position.North + vn * minutes * 60.0);
            Segment(vh, s.Position, end, 1.6f, s.Color);
        }
        foreach (Ship s in Ships) Hull(vh, s);
        foreach (Marker m in Failures) Ring(vh, m.Position, 7f, 2.5f, FailureColor);
    }

    // Силуэт: нос — точка, плечи — B от носа, корма — прямая; поворот по курсу (по часовой от N)
    private readonly List<WorldPosition> hull = new List<WorldPosition>(5);

    private void Hull(VertexHelper vh, Ship s)
    {
        double L = Math.Max(1.0, s.LengthM), B = Math.Max(0.5, s.BeamM);
        double k = Math.Max(1.0, minHullPx / (L * scale));   // мелко — увеличить до minHullPx
        L *= k; B *= k;
        double psi = s.HeadingDeg * Math.PI / 180.0, sn = Math.Sin(psi), cs = Math.Cos(psi);
        hull.Clear();
        void P(double x, double y) => hull.Add(new WorldPosition(s.Position.East + x * cs + y * sn, s.Position.North - x * sn + y * cs));
        P(0, 0.5 * L);
        P(0.5 * B, 0.5 * L - Math.Min(B, 0.4 * L));
        P(0.5 * B, -0.5 * L);
        P(-0.5 * B, -0.5 * L);
        P(-0.5 * B, 0.5 * L - Math.Min(B, 0.4 * L));
        Polygon(vh, hull, s.Color);
    }
}
