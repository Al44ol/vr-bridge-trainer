// ============================================================
// PlotterTests.cs — Навигационный планшет (D2)
//
// Шкала дальности и масштаб; какие цели видит обучающийся (обнаружение C2, флаг
// сценария C4, режим инструктора «все цели»); пеленг, дистанция и время до точки;
// опасная цель — ближайшая по CPA; раскладка «курс вверх»: повёрнутый слой при
// любом курсе закрывает окно, судно — в своей точке экрана, точка прямо по носу —
// вверху; окно карты планшета (вписывание в окно меньше слоя, шкала без сдвига
// центра); новое событие журнала — в конце перечня, с текстом, читается из записи.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

public class PlotterTests
{
    [Test]
    public void Ranges_StepScaleAndTitle()
    {
        Assert.AreEqual(2.0, PlotterMath.RangeNm(PlotterMath.DefaultRangeIndex));
        Assert.AreEqual(0, PlotterMath.StepRange(0, -1), "меньше 0,25 мили — нет");
        Assert.AreEqual(PlotterMath.RangesNm.Length - 1, PlotterMath.StepRange(PlotterMath.RangesNm.Length - 1, +1), "больше 8 миль — нет");
        Assert.AreEqual(4, PlotterMath.StepRange(3, +5), "шаг — на одну шкалу");
        // 2 мили от центра до верхнего края окна высотой 400 пикселей: 3704 м / 200 пикс.
        Assert.AreEqual(18.52, PlotterMath.MetersPerPixel(2, 400), 1e-9);
        Assert.AreEqual("0,25 мили", PlotterMath.RangeTitle(0.25));
        Assert.AreEqual("0,5 мили", PlotterMath.RangeTitle(0.5));
        Assert.AreEqual("1 миля", PlotterMath.RangeTitle(1));
        Assert.AreEqual("2 мили", PlotterMath.RangeTitle(2));
        Assert.AreEqual("8 миль", PlotterMath.RangeTitle(8));
    }

    [TestCase(false, false, false, false, true, TestName = "свободное плавание — все цели")]
    [TestCase(true, false, false, false, false, TestName = "упражнение — до обнаружения не видна")]
    [TestCase(true, false, false, true, true, TestName = "упражнение — обнаружена")]
    [TestCase(true, true, false, true, false, TestName = "флаг сценария — не видна и после обнаружения")]
    [TestCase(true, true, true, false, true, TestName = "все цели (инструктор) — видна и при флаге")]
    [TestCase(true, false, true, false, true, TestName = "все цели (инструктор) — видна до обнаружения")]
    public void Targets_ShownByDetectionFlagAndReveal(bool scenario, bool hide, bool reveal, bool detected, bool expected)
    {
        Assert.AreEqual(expected, PlotterMath.ShowTarget(scenario, hide, reveal, detected));
    }

    [Test]
    public void Waypoint_BearingDistanceEta_NearestByCpa()
    {
        WaypointBearing w = PlotterMath.ToWaypoint(new WorldPosition(100, 200), new WorldPosition(1100, 1200), 10);
        Assert.AreEqual(45.0, w.BearingDeg, 1e-9);
        Assert.AreEqual(1000 * Math.Sqrt(2), w.DistanceM, 1e-9);
        Assert.AreEqual(1000 * Math.Sqrt(2) / (10 * 1852.0 / 3600.0), w.EtaS, 1e-9, "время на 10 уз");
        Assert.IsTrue(double.IsNaN(PlotterMath.ToWaypoint(default, new WorldPosition(0, 500), 0.1).EtaS), "без хода — без времени");
        Assert.AreEqual(270.0, PlotterMath.ToWaypoint(default, new WorldPosition(-10, 0), 5).BearingDeg, 1e-9);
        Assert.AreEqual(180.0, PlotterMath.ToWaypoint(default, new WorldPosition(0, -10), 5).BearingDeg, 1e-9);

        var list = new List<CpaResult>
        {
            new CpaResult { Cpa = 50, Tcpa = -30 },    // уже прошла — не опасна
            new CpaResult { Cpa = 400, Tcpa = 300 },
            new CpaResult { Cpa = 200, Tcpa = 600 },
            new CpaResult { Cpa = 200, Tcpa = 240 },   // тот же CPA, раньше
        };
        Assert.AreEqual(3, PlotterMath.NearestByCpa(list));
        Assert.AreEqual(-1, PlotterMath.NearestByCpa(new List<CpaResult> { new CpaResult { Cpa = 10, Tcpa = -1 } }), "все расходятся — опасной нет");
    }

    [Test]
    public void HeadUp_LayerCoversWindow_ShipAtItsPoint_AheadIsUp()
    {
        const double W = 400, H = 400;
        PlotterLayout L = PlotterMath.Layout(W, H, true, 0.3, 0.35);
        Assert.IsTrue(L.HeadUp);
        Assert.AreEqual(-60, L.ShipY, 1e-9, "судно на 0,3 полувысоты ниже центра");
        double half = 0.5 * L.ContentSide;
        double dAxis = 0.5 * L.FollowBox * L.ContentSide;   // допустимый уход судна по оси (MapViewport.Track)
        var corners = new[] { (-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2) };
        for (double psi = 0; psi < 360; psi += 7.5)
            foreach (double px in new[] { -dAxis, 0, dAxis })
                foreach (double py in new[] { -dAxis, 0, dAxis })
                {
                    PlotterMath.ContentOffset(psi, px, py, L.ShipX, L.ShipY, out double ox, out double oy);
                    // Судно (точка слоя p) — в точке S окна
                    PlotterMath.Rotate(px, py, psi, out double sx, out double sy);
                    Assert.AreEqual(L.ShipX, ox + sx, 1e-9);
                    Assert.AreEqual(L.ShipY, oy + sy, 1e-9);
                    // Угол окна c → точка слоя R(−ψ)·(c − offset) — внутри слоя
                    foreach ((double cx, double cy) in corners)
                    {
                        PlotterMath.Rotate(cx - ox, cy - oy, -psi, out double qx, out double qy);
                        Assert.LessOrEqual(Math.Abs(qx), half + 1e-9, $"ψ {psi}°, судно ({px:F0}; {py:F0}): угол окна вне слоя");
                        Assert.LessOrEqual(Math.Abs(qy), half + 1e-9);
                    }
                }

        // Точка прямо по носу (курс ψ по часовой от N) на экране — прямо вверх от судна
        foreach (double psi in new[] { 0.0, 37.0, 90.0, 200.0, 315.0 })
        {
            double a = psi * Math.PI / 180.0;
            PlotterMath.Rotate(Math.Sin(a) * 100, Math.Cos(a) * 100, psi, out double ux, out double uy);
            Assert.AreEqual(0, ux, 1e-9, $"ψ {psi}°");
            Assert.AreEqual(100, uy, 1e-9);
        }

        PlotterLayout n = PlotterMath.Layout(W, H, false);
        Assert.IsFalse(n.HeadUp);
        Assert.AreEqual(W, n.ContentSide, 1e-9, "север вверх: слой = окно");
        Assert.AreEqual(PlotterMath.NorthUpFollowBox, n.FollowBox, 1e-9);
    }

    [Test]
    public void Viewport_FitIntoWindowSmallerThanLayer_ScaleKeepsCenter()
    {
        var v = new MapViewport { WidthPx = 886, HeightPx = 886, MetersPerPixel = 5, Center = new WorldPosition(0, 0) };
        var r = new MapRect(-500, -3000, 3100, 5000);
        v.Fit(r, 0.05, 400, 400);
        Assert.IsFalse(v.Follow);
        double halfWin = 200 * v.MetersPerPixel;
        Assert.LessOrEqual(r.Height * 1.1, 2 * halfWin + 1e-6, "маршрут — в видимом окне 400 × 400, а не во всём слое");
        Assert.AreEqual(r.Center.East, v.Center.East, 1e-9);

        int ver = v.Version;
        WorldPosition c = v.Center;
        v.SetMetersPerPixel(18.52);
        Assert.AreEqual(18.52, v.MetersPerPixel, 1e-12);
        Assert.AreEqual(c.East, v.Center.East, 0.0, "шкала меняется без сдвига центра");
        Assert.Greater(v.Version, ver, "слой перестраивается");
        ver = v.Version;
        v.SetMetersPerPixel(18.52);
        Assert.AreEqual(ver, v.Version, "та же шкала — без перестройки");
    }

    [Test]
    public void RevealEvent_AppendedAtEnd_TextAndReadBack()
    {
        Assert.Greater((int)ExerciseEventType.PlotterRevealTargets, (int)ExerciseEventType.SteeringRestored, "новый тип — в конец перечня");
        string on = new ExerciseEvent(ExerciseEventType.PlotterRevealTargets) { Value = 1, ByInstructor = true }.Describe();
        string off = new ExerciseEvent(ExerciseEventType.PlotterRevealTargets) { Value = 0, ByInstructor = true }.Describe();
        StringAssert.Contains("ВСЕ цели", on);
        StringAssert.Contains("только обнаруженные", off);
        Assert.IsTrue(Enum.TryParse(ExerciseEventType.PlotterRevealTargets.ToString(), out ExerciseEventType t) && t == ExerciseEventType.PlotterRevealTargets,
                      "events.csv хранит тип по имени — читается обратно");
    }
}
