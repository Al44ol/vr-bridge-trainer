// ============================================================
// FogSignals.cs — Туманные сигналы других судов (C4)
//
// Правило 35: в районе ограниченной видимости или вблизи него судно с механическим
// двигателем на ходу подаёт
//     имеющее ход   — один продолжительный звук не реже чем через 2 мин (35 а);
//     не имеющее хода — два продолжительных с паузой около 2 с, не реже чем через 2 мин (35 б).
// Модель: интервал IntervalS = 110 с (≤ 120 с), первый сигнал цели i — через
// FirstS + i·StaggerS после начала ограниченной видимости (несинхронно); «имеет ход» —
// скорость ≥ MakingWayKnots (0,5 уз, предварительно). Продолжительный звук — 5 с.
// Слышимость (Приложение III, п. 1 в) по длине судна L:
//     L ≥ 200 м — 2 мили; 75 ≤ L < 200 — 1,5; 20 ≤ L < 75 — 1; L < 20 — 0,5 мили.
// Основная частота (Приложение III, п. 1 а) — середина диапазона по логарифмической шкале:
//     L ≥ 200 м — 70…200 Гц (118 Гц); 75 ≤ L < 200 — 130…350 Гц (213 Гц); L < 75 — 250…700 Гц (418 Гц).
// Распространение: звук приходит с задержкой d/c, c = 340 м/с (на 1,5 мили — около 8 с).
// Сигнал «слышен», если дистанция в момент подачи не больше дальности слышимости; событие
// TargetSoundSignal публикуется в момент прихода звука к своему судну.
// Громкость в VR (восприятие, не физика: падение 1/r от 1 м на милю дало бы −65 дБ):
//     L_дБ = −30·lg(d/d₀)/lg(D_сл/d₀), d₀ = 100 м (0 дБ ближе); дальше D_сл — тишина;
// высокие частоты ослабевают с дистанцией (атмосферное поглощение — приближённо, фильтр 5000 → 1500 Гц).
// Ограничения: эхо, ветер и рефракция звука не моделируются; направление в Unity без HRTF —
// только панорама «лево — право» (спереди/сзади — поворотом головы).
// ============================================================
using System;
using System.Collections.Generic;

public struct FogSignalSource
{
    public string Id;
    public int Index;
    public WorldPosition Position;
    public double SpeedKnots;
    public double LengthM;
}

public struct HeardSignal
{
    public string Id;
    public int Index;
    public SoundSignalKind Kind;
    public double EmitTime, ArrivalTime;
    /// <summary>Дистанция в момент подачи, м.</summary>
    public double DistanceM;
    /// <summary>Курсовой угол цели со своего судна в момент прихода звука, ° (0…360).</summary>
    public double QOwnDeg;
    public double LengthM;
}

public static class FogSignals
{
    public const double SoundSpeedMs = 340.0;
    public const double ProlongedS = 5.0, PauseS = 2.0;
    public const double ReferenceM = 100.0, RangeLossDb = 30.0;

    /// <summary>Дальность слышимости свистка, мили (Приложение III, п. 1 в).</summary>
    public static double AudibleRangeNm(double lengthM) =>
        lengthM >= 200.0 ? 2.0 : lengthM >= 75.0 ? 1.5 : lengthM >= 20.0 ? 1.0 : 0.5;

    /// <summary>Основная частота свистка, Гц (Приложение III, п. 1 а).</summary>
    public static double FrequencyHz(double lengthM) =>
        lengthM >= 200.0 ? Math.Sqrt(70.0 * 200.0) : lengthM >= 75.0 ? Math.Sqrt(130.0 * 350.0) : Math.Sqrt(250.0 * 700.0);

    /// <summary>Сигнал судна на ходу по скорости (правило 35 а, б).</summary>
    public static SoundSignalKind KindFor(double speedKnots, double makingWayKnots) =>
        Math.Abs(speedKnots) >= makingWayKnots ? SoundSignalKind.FogMakingWay : SoundSignalKind.FogNotMakingWay;

    /// <summary>Длительность сигнала, с: Д — 5 с, ДД — 5 + 2 + 5 с.</summary>
    public static double DurationS(SoundSignalKind k) => k == SoundSignalKind.FogNotMakingWay ? 2.0 * ProlongedS + PauseS : ProlongedS;

    /// <summary>Громкость 0…1 (линейная) на дистанции d при дальности слышимости rangeM.</summary>
    public static double Loudness(double distanceM, double rangeM)
    {
        if (distanceM > rangeM) return 0.0;
        if (distanceM <= ReferenceM) return 1.0;
        double db = -RangeLossDb * Math.Log10(distanceM / ReferenceM) / Math.Log10(Math.Max(rangeM, ReferenceM * 1.01) / ReferenceM);
        return Math.Pow(10.0, db / 20.0);
    }

    /// <summary>Частота среза фильтра нижних частот, Гц (5000 Гц вблизи → 1500 Гц на границе слышимости).</summary>
    public static double LowPassHz(double distanceM, double rangeM)
    {
        double k = Math.Max(0.0, Math.Min(1.0, (distanceM - ReferenceM) / Math.Max(1.0, rangeM - ReferenceM)));
        return 5000.0 * Math.Pow(1500.0 / 5000.0, k);
    }

    /// <summary>Ограниченная видимость (правила 19, 35): V меньше порога, мили.</summary>
    public static bool Restricted(double visibilityM, double thresholdNm) =>
        visibilityM < thresholdNm * NavigationLights.MetersPerNm;
}

/// <summary>Подача туманных сигналов целями и их приход к своему судну (сцена и ExerciseSimulation).</summary>
public sealed class FogSignalSystem
{
    public double IntervalS = 110.0, FirstS = 20.0, StaggerS = 35.0;
    public double MakingWayKnots = 0.5;

    private struct Pending
    {
        public string Id;
        public int Index;
        public SoundSignalKind Kind;
        public double EmitTime, ArrivalTime, DistanceM, RangeM, LengthM;
    }

    private readonly Dictionary<string, double> next = new Dictionary<string, double>();
    private readonly List<Pending> pending = new List<Pending>();
    private double restrictedSince = double.NaN;

    public void Reset()
    {
        next.Clear();
        pending.Clear();
        restrictedSince = double.NaN;
    }

    /// <summary>
    /// Шаг: t — время, restricted — ограниченная видимость сейчас; own — своё судно;
    /// heard — сигналы, звук которых дошёл до своего судна на этом шаге (и был слышен).
    /// </summary>
    public void Update(double t, bool restricted, WorldPosition own, double ownHeadingDeg,
                       IReadOnlyList<FogSignalSource> sources, List<HeardSignal> heard)
    {
        heard.Clear();
        if (restricted)
        {
            if (double.IsNaN(restrictedSince)) restrictedSince = t;
            foreach (FogSignalSource s in sources)
            {
                if (!next.TryGetValue(s.Id, out double due)) next[s.Id] = due = restrictedSince + FirstS + StaggerS * Math.Max(0, s.Index);
                if (t + 1e-9 < due) continue;
                double d = (s.Position - own).Magnitude;
                pending.Add(new Pending
                {
                    Id = s.Id, Index = s.Index, Kind = FogSignals.KindFor(s.SpeedKnots, MakingWayKnots),
                    EmitTime = t, ArrivalTime = t + d / FogSignals.SoundSpeedMs, DistanceM = d,
                    RangeM = FogSignals.AudibleRangeNm(s.LengthM) * NavigationLights.MetersPerNm, LengthM = s.LengthM
                });
                while (due <= t + 1e-9) due += IntervalS;
                next[s.Id] = due;
            }
        }
        else
        {
            restrictedSince = double.NaN;
            next.Clear();
        }

        for (int i = 0; i < pending.Count; i++)
        {
            Pending p = pending[i];
            if (p.ArrivalTime > t + 1e-9) continue;
            pending.RemoveAt(i--);
            if (p.DistanceM > p.RangeM) continue;   // не слышен
            WorldPosition pos = own;
            foreach (FogSignalSource s in sources) if (s.Id == p.Id) { pos = s.Position; break; }
            double brg = CpaCalculator.Bearing(pos.East - own.East, pos.North - own.North);
            heard.Add(new HeardSignal
            {
                Id = p.Id, Index = p.Index, Kind = p.Kind, EmitTime = p.EmitTime, ArrivalTime = t,
                DistanceM = p.DistanceM, QOwnDeg = CpaCalculator.RelativeBearing(brg, ownHeadingDeg), LengthM = p.LengthM
            });
        }
    }
}
