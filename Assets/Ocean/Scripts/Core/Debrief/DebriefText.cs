// ============================================================
// DebriefText.cs — Тексты разбора упражнения (C3)
//
// Общие для разбора в шлеме (ScenarioPanel) и отчёта report.html:
//   • Violations — нарушения и замечания на русском: что сделано не так,
//     правило МППСС, что измерено («Поворот влево при сближении на встречных
//     курсах — T1, правило 14 а: первая перекладка влево»). Сначала аварии,
//     затем обязательные критерии, затем остальные (замечания);
//   • Rows / Page — таблица критериев «значение / порог / результат»
//     и её разбиение на страницы панели;
//   • EncounterLine — сближение одной строкой.
// Текст нарушения задаётся для каждого кода каталога (AssessmentCatalog):
// новый критерий без текста ловит тест DebriefTests.
// ============================================================
using System;
using System.Collections.Generic;

public enum CriterionState { Passed, Failed, FailedMandatory, NotEvaluated }

/// <summary>Строка таблицы критериев.</summary>
public sealed class CriterionRow
{
    public string Code = "", Title = "", Subject = "", Rule = "";
    public string Value = "", Threshold = "", Note = "";
    public double Score = double.NaN;
    public CriterionState State;

    /// <summary>«+» — выполнен, «×» — не выполнен, «–» — не оценивался.</summary>
    public string Mark => State == CriterionState.Passed ? "+" : State == CriterionState.NotEvaluated ? "–" : "×";
    public string TitleWithSubject => string.IsNullOrEmpty(Subject) ? Title : $"{Title} ({Subject})";
}

/// <summary>Нарушение или замечание.</summary>
public sealed class DebriefViolation
{
    public string Code = "", Subject = "";
    /// <summary>Авария (провал) или обязательный критерий.</summary>
    public bool Severe;
    public string Text = "";
}

public static class DebriefText
{
    // Что сделано не так — по коду критерия (текст для невыполненного критерия)
    private static readonly Dictionary<string, string> Failure = new Dictionary<string, string>
    {
        { AssessmentCatalog.E1, "Манёвр уступающего судна недостаточен или его не было" },
        { AssessmentCatalog.E2, "Манёвр для расхождения выполнен поздно" },
        { AssessmentCatalog.E3, "Поворот влево при сближении на встречных курсах" },
        { AssessmentCatalog.E4, "Пересечение курса по носу судна, находящегося справа" },
        { AssessmentCatalog.E5, "Поворот влево на судно, находящееся справа" },
        { AssessmentCatalog.E6, "Привилегированное судно изменило курс или скорость раньше времени" },
        { AssessmentCatalog.E7, "Поворот влево на судно, находящееся слева (своё судно — привилегированное)" },
        { AssessmentCatalog.E8, "Расхождение на дистанции меньше допустимой" },
        { AssessmentCatalog.E9, "Запоздалая реакция на опасность столкновения" },
        { AssessmentCatalog.R1, "Большое боковое отклонение от линии пути" },
        { AssessmentCatalog.R2, "Большое среднеквадратичное отклонение от линии пути" },
        { AssessmentCatalog.R3, "Долгое нахождение вне коридора" },
        { AssessmentCatalog.R4, "Пройдены не все путевые точки" },
        { AssessmentCatalog.R5, "Не уложился в лимит времени" },
        { AssessmentCatalog.R6, "Большой вынос на повороте" },
        { AssessmentCatalog.R7, "Перекладка руля на поворот не вовремя" },
        { AssessmentCatalog.R8a, "В узкости судно держалось левее оси" },
        { AssessmentCatalog.R8b, "Расхождение в узкости не у правой стороны" },
        { AssessmentCatalog.F1, "Пропуск туманного сигнала: промежуток между сигналами больше 2 минут" },
        { AssessmentCatalog.F2, "Туманный сигнал не соответствует ходу судна" },
        { AssessmentCatalog.F3, "Средняя скорость в ограниченную видимость выше безопасной" },
        { AssessmentCatalog.F4, "Скорость в ограниченную видимость превышала безопасную" },
        { AssessmentCatalog.F5, "Услышав туманный сигнал судна впереди траверза, ход не снижен до минимального" },
        { AssessmentCatalog.F6, "Поворот на невидимое судно в ограниченную видимость" },
        { AssessmentCatalog.N1, "Запоздалая реакция на отказ рулевого устройства (ход не снижен)" },
        { AssessmentCatalog.N2, "Запоздалая реакция на отказ главного двигателя (не дан «Стоп»)" },
        { AssessmentCatalog.A1, "Посадка на мель" },
        { AssessmentCatalog.A2, "Столкновение" }
    };

    public static bool HasFailureText(string code) => Failure.ContainsKey(code);

    public static string FailureText(string code) => Failure.TryGetValue(code, out string t) ? t : "Критерий не выполнен";

    public static CriterionState StateOf(CriterionResult c) =>
        !c.Evaluated ? CriterionState.NotEvaluated : c.Passed ? CriterionState.Passed :
        c.Mandatory ? CriterionState.FailedMandatory : CriterionState.Failed;

    /// <summary>Нарушение одной строкой: «Текст — объект, правило: пояснение».</summary>
    public static string ViolationText(CriterionResult c)
    {
        // Авария: пояснение уже говорит всё («столкновение с «T2»»)
        if (c.Info.Group == AssessmentGroup.Accidents)
            return string.IsNullOrEmpty(c.Note) ? FailureText(c.Code) : char.ToUpperInvariant(c.Note[0]) + c.Note.Substring(1);
        var b = new System.Text.StringBuilder(FailureText(c.Code));
        string rule = c.Info.Rule ?? "";
        bool isRule = rule.StartsWith("правил", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(c.Subject) || isRule)
        {
            b.Append(" —");
            if (!string.IsNullOrEmpty(c.Subject)) b.Append(' ').Append(c.Subject);
            if (isRule) b.Append(string.IsNullOrEmpty(c.Subject) ? " " : ", ").Append(rule);
        }
        if (!string.IsNullOrEmpty(c.Note)) b.Append(": ").Append(c.Note);
        if (c.Info.Group != AssessmentGroup.Accidents && !c.Mandatory) b.Append(" (замечание)");
        return b.ToString();
    }

    /// <summary>Нарушения и замечания: аварии, обязательные, остальные (в порядке критериев).</summary>
    public static List<DebriefViolation> Violations(AssessmentResult r)
    {
        var severe = new List<DebriefViolation>();
        var minor = new List<DebriefViolation>();
        if (r == null) return severe;
        foreach (CriterionResult c in r.Items)
        {
            if (!c.Evaluated || c.Passed) continue;
            bool isSevere = c.Mandatory || c.Info.Group == AssessmentGroup.Accidents;
            var v = new DebriefViolation { Code = c.Code, Subject = c.Subject, Severe = isSevere, Text = ViolationText(c) };
            if (c.Info.Group == AssessmentGroup.Accidents) severe.Insert(0, v);
            else (isSevere ? severe : minor).Add(v);
        }
        severe.AddRange(minor);
        return severe;
    }

    // ─────────── Таблица критериев ───────────
    public static List<CriterionRow> Rows(AssessmentResult r)
    {
        var list = new List<CriterionRow>();
        if (r == null) return list;
        foreach (CriterionResult c in r.Items)
            list.Add(new CriterionRow
            {
                Code = c.Code, Title = c.Info.Title, Subject = c.Subject ?? "", Rule = c.Info.Rule ?? "",
                Value = AssessmentResult.ValueText(c), Threshold = ThresholdText(c), Note = c.Note ?? "",
                Score = c.Evaluated ? c.Score : double.NaN, State = StateOf(c)
            });
        return list;
    }

    /// <summary>Порог зачёта: «≥ 2,0 мин», «≤ 60 с»; для «да/нет» — «да», для аварий — «0».</summary>
    public static string ThresholdText(CriterionResult c)
    {
        string p = AssessmentResult.PassText(c);
        if (p.Length > 0) return p;
        return c.Info.Unit.StartsWith("1 —", StringComparison.Ordinal) ? "да" : "";
    }

    public static int PageCount(int rows, int perPage) => perPage <= 0 ? 1 : Math.Max(1, (rows + perPage - 1) / perPage);

    /// <summary>Строки страницы page (с 0; выход за границы — ближайшая страница).</summary>
    public static List<T> Page<T>(IList<T> rows, int page, int perPage)
    {
        var list = new List<T>();
        if (rows == null || rows.Count == 0) return list;
        int pages = PageCount(rows.Count, perPage);
        page = Math.Max(0, Math.Min(page, pages - 1));
        for (int i = page * perPage; i < Math.Min(rows.Count, (page + 1) * perPage); i++) list.Add(rows[i]);
        return list;
    }

    // ─────────── Сближения ───────────
    /// <summary>«T2: пересечение курсов, цель справа — манёвр через 30 с (руль 35° ПБ); наименьшая дистанция 1106 м при допустимой 926 м».</summary>
    public static string EncounterLine(EncounterSummary e)
    {
        var b = new System.Text.StringBuilder();
        b.Append(e.TargetId).Append(": ").Append(EncounterClassifier.Title(e.Situation));
        if (!double.IsNaN(e.ActionTime) && !string.IsNullOrEmpty(e.Action))
            b.Append(" — манёвр через ").Append(ScenarioText.Num(Math.Max(0, e.ActionTime - e.OnsetTime))).Append(" с (").Append(e.Action).Append(')');
        else if (e.Complete && (e.Situation == EncounterSituation.HeadOn || e.Situation == EncounterSituation.CrossingGiveWay
                                 || e.Situation == EncounterSituation.RestrictedVisibility))
            b.Append(" — манёвра не было");
        if (e.Collided) b.Append("; столкновение");
        else if (e.Complete && !double.IsNaN(e.MinDistanceM))
            b.Append("; наименьшая дистанция ").Append(ScenarioText.Num(e.MinDistanceM)).Append(" м при допустимой ").Append(ScenarioText.Num(e.RequiredCpaM)).Append(" м");
        else if (!e.Complete) b.Append("; сближение не завершено");
        return b.ToString();
    }

    /// <summary>Вердикт: «ЗАЧЁТ», «НЕЗАЧЁТ», «ПРОВАЛ».</summary>
    public static string Verdict(AssessmentResult r) => r == null ? "—" : r.Failed ? "ПРОВАЛ" : r.Passed ? "ЗАЧЁТ" : "НЕЗАЧЁТ";

    /// <summary>Почему незачёт / провал (пусто — зачёт).</summary>
    public static string VerdictReason(AssessmentResult r)
    {
        if (r == null || r.Passed) return "";
        if (r.Failed) return "провал: " + r.FailureText;
        if (r.MandatoryFailed.Count > 0) return "не выполнены обязательные критерии: " + string.Join(", ", r.MandatoryFailed);
        if (!double.IsNaN(r.Score)) return $"балл ниже проходного ({ScenarioText.Num(r.PassScore)})";
        return "нечего оценивать";
    }

    /// <summary>Баллы групп одной строкой: «расхождение с судами 96 · время реакции 100 · маршрут 89».</summary>
    public static string GroupsLine(AssessmentResult r)
    {
        var parts = new List<string>();
        if (r != null)
            foreach (AssessmentGroup g in AssessmentCatalog.ScoredGroups)
                if (r.GroupScores.TryGetValue(g, out double gs)) parts.Add($"{AssessmentCatalog.GroupTitle(g).ToLowerInvariant()} {ScenarioText.Num(gs)}");
        return string.Join(" · ", parts);
    }
}
