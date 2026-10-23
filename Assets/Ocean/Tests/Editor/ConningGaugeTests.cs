// ============================================================
// ConningGaugeTests.cs — Приборы мониторов пульта (запрос 16а, Core): шкала стрелочного прибора (угол, упор,
// деления, точка на окружности), картушка (курс под курсовой чертой), обороты ГД по телеграфу с инерцией, подписи.
// ============================================================
using System;
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
