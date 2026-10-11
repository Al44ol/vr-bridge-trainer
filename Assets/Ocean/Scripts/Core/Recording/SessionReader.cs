// ============================================================
// SessionReader.cs — Чтение записанной сессии (C1)
//
// Офлайн-вход для разбора: окно Replay (C1), оценка по записи (C2),
// отчёт (C3), в будущем — разбор в шлеме. Без зависимостей от редактора.
//   • события читаются обратно в ExerciseEvent — офлайн-оценка получает
//     тот же поток событий, что и подписчики шины в реальном времени;
//   • столбцы ищутся по заголовку (лишние и новые столбцы не мешают);
//   • файл можно читать, пока он пишется (FileShare.ReadWrite).
// StateAt(t) — состояние судна в любой момент: линейная интерполяция между
// строками траектории, курс — по кратчайшей дуге (359° → 1° через 0°),
// дискретные величины (телеграф, участок, на мели) — по предыдущей строке.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class RecordedSession
{
    public string Folder = "";
    public SessionMeta Meta = new SessionMeta();
    public readonly List<TrackSample> Track = new List<TrackSample>();
    public readonly List<ExerciseEvent> Events = new List<ExerciseEvent>();
    public readonly List<TargetSample> Targets = new List<TargetSample>();
    /// <summary>Замечания при чтении (нет файла, нераспознанные строки).</summary>
    public readonly List<string> Warnings = new List<string>();

    public string Name => Path.GetFileName(Folder);

    /// <summary>Длительность записи, с (по последней строке траектории или событию).</summary>
    public double Duration
    {
        get
        {
            double d = Track.Count > 0 ? Track[Track.Count - 1].Time : 0;
            if (Events.Count > 0) d = Math.Max(d, Events[Events.Count - 1].Time);
            return d;
        }
    }

    /// <summary>Номер последней строки траектории с Time ≤ t (−1 — до начала).</summary>
    public int IndexAt(double t)
    {
        int lo = 0, hi = Track.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (Track[mid].Time <= t) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }

    /// <summary>Состояние судна в момент t (интерполяция между строками).</summary>
    public TrackSample StateAt(double t)
    {
        if (Track.Count == 0) return new TrackSample { Time = t, Leg = -1, XteM = double.NaN, XteLimitM = double.NaN };
        int i = IndexAt(t);
        if (i < 0) return Track[0];
        if (i >= Track.Count - 1) return Track[Track.Count - 1];
        TrackSample a = Track[i], b = Track[i + 1];
        double span = b.Time - a.Time;
        double k = span > 1e-9 ? (t - a.Time) / span : 0.0;
        TrackSample s = a;
        s.Time = t;
        s.East = Lerp(a.East, b.East, k);
        s.North = Lerp(a.North, b.North, k);
        s.HeadingDeg = LerpAngle(a.HeadingDeg, b.HeadingDeg, k);
        s.CogDeg = LerpAngle(a.CogDeg, b.CogDeg, k);
        s.SpeedKnots = Lerp(a.SpeedKnots, b.SpeedKnots, k);
        s.RotDegMin = Lerp(a.RotDegMin, b.RotDegMin, k);
        s.RudderOrderedDeg = Lerp(a.RudderOrderedDeg, b.RudderOrderedDeg, k);
        s.RudderActualDeg = Lerp(a.RudderActualDeg, b.RudderActualDeg, k);
        if (a.Leg == b.Leg) s.XteM = Lerp(a.XteM, b.XteM, k);   // на смене участка XTE скачет — не смешивать
        return s;
    }

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>Интерполяция угла 0…360° по кратчайшей дуге.</summary>
    public static double LerpAngle(double a, double b, double k)
    {
        double d = Math.IEEERemainder(b - a, 360.0);
        double v = (a + d * k) % 360.0;
        return v < 0 ? v + 360.0 : v;
    }
}

public static class SessionReader
{
    public static RecordedSession Load(string folder)
    {
        var s = new RecordedSession { Folder = folder };
        string meta = Path.Combine(folder, SessionFormat.MetaFile);
        if (File.Exists(meta))
        {
            try { JsonUtility.FromJsonOverwrite(ReadAll(meta), s.Meta); }
            catch (Exception e) { s.Warnings.Add($"{SessionFormat.MetaFile}: {e.Message}"); }
        }
        else s.Warnings.Add($"нет {SessionFormat.MetaFile}");

        ReadTable(Path.Combine(folder, SessionFormat.TrackFile), s, (c, f) => s.Track.Add(ParseTrack(c, f)));
        ReadTable(Path.Combine(folder, SessionFormat.TargetsFile), s, (c, f) => s.Targets.Add(ParseTarget(c, f)));
        ReadTable(Path.Combine(folder, SessionFormat.EventsFile), s, (c, f) =>
        {
            if (TryParseEvent(c, f, out ExerciseEvent e)) s.Events.Add(e);
            else s.Warnings.Add($"{SessionFormat.EventsFile}: неизвестный тип события «{Get(c, f, "type")}»");
        });
        return s;
    }

    // Колонки по заголовку
    private sealed class Columns
    {
        public readonly Dictionary<string, int> Index = new Dictionary<string, int>();
        public Columns(List<string> header)
        {
            for (int i = 0; i < header.Count; i++) Index[header[i].Trim()] = i;
        }
    }

    private static string Get(Columns c, List<string> f, string name) =>
        c.Index.TryGetValue(name, out int i) && i < f.Count ? f[i] : "";

    private static double D(Columns c, List<string> f, string name) => CsvFormat.ParseDouble(Get(c, f, name));
    private static int I(Columns c, List<string> f, string name, int fallback = -1) => CsvFormat.ParseInt(Get(c, f, name), fallback);

    private static void ReadTable(string path, RecordedSession s, Action<Columns, List<string>> row)
    {
        if (!File.Exists(path))
        {
            s.Warnings.Add($"нет {Path.GetFileName(path)}");
            return;
        }
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(fs, new UTF8Encoding(false), true))
        {
            string headerLine = r.ReadLine();
            if (headerLine == null) return;
            var cols = new Columns(CsvFormat.Split(headerLine));
            string line;
            while ((line = r.ReadLine()) != null)
                if (line.Length > 0) row(cols, CsvFormat.Split(line));
        }
    }

    private static string ReadAll(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(fs, new UTF8Encoding(false), true))
            return r.ReadToEnd();
    }

    private static TrackSample ParseTrack(Columns c, List<string> f) => new TrackSample
    {
        Time = D(c, f, "t_s"),
        East = D(c, f, "x_e_m"), North = D(c, f, "y_n_m"),
        HeadingDeg = D(c, f, "heading_deg"), CogDeg = D(c, f, "cog_deg"),
        SpeedKnots = D(c, f, "speed_kn"), RotDegMin = D(c, f, "rot_deg_min"),
        RudderOrderedDeg = D(c, f, "rudder_ordered_deg"), RudderActualDeg = D(c, f, "rudder_actual_deg"),
        TelegraphSf = D(c, f, "telegraph_sf"), Telegraph = I(c, f, "telegraph", (int)TelegraphOrder.Stop),
        XteM = D(c, f, "xte_m"), Leg = I(c, f, "leg"), XteLimitM = D(c, f, "xte_limit_m"),
        Grounded = I(c, f, "grounded", 0) == 1
    };

    private static TargetSample ParseTarget(Columns c, List<string> f) => new TargetSample
    {
        Time = D(c, f, "t_s"), Id = Get(c, f, "id"),
        East = D(c, f, "x_e_m"), North = D(c, f, "y_n_m"),
        HeadingDeg = D(c, f, "heading_deg"), SpeedKnots = D(c, f, "speed_kn")
    };

    private static bool TryParseEvent(Columns c, List<string> f, out ExerciseEvent e)
    {
        e = default;
        if (!Enum.TryParse(Get(c, f, "type"), out ExerciseEventType type)) return false;
        e = new ExerciseEvent(type)
        {
            Time = D(c, f, "t_s"),
            InExercise = true,
            Index = I(c, f, "index"),
            Value = D(c, f, "value"),
            Value2 = D(c, f, "value2"),
            Subject = Get(c, f, "subject"),
            ByInstructor = I(c, f, "by_instructor", 0) == 1,
            Position = new WorldPosition(D(c, f, "x_e_m"), D(c, f, "y_n_m")),
            HeadingDeg = (float)D(c, f, "heading_deg"),
            SpeedKnots = (float)D(c, f, "speed_kn")
        };
        if (Enum.TryParse(Get(c, f, "end_reason"), out ScenarioEndReason reason)) e.EndReason = reason;
        return true;
    }
}
