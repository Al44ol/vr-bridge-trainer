// ============================================================
// ScenarioText.cs — Русские названия и форматирование для сценариев (B2)
// (брифинг, экран результатов, консоль; позже — отчёт C3)
// ============================================================
using System;

public static class ScenarioText
{
    /// <summary>Положение телеграфа SF (−1…1) — как в EngineTelegraph по умолчанию.</summary>
    public static double SpeedFactor(TelegraphOrder o)
    {
        switch (o)
        {
            case TelegraphOrder.FullAhead: return 1.0;
            case TelegraphOrder.HalfAhead: return 0.65;
            case TelegraphOrder.SlowAhead: return 0.4;
            case TelegraphOrder.DeadSlowAhead: return 0.2;
            case TelegraphOrder.DeadSlowAstern: return -0.2;
            case TelegraphOrder.SlowAstern: return -0.4;
            case TelegraphOrder.HalfAstern: return -0.65;
            case TelegraphOrder.FullAstern: return -1.0;
            default: return 0.0;
        }
    }

    public static string Telegraph(TelegraphOrder o)
    {
        switch (o)
        {
            case TelegraphOrder.FullAhead: return "Полный вперёд";
            case TelegraphOrder.HalfAhead: return "Средний вперёд";
            case TelegraphOrder.SlowAhead: return "Малый вперёд";
            case TelegraphOrder.DeadSlowAhead: return "Самый малый вперёд";
            case TelegraphOrder.DeadSlowAstern: return "Самый малый назад";
            case TelegraphOrder.SlowAstern: return "Малый назад";
            case TelegraphOrder.HalfAstern: return "Средний назад";
            case TelegraphOrder.FullAstern: return "Полный назад";
            default: return "Стоп";
        }
    }

    public static string Weather(WeatherPresetId w)
    {
        switch (w)
        {
            case WeatherPresetId.Calm: return "штиль, видимость ≈ 5,8 км";
            case WeatherPresetId.Storm: return "шторм, видимость ≈ 1,2 км";
            case WeatherPresetId.Night: return "слабое волнение, видимость ≈ 11,5 км (6,2 мили)";   // B4: темноту даёт время суток
            default: return "умеренное волнение, видимость ≈ 4,3 км";
        }
    }

    /// <summary>Короткое название пресета погоды (события, журнал).</summary>
    public static string WeatherTitle(WeatherPresetId w)
    {
        switch (w)
        {
            case WeatherPresetId.Calm: return "штиль";
            case WeatherPresetId.Storm: return "шторм";
            case WeatherPresetId.Night: return "ясно, видимость 6,2 мили";
            default: return "умеренное волнение";
        }
    }

    /// <summary>Положение телеграфа по SF — ближайшее из TelegraphOrder (C1: столбец telegraph).</summary>
    public static TelegraphOrder OrderFromSpeedFactor(double sf)
    {
        TelegraphOrder best = TelegraphOrder.Stop;
        double bestDiff = double.MaxValue;
        foreach (TelegraphOrder o in (TelegraphOrder[])Enum.GetValues(typeof(TelegraphOrder)))
        {
            double d = Math.Abs(SpeedFactor(o) - sf);
            if (d < bestDiff) { bestDiff = d; best = o; }
        }
        return best;
    }

    /// <summary>Угол руля словами: «10,0° ПБ», «5,5° ЛБ», «0°».</summary>
    public static string Rudder(double deg)
    {
        if (double.IsNaN(deg)) return "—";
        if (Math.Abs(deg) < 0.05) return "0°";
        return $"{Num(Math.Abs(deg), 1)}° {(deg > 0 ? "ПБ" : "ЛБ")}";
    }

    /// <summary>Условия сценария (B4): «ночь · слабое волнение, видимость …» — время суток и погода раздельно.</summary>
    public static string Conditions(WeatherPresetId w, TimeOfDay t)
    {
        string tod = TimeOfDayTitle(t);
        return char.ToUpper(tod[0]) + tod.Substring(1) + " · " + Weather(w);
    }

    public static string TimeOfDayTitle(TimeOfDay t)
    {
        switch (t)
        {
            case TimeOfDay.Twilight: return "сумерки";
            case TimeOfDay.Night: return "ночь";
            default: return "день";
        }
    }

    public static string EndReason(ScenarioEndReason r)
    {
        switch (r)
        {
            case ScenarioEndReason.Arrived: return "прибытие в конечную точку";
            case ScenarioEndReason.Grounding: return "посадка на мель";
            case ScenarioEndReason.Collision: return "столкновение";
            case ScenarioEndReason.TimeLimit: return "истекло время";
            case ScenarioEndReason.Aborted: return "завершено досрочно";
            default: return "—";
        }
    }

    // ─────────── Другие суда (B3) ───────────
    public static string VesselType(TrafficVesselType t)
    {
        switch (t)
        {
            case TrafficVesselType.Tanker: return "танкер";
            case TrafficVesselType.Small: return "малое судно";
            default: return "сухогруз";
        }
    }

    public static string Side(MeetingSide s)
    {
        switch (s)
        {
            case MeetingSide.Port: return "по левому борту";
            case MeetingSide.Ahead: return "по носу";
            case MeetingSide.Astern: return "по корме";
            default: return "по правому борту";
        }
    }

    /// <summary>Курсовой угол словами: «35° ПБ», «120° ЛБ», «прямо по носу», «прямо по корме».</summary>
    public static string RelativeBearing(double q)
    {
        q = (q % 360.0 + 360.0) % 360.0;
        if (q < 0.5 || q > 359.5) return "прямо по носу";
        if (Math.Abs(q - 180.0) < 0.5) return "прямо по корме";
        return q < 180.0 ? $"{Num(q)}° ПБ" : $"{Num(360.0 - q)}° ЛБ";
    }

    /// <summary>Время «мм:сс» (или «ч:мм:сс»).</summary>
    public static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Число с десятичной запятой (для текста на русском).</summary>
    public static string Num(double v, int digits = 0) =>
        v.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',').Replace('-', '−');

    /// <summary>Перечень условий завершения.</summary>
    public static string EndConditions(ScenarioDefinition s)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (s.Ends(ScenarioEndConditions.Arrival)) parts.Add("прибытие в последнюю точку");
        if (s.Ends(ScenarioEndConditions.Grounding)) parts.Add("посадка на мель");
        if (s.Ends(ScenarioEndConditions.Collision)) parts.Add("столкновение");
        if (s.Ends(ScenarioEndConditions.TimeLimit) && s.timeLimitSeconds > 0) parts.Add("истечение времени");
        return parts.Count > 0 ? string.Join(", ", parts) : "только по команде инструктора";
    }
}
