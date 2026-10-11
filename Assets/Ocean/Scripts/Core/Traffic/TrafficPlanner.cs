// ============================================================
// TrafficPlanner.cs — Начальное состояние и путь цели по описанию сценария (B3)
//
// По TrafficShipDefinition и начальным условиям своего судна:
//   • позиция: абсолютная; по пеленгу и дистанции от начальной позиции своего
//     судна; или «точка встречи» (CpaCalculator.MeetingStart: своё судно идёт
//     начальным курсом и начальной скоростью);
//   • курс: при движении по маршруту — на первую точку, иначе — заданный;
//   • путь (точки после старта): маршрут; в режиме «точка встречи» — сначала
//     точка, в которой цель окажется через TCPA + HoldAfterTcpa на заданном
//     курсе (до неё курс не меняется, поэтому CPA не искажается), затем маршрут.
// Тот же расчёт используют TrafficController (сцена), инспектор и гизмо
// сценария, session.json (C1) и тесты.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Начальное состояние и путь цели.</summary>
public struct TrafficStart
{
    public WorldPosition Position;
    public double HeadingDeg;
    public double SpeedKnots;
    /// <summary>Точки пути после старта (пусто — постоянный курс).</summary>
    public List<WorldPosition> Path;
    /// <summary>Режим «точка встречи»: позиция цели в момент TCPA.</summary>
    public bool HasMeetingPoint;
    public WorldPosition MeetingPoint;
    /// <summary>Сторона встречи определена неоднозначно (см. CpaCalculator.MeetingStart).</summary>
    public bool SideAmbiguous;
    /// <summary>Проблема расчёта (пусто — нет).</summary>
    public string Problem;
}

public static class TrafficPlanner
{
    /// <summary>Сколько цель держит курс после TCPA, прежде чем идти по маршруту, с.</summary>
    public const double HoldAfterTcpaSeconds = 120.0;

    public static TrafficStart Resolve(TrafficShipDefinition d, OwnShipInitialState own)
    {
        var r = new TrafficStart
        {
            Position = d.position,
            HeadingDeg = Wrap360(d.headingDeg),
            SpeedKnots = Math.Max(0.0, d.speedKnots),
            Path = new List<WorldPosition>(),
            Problem = ""
        };
        WorldPosition ownPos = own != null ? own.position : default;
        double ownCourse = own != null ? own.headingDeg : 0.0;
        double ownSpeed = own != null ? own.speedKnots : 0.0;
        bool hasRoute = d.motion == TrafficMotion.Route && d.route != null && d.route.Count > 0;

        switch (d.placement)
        {
            case TrafficPlacement.RelativeToOwnShip:
            {
                double b = d.bearingDeg * Math.PI / 180.0, dist = d.distanceNm * CpaCalculator.MetersPerNm;
                r.Position = new WorldPosition(ownPos.East + dist * Math.Sin(b), ownPos.North + dist * Math.Cos(b));
                break;
            }
            case TrafficPlacement.MeetingPoint:
            {
                double cpa = Math.Max(0.0, d.cpaNm) * CpaCalculator.MetersPerNm, tcpa = d.tcpaMinutes * 60.0;
                if (!CpaCalculator.MeetingStart(ownPos, ownCourse, ownSpeed, r.HeadingDeg, r.SpeedKnots,
                                                cpa, tcpa, d.meetingSide, out WorldPosition start, out bool ambiguous))
                {
                    r.Problem = "нет относительного движения (курс и скорость цели совпадают со своим судном) — точку встречи не построить";
                    r.Position = ownPos;
                }
                else r.Position = start;
                r.SideAmbiguous = ambiguous;
                CpaCalculator.Velocity(r.HeadingDeg, r.SpeedKnots, out double ve, out double vn);
                r.HasMeetingPoint = true;
                r.MeetingPoint = new WorldPosition(r.Position.East + ve * tcpa, r.Position.North + vn * tcpa);
                if (hasRoute)
                {
                    double t = tcpa + HoldAfterTcpaSeconds;
                    r.Path.Add(new WorldPosition(r.Position.East + ve * t, r.Position.North + vn * t));
                    r.Path.AddRange(d.route);
                }
                return r;
            }
        }

        if (hasRoute)
        {
            r.Path.AddRange(d.route);
            WorldPosition first = d.route[0];
            if ((first - r.Position).Magnitude > 1.0)
                r.HeadingDeg = CpaCalculator.Bearing(first.East - r.Position.East, first.North - r.Position.North);
        }
        return r;
    }

    public static double Wrap360(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }
}
