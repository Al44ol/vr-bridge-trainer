// ============================================================
// DebriefBuild.cs — Построение разбора после упражнения (C3)
//
// В шлеме после закрытия записи: чтение файлов сессии, схема траектории
// и report.html — в фоновом потоке (чтение ~0,5 МБ и сборка страницы не
// должны задерживать кадр). Итог оценки берётся готовый (оценка в реальном
// времени; по C2 она бит в бит равна оценке по записи): офлайн-оценка
// создаёт ShipParameters (ScriptableObject), а это в Unity можно только
// в главном потоке. Панель разбора опрашивает Done и рисует схему по Map.
// Run() — то же синхронно (тесты, редактор).
// Запрос 17: графики (DebriefPlots) — для вкладки «Графики».
// ============================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class DebriefBuild
{
    public readonly string Folder;
    public readonly AssessmentResult Result;
    public bool WriteReport = true;

    /// <summary>Готово (успешно или с ошибкой).</summary>
    public bool Done => done;
    public RecordedSession Session { get; private set; }
    public DebriefMapData Map { get; private set; }
    /// <summary>Графики разбора (запрос 17): те же ряды, что в report.html.</summary>
    public List<DebriefPlot> Plots { get; private set; }
    public string ReportPath { get; private set; } = "";
    public string Error { get; private set; } = "";
    /// <summary>Время построения, мс (для журнала и ThesisNotes).</summary>
    public double ElapsedMs { get; private set; }

    private volatile bool done;

    public DebriefBuild(string folder, AssessmentResult result)
    {
        Folder = folder;
        Result = result;
    }

    /// <summary>Запустить в фоновом потоке.</summary>
    public static DebriefBuild Start(string folder, AssessmentResult result, bool writeReport = true)
    {
        var b = new DebriefBuild(folder, result) { WriteReport = writeReport };
        Task.Run(() => b.Run());
        return b;
    }

    public void Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            RecordedSession s = SessionReader.Load(Folder);
            DebriefMapData map = DebriefMap.Build(s, Result);
            List<DebriefPlot> plots = DebriefPlots.Build(s, Result);
            if (WriteReport)
                ReportPath = SessionReport.Write(Folder, SessionReport.Build(s, Result, map, new ReportOptions { AssessmentSource = "оценка в шлеме" }));
            Session = s;
            Map = map;
            Plots = plots;
        }
        catch (Exception e) { Error = e.Message; }
        finally
        {
            ElapsedMs = sw.Elapsed.TotalMilliseconds;
            done = true;
        }
    }
}
