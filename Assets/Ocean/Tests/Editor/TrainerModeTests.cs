// ============================================================
// TrainerModeTests.cs — Режим тренажёра: экзамен / обучение / инструктор (перед запросом 19.2, п. B)
//
// Права режимов; переключение и событие Changed; сводка TrainerModeUsage по событиям
// (состояние на начало, переключения, «с какого момента»); строка итогов; новое событие —
// в конце перечня, с текстом, читается из events.csv; report.html — строка только при
// событиях режима (записи до п. B — отчёт прежний).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

public class TrainerModeTests
{
    private static ExerciseEvent Mode(double t, TrainerMode mode, int previous) =>
        new ExerciseEvent(ExerciseEventType.TrainerModeChanged) { Time = t, Index = (int)mode, Value = previous };

    [TearDown]
    public void RestoreDefault() => TrainerModes.Reset();

    [Test]
    public void Rights_ByMode()
    {
        Assert.IsFalse(TrainerModes.HintsAllowed(TrainerMode.Exam));
        Assert.IsFalse(TrainerModes.InstructorCommandsAllowed(TrainerMode.Exam));
        Assert.IsFalse(TrainerModes.DebugAllowed(TrainerMode.Exam));
        Assert.IsTrue(TrainerModes.HintsAllowed(TrainerMode.Training));
        Assert.IsTrue(TrainerModes.InstructorCommandsAllowed(TrainerMode.Training));
        Assert.IsFalse(TrainerModes.DebugAllowed(TrainerMode.Training));
        Assert.IsTrue(TrainerModes.HintsAllowed(TrainerMode.Instructor));
        Assert.IsTrue(TrainerModes.DebugAllowed(TrainerMode.Instructor));
        Assert.AreEqual(TrainerMode.Instructor, TrainerModes.Default, "по умолчанию — как до режимов: всё доступно");
        Assert.AreEqual(TrainerMode.Instructor, TrainerModes.FromIndex(7), "неизвестное число — режим по умолчанию");
    }

    [Test]
    public void Set_RaisesChangedOnlyOnChange()
    {
        TrainerModes.Reset();
        var seen = new List<(TrainerMode, TrainerMode)>();
        TrainerModes.Changed += (a, b) => seen.Add((a, b));
        Assert.IsFalse(TrainerModes.Set(TrainerMode.Instructor), "тот же режим — без события");
        Assert.IsTrue(TrainerModes.Set(TrainerMode.Exam));
        Assert.IsTrue(TrainerModes.Set(TrainerMode.Training));
        Assert.AreEqual(TrainerMode.Training, TrainerModes.Current);
        CollectionAssert.AreEqual(new[] { (TrainerMode.Instructor, TrainerMode.Exam), (TrainerMode.Exam, TrainerMode.Training) }, seen);
    }

    [Test]
    public void Usage_ExamThenInstructor_FromTime()
    {
        TrainerModeUsage u = TrainerModeUsage.From(new[]
        {
            Mode(0.0, TrainerMode.Exam, -1),
            Mode(192.4, TrainerMode.Instructor, (int)TrainerMode.Exam),
            Mode(250.0, TrainerMode.Exam, (int)TrainerMode.Instructor)
        });
        Assert.IsTrue(u.Recorded);
        Assert.AreEqual(TrainerMode.Exam, u.StartMode);
        Assert.AreEqual(TrainerMode.Instructor, u.HighestMode);
        Assert.AreEqual(192.4, u.InstructorFrom, 1e-9);
        Assert.AreEqual(192.4, u.TrainingFrom, 1e-9, "инструктор включает права обучения");
        Assert.AreEqual(2, u.Changes);
        Assert.AreEqual("Был использован режим инструктора (с 03:12; начало — экзамен, переключений 2)", u.Line());
    }

    [Test]
    public void Usage_StartsInInstructor_FromZero_NoChanges()
    {
        TrainerModeUsage u = TrainerModeUsage.From(new[] { Mode(0.0, TrainerMode.Instructor, -1) });
        Assert.AreEqual(0.0, u.InstructorFrom);
        Assert.AreEqual(0, u.Changes);
        Assert.AreEqual("Был использован режим инструктора (с 00:00)", u.Line());
    }

    [Test]
    public void Usage_TrainingOnly_And_ExamOnly()
    {
        TrainerModeUsage t = TrainerModeUsage.From(new[] { Mode(0.0, TrainerMode.Exam, -1), Mode(65.0, TrainerMode.Training, (int)TrainerMode.Exam) });
        Assert.IsTrue(t.UsedTraining);
        Assert.IsFalse(t.UsedInstructor);
        StringAssert.StartsWith("Был использован режим обучения — подсказки доступны (с 01:05", t.Line());

        TrainerModeUsage e = TrainerModeUsage.From(new[] { Mode(0.0, TrainerMode.Exam, -1) });
        Assert.IsFalse(e.UsedTraining || e.UsedInstructor);
        StringAssert.StartsWith("Режим тренажёра: экзамен", e.Line());
    }

    [Test]
    public void Usage_ChangeWithoutSnapshot_StartIsPreviousMode()
    {
        // Журнал без состояния на начало (например, обрезан): начало — прежний режим первого переключения
        TrainerModeUsage u = TrainerModeUsage.From(new[] { Mode(30.0, TrainerMode.Training, (int)TrainerMode.Exam) });
        Assert.AreEqual(TrainerMode.Exam, u.StartMode);
        Assert.AreEqual(30.0, u.TrainingFrom);
        Assert.AreEqual(1, u.Changes);
    }

    [Test]
    public void Usage_NoEvents_NoLine()
    {
        TrainerModeUsage u = TrainerModeUsage.From(new List<ExerciseEvent>());
        Assert.IsFalse(u.Recorded);
        Assert.AreEqual("", u.Line(), "записи до п. B — строки в итогах нет");
        Assert.AreEqual("", TrainerModeUsage.From(null).Line());
    }

    [Test]
    public void Event_AppendedAtEnd_WithText_Parsable()
    {
        Assert.Greater((int)ExerciseEventType.TrainerModeChanged, (int)ExerciseEventType.PlotterRevealTargets, "новый тип — в конец перечня");
        StringAssert.Contains("на начало: экзамен", Mode(0, TrainerMode.Exam, -1).Describe());
        string change = new ExerciseEvent(ExerciseEventType.TrainerModeChanged) { Index = 2, Value = 0, ByInstructor = true }.Describe();
        Assert.AreEqual("РЕЖИМ ТРЕНАЖЁРА: инструктор (был: экзамен) (окно инструктора)", change);
        Assert.IsTrue(Enum.TryParse(ExerciseEventType.TrainerModeChanged.ToString(), out ExerciseEventType t) && t == ExerciseEventType.TrainerModeChanged);
    }

    [Test]
    public void Summary_SessionJsonFields()
    {
        var log = new List<ExerciseEvent> { Mode(0.0, TrainerMode.Exam, -1), Mode(120.0, TrainerMode.Training, 0) };
        SessionSummary s = SessionSummary.From(ExerciseSummary.From(null, log, null));
        Assert.AreEqual("Exam", s.trainer_mode_start);
        Assert.AreEqual("Training", s.trainer_mode_highest);
        Assert.AreEqual(120.0, s.training_mode_from_s);
        Assert.AreEqual(-1.0, s.instructor_mode_from_s);
        Assert.AreEqual(1, s.trainer_mode_changes);

        SessionSummary old = SessionSummary.From(ExerciseSummary.From(null, new List<ExerciseEvent>(), null));
        Assert.AreEqual("", old.trainer_mode_start, "без событий режима — пусто");
        Assert.AreEqual(-1.0, old.training_mode_from_s);
    }

    [Test]
    public void Report_LineOnlyWithModeEvents()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sim = new ExerciseSimulation { Scenario = AssessmentTests.DemoB2(), RecordFolder = dir };
            DemoAssessmentRuns.Configure(sim, "steer_late");
            sim.Run();
            RecordedSession rs = SessionReader.Load(dir);
            AssessmentResult r = SessionAssessment.Assess(rs);
            DebriefMapData map = DebriefMap.Build(rs, r);

            string before = SessionReport.Build(rs, r, map);
            StringAssert.DoesNotContain("Режим тренажёра", before, "прогон без событий режима — отчёт прежний");
            StringAssert.DoesNotContain("режим инструктора", before);

            rs.Events.Insert(0, Mode(0.0, TrainerMode.Exam, -1));
            rs.Events.Add(Mode(192.0, TrainerMode.Instructor, (int)TrainerMode.Exam));
            string after = SessionReport.Build(rs, r, map);
            StringAssert.Contains("<p><b>Был использован режим инструктора (с 03:12; начало — экзамен, переключений 1).</b></p>", after);
            StringAssert.Contains("РЕЖИМ ТРЕНАЖЁРА: инструктор (был: экзамен)", after, "журнал событий");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
