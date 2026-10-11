// ============================================================
// ExerciseTargets.cs — Реестр других судов для записи (C1 → B3)
//
// Модуль других судов (B3, TrafficController) регистрирует каждую цель
// (TrafficVessel); ExerciseRecorder пишет все зарегистрированные цели
// в targets.csv с частотой траектории. Без целей в сценарии — только заголовок.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

/// <summary>Другое судно, состояние которого пишется в targets.csv.</summary>
public interface ITrackedTarget
{
    string TargetId { get; }
    WorldPosition Position { get; }
    /// <summary>Курс, градусы 0…360.</summary>
    double HeadingDeg { get; }
    double SpeedKnots { get; }
}

public static class ExerciseTargets
{
    private static readonly List<ITrackedTarget> targets = new List<ITrackedTarget>();

    public static IReadOnlyList<ITrackedTarget> All => targets;

    public static void Register(ITrackedTarget t)
    {
        if (t != null && !targets.Contains(t)) targets.Add(t);
    }

    public static void Unregister(ITrackedTarget t) => targets.Remove(t);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() => targets.Clear();
}
