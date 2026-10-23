// ============================================================
// ConningGaugeTests.cs — Приборы мониторов пульта (запрос 16а, Core): шкала стрелочного прибора (угол, упор,
// деления, точка на окружности), картушка (курс под курсовой чертой), обороты ГД по телеграфу с инерцией, подписи.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

public class ConningGaugeTests
{
    [Test]
    public void Scale_AngleClampTicksPolar()
    {
        // Руль ±35° на дуге −60…+60° (0 — вверх): 0° руля — стрелка вверх, 35° ПБ — +60°
        var rudder = new GaugeScale(-35, 35, -60, 60);
        Assert.AreEqual(0.0, rudder.AngleOf(0), 1e-9);
        Assert.AreEqual(60.0, rudder.AngleOf(35), 1e-9);
        Assert.AreEqual(-30.0, rudder.AngleOf(-17.5), 1e-9);
        Assert.AreEqual(60.0, rudder.AngleOf(50), 1e-9, "за шкалой — упор");
        CollectionAssert.AreEqual(new[] { -30.0, -20, -10, 0, 10, 20, 30 }, rudder.Ticks(10));
        Assert.AreEqual(15, rudder.Ticks(5).Count);

        (double x, double y) top = GaugeScale.Polar(0, 100), right = GaugeScale.Polar(90, 100);
        Assert.AreEqual(0.0, top.x, 1e-9);
        Assert.AreEqual(100.0, top.y, 1e-9);
        Assert.AreEqual(100.0, right.x, 1e-9, "по часовой: 90° — вправо");
        Assert.AreEqual(0.0, right.y, 1e-9);
    }

    [Test]
    public void Compass_HeadingUnderLubberLine()
    {
        Assert.AreEqual(0.0, CompassCard.ScreenAngle(45, 45), 1e-9, "курс — вверху");
        Assert.AreEqual(315.0, CompassCard.ScreenAngle(0, 45), 1e-9, "N — слева от курса 045°");
        Assert.AreEqual(90.0, CompassCard.ScreenAngle(135, 45), 1e-9);
        Assert.AreEqual(-45.0, CompassCard.CardRotation(45), 1e-9);
        Assert.AreEqual(-350.0, CompassCard.CardRotation(-10), 1e-9);
    }

    [Test]
    public void EngineRpm_TelegraphLagFailure()
    {
        var e = new EngineRpm { RatedRpm = 750, TimeConstantS = 4 };
        Assert.AreEqual(487.5, e.Target(0.65, false), 1e-9, "«Средний вперёд» — 0,65 · n_ном");
        Assert.AreEqual(-750.0, e.Target(-1, false), 1e-9);
        Assert.AreEqual(0.0, e.Target(1, true), 1e-9, "отказ ГД — 0");
        double rpm = 0;
        for (int i = 0; i < 200; i++) rpm = e.Step(1, false, 0.02);   // 4 с = T
        Assert.AreEqual(750 * (1 - Math.Exp(-1)), rpm, 1.0, "через T — 63 %");
        for (int i = 0; i < 2000; i++) e.Step(1, false, 0.02);
        Assert.AreEqual(750.0, e.Rpm, 1e-9);
        for (int i = 0; i < 2000; i++) e.Step(1, true, 0.02);
        Assert.AreEqual(0.0, e.Rpm, 1e-9, "при отказе обороты падают до нуля");
    }

    // Корпус монитора W × H × T, наклонён назад на tiltDeg вокруг оси X (как запечённый поворот из Blender), центр — (0; 1; 0)
    private static void Box(double w, double h, double t, double tiltDeg, out List<V3> v, out List<int> tri)
    {
        double a = tiltDeg * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
        v = new List<V3>();
        tri = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            double x = (i & 1) == 0 ? -w / 2 : w / 2, y = (i & 2) == 0 ? -h / 2 : h / 2, z = (i & 4) == 0 ? -t / 2 : t / 2;
            v.Add(new V3(x, 1.0 + y * ca - z * sa, y * sa + z * ca));   // поворот вокруг X
        }
        int[][] quads = { new[] { 4, 5, 7, 6 }, new[] { 0, 2, 3, 1 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        foreach (int[] q in quads) { tri.AddRange(new[] { q[0], q[1], q[2], q[0], q[2], q[3] }); }
    }

    [Test]
    public void ScreenFace_TiltedAndLyingMonitor()
    {
        // Монитор 0,5 × 0,3 × 0,05 м, наклонён на 20°, игрок спереди (+Z) и выше
        Box(0.5, 0.3, 0.05, 20, out List<V3> v, out List<int> tri);
        ScreenFaceResult f = ScreenFace.Find(v, tri, new V3(0, 1.6, 1.5), new V3(0, 1, 0));
        Assert.IsNotNull(f);
        Assert.AreEqual(0.5, f.Width, 1e-6);
        Assert.AreEqual(0.3, f.Height, 1e-6);
        double a = 20 * Math.PI / 180;
        Assert.AreEqual(-Math.Sin(a), f.Normal.Y, 1e-6, "нормаль лицевой грани — с наклоном (не по осям судна)");
        Assert.AreEqual(Math.Cos(a), f.Normal.Z, 1e-6);
        Assert.AreEqual(Math.Cos(a), f.Up.Y, 1e-6, "«верх» — по экрану");
        Assert.AreEqual(0.025 * Math.Cos(a), f.Center.Z, 1e-6, "центр — на лицевой грани");

        // Лежащая панель 1,1 × 0,9 × 0,04 (наклон 90°): «верх» — от игрока
        Box(1.1, 0.9, 0.04, 90, out v, out tri);
        f = ScreenFace.Find(v, tri, new V3(0, 1.6, 1.5), new V3(0, 1, 0));
        Assert.AreEqual(1.0, f.Normal.Y, 1e-6, "лежит лицом вверх");
        Assert.AreEqual(-1.0, f.Up.Z, 1e-6, "«верх» — от игрока");
        Assert.AreEqual(1.1, f.Width, 1e-6);
        Assert.AreEqual(0.9, f.Height, 1e-6);
    }

    [Test]
    public void Text_HeadingRudderRpmTelegraph()
    {
        Assert.AreEqual("034,9", ConningText.Heading(34.94));
        Assert.AreEqual("000,0", ConningText.Heading(359.97));
        Assert.AreEqual("350,0", ConningText.Heading(-10));
        Assert.AreEqual("12° ПБ", ConningText.Rudder(12.3));
        Assert.AreEqual("5° ЛБ", ConningText.Rudder(-5));
        Assert.AreEqual("0°", ConningText.Rudder(0.3));
        Assert.AreEqual("+540", ConningText.Rpm(540.2));
        Assert.AreEqual("−300", ConningText.Rpm(-300));
        Assert.AreEqual("0", ConningText.Rpm(0.4));
        Assert.AreEqual("СРЕДНИЙ ВПЕРЁД", ConningText.Telegraph("Half Ahead"));
        Assert.AreEqual("САМЫЙ МАЛЫЙ НАЗАД", ConningText.Telegraph("Dead Slow Astern"));
        Assert.AreEqual("СТОП", ConningText.Telegraph("Stop"));
    }
}
