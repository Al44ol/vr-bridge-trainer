// ============================================================
// ScenarioChecks.cs — Проверка сценария в редакторе и заготовка брифинга (запрос 21.4б)
//
// Check — замечания ScenarioDefinition.Validate() и редактора (RouteEditing.Check, 21.5а — TrafficEditing.Check), разделённые на:
//   • ошибки (×) — с ними упражнение не запустить («Пройти в тренажёре» не работает): нет акватории,
//     в маршруте меньше двух точек, точка или старт вне района, старт на мели, XTE ≤ 0, точки совпадают,
//     нет идентификатора или он занят встроенным сценарием;
//   • предупреждения (!) — остальное (пустой брифинг, ветер без 3 СС, участок над мелью и т. д.).
// Ошибки узнаются по тексту замечаний Validate (ErrorStarts, PointErrorMarks): Validate — общий для ассетов, файлов и
// редактора и возвращает один список; новые блокирующие замечания — дописать сюда.
// Briefing — текст брифинга по сценарию (судно, старт, маршрут, условия, лимит, завершение, обязательные
// критерии); инструктор правит его дальше вручную.
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;

public static class ScenarioChecks
{
    /// <summary>Начала замечаний Validate, с которыми упражнение не запускается (своё судно, маршрут, акватория).</summary>
    public static readonly string[] ErrorStarts =
    {
        "Не задан идентификатор", "Не назначена акватория", "В маршруте меньше двух", "Путевая точка",
        "Не заданы начальные условия", "Начальная позиция"
    };
    /// <summary>У замечаний о точке маршрута («Точка 2 «WP3»: …») — части текста, с которыми не запускается.</summary>
    public static readonly string[] PointErrorMarks = { "XTE должно быть больше 0", "совпадают", "вне района акватории" };

    public static bool IsError(string problem)
    {
        foreach (string m in ErrorStarts) if (problem.StartsWith(m, StringComparison.Ordinal)) return true;
        if (problem.StartsWith("Точк", StringComparison.Ordinal))
            foreach (string m in PointErrorMarks) if (problem.IndexOf(m, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    public static AreaCheckResult Check(ScenarioDefinition s, ScenarioCatalog catalog)
    {
        var r = new AreaCheckResult();
        if (s == null) { r.Errors.Add("Сценарий не открыт."); return r; }
        foreach (string p in s.Validate()) (IsError(p) ? r.Errors : r.Warnings).Add(p);
        string id = (s.id ?? "").Trim();
        if (id.Length > 0 && catalog != null && catalog.Scenarios.Exists(x => x != null && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase)))
            r.Errors.Add($"Идентификатор «{id}» — у встроенного сценария; задайте другой.");
        r.Warnings.AddRange(RouteEditing.Check(s));
        r.Warnings.AddRange(TrafficEditing.Check(s));   // 21.5а
        return r;
    }

    // ─────────── Заготовка брифинга ───────────
    public static string Briefing(ScenarioDefinition s)
    {
        var b = new StringBuilder();
        ShipParameters p = s.shipParameters;
        OwnShipInitialState st = s.initial ?? new OwnShipInitialState();
        string ship = s.ownShipType == ScenarioShipType.Tug ? "свой буксир" : "судно (модель 45 м)";
        if (p != null) ship += $", L {ScenarioText.Num(p.length, 0)} м, осадка {ScenarioText.Num(p.draft, 1)} м";
        b.Append($"Своё {ship}. Начальное положение: E {ScenarioText.Num(st.position.East, 0)} м, N {ScenarioText.Num(st.position.North, 0)} м; " +
                 $"курс {ScenarioText.Num(st.headingDeg, 0)}°, {ScenarioText.Num(st.speedKnots, 1)} уз, телеграф «{ScenarioText.Telegraph(st.telegraph)}»");
        if (Math.Abs(st.rudderDeg) > 0.05f) b.Append($", руль {ScenarioText.Rudder(st.rudderDeg)}");
        b.Append(".\n\n");

        List<RouteWaypoint> r = s.route ?? new List<RouteWaypoint>();
        if (r.Count >= 2)
        {
            b.Append($"Задача: пройти по путевым точкам ({r.Count}, всего {ScenarioText.Num(s.RouteLength / 1852.0, 1)} мили):\n");
            for (int i = 0; i + 1 < r.Count; i++)
            {
                if (r[i] == null || r[i + 1] == null) continue;
                b.Append($"  {i + 1}. {r[i].name} → {r[i + 1].name}: курс {ScenarioText.Num(RouteEditing.LegCourse(r, i), 0)}°, " +
                         $"{ScenarioText.Num(RouteEditing.LegLength(r, i) / 1852.0, 2)} мили, допустимое отклонение {ScenarioText.Num(r[i + 1].xteLimit, 0)} м" +
                         (r[i + 1].narrowChannel ? " (узкость — держаться правой стороны, правило 9)" : "") + ";\n");
            }
            b.Append('\n');
        }
        else b.Append("Задача: маршрут не задан.\n\n");

        b.Append("Условия: " + ScenarioText.Conditions(s.weather, s.timeOfDay));
        if (s.visibilityMeters > 0f) b.Append($"; видимость {ScenarioText.Num(s.visibilityMeters / 1852.0, 1)} мили");
        if (s.windKnots > 0f)
        {
            b.Append($"; ветер {ScenarioText.Num(s.windKnots, 0)} уз ");
            b.Append(s.windRandomSectorDeg >= 360f ? "(направление случайное)"
                   : s.windRandomSectorDeg > 0f ? $"около {ScenarioText.Num(s.windFromDeg, 0)}° (± {ScenarioText.Num(0.5 * s.windRandomSectorDeg, 0)}°)"
                   : $"с {ScenarioText.Num(s.windFromDeg, 0)}°");
        }
        b.Append(".\n");
        if (s.safeSpeedKnots > 0f) b.Append($"Безопасная скорость в ограниченную видимость — не более {ScenarioText.Num(s.safeSpeedKnots, 1)} уз.\n");
        if (s.traffic != null && s.traffic.Count > 0) b.Append($"В районе другие суда ({s.traffic.Count}) — расходиться по МППСС-72.\n");
        if (s.IsMooring) b.Append("Швартовка к причалу — по заданию сценария.\n");
        if (s.tugAvailable) b.Append("Буксир можно вызвать из меню B («Буксир»).\n");
        b.Append(s.timeLimitSeconds > 0f ? $"Лимит времени — {ScenarioText.Num(s.timeLimitSeconds / 60.0, 0)} мин. " : "Лимита времени нет. ");
        b.Append($"Упражнение завершается: {ScenarioText.EndConditions(s)}.");

        AssessmentSettings a = s.EffectiveAssessment();
        var man = new List<string>();
        foreach (CriterionInfo c in AssessmentCatalog.All)
        {
            CriterionSetting x = a.Get(c.Code);
            if (x.enabled && x.mandatory) man.Add($"{c.Code} — {c.Title.ToLowerInvariant()}");
        }
        if (man.Count > 0) b.Append("\n\nОбязательно (без этого — незачёт): " + string.Join("; ", man) + ".");
        return b.ToString();
    }
}
