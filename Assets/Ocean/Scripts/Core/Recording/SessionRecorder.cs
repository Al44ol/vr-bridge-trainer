// ============================================================
// SessionRecorder.cs — Запись одной сессии упражнения (C1)
//
// Чистый класс: в сцене им управляет ExerciseRecorder, в тестах — прогон
// сценария без сцены. Строки копятся в памяти (StringBuilder), Flush отдаёт
// накопленное фоновому писателю (SessionFileWriter) — в сцене не чаще
// раза в секунду. Close пишет остаток и окончательный session.json.
//
// При открытии сразу создаются все четыре файла: CSV — с заголовком,
// session.json — с completed = false. Если приложение закроется аварийно,
// в папке останутся данные до последнего сброса и метаданные начала.
// ============================================================
using System;
using System.Text;
using UnityEngine;

public sealed class SessionRecorder
{
    public string Folder { get; }
    public SessionMeta Meta { get; }
    public bool IsOpen { get; private set; }
    public int TrackRows { get; private set; }
    public int TargetRows { get; private set; }
    public int EventRows { get; private set; }
    public SessionFileWriter Writer => writer;
    /// <summary>Время последней строки траектории, с (NaN — строк ещё нет).</summary>
    public double LastTrackTime { get; private set; } = double.NaN;

    private readonly SessionFileWriter writer;
    private readonly StringBuilder track = new StringBuilder(4096);
    private readonly StringBuilder targets = new StringBuilder(1024);
    private readonly StringBuilder events = new StringBuilder(1024);

    public SessionRecorder(string folder, SessionMeta meta, bool background = true)
    {
        Folder = folder;
        Meta = meta ?? new SessionMeta();
        writer = new SessionFileWriter(folder, background);
        writer.Append(SessionFormat.TrackFile, SessionFormat.TrackHeader + "\n");
        writer.Append(SessionFormat.TargetsFile, SessionFormat.TargetsHeader + "\n");
        writer.Append(SessionFormat.EventsFile, SessionFormat.EventsHeader + "\n");
        WriteMeta();
        IsOpen = true;
    }

    public void AddTrack(in TrackSample s)
    {
        if (!IsOpen) return;
        SessionFormat.AppendTrack(track, s);
        TrackRows++;
        LastTrackTime = s.Time;
    }

    public void AddTarget(in TargetSample s)
    {
        if (!IsOpen) return;
        SessionFormat.AppendTarget(targets, s);
        TargetRows++;
    }

    public void AddEvent(in ExerciseEvent e)
    {
        if (!IsOpen) return;
        EventRows++;
        SessionFormat.AppendEvent(events, EventRows, e);
    }

    /// <summary>Отдать накопленные строки писателю (не ждёт записи на диск).</summary>
    public void Flush()
    {
        Take(track, SessionFormat.TrackFile);
        Take(targets, SessionFormat.TargetsFile);
        Take(events, SessionFormat.EventsFile);
    }

    /// <summary>Дождаться записи на диск (пауза приложения).</summary>
    public bool FlushAndWait(int timeoutMs = 2000)
    {
        Flush();
        return writer.Flush(timeoutMs);
    }

    /// <summary>Завершить сессию: остаток строк, session.json, остановка писателя.</summary>
    public bool Close(int timeoutMs = 2000)
    {
        if (!IsOpen) return true;
        Flush();
        Meta.summary.track_rows = TrackRows;
        Meta.summary.target_rows = TargetRows;
        Meta.summary.event_rows = EventRows;
        WriteMeta();
        IsOpen = false;
        return writer.Close(timeoutMs);
    }

    /// <summary>Переписать session.json (при старте и в конце).</summary>
    public void WriteMeta() => writer.Replace(SessionFormat.MetaFile, JsonUtility.ToJson(Meta, true));

    private void Take(StringBuilder b, string file)
    {
        if (b.Length == 0) return;
        writer.Append(file, b.ToString());
        b.Clear();
    }
}

/// <summary>
/// Опрос с заданной частотой по времени упражнения: строки в t = 0, T, 2T…
/// (T = 1/f). Номер следующей строки считается от времени, а не накоплением T,
/// поэтому ошибка округления не копится; допуск 1 мкс — против t = 0,19999…
/// </summary>
public sealed class TrackSampler
{
    public double Period { get; private set; }
    private long next;

    public TrackSampler(double rateHz) { SetRate(rateHz); }

    public void SetRate(double rateHz) => Period = 1.0 / Math.Max(0.1, Math.Min(50.0, rateHz));

    public void Reset() => next = 0;

    /// <summary>Пора писать строку для времени t.</summary>
    public bool Due(double t)
    {
        const double Eps = 1e-6;
        if (t + Eps < next * Period) return false;
        next = (long)Math.Floor((t + Eps) / Period) + 1;
        return true;
    }
}
