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
            case WeatherPresetId.Night: return "ночь, видимость ≈ 4,3 км";
            default: return "умеренное волнение, видимость ≈ 4,3 км";
        }
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
