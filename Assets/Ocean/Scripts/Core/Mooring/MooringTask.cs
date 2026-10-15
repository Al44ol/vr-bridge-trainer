// ============================================================
// MooringTask.cs — Задание сценария швартовки (запрос 19.2б-3, часть «а»)
//
// Сценарий швартовки (ScenarioDefinition.mooring) — четыре задания, которые обучающийся видит
// на брифинге и во вкладке «Задание» планшета:
//   1. Подойти лагом            — борт по всей длине не дальше d_лаг от кромки, угол к кромке ≤ α_лаг;
//   2. Остановиться у кранцев   — касание кранцев или зазор ≤ d_ост, ход ≤ U_ост;
//   3. Подать концы             — хотя бы один носовой и один кормовой конец на кнехтах причала;
//   4. Ошвартоваться            — концы поданы, судно у кранцев, ход ≤ U_шв непрерывно T_шв —
//                                 упражнение завершается (условие ScenarioEndConditions.Moored).
// Зазор — BerthReadout (19.2б-2): от борта к причалу до кромки по нормали у носа (x = +0,25·L)
// и у кормы (x = −0,45·L); у кранцев при касании он ≈ вылету кранца (0,37 м у причала №1).
// Ход — скорость относительно грунта (течения нет — она же относительно воды).
//
// Этапы 1–3 отмечаются независимо, как только выполнено их условие (концы можно подать раньше,
// чем судно остановилось); «ошвартован» отмечает и все предыдущие. Отметка не снимается.
// Каждая новая отметка — событие шины MooringStage (источник — ScenarioRunner), по нему же
// задание оценивается офлайн (events.csv). Значения по умолчанию — предварительные.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Этап швартовки (Index события MooringStage).</summary>
public enum MooringStage
{
    None,
    Alongside,   // подошёл лагом
    AtFenders,   // остановился у кранцев
    LinesFast,   // концы с носа и кормы поданы
    Moored       // ошвартован
}

/// <summary>Задание швартовки в сценарии (ScenarioDefinition.mooring).</summary>
[Serializable]
public class MooringTaskDefinition
{
    [Tooltip("Сценарий швартовки: задания «подойти лагом → остановиться у кранцев → подать концы → ошвартоваться» (19.2б-3)")]
    public bool enabled;
    [Tooltip("Причал — номер в WaterArea.berths (с 0)")]
    public int berthIndex;

    [Header("1. Подойти лагом")]
    [Tooltip("Зазор борта до кромки у носа и у кормы не больше, м")]
    public float alongsideGapM = 10f;
    [Tooltip("Угол диаметральной плоскости к кромке не больше, °")]
    public float alongsideAngleDeg = 10f;

    [Header("2. Остановиться у кранцев")]
    [Tooltip("Касание кранцев или зазор у носа и у кормы не больше, м")]
    public float stopGapM = 1f;
    [Tooltip("Ход относительно грунта не больше, уз")]
    public float stopSpeedKnots = 0.2f;

    [Header("4. Ошвартован (концы с носа и кормы, у кранцев)")]
    [Tooltip("Ход относительно грунта не больше, уз")]
    public float mooredSpeedKnots = 0.1f;
    [Tooltip("… непрерывно, с")]
    public float mooredHoldS = 30f;

    [Header("Оценка")]
    [Tooltip("Контрольное время швартовки, с (критерий времени — 19.2б-3, часть «в»); 0 — не оценивается")]
    public float targetTimeS = 600f;

    /// <summary>Причал задания в акватории (null — нет такого).</summary>
    public BerthArea BerthIn(WaterArea area) =>
        area != null && area.berths != null && berthIndex >= 0 && berthIndex < area.berths.Count ? area.berths[berthIndex] : null;

    public MooringTaskDefinition Clone() => (MooringTaskDefinition)MemberwiseClone();
}

/// <summary>Состояние для проверки этапов (сцена — ScenarioRunner, прогон — ExerciseSimulation).</summary>
public struct MooringTaskInput
{
    /// <summary>Зазоры, скорости к кромке, угол — до причала задания.</summary>
    public BerthReadout Readout;
    /// <summary>Корпус касается кранцев (MooringSystem.InContact).</summary>
    public bool FenderContact;
    /// <summary>Ход относительно грунта, уз.</summary>
    public double SpeedOverGroundKn;
    /// <summary>Подан хотя бы один носовой / кормовой конец (MooringTasks.LinesFast).</summary>
    public bool BowLineFast, SternLineFast;
}

/// <summary>Отслеживание этапов швартовки во время упражнения.</summary>
public sealed class MooringTaskTracker
{
    public const int StageCount = 4;

    public readonly MooringTaskDefinition Task;
    private readonly bool[] done = new bool[StageCount + 1];
    private readonly double[] doneAt = new double[StageCount + 1];
    private readonly List<MooringStage> fresh = new List<MooringStage>();

    /// <summary>Сколько условие «ошвартован» уже держится, с.</summary>
    public double HoldTime { get; private set; }
    public bool Moored => done[(int)MooringStage.Moored];
    public int DoneCount { get { int n = 0; for (int i = 1; i <= StageCount; i++) if (done[i]) n++; return n; } }

    public MooringTaskTracker(MooringTaskDefinition task)
    {
        Task = task ?? new MooringTaskDefinition();
        Reset();
    }

    public void Reset()
    {
        for (int i = 0; i <= StageCount; i++) { done[i] = false; doneAt[i] = double.NaN; }
        HoldTime = 0.0;
    }

    public bool Done(MooringStage s) => (int)s >= 1 && (int)s <= StageCount && done[(int)s];
    /// <summary>Время выполнения этапа, с упражнения (NaN — не выполнен).</summary>
    public double DoneAt(MooringStage s) => (int)s >= 1 && (int)s <= StageCount ? doneAt[(int)s] : double.NaN;

    /// <summary>Шаг: новые выполненные этапы — по порядку (пусто — ничего нового).</summary>
    public IReadOnlyList<MooringStage> Update(in MooringTaskInput x, double time, double dt)
    {
        fresh.Clear();
        bool lines = x.BowLineFast && x.SternLineFast;
        bool atFenders = MooringTasks.AtFenders(Task, x);
        bool mooredNow = lines && atFenders && x.SpeedOverGroundKn <= Task.mooredSpeedKnots;
        HoldTime = mooredNow ? HoldTime + dt : 0.0;

        if (MooringTasks.Alongside(Task, x)) Mark(MooringStage.Alongside, time);
        if (atFenders && x.SpeedOverGroundKn <= Task.stopSpeedKnots) Mark(MooringStage.AtFenders, time);
        if (lines) Mark(MooringStage.LinesFast, time);
        if (mooredNow && HoldTime >= Task.mooredHoldS - 1e-9)
        {
            for (int s = 1; s < StageCount; s++) Mark((MooringStage)s, time);   // ошвартован ⇒ все предыдущие
            Mark(MooringStage.Moored, time);
        }
        fresh.Sort();
        return fresh;
    }

    private void Mark(MooringStage s, double time)
    {
        if (done[(int)s]) return;
        done[(int)s] = true;
        doneAt[(int)s] = time;
        fresh.Add(s);
    }
}

/// <summary>Условия и тексты заданий швартовки.</summary>
public static class MooringTasks
{
    public static bool Alongside(MooringTaskDefinition t, in MooringTaskInput x) =>
        x.Readout.Valid && Math.Max(x.Readout.BowGapM, x.Readout.SternGapM) <= t.alongsideGapM && Math.Abs(x.Readout.AngleDeg) <= t.alongsideAngleDeg;

    public static bool AtFenders(MooringTaskDefinition t, in MooringTaskInput x) =>
        x.Readout.Valid && (x.FenderContact || Math.Max(x.Readout.BowGapM, x.Readout.SternGapM) <= t.stopGapM);

    /// <summary>Поданы ли носовой и кормовой концы (носовые — продольный, прижимной, шпринг; кормовые — так же).</summary>
    public static void LinesFast(IReadOnlyList<MooringLine> lines, out bool bow, out bool stern)
    {
        bow = stern = false;
        if (lines == null) return;
        foreach (MooringLine l in lines)
        {
            if (l == null || l.State != MooringLineState.Fast) continue;
            if (IsBow(l.Kind)) bow = true; else stern = true;
        }
    }

    public static bool IsBow(MooringLineKind k) => k == MooringLineKind.HeadLine || k == MooringLineKind.ForwardBreast || k == MooringLineKind.ForwardSpring;

    public static string Title(MooringStage s)
    {
        switch (s)
        {
            case MooringStage.Alongside: return "Подойти лагом";
            case MooringStage.AtFenders: return "Остановиться у кранцев";
            case MooringStage.LinesFast: return "Подать концы с носа и кормы";
            case MooringStage.Moored: return "Ошвартоваться";
            default: return "—";
        }
    }

    /// <summary>Условие этапа словами (брифинг, вкладка «Задание»).</summary>
    public static string Condition(MooringTaskDefinition t, MooringStage s)
    {
        switch (s)
        {
            case MooringStage.Alongside:
                return $"борт у носа и у кормы не дальше {ScenarioText.Num(t.alongsideGapM)} м от кромки, угол к кромке не больше {ScenarioText.Num(t.alongsideAngleDeg)}°";
            case MooringStage.AtFenders:
                return $"у кранцев (касание или до {ScenarioText.Num(t.stopGapM)} м), ход не больше {ScenarioText.Num(t.stopSpeedKnots, 1)} уз";
            case MooringStage.LinesFast:
                return "хотя бы один носовой и один кормовой конец на кнехтах причала";
            case MooringStage.Moored:
                return $"концы поданы, судно у кранцев, ход не больше {ScenarioText.Num(t.mooredSpeedKnots, 1)} уз непрерывно {ScenarioText.Num(t.mooredHoldS)} с — упражнение завершается";
            default: return "";
        }
    }

    /// <summary>Борт к причалу при курсе headingDeg: «левым» / «правым» (как MooringSystem.SideToBerth).</summary>
    public static string SideTitle(BerthArea b, double headingDeg)
    {
        if (b == null) return "";
        double psi = headingDeg * Math.PI / 180.0;
        WorldPosition n = b.NAxis;
        double sE = Math.Cos(psi), sN = -Math.Sin(psi);   // на правый борт
        return -(sE * n.East + sN * n.North) >= 0.0 ? "правым" : "левым";
    }
}
