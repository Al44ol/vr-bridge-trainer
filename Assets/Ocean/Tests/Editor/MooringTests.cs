// ============================================================
// MooringTests.cs — Причал, кранцы, концы, подруливающее (запрос 19.2, Core)
//
// Кривая кранца и энергия; подбор кранца по энергии; оси причала; корпус и сила в точке
// (знаки момента); навал лагом — энергия ≈ ½·m₂₂·v², восстановление, перегрузка кранца;
// конец — провис, разрыв, характеристика лебёдки, травление тормоза; удержание на концах;
// подруливающее — знак, падение на ходу, Номото не реагирует; подача конца — дистанция.
// ============================================================
using System;
using NUnit.Framework;

public class MooringTests
{
    private const double Kn = 0.514444;

    private static ShipParameters Model45m() => LowSpeedModelTests.Model45m();

    // ─────────── Кранец ───────────
    [Test]
    public void Fender_CurveEnergyAndHysteresis()
    {
        Assert.AreEqual(0.448, FenderCurve.EnergyFraction(1.0), 1e-9, "E_н = 0,448·R_н·δ_н");
        double prev = -1;
        for (double x = 0; x <= 1.5; x += 0.05)
        {
            double f = FenderCurve.ReactionFraction(x);
            Assert.Greater(f, prev, $"реакция растёт с обжатием (x = {x:F2})");
            prev = f;
        }
        Assert.AreEqual(1.0, FenderCurve.ReactionFraction(1.0), 1e-12);
        Assert.AreEqual(1.0, FenderCurve.HysteresisFactor(0.1), "нагрузка — без потерь");
        Assert.AreEqual(1.0 - FenderCurve.Hysteresis, FenderCurve.HysteresisFactor(-0.1), 1e-12, "разгрузка — ниже на h");

        FenderSpec s = FenderSpec.ForEnergy(100e3, 2.0);
        Assert.AreEqual(100e3, s.RatedEnergy, 1.0, "подбор по энергии");
        Assert.AreEqual(0.5 * s.OuterDiameter, s.RatedDeflection, 1e-9);
        Assert.AreEqual(33.6e3, FenderSpec.Reference.RatedEnergy, 1.0);
    }

    // ─────────── Геометрия ───────────
    [Test]
    public void Berth_AxesRoundTripAndPositions()
    {
        var b = new BerthArea { a = new WorldPosition(100, 200), b = new WorldPosition(100, 300) };   // на север
        Assert.AreEqual(1.0, b.NAxis.East, 1e-12, "вода — справа от A→B (на восток)");
        Assert.AreEqual(0.0, b.BearingDeg, 1e-9);
        b.ToLocal(new WorldPosition(105, 230), out double t, out double n);
        Assert.AreEqual(30.0, t, 1e-9);
        Assert.AreEqual(5.0, n, 1e-9);
        WorldPosition w = b.ToWorld(t, n);
        Assert.AreEqual(105.0, w.East, 1e-9);
        Assert.AreEqual(230.0, w.North, 1e-9);
        foreach (double f in b.FenderPositions()) Assert.IsTrue(f >= b.fenderEndMarginM - 1e-9 && f <= b.Length - b.fenderEndMarginM + 1e-9);
        Assert.AreEqual(13, b.FenderPositions().Count, "100 м, отступы 4 м, шаг не больше 8 м: 92/8 → 12 промежутков по 7,67 м");
        Assert.AreEqual(2, b.NearestBollard(23.0), "кнехты 3; 12,4; 21,8 … м (шаг ≤ 10 м)");
    }

    [Test]
    public void Hull_HalfBeamAndForceMomentSigns()
    {
        ShipParameters p = Model45m();
        Assert.AreEqual(0.5 * p.beam, MooringHull.HalfBeamAt(0.0, p.length, p.beam), 1e-6);
        Assert.AreEqual(0.0, MooringHull.HalfBeamAt(0.5 * p.length, p.length, p.beam), 1e-6);

        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, Psi = Math.PI / 2 };   // на восток
        // На правый борт (юг) в носу — нос вправо
        MooringHull.ApplyWorldForce(m, 0, -1000, MooringHull.ToWorld(0, 0, m.Psi, 20, 0));
        m.Step(0.02);
        Assert.Greater(m.LastExternalY, 0.0);
        Assert.Greater(m.LastExternalN, 0.0, "сила на ПБ в носу — момент нос вправо");
        // Вперёд (на восток) в точке правого борта — нос влево
        MooringHull.ApplyWorldForce(m, 1000, 0, MooringHull.ToWorld(m.East, m.North, m.Psi, 0, 4));
        m.Step(0.02);
        Assert.Greater(m.LastExternalX, 0.0);
        Assert.Less(m.LastExternalN, 0.0, "продольная сила у правого борта — нос влево");
        // Поперечная на миделе — без момента
        MooringHull.ApplyWorldForce(m, 0, 1000, new WorldPosition(m.East, m.North));
        m.Step(0.02);
        Assert.AreEqual(0.0, m.LastExternalN, 1e-6);
    }

    // ─────────── Навал ───────────
    [Test]
    public void Impact_Broadside_EnergyMatchesKinetic_Rebounds()
    {
        ShipParameters p = Model45m();
        BerthArea b = BerthArea.ForShip(p);
        BerthingTrials.ImpactRun r = BerthingTrials.Impact(p, b, 0.2, 0.0, true);
        // ≈ ½·m₂₂·v²; меньше — часть энергии уходит во вращение: кранцы под цилиндрической вставкой
        // (от кормы до 0,25·L) несимметричны относительно ЦТ — эксцентриситет, аналог C_E < 1 по PIANC
        Assert.That(r.EnergyShare, Is.InRange(0.8, 1.02), "поглощённая энергия ≈ ½·m₂₂·v²");
        Assert.That(r.Restitution, Is.InRange(0.7, 0.85), "гистерезис 0,35: e ≈ √0,65 ≈ 0,8");
        Assert.That(r.MaxRatio, Is.InRange(0.3, 0.7));
        Assert.IsFalse(r.QuayImpact || r.FenderOverload);
        Assert.Greater(r.Fenders, 1);
        Assert.Greater(r.Series.Count, 10);

        BerthingTrials.ImpactRun hard = BerthingTrials.Impact(p, b, 0.5, 0.0, false);
        Assert.IsTrue(hard.FenderOverload, "0,5 уз — кранцы сверх номинала (подобраны на 2 × энергию 0,15 м/с при C_E 0,5)");
        Assert.Greater(hard.Energy, 5.0 * r.Energy, "энергия ∝ v²");
    }

    // ─────────── Конец ───────────
    [Test]
    public void Line_SlackBreakWinchBrake()
    {
        double mbl = 270e3;
        Assert.AreEqual(0.0, MooringLine.ElasticTension(mbl, 10.0, 9.0, out double k0));
        Assert.AreEqual(0.0, k0);
        Assert.AreEqual(mbl, MooringLine.ElasticTension(mbl, 10.0, 10.0 * (1 + MooringLine.BreakStrain), out _), 1e-6, "MBL при ε_р");
        Assert.AreEqual(MooringLine.WinchSpeed, MooringLine.HaulSpeed(0.0, mbl), 1e-12);
        Assert.AreEqual(0.0, MooringLine.HaulSpeed(MooringLine.WinchPullFraction * mbl, mbl), 1e-12, "на тяге барабана — стоит");

        var line = new MooringLine(MooringLineKind.HeadLine, 20, 15, mbl, 60);
        line.MakeFast(0, -1, 10.0, 0.0);
        // Тормоз: натяжение > 0,6·MBL — конец травится
        double chord = 10.0 * (1 + 0.15);
        line.Update(chord, 0.02, 1e6);
        line.Update(chord, 0.02, 1e6);
        Assert.Greater(line.Tension, MooringLine.BrakeFraction * mbl);
        Assert.Greater(line.RestLength, 10.0, "тормоз травит");
        // На кнехтах — не травится, рвётся
        line.MakeFast(0, -1, 10.0, 0.0);
        line.OnBitts = true;
        line.Update(10.0, 0.02, 1e6);
        MooringLineEvent ev = line.Update(10.0 * 1.25, 0.02, 1e6);
        Assert.AreEqual(MooringLineEvent.Broken, ev);
        Assert.AreEqual(MooringLineState.Broken, line.State);
        Assert.AreEqual(10.0, line.RestLength, 1e-12);
    }

    [Test]
    public void Hold_OffBerth_BreastFirst_BittsBreak_BrakeRenders()
    {
        ShipParameters p = Model45m();
        BerthArea b = BerthArea.ForShip(p);
        BerthingTrials.HoldRun bitts = BerthingTrials.Hold(p, b, true, false);
        StringAssert.Contains("прижимной", bitts.LineOverload, "от причала первыми нагружаются прижимные");
        Assert.AreEqual("обрыв", bitts.LimitKind);
        Assert.Greater(bitts.ForceLimit, bitts.ForceOverload);

        BerthingTrials.HoldRun brake = BerthingTrials.Hold(p, b, false, false);
        StringAssert.StartsWith("травление", brake.LimitKind, "на тормозах — травятся, не рвутся");
        double mbl = MooringLine.MblFor(p);
        foreach (var s in brake.Series)
            foreach (double t in s.tension) Assert.Less(t, 0.75 * mbl, "тормоз держит ≈ 0,6·MBL");
    }

    [Test]
    public void GiveLine_TooFar_Refused_Near_Fast()
    {
        ShipParameters p = Model45m();
        BerthArea b = BerthArea.ForShip(p);
        ShipDynamicsModel far = BerthingTrials.Place(p, b, 100.0);
        var sys = new MooringSystem(b, p);
        Assert.IsFalse(sys.TryGiveLine(0, far, 0, out string why));
        StringAssert.Contains("больше", why);

        ShipDynamicsModel near = BerthingTrials.Place(p, b, 0.5);
        Assert.IsTrue(sys.TryGiveLine(1, near, 0, out _));
        Assert.AreEqual(MooringLineState.Fast, sys.Lines[1].State);
        Assert.AreEqual(-1, sys.Lines[1].Side, "судно на восток, причал к северу — левым бортом");
        Assert.AreEqual(MooringEventType.LineMadeFast, sys.Events[0].Type);
    }

    // ─────────── Подруливающее ───────────
    [Test]
    public void Thruster_SignSpeedLossNomotoIgnores()
    {
        ShipParameters p = Model45m();
        Assert.AreEqual(0.5, BowThruster.SpeedFactor(BowThruster.HalfEffectSpeed), 1e-12);
        BerthingTrials.ThrusterRun rest = BerthingTrials.Thruster(p, 0.0), moving = BerthingTrials.Thruster(p, 4.0);
        Assert.Greater(rest.DeltaPsi, 0.0, "упор на ПБ — нос вправо");
        Assert.Greater(rest.DeltaPsi, 5.0 * moving.DeltaPsi, "на 4 уз эффект падает");

        var nomoto = new ShipDynamicsModel(p);
        BowThruster th = BowThruster.For(p);
        th.Command = 1.0;
        for (int i = 0; i < 500; i++) { th.Apply(nomoto, 0.02); nomoto.Step(0.02); }
        Assert.AreEqual(0.0, nomoto.R, "Номото внешние силы не учитывает");
    }
}
