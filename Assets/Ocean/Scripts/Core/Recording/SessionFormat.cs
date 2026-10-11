// ============================================================
// SessionFormat.cs — Формат файлов сессии упражнения (C1)
//
// Папка сессии <yyyyMMdd_HHmmss>_<сценарий>_<ID обучающегося>/:
//   track.csv    — своё судно с частотой 5 Гц (по времени упражнения);
//   targets.csv  — другие суда (B3), та же частота, строка на цель;
//   events.csv   — все события шины ExerciseEvents за упражнение, по одному
//                  на строку, в порядке доставки (n — порядковый номер);
//   session.json — метаданные: сценарий (с маршрутом), судно, приложение,
//                  начало/конец, причина завершения, предварительный итог.
// Время t_s — время упражнения (симуляционное) от «Начать», с.
// Координаты: x_e_m — восток, y_n_m — север, м (WorldPosition); курс 0 = N,
// по часовой; руль и XTE > 0 — правый борт / правее линии пути.
// Смысл index / value / value2 событий — в шапке ExerciseEvents.cs.
// Столбцы ищутся по заголовку — новые столбцы в конце не ломают чтение.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>Строка track.csv — состояние своего судна.</summary>
public struct TrackSample
{
    public double Time;
    public double East, North;
    public double HeadingDeg, CogDeg;
    public double SpeedKnots;
    public double RotDegMin;
    public double RudderOrderedDeg, RudderActualDeg;
    public double TelegraphSf;
    /// <summary>Положение телеграфа — номер TelegraphOrder (0 — «Полный вперёд» … 8 — «Полный назад»).</summary>
    public int Telegraph;
    public double XteM;
    /// <summary>Участок маршрута (RouteTracker.Leg: участок i ведёт к точке i + 1); −1 — маршрута нет.</summary>
    public int Leg;
    public double XteLimitM;
    public bool Grounded;

    public WorldPosition Position => new WorldPosition(East, North);
}

/// <summary>Строка targets.csv — другое судно.</summary>
public struct TargetSample
{
    public double Time;
    public string Id;
    public double East, North;
    public double HeadingDeg, SpeedKnots;
}

public static class SessionFormat
{
    public const string FormatName = "VRBridge.Session";
    public const int FormatVersion = 1;

    public const string TrackFile = "track.csv";
    public const string TargetsFile = "targets.csv";
    public const string EventsFile = "events.csv";
    public const string MetaFile = "session.json";
    /// <summary>Метка «не удалять при ротации».</summary>
    public const string KeepFile = "keep";

    public const string TrackHeader =
        "t_s;x_e_m;y_n_m;heading_deg;cog_deg;speed_kn;rot_deg_min;rudder_ordered_deg;rudder_actual_deg;telegraph_sf;telegraph;xte_m;leg;xte_limit_m;grounded";
    public const string TargetsHeader = "t_s;id;x_e_m;y_n_m;heading_deg;speed_kn";
    public const string EventsHeader =
        "n;t_s;type;index;value;value2;subject;end_reason;by_instructor;x_e_m;y_n_m;heading_deg;speed_kn;description";

    // ─────────── Строки ───────────
    public static void AppendTrack(StringBuilder b, in TrackSample s)
    {
        CsvFormat.Num(b, s.Time, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.East, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.North, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.HeadingDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.CogDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.SpeedKnots, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.RotDegMin, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.RudderOrderedDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.RudderActualDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.TelegraphSf, 2); CsvFormat.Sep(b);
        CsvFormat.Int(b, s.Telegraph); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.XteM, 2); CsvFormat.Sep(b);
        if (s.Leg >= 0) CsvFormat.Int(b, s.Leg);
        CsvFormat.Sep(b);
        CsvFormat.Num(b, s.XteLimitM, 1); CsvFormat.Sep(b);
        b.Append(s.Grounded ? '1' : '0');
        b.Append('\n');
    }

    public static void AppendTarget(StringBuilder b, in TargetSample s)
    {
        CsvFormat.Num(b, s.Time, 3); CsvFormat.Sep(b);
        CsvFormat.Text(b, s.Id); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.East, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.North, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.HeadingDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.SpeedKnots, 3);
        b.Append('\n');
    }

    public static void AppendEvent(StringBuilder b, int n, in ExerciseEvent e)
    {
        CsvFormat.Int(b, n); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.Time, 3); CsvFormat.Sep(b);
        b.Append(e.Type.ToString()); CsvFormat.Sep(b);
        if (e.Index >= 0) CsvFormat.Int(b, e.Index);
        CsvFormat.Sep(b);
        CsvFormat.Num(b, e.Value, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.Value2, 3); CsvFormat.Sep(b);
        CsvFormat.Text(b, e.Subject); CsvFormat.Sep(b);
        if (e.EndReason != ScenarioEndReason.None) b.Append(e.EndReason.ToString());
        CsvFormat.Sep(b);
        b.Append(e.ByInstructor ? '1' : '0'); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.Position.East, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.Position.North, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.HeadingDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, e.SpeedKnots, 3); CsvFormat.Sep(b);
        CsvFormat.Text(b, e.Describe());
        b.Append('\n');
    }

    // ─────────── Имя папки ───────────
    /// <summary>Имя папки сессии: yyyyMMdd_HHmmss_&lt;сценарий&gt;_&lt;ID&gt; (без ID — noid).</summary>
    public static string FolderName(DateTime localStart, string scenarioId, string traineeId) =>
        $"{localStart:yyyyMMdd_HHmmss}_{Sanitize(scenarioId, "scenario")}_{Sanitize(traineeId, "noid")}";

    /// <summary>Строка, пригодная для имени файла (недопустимые символы и пробелы → «_»).</summary>
    public static string Sanitize(string s, string fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var b = new StringBuilder(s.Trim());
        char[] bad = Path.GetInvalidFileNameChars();
        for (int i = 0; i < b.Length; i++)
            if (Array.IndexOf(bad, b[i]) >= 0 || char.IsWhiteSpace(b[i]) || b[i] == '.') b[i] = '_';
        return b.ToString();
    }
}

// ─────────── session.json (JsonUtility; имена полей — с единицами) ───────────
[Serializable]
public class SessionMeta
{
    public string format = SessionFormat.FormatName;
    public int format_version = SessionFormat.FormatVersion;
    public string session_id = "";
    /// <summary>Обезличенный код обучающегося (без ФИО).</summary>
    public string trainee_id = "";
    /// <summary>Упражнение завершено штатно (ExerciseEnded); false — запись прервана.</summary>
    public bool completed;
    /// <summary>ScenarioEndReason или Interrupted (выход из Play Mode / закрытие приложения).</summary>
    public string end_reason = "";
    public string end_reason_ru = "";
    public bool by_instructor;
    public string start_local = "", start_utc = "", end_local = "", end_utc = "";
    /// <summary>Длительность упражнения (симуляционная), с.</summary>
    public double duration_s;
    /// <summary>Длительность по часам ПК, с.</summary>
    public double wall_duration_s;
    public SessionApp app = new SessionApp();
    public SessionScenario scenario = new SessionScenario();
    public SessionShip ship = new SessionShip();
    public SessionRecording recording = new SessionRecording();
    public SessionSummary summary = new SessionSummary();

    public const string Interrupted = "Interrupted";

    /// <summary>Метаданные начала сессии по сценарию и параметрам судна.</summary>
    public static SessionMeta Create(ScenarioDefinition s, ShipParameters p, string traineeId, DateTime localStart)
    {
        var m = new SessionMeta
        {
            trainee_id = traineeId ?? "",
            start_local = localStart.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            start_utc = localStart.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            end_reason = SessionMeta.Interrupted,
            end_reason_ru = "запись прервана"
        };
        m.session_id = SessionFormat.FolderName(localStart, s != null ? s.id : "", traineeId);
        if (s != null) m.scenario = SessionScenario.From(s);
        if (p != null) m.ship = SessionShip.From(p);
        return m;
    }

    internal static double R(double v) => Math.Round(v, 6);
}

[Serializable]
public class SessionApp
{
    public string product = "";
    public string version = "";
    public string unity_version = "";
    public string platform = "";
    public bool is_editor;
    /// <summary>Коммит git (только в редакторе).</summary>
    public string git_commit = "";
}

[Serializable]
public class SessionWaypoint
{
    public string name = "";
    public double x_e_m, y_n_m;
    /// <summary>XTE доп. на участке, ведущем к точке, м.</summary>
    public double xte_limit_m;
}

[Serializable]
public class SessionInitial
{
    public double x_e_m, y_n_m, heading_deg, speed_kn, rudder_deg;
    public string telegraph = "";
    public double telegraph_sf;
}

[Serializable]
public class SessionScenario
{
    public string id = "", title = "", asset = "";
    public string water_area = "";
    public string weather = "";
    public string time_of_day = "";
    public double visibility_m;
    public double time_limit_s;
    public string end_conditions = "";
    public SessionInitial initial = new SessionInitial();
    public List<SessionWaypoint> route = new List<SessionWaypoint>();

    public static SessionScenario From(ScenarioDefinition s)
    {
        var r = new SessionScenario
        {
            id = s.id ?? "", title = s.title ?? "", asset = s.name,
            water_area = s.waterArea != null ? s.waterArea.name : "",
            weather = s.weather.ToString(), time_of_day = s.timeOfDay.ToString(),
            visibility_m = SessionMeta.R(s.visibilityMeters), time_limit_s = SessionMeta.R(s.timeLimitSeconds),
            end_conditions = s.endConditions.ToString()
        };
        if (s.initial != null)
            r.initial = new SessionInitial
            {
                x_e_m = s.initial.position.East, y_n_m = s.initial.position.North,
                heading_deg = SessionMeta.R(s.initial.headingDeg), speed_kn = SessionMeta.R(s.initial.speedKnots),
                rudder_deg = SessionMeta.R(s.initial.rudderDeg),
                telegraph = s.initial.telegraph.ToString(), telegraph_sf = ScenarioText.SpeedFactor(s.initial.telegraph)
            };
        if (s.route != null)
            foreach (RouteWaypoint w in s.route)
                if (w != null)
                    r.route.Add(new SessionWaypoint { name = w.name, x_e_m = w.position.East, y_n_m = w.position.North, xte_limit_m = SessionMeta.R(w.xteLimit) });
        return r;
    }

    /// <summary>Маршрут в виде путевых точек (для RouteTracker офлайн, C2).</summary>
    public List<RouteWaypoint> ToRoute()
    {
        var list = new List<RouteWaypoint>();
        foreach (SessionWaypoint w in route)
            list.Add(new RouteWaypoint(w.name, new WorldPosition(w.x_e_m, w.y_n_m), (float)w.xte_limit_m));
        return list;
    }
}

[Serializable]
public class SessionShip
{
    public string asset = "";
    public double length_m, service_speed_kn;
    public double tau_accel_s, tau_decel_s, astern_factor;
    public string yaw_model = "";
    public double k_prime, t_prime, norrbin_alpha_prime, turn_speed_loss_coeff, min_calc_speed_ms;
    public double max_rudder_deg, rudder_rate_deg_s;
    public double beam_m, draft_m;

    public static SessionShip From(ShipParameters p) => new SessionShip
    {
        asset = p.name,
        length_m = SessionMeta.R(p.length), service_speed_kn = SessionMeta.R(p.serviceSpeedKnots),
        tau_accel_s = SessionMeta.R(p.accelerationTimeConstant), tau_decel_s = SessionMeta.R(p.decelerationTimeConstant),
        astern_factor = SessionMeta.R(p.asternFactor),
        yaw_model = p.yawModel.ToString(),
        k_prime = SessionMeta.R(p.nomotoKPrime), t_prime = SessionMeta.R(p.nomotoTPrime),
        norrbin_alpha_prime = SessionMeta.R(p.norrbinAlphaPrime), turn_speed_loss_coeff = SessionMeta.R(p.turnSpeedLossCoeff),
        min_calc_speed_ms = SessionMeta.R(p.minCalcSpeed),
        max_rudder_deg = SessionMeta.R(p.maxRudderAngle), rudder_rate_deg_s = SessionMeta.R(p.rudderRate),
        beam_m = SessionMeta.R(p.beam), draft_m = SessionMeta.R(p.draft)
    };
}

[Serializable]
public class SessionRecording
{
    public double track_rate_hz;
    public double fixed_dt_s;
    public string time_base = "t_s — время упражнения (симуляционное) от «Начать», с";
    public string coordinates = "x_e_m — восток, y_n_m — север, м; курс 0 = N, по часовой; руль и XTE > 0 — правый борт / правее линии пути";
    public List<string> files = new List<string> { SessionFormat.TrackFile, SessionFormat.TargetsFile, SessionFormat.EventsFile, SessionFormat.MetaFile };
    /// <summary>Названия положений телеграфа по номеру (столбец telegraph, index события TelegraphChanged).</summary>
    public List<string> telegraph_positions = new List<string>();

    public SessionRecording()
    {
        foreach (TelegraphOrder o in Enum.GetValues(typeof(TelegraphOrder))) telegraph_positions.Add(ScenarioText.Telegraph(o));
    }
}

[Serializable]
public class SessionSummary
{
    public int waypoints_passed, waypoints_total;
    public double max_abs_xte_m, time_outside_corridor_s;
    public int corridor_exits, shoal_warnings, groundings;
    public int helm_actions, telegraph_orders, horn_signals, weather_changes;
    public int track_rows, event_rows, target_rows;

    public static SessionSummary From(ExerciseSummary s) => new SessionSummary
    {
        waypoints_passed = s.WaypointsPassed, waypoints_total = s.WaypointsTotal,
        max_abs_xte_m = Math.Round(s.MaxAbsXte, 2), time_outside_corridor_s = Math.Round(s.TimeOutsideCorridor, 2),
        corridor_exits = s.CorridorExits, shoal_warnings = s.ShoalWarnings, groundings = s.Groundings,
        helm_actions = s.HelmActions, telegraph_orders = s.TelegraphOrders, horn_signals = s.HornSignals, weather_changes = s.WeatherChanges
    };
}
