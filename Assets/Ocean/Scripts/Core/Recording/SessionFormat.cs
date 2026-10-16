// ============================================================
// SessionFormat.cs — Формат файлов сессии упражнения (C1)
//
// Папка сессии <yyyyMMdd_HHmmss>_<сценарий>_<ID обучающегося>/:
//   track.csv    — своё судно с частотой 5 Гц (по времени упражнения);
//   targets.csv  — другие суда (B3), та же частота, строка на цель;
//   events.csv   — все события шины ExerciseEvents за упражнение, по одному
//                  на строку, в порядке доставки (n — порядковый номер);
//   session.json — метаданные: сценарий (с маршрутом), судно, приложение,
//                  начало/конец, причина завершения, предварительный итог;
//   mooring.csv  — 19.2б-3б: состояние у причала (зазоры, кранцы, концы) в те же
//                  моменты, что track.csv; только если причал понадобился (MooringRecording.cs).
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
    /// <summary>Оценка по критериям (C2): строка на критерий и цель.</summary>
    public const string AssessmentFile = "assessment.csv";
    /// <summary>Отчёт по упражнению (C3): самодостаточная страница для браузера и печати.</summary>
    public const string ReportFile = "report.html";
    /// <summary>19.2б-3б: состояние у причала (MooringRecording); только если причал понадобился.</summary>
    public const string MooringFile = "mooring.csv";
    public static string MooringHeader => MooringRecording.Header;

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

    // ─────────── Округление как в файле (C2) ───────────
    // Оценка в реальном времени получает строки траектории, целей и события в том виде,
    // в каком их прочитает SessionReader из CSV (те же знаки после точки). Поэтому оценка
    // в шлеме и офлайн-оценка по записи той же сессии совпадают до бита.

    public static TrackSample Quantize(in TrackSample s) => new TrackSample
    {
        Time = CsvFormat.Round(s.Time, 3),
        East = CsvFormat.Round(s.East, 2), North = CsvFormat.Round(s.North, 2),
        HeadingDeg = CsvFormat.Round(s.HeadingDeg, 2), CogDeg = CsvFormat.Round(s.CogDeg, 2),
        SpeedKnots = CsvFormat.Round(s.SpeedKnots, 3), RotDegMin = CsvFormat.Round(s.RotDegMin, 2),
        RudderOrderedDeg = CsvFormat.Round(s.RudderOrderedDeg, 2), RudderActualDeg = CsvFormat.Round(s.RudderActualDeg, 2),
        TelegraphSf = CsvFormat.Round(s.TelegraphSf, 2), Telegraph = s.Telegraph,
        XteM = CsvFormat.Round(s.XteM, 2), Leg = s.Leg >= 0 ? s.Leg : -1, XteLimitM = CsvFormat.Round(s.XteLimitM, 1),
        Grounded = s.Grounded
    };

    public static TargetSample Quantize(in TargetSample s) => new TargetSample
    {
        Time = CsvFormat.Round(s.Time, 3), Id = CsvFormat.Clean(s.Id),
        East = CsvFormat.Round(s.East, 2), North = CsvFormat.Round(s.North, 2),
        HeadingDeg = CsvFormat.Round(s.HeadingDeg, 2), SpeedKnots = CsvFormat.Round(s.SpeedKnots, 3)
    };

    public static void AppendMooring(StringBuilder b, in MooringSample s) => MooringRecording.Append(b, s);

    public static MooringSample Quantize(in MooringSample s) => MooringRecording.Quantize(s);

    public static ExerciseEvent Quantize(in ExerciseEvent e) => new ExerciseEvent(e.Type)
    {
        Time = CsvFormat.Round(e.Time, 3), InExercise = true,
        Index = e.Index >= 0 ? e.Index : -1,
        Value = CsvFormat.Round(e.Value, 3), Value2 = CsvFormat.Round(e.Value2, 3),
        Subject = CsvFormat.Clean(e.Subject), EndReason = e.EndReason, ByInstructor = e.ByInstructor,
        Position = new WorldPosition(CsvFormat.Round(e.Position.East, 2), CsvFormat.Round(e.Position.North, 2)),
        HeadingDeg = (float)CsvFormat.Round(e.HeadingDeg, 2), SpeedKnots = (float)CsvFormat.Round(e.SpeedKnots, 3)
    };

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
    /// <summary>Пороги и веса оценки, с которыми упражнение оценивалось в шлеме (C2): офлайн-оценка по умолчанию берёт их.</summary>
    public AssessmentSettings assessment_settings = new AssessmentSettings();
    /// <summary>Итог оценки в шлеме (C2); подробно — assessment.csv.</summary>
    public SessionAssessmentSummary assessment = new SessionAssessmentSummary();

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
        m.assessment_settings = AssessmentSettings.From(s != null ? s.assessment : null);
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
    /// <summary>Участок к точке — узкость (правило 9; C2).</summary>
    public bool narrow_channel;
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
    /// <summary>Дистанция обнаружения целей, мили; 0 — по видимости и времени суток (C2).</summary>
    public double detection_range_nm;
    /// <summary>Безопасная скорость в ограниченную видимость, уз; 0 — не оценивается (C4).</summary>
    public double safe_speed_kn;
    /// <summary>Цели не показываются на планшете и в «Текущем балле» (C4).</summary>
    public bool hide_targets;
    public SessionInitial initial = new SessionInitial();
    public List<SessionWaypoint> route = new List<SessionWaypoint>();
    /// <summary>Другие суда (B3): описание и рассчитанное начальное состояние (TrafficPlanner).</summary>
    public List<SessionTraffic> traffic = new List<SessionTraffic>();
    /// <summary>Снимок акватории (C3): знаки, мели, суша, фарватеры — для схемы траектории по записи.</summary>
    public SessionArea area = new SessionArea();
    /// <summary>19.2б-3: модель движения сценария (ScenarioMotionModel: AsSelected — как в тренажёре; фактическая — ship.motion_model).</summary>
    public string motion_model = "";
    /// <summary>19.2б-3: задание швартовки (enabled = false — не швартовка; у записей до 19.2б-3 поля нет).</summary>
    public SessionMooringTask mooring = new SessionMooringTask();
    /// <summary>19.3б: ветер сценария, уз (0 — нет), и «откуда», °; изменения в упражнении — события WindChanged.</summary>
    public double wind_kn, wind_from_deg;
    /// <summary>19.3б-2: 0 — направление задано; > 0 — случайное в секторе этой ширины, ° (фактическое — WindChanged «на начало»).</summary>
    public double wind_random_sector_deg;
    /// <summary>19.3б: буксир можно вызвать в упражнении (меню B «Буксир»).</summary>
    public bool tug_available;

    public static SessionScenario From(ScenarioDefinition s)
    {
        var r = new SessionScenario
        {
            id = s.id ?? "", title = s.title ?? "", asset = s.name,
            water_area = s.waterArea != null ? s.waterArea.name : "",
            weather = s.weather.ToString(), time_of_day = s.timeOfDay.ToString(),
            visibility_m = SessionMeta.R(s.visibilityMeters), time_limit_s = SessionMeta.R(s.timeLimitSeconds),
            end_conditions = s.endConditions.ToString(),
            detection_range_nm = SessionMeta.R(s.detectionRangeNm),
            safe_speed_kn = SessionMeta.R(s.safeSpeedKnots), hide_targets = s.hideTargetsOnPlotter,
            area = SessionArea.From(s.waterArea),
            motion_model = s.motionModel.ToString(),
            mooring = SessionMooringTask.From(s),
            wind_kn = SessionMeta.R(s.windKnots), wind_from_deg = SessionMeta.R(s.windFromDeg), wind_random_sector_deg = SessionMeta.R(s.windRandomSectorDeg),
            tug_available = s.tugAvailable
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
                    r.route.Add(new SessionWaypoint { name = w.name, x_e_m = w.position.East, y_n_m = w.position.North, xte_limit_m = SessionMeta.R(w.xteLimit), narrow_channel = w.narrowChannel });
        if (s.traffic != null)
            for (int i = 0; i < s.traffic.Count; i++)
                if (s.traffic[i] != null) r.traffic.Add(SessionTraffic.From(s.traffic[i], i, s.initial));
        return r;
    }

    /// <summary>Маршрут в виде путевых точек (для RouteTracker офлайн, C2).</summary>
    public List<RouteWaypoint> ToRoute()
    {
        var list = new List<RouteWaypoint>();
        foreach (SessionWaypoint w in route)
            list.Add(new RouteWaypoint(w.name, new WorldPosition(w.x_e_m, w.y_n_m), (float)w.xte_limit_m, w.narrow_channel));
        return list;
    }
}

[Serializable]
public class SessionMooringTask
{
    public bool enabled;
    public int berth_index;
    public string berth_name = "";
    public double alongside_gap_m, alongside_angle_deg, stop_gap_m, stop_speed_kn, moored_speed_kn, moored_hold_s, target_time_s;

    public static SessionMooringTask From(ScenarioDefinition s)
    {
        var r = new SessionMooringTask();
        if (s == null || !s.IsMooring) return r;
        MooringTaskDefinition t = s.mooring;
        BerthArea b = t.BerthIn(s.waterArea);
        r.enabled = true;
        r.berth_index = t.berthIndex;
        r.berth_name = b != null ? b.name ?? "" : "";
        r.alongside_gap_m = SessionMeta.R(t.alongsideGapM); r.alongside_angle_deg = SessionMeta.R(t.alongsideAngleDeg);
        r.stop_gap_m = SessionMeta.R(t.stopGapM); r.stop_speed_kn = SessionMeta.R(t.stopSpeedKnots);
        r.moored_speed_kn = SessionMeta.R(t.mooredSpeedKnots); r.moored_hold_s = SessionMeta.R(t.mooredHoldS);
        r.target_time_s = SessionMeta.R(t.targetTimeS);
        return r;
    }

    /// <summary>Задание для оценки и прогонов (null — не швартовка).</summary>
    public MooringTaskDefinition ToDefinition() => !enabled ? null : new MooringTaskDefinition
    {
        enabled = true, berthIndex = berth_index,
        alongsideGapM = (float)alongside_gap_m, alongsideAngleDeg = (float)alongside_angle_deg,
        stopGapM = (float)stop_gap_m, stopSpeedKnots = (float)stop_speed_kn,
        mooredSpeedKnots = (float)moored_speed_kn, mooredHoldS = (float)moored_hold_s, targetTimeS = (float)target_time_s
    };
}

[Serializable]
public class SessionPoint
{
    public double x_e_m, y_n_m;
}

/// <summary>Другое судно в session.json (B3). id — как в targets.csv.</summary>
[Serializable]
public class SessionTraffic
{
    public string id = "", name = "", type = "", parameters = "", placement = "", motion = "", lights = "";
    public double length_m, beam_m, service_speed_kn;
    /// <summary>Начальное состояние (рассчитанное).</summary>
    public double x_e_m, y_n_m, heading_deg, speed_kn;
    /// <summary>Точка встречи: заданные CPA, мили, TCPA, мин, сторона; позиция цели в момент TCPA.</summary>
    public double cpa_nm, tcpa_min;
    public string meeting_side = "";
    public double meeting_x_e_m, meeting_y_n_m;
    /// <summary>Путь после старта (пусто — постоянный курс).</summary>
    public List<SessionPoint> path = new List<SessionPoint>();
    /// <summary>Допустимая дистанция расхождения, мили; 0 — по набору критериев (C2).</summary>
    public double required_cpa_nm;

    public static SessionTraffic From(TrafficShipDefinition d, int index, OwnShipInitialState own)
    {
        TrafficStart st = TrafficPlanner.Resolve(d, own);
        var t = new SessionTraffic
        {
            id = d.TargetId(index), name = d.name ?? "", type = d.type.ToString(),
            parameters = d.parameters != null ? d.parameters.name : "",
            placement = d.placement.ToString(), motion = d.motion.ToString(), lights = d.lights.ToString(),
            length_m = d.parameters != null ? SessionMeta.R(d.parameters.length) : 0.0,
            beam_m = d.parameters != null ? SessionMeta.R(d.parameters.beam) : 0.0,
            service_speed_kn = d.parameters != null ? SessionMeta.R(d.parameters.serviceSpeedKnots) : 0.0,
            x_e_m = SessionMeta.R(st.Position.East), y_n_m = SessionMeta.R(st.Position.North),
            heading_deg = SessionMeta.R(st.HeadingDeg), speed_kn = SessionMeta.R(st.SpeedKnots),
            required_cpa_nm = SessionMeta.R(d.requiredCpaNm)
        };
        if (d.placement == TrafficPlacement.MeetingPoint)
        {
            t.cpa_nm = SessionMeta.R(d.cpaNm); t.tcpa_min = SessionMeta.R(d.tcpaMinutes);
            t.meeting_side = d.meetingSide.ToString();
            t.meeting_x_e_m = SessionMeta.R(st.MeetingPoint.East); t.meeting_y_n_m = SessionMeta.R(st.MeetingPoint.North);
        }
        if (st.Path != null)
            foreach (WorldPosition p in st.Path) t.path.Add(new SessionPoint { x_e_m = SessionMeta.R(p.East), y_n_m = SessionMeta.R(p.North) });
        return t;
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
    /// <summary>Модель движения (запрос 19.1): Course1Dof — Номото/Норрбин, Manoeuvring3Dof — 3 СС.</summary>
    public string motion_model = "";
    /// <summary>19.3б-2: контур корпуса — конец цилиндрической вставки к носу, доля L (MooringHull).</summary>
    public double hull_parallel_l;

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
        beam_m = SessionMeta.R(p.beam), draft_m = SessionMeta.R(p.draft),
        motion_model = p.motionModel.ToString(),
        hull_parallel_l = SessionMeta.R(p.hullParallelForwardL)
    };

    /// <summary>Конец цилиндрической вставки, доля L (у записей до 19.3б-2 поля нет — 0,25, как было).</summary>
    public double HullParallelL => hull_parallel_l > 0 ? hull_parallel_l : MooringHull.ParallelForwardL;
}

[Serializable]
public class SessionRecording
{
    public double track_rate_hz;
    public double fixed_dt_s;
    public string time_base = "t_s — время упражнения (симуляционное) от «Начать», с";
    public string coordinates = "x_e_m — восток, y_n_m — север, м; курс 0 = N, по часовой; руль и XTE > 0 — правый борт / правее линии пути";
    public List<string> files = new List<string> { SessionFormat.TrackFile, SessionFormat.TargetsFile, SessionFormat.EventsFile, SessionFormat.MetaFile, SessionFormat.AssessmentFile, SessionFormat.ReportFile };
    /// <summary>Названия положений телеграфа по номеру (столбец telegraph, index события TelegraphChanged).</summary>
    public List<string> telegraph_positions = new List<string>();
    /// <summary>19.2б-3б: состояния концов по номеру (столбцы *_state в mooring.csv, MooringLineStatus).</summary>
    public List<string> mooring_line_states = new List<string>();
    /// <summary>19.3б: столбцы буксира в mooring.csv — места (tug_station), режимы (tug_mode), состояния троса (towline_state).</summary>
    public List<string> tug_stations = new List<string>(), tug_modes = new List<string>(), towline_states = new List<string>();

    public SessionRecording()
    {
        foreach (TelegraphOrder o in Enum.GetValues(typeof(TelegraphOrder))) telegraph_positions.Add(ScenarioText.Telegraph(o));
        mooring_line_states = MooringRecording.StatusTitles();
        tug_stations = TowingSample.StationTitles();
        tug_modes = TowingSample.ModeTitles();
        towline_states = TowingSample.LineTitles();
    }
}

[Serializable]
public class SessionSummary
{
    public int waypoints_passed, waypoints_total;
    public double max_abs_xte_m, time_outside_corridor_s;
    public int corridor_exits, shoal_warnings, groundings;
    public int collisions;
    public int helm_actions, telegraph_orders, horn_signals, weather_changes;
    public int track_rows, event_rows, target_rows;
    /// <summary>19.2б-3б: строк mooring.csv (0 — файла нет).</summary>
    public int mooring_rows;
    /// <summary>
    /// Режим тренажёра (перед 19.2, п. B; TrainerMode): на начало, наибольший за упражнение,
    /// с какого момента был режим обучения / инструктора, с (−1 — не было), переключений.
    /// Пусто — запись до п. B. Разбор берёт режимы из events.csv (TrainerModeUsage), это поле — для сводок.
    /// </summary>
    public string trainer_mode_start = "", trainer_mode_highest = "";
    public double training_mode_from_s = -1, instructor_mode_from_s = -1;
    public int trainer_mode_changes;
    /// <summary>19.2б-3: сценарий швартовки — выполнено заданий (из 4), время «ошвартован», с (−1 — нет).</summary>
    public int mooring_stages_done;
    public double moored_s = -1;

    public static SessionSummary From(ExerciseSummary s) => new SessionSummary
    {
        trainer_mode_start = s.Modes != null && s.Modes.Recorded ? s.Modes.StartMode.ToString() : "",
        trainer_mode_highest = s.Modes != null && s.Modes.Recorded ? s.Modes.HighestMode.ToString() : "",
        training_mode_from_s = s.Modes != null && s.Modes.UsedTraining ? Math.Round(s.Modes.TrainingFrom, 2) : -1,
        instructor_mode_from_s = s.Modes != null && s.Modes.UsedInstructor ? Math.Round(s.Modes.InstructorFrom, 2) : -1,
        trainer_mode_changes = s.Modes != null ? s.Modes.Changes : 0,
        waypoints_passed = s.WaypointsPassed, waypoints_total = s.WaypointsTotal,
        max_abs_xte_m = Math.Round(s.MaxAbsXte, 2), time_outside_corridor_s = Math.Round(s.TimeOutsideCorridor, 2),
        corridor_exits = s.CorridorExits, shoal_warnings = s.ShoalWarnings, groundings = s.Groundings,
        collisions = s.Collisions,
        helm_actions = s.HelmActions, telegraph_orders = s.TelegraphOrders, horn_signals = s.HornSignals, weather_changes = s.WeatherChanges,
        mooring_stages_done = s.MooringStagesDone, moored_s = double.IsNaN(s.MooredAt) ? -1 : Math.Round(s.MooredAt, 2)
    };
}
