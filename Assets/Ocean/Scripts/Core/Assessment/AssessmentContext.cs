// ============================================================
// AssessmentContext.cs — Исходные данные оценки (C2)
//
// Всё, что оценке нужно кроме событий и траектории, берётся из метаданных
// сессии (session.json): маршрут с XTE доп. и признаком узкости, погода и время
// суток в начале, видимость и дистанция обнаружения сценария, лимит времени,
// параметры своего судна (для рекомендуемого момента перекладки), цели (длина —
// для дальности огней, CPA_доп) и снимок порогов оценки.
// В реальном времени контекст строится из того же SessionMeta, пропущенного
// через JSON (FromScenario) — числа те же, что офлайн прочитаются из файла.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class AssessmentTargetInfo
{
    public string Id = "", Name = "";
    public int Index = -1;
    public double LengthM;
    /// <summary>CPA_доп цели, мили (0 — по умолчанию из настроек).</summary>
    public double RequiredCpaNm;
}

public sealed class AssessmentContext
{
    public AssessmentSettings Settings = AssessmentSettings.Default();
    public string ScenarioId = "", Title = "";
    public readonly List<RouteWaypoint> Route = new List<RouteWaypoint>();
    public WeatherPresetId Weather = WeatherPresetId.Moderate;
    public TimeOfDay TimeOfDay = TimeOfDay.Day;
    /// <summary>Видимость сценария, м (0 — по пресету погоды).</summary>
    public double ScenarioVisibilityM;
    /// <summary>Дистанция обнаружения сценария, мили (0 — по видимости и времени суток).</summary>
    public double DetectionRangeNm;
    public double TimeLimitS;
    /// <summary>Параметры своего судна (null — нет в записи: момент перекладки не оценивается).</summary>
    public ShipParameters Own;
    public readonly List<AssessmentTargetInfo> Targets = new List<AssessmentTargetInfo>();

    public AssessmentTargetInfo Target(string id)
    {
        foreach (AssessmentTargetInfo t in Targets) if (t.Id == id) return t;
        return null;
    }

    /// <summary>CPA_доп цели, м.</summary>
    public double RequiredCpaM(AssessmentTargetInfo t) =>
        (t != null && t.RequiredCpaNm > 0.0 ? t.RequiredCpaNm : Settings.defaultRequiredCpaNm) * NavigationLights.MetersPerNm;

    public static AssessmentContext From(SessionMeta m)
    {
        var c = new AssessmentContext();
        if (m == null) return c;
        if (m.assessment_settings != null) c.Settings = m.assessment_settings;
        SessionScenario s = m.scenario ?? new SessionScenario();
        c.ScenarioId = s.id ?? "";
        c.Title = s.title ?? "";
        c.Route.AddRange(s.ToRoute());
        if (Enum.TryParse(s.weather, out WeatherPresetId w)) c.Weather = w;
        if (Enum.TryParse(s.time_of_day, out TimeOfDay t)) c.TimeOfDay = t;
        c.ScenarioVisibilityM = s.visibility_m;
        c.DetectionRangeNm = s.detection_range_nm;
        c.TimeLimitS = s.time_limit_s;
        if (s.traffic != null)
            for (int i = 0; i < s.traffic.Count; i++)
            {
                SessionTraffic x = s.traffic[i];
                if (x == null) continue;
                c.Targets.Add(new AssessmentTargetInfo { Id = x.id ?? "", Name = x.name ?? "", Index = i, LengthM = x.length_m, RequiredCpaNm = x.required_cpa_nm });
            }
        c.Own = ShipFrom(m.ship);
        return c;
    }

    /// <summary>Контекст в реальном времени: те же метаданные, что запишутся в session.json, через JSON.</summary>
    public static AssessmentContext FromScenario(ScenarioDefinition s, ShipParameters p)
    {
        SessionMeta meta = SessionMeta.Create(s, p, "", new DateTime(2000, 1, 1));
        var copy = new SessionMeta();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(meta), copy);
        return From(copy);
    }

    private static ShipParameters ShipFrom(SessionShip s)
    {
        if (s == null || s.length_m <= 0.0) return null;
        ShipParameters p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = s.asset ?? "";
        p.length = (float)s.length_m;
        p.serviceSpeedKnots = (float)s.service_speed_kn;
        p.accelerationTimeConstant = (float)s.tau_accel_s;
        p.decelerationTimeConstant = (float)s.tau_decel_s;
        p.asternFactor = (float)s.astern_factor;
        if (Enum.TryParse(s.yaw_model, out YawModel ym)) p.yawModel = ym;
        p.nomotoKPrime = (float)s.k_prime;
        p.nomotoTPrime = (float)s.t_prime;
        p.norrbinAlphaPrime = (float)s.norrbin_alpha_prime;
        p.turnSpeedLossCoeff = (float)s.turn_speed_loss_coeff;
        p.minCalcSpeed = (float)s.min_calc_speed_ms;
        p.maxRudderAngle = (float)s.max_rudder_deg;
        p.rudderRate = (float)s.rudder_rate_deg_s;
        p.beam = (float)s.beam_m;
        p.draft = (float)s.draft_m;
        return p;
    }
}
