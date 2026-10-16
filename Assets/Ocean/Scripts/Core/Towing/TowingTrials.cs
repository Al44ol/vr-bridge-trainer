// ============================================================
// TowingTrials.cs — Стенд буксировки и ветра без Play Mode (запрос 19.3а)
//
// Своё судно (обычно Model45m) — модель 3 СС, машина «Стоп», руль 0; буксир — ShipPresets.FillTug,
// TugSpec (тяга на гаке 20 т); dt = 0,02 с. Испытания:
//   1. Тяга на гаке: трос 30 м на неподвижную точку, «тянуть» 50 и 100 % — установившееся натяжение
//      против заданной тяги (проверка автоматики и разложения на колонки).
//   2. Свободный ход буксира: полный упор вперёд — установившийся ход (τ = 13 с, ≈ 12 уз).
//   3. Буксир тянет судно лагом: трос 30 м от кормы (x = −0,45·L) или носа (+0,45·L), сила на ЛБ (270°),
//      50 и 100 % — за 180 с: поперечная скорость судна, угловая скорость, изменение курса, натяжение.
//   4. Буксир толкает в борт: у миделя и у кормы (x = −0,3·L), на ЛБ, 50 и 100 %.
//   5. Ветер: коэффициенты Isherwood C_X, C_Y, C_N судна по углу 0…180°; дрейф судна без хода
//      при ветре с ПБ (траверз) 10/15/20/25 уз — установившаяся скорость дрейфа и разворот за 300 с.
//   6. Буксир против ветра: ветер с ЛБ 20 уз (отжимной, если причал по ЛБ), буксир толкает у миделя на ЛБ
//      0/10/25/50 % — поперечная скорость судна через 180 с: какой доли тяги хватает, чтобы удержать.
// ============================================================
using System;
using System.Collections.Generic;

public static class TowingTrials
{
    public const double Dt = 0.02, LineLength = 30.0;
    private const double Kn = ShipParameters.KnotsToMs, Rad2Deg = 180.0 / Math.PI;

    /// <summary>Строка ряда: t, с; v судна лагом, уз (> 0 — на ПБ); r, °/с; Δψ, °; натяжение / упор, кН; сила кранца, кН.</summary>
    public struct Row
    {
        public double T, SwayKn, RateDegS, DeltaPsiDeg, TensionKn, PushKn, SurgeKn;
    }

    public sealed class Run
    {
        public string Key = "", Title = "";
        public double Power, DirectionDeg, WindKn, PointX;
        public TugMode Mode;
        /// <summary>Итог: поперечная скорость, уз; угловая скорость, °/с; изменение курса, °; натяжение / сила кранца, кН (среднее за последние 20 с).</summary>
        public double SwayKn, RateDegS, DeltaPsiDeg, ForceKn, MaxForceKn, SurgeKn;
        public readonly List<Row> Series = new List<Row>();
    }

    public sealed class Report
    {
        public ShipParameters Ship, TugParams;
        public TugSpec Spec;
        public ShipWindArea ShipWind, TugWind;
        public double BollardPullKn, TugFreeSpeedKn;
        public readonly List<Run> Bollard = new List<Run>();
        public readonly List<Run> Lateral = new List<Run>();
        public readonly List<Run> Wind = new List<Run>();
        public readonly List<Run> Hold = new List<Run>();
        /// <summary>Коэффициенты Isherwood судна: γ, °; C_X, C_Y, C_N.</summary>
        public readonly List<(double gamma, double cx, double cy, double cn)> Coefficients = new List<(double, double, double, double)>();
    }

    public static ShipParameters TugParameters()
    {
        var p = UnityEngine.ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Tug";
        ShipPresets.FillTug(p);
        return p;
    }

    public static Report RunAll(ShipParameters ship)
    {
        ShipParameters tug = TugParameters();
        var r = new Report { Ship = ship, TugParams = tug, Spec = TugSpec.For(tug), ShipWind = ShipWindArea.Estimate(ship), TugWind = ShipWindArea.Estimate(tug) };
        r.BollardPullKn = r.Spec.BollardPull / 1e3;
        foreach (double p in new[] { 0.5, 1.0 }) r.Bollard.Add(BollardPull(ship, tug, p));
        r.TugFreeSpeedKn = FreeRun(tug);
        foreach (double x in new[] { -0.45, 0.45 })
            foreach (double p in new[] { 0.5, 1.0 })
                r.Lateral.Add(Lateral(ship, tug, TugMode.Pull, x, p, 0.0));
        foreach (double x in new[] { 0.0, -0.3 })
            foreach (double p in new[] { 0.5, 1.0 })
                r.Lateral.Add(Lateral(ship, tug, TugMode.Push, x, p, 0.0));
        for (int g = 0; g <= 180; g += 10)
        {
            WindLoad.Coefficients(r.ShipWind, g, out double cx, out double cy, out double cn);
            r.Coefficients.Add((g, cx, cy, cn));
        }
        foreach (double w in new[] { 10.0, 15.0, 20.0, 25.0 }) r.Wind.Add(WindDrift(ship, w));
        foreach (double p in new[] { 0.0, 0.1, 0.25, 0.5 }) r.Hold.Add(Lateral(ship, tug, TugMode.Push, 0.0, p, 20.0));
        return r;
    }

    // 1. Тяга на гаке: трос на неподвижной точке (0, 0), буксир — к северу, тянет на север
    public static Run BollardPull(ShipParameters ship, ShipParameters tug, double power)
    {
        var sys = new TowingSystem(null, tug) { Anchor = new WorldPosition(0, 0) };
        TugCommand c = TugCommand.Pull(0.0, power);
        sys.MakeFast(LineLength, 0);
        sys.PlaceTug(null, c, LineLength);
        var run = new Run { Key = $"bollard_{power * 100:0}", Title = $"Тяга на гаке, {power * 100:0} %", Power = power, Mode = TugMode.Pull };
        double sum = 0; int n = 0;
        for (int k = 0; k <= (int)(120 / Dt); k++)
        {
            double t = k * Dt;
            sys.Apply(null, Dt, t);
            if (k % 50 == 0) run.Series.Add(new Row { T = t, TensionKn = sys.Line.Tension / 1e3 });
            if (t >= 100) { sum += sys.Line.Tension; n++; }
            run.MaxForceKn = Math.Max(run.MaxForceKn, sys.Line.Tension / 1e3);
        }
        run.ForceKn = n > 0 ? sum / n / 1e3 : 0;
        return run;
    }

    // 2. Свободный ход буксира: полный упор вперёд, без автоматики
    public static double FreeRun(ShipParameters tug)
    {
        var tv = new TugVessel(tug, TugSpec.For(tug));
        tv.Model.Teleport(0, 0, 0, 0, 0);
        for (int k = 0; k < (int)(180 / Dt); k++)
        {
            tv.OrderX = tv.Spec.BollardPull; tv.OrderY = 0; tv.OrderN = 0;
            tv.ApplyThrust(Dt);
            tv.Model.Step(Dt);
        }
        return tv.Model.U / Kn;
    }

    // 3, 4, 6. Буксир тянет / толкает судно; windKn > 0 — ветер с ЛБ (траверз), буксир толкает на ЛБ
    public static Run Lateral(ShipParameters ship, ShipParameters tug, TugMode mode, double pointL, double power, double windKn, double seconds = 180)
    {
        var m = new ShipDynamicsModel(ship) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        m.Teleport(0, 0, 0, 0, 0);
        var sys = new TowingSystem(ship, tug);
        if (mode == TugMode.Pull) sys.TowPointX = pointL * ship.length; else sys.PushPointX = pointL * ship.length;
        if (windKn > 0) sys.Wind = Wind.Knots(windKn, 270.0);   // откуда — с запада (ЛБ при курсе 0°)
        TugCommand c = mode == TugMode.Pull ? TugCommand.Pull(270.0, power) : TugCommand.Push(270.0, power);
        if (mode == TugMode.Pull) sys.MakeFast(LineLength, 0);
        sys.PlaceTug(m, c, LineLength);
        string where = mode == TugMode.Pull ? (pointL < 0 ? "от кормы" : "от носа") : (Math.Abs(pointL) < 1e-6 ? "у миделя" : "у кормы");
        var run = new Run
        {
            Key = windKn > 0 ? $"hold_wind{windKn:0}_{power * 100:0}" : $"{(mode == TugMode.Pull ? "pull" : "push")}_{(pointL < 0 ? "aft" : pointL > 0 ? "fwd" : "mid")}_{power * 100:0}",
            Title = (windKn > 0 ? $"Ветер {windKn:0} уз с ЛБ, " : "") + $"{(mode == TugMode.Pull ? "тянет" : "толкает")} {where} на ЛБ, {power * 100:0} %",
            Mode = mode, Power = power, DirectionDeg = 270.0, WindKn = windKn, PointX = pointL
        };
        double psi0 = m.Psi, sv = 0, sr = 0, sf = 0, su = 0;
        int n = 0;
        for (int k = 0; k <= (int)(seconds / Dt); k++)
        {
            double t = k * Dt;
            sys.Apply(m, Dt, t);
            m.Step(Dt);
            double force = mode == TugMode.Pull ? sys.Line.Tension : sys.PushForce;
            run.MaxForceKn = Math.Max(run.MaxForceKn, force / 1e3);
            if (k % 50 == 0)
                run.Series.Add(new Row
                {
                    T = t, SwayKn = m.V / Kn, SurgeKn = m.U / Kn, RateDegS = m.R * Rad2Deg, DeltaPsiDeg = Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI) * Rad2Deg,
                    TensionKn = mode == TugMode.Pull ? force / 1e3 : 0, PushKn = mode == TugMode.Push ? force / 1e3 : 0
                });
            if (t >= seconds - 20) { sv += m.V; sr += m.R; sf += force; su += m.U; n++; }
        }
        run.SwayKn = sv / n / Kn; run.RateDegS = sr / n * Rad2Deg; run.ForceKn = sf / n / 1e3; run.SurgeKn = su / n / Kn;
        run.DeltaPsiDeg = Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI) * Rad2Deg;
        return run;
    }

    // 5. Дрейф без хода при ветре с ПБ (с востока при курсе 0°)
    public static Run WindDrift(ShipParameters ship, double windKn, double seconds = 300)
    {
        var m = new ShipDynamicsModel(ship) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        m.Teleport(0, 0, 0, 0, 0);
        ShipWindArea a = ShipWindArea.Estimate(ship);
        Wind w = Wind.Knots(windKn, 90.0);
        var run = new Run { Key = $"wind_{windKn:0}", Title = $"Дрейф: ветер {windKn:0} уз с ПБ", WindKn = windKn };
        double psi0 = m.Psi, sv = 0, sr = 0; int n = 0;
        for (int k = 0; k <= (int)(seconds / Dt); k++)
        {
            double t = k * Dt;
            WindForce f = WindLoad.Apply(m, a, w);
            m.Step(Dt);
            if (k % 50 == 0) run.Series.Add(new Row { T = t, SwayKn = m.V / Kn, SurgeKn = m.U / Kn, RateDegS = m.R * Rad2Deg, DeltaPsiDeg = Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI) * Rad2Deg, PushKn = f.Y / 1e3 });
            if (t >= seconds - 20) { sv += m.V; sr += m.R; n++; }
            if (k == 0) run.ForceKn = f.Y / 1e3;
        }
        run.SwayKn = sv / n / Kn; run.RateDegS = sr / n * Rad2Deg;
        run.DeltaPsiDeg = Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI) * Rad2Deg;
        return run;
    }
}
