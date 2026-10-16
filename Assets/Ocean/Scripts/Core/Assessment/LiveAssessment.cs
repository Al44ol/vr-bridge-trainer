// ============================================================
// LiveAssessment.cs — Оценка в реальном времени и по записи (C2)
//
// LiveAssessment — обёртка ExerciseAssessor для сцены (ExerciseAssessmentMonitor)
// и прогона сценария вне сцены (ExerciseSimulation):
//   • срезы — с той же частотой и в те же моменты, что строки track.csv
//     (TrackSampler, срез t = 0 в момент «Начать», последний — в момент конца);
//   • события и срезы округляются как в CSV (SessionFormat.Quantize);
//   • события самой оценки (обнаружение цели, начало сближения) выдаются
//     наружу (Output) — их публикует в шину владелец.
// SessionAssessment.Assess — та же оценка по записанной сессии (SessionReader).
// Требование C2: оценка в шлеме и по записи той же сессии совпадают
// (тест AssessmentTests.LiveAndOffline_SameSession_IdenticalResults).
// ============================================================
using System;
using System.Collections.Generic;

public sealed class LiveAssessment
{
    public ExerciseAssessor Assessor { get; private set; }
    public bool Running { get; private set; }
    /// <summary>Итог последнего упражнения (после End).</summary>
    public AssessmentResult Result { get; private set; }
    public double LastSampleTime { get; private set; } = double.NaN;

    /// <summary>События оценки (TargetDetected, EncounterStarted) — опубликовать в шину.</summary>
    public event Action<ExerciseEvent> Output;

    private readonly TrackSampler sampler = new TrackSampler(5);
    private readonly List<TargetSample> buffer = new List<TargetSample>();

    public void Begin(AssessmentContext context, double rateHz)
    {
        Assessor = new ExerciseAssessor(context);
        Assessor.TargetDetected += t => Output?.Invoke(new ExerciseEvent(ExerciseEventType.TargetDetected)
        {
            Subject = t.Info.Id, Index = t.Info.Index, Value = t.DetectRangeNm, Value2 = t.DetectionRangeNm
        });
        Assessor.EncounterStarted += t => Output?.Invoke(new ExerciseEvent(ExerciseEventType.EncounterStarted)
        {
            Subject = t.Info.Id, Index = (int)t.Encounter.Situation,
            Value = t.Encounter.OnsetCpaM / NavigationLights.MetersPerNm, Value2 = t.Encounter.OnsetTcpaS / 60.0
        });
        sampler.SetRate(rateHz);
        sampler.Reset();
        LastSampleTime = double.NaN;
        Result = null;
        Running = true;
    }

    public void OnEvent(in ExerciseEvent e)
    {
        if (!Running || !e.InExercise) return;
        Assessor.AddEvent(SessionFormat.Quantize(e));
    }

    /// <summary>Пора брать срез (по времени упражнения).</summary>
    public bool Due(double t) => Running && sampler.Due(t);

    /// <summary>Нужен последний срез в момент конца (как последняя строка track.csv).</summary>
    public bool NeedsFinalSample(double endTime) => Running && (double.IsNaN(LastSampleTime) || LastSampleTime < endTime - 1e-6);

    public void AddSample(in TrackSample own, IReadOnlyList<TargetSample> targets)
    {
        if (!Running) return;
        buffer.Clear();
        if (targets != null) foreach (TargetSample t in targets) buffer.Add(SessionFormat.Quantize(t));
        TrackSample q = SessionFormat.Quantize(own);
        Assessor.AddSample(q, buffer);
        LastSampleTime = own.Time;
    }

    /// <summary>19.2б-3в: срез с строкой mooring.csv (та же строка, что в файле, — после Quantize).</summary>
    public void AddSample(in TrackSample own, IReadOnlyList<TargetSample> targets, in MooringSample mooring)
    {
        if (!Running) return;
        AddSample(own, targets);
        if (mooring.Lines != null) Assessor.AddMooring(SessionFormat.Quantize(mooring));
    }

    public AssessmentResult End()
    {
        if (!Running) return Result;
        Running = false;
        Result = Assessor.Finish();
        return Result;
    }

    /// <summary>Текущая оценка: во время упражнения — промежуточная, после — итог.</summary>
    public AssessmentResult Current() => Running ? Assessor.Evaluate() : Result;
}

public static class SessionAssessment
{
    /// <summary>Оценка по записанной сессии. settings — другие пороги (null — из session.json).</summary>
    public static AssessmentResult Assess(RecordedSession s, AssessmentSettings settings = null)
    {
        AssessmentContext ctx = AssessmentContext.From(s.Meta);
        if (settings != null) ctx.Settings = settings;
        var a = new ExerciseAssessor(ctx);
        int ei = 0, ti = 0;
        var tgts = new List<TargetSample>();
        foreach (TrackSample own in s.Track)
        {
            while (ei < s.Events.Count && s.Events[ei].Time <= own.Time + 1e-9) a.AddEvent(s.Events[ei++]);
            tgts.Clear();
            while (ti < s.Targets.Count && s.Targets[ti].Time < own.Time - 1e-9) ti++;
            while (ti < s.Targets.Count && Math.Abs(s.Targets[ti].Time - own.Time) <= 1e-9) tgts.Add(s.Targets[ti++]);
            a.AddSample(own, tgts);
        }
        while (ei < s.Events.Count) a.AddEvent(s.Events[ei++]);
        foreach (MooringSample m in s.Mooring) a.AddMooring(m);   // 19.2б-3в: строки mooring.csv
        return a.Finish();
    }
}
