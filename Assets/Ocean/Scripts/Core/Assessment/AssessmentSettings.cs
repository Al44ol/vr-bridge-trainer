// ============================================================
// AssessmentSettings.cs — Перечень критериев оценки, пороги и веса (C2)
//
// Каталог (AssessmentCatalog) — что измеряется: код, группа, название, правило
// МППСС, единица, направление («больше — лучше» или наоборот). Это часть
// методики и задаётся в коде.
// Настройки (AssessmentSettings) — числа: включён ли критерий, обязательный ли он,
// три порога и вес. Хранятся в ассете AssessmentCriteria и снимком — в session.json
// (офлайн-оценка по умолчанию берёт пороги, с которыми упражнение оценивалось
// в шлеме). ВСЕ значения по умолчанию — ПРЕДВАРИТЕЛЬНЫЕ, обосновываются
// в главе 2 ВКР (список — ThesisNotes, раздел 10).
//
// Балл критерия по значению v (пороги «полный балл» v₁, «зачёт» v_з, «ноль» v₀):
//     s = 1 при v не хуже v₁;  s = 0 при v не лучше v₀;  между — линейно;
//     зачёт ⇔ v не хуже v_з.
// Обязательный критерий: незачёт → незачёт упражнения независимо от балла.
// Итог: балл = Σ_g W_g·S_g / Σ_g W_g (только группы, где что-то оценено),
//       S_g = Σ w_c·s_c / Σ w_c (критерий по нескольким целям — среднее);
//       зачёт = нет аварии и все обязательные выполнены и балл ≥ проходного.
// ============================================================
using System;
using System.Collections.Generic;

public enum AssessmentGroup
{
    Encounters,   // расхождение с судами (МППСС, правила 8, 9, 13–17)
    Reaction,     // время реакции
    Route,        // маршрут
    Accidents     // аварии — провал, в балл не входят
}

/// <summary>Описание критерия (методика, задаётся в коде).</summary>
public sealed class CriterionInfo
{
    public string Code, Title, Rule, Unit, Basis;
    public AssessmentGroup Group;
    public bool HigherIsBetter;
    /// <summary>Значения по умолчанию (предварительные).</summary>
    public bool Mandatory;
    public double Full, Pass, Zero, Weight;
}

/// <summary>Числа одного критерия (ассет, session.json).</summary>
[Serializable]
public class CriterionSetting
{
    public string code = "";
    public bool enabled = true;
    public bool mandatory;
    /// <summary>Порог полного балла, зачёта и нулевого балла (в единицах критерия).</summary>
    public double full, pass, zero;
    public double weight = 1;

    public CriterionSetting Clone() => (CriterionSetting)MemberwiseClone();
}

/// <summary>Параметры оценки (ассет AssessmentCriteria, снимок — session.json).</summary>
[Serializable]
public class AssessmentSettings
{
    [UnityEngine.Tooltip("Допустимая дистанция расхождения CPA_доп по умолчанию, мили (у цели можно задать свою)")]
    public double defaultRequiredCpaNm = 0.5;
    [UnityEngine.Tooltip("Опасность столкновения: CPA < k·CPA_доп (правило 7)")]
    public double riskCpaFactor = 2.0;
    [UnityEngine.Tooltip("Горизонт опасности: 0 < TCPA ≤ T_гор, мин")]
    public double riskHorizonMin = 20.0;
    [UnityEngine.Tooltip("Встречные курсы: оба судна видят друг друга в пределах ±α от носа, °")]
    public double headOnSectorDeg = 6.0;
    [UnityEngine.Tooltip("Цель изменила курс больше чем на … ° — ситуация определяется заново (цель идёт по фарватеру)")]
    public double targetTurnDeg = 10.0;
    [UnityEngine.Tooltip("Окно расчёта скорости изменения пеленга, с")]
    public double bearingWindowS = 60.0;
    [UnityEngine.Tooltip("Пеленг «заметно не меняется» (правило 7 г): |dП/dt| < ε, °/мин")]
    public double steadyBearingDegPerMin = 1.0;
    [UnityEngine.Tooltip("Управляющее действие для расхождения: перекладка руля не меньше, °")]
    public double actionRudderDeg = 5.0;
    [UnityEngine.Tooltip("Манёвр уступающего достаточен (E1): изменение курса не меньше, °")]
    public double actionCourseDeg = 30.0;
    [UnityEngine.Tooltip("… или снижение скорости не меньше, доля")]
    public double actionSpeedReduction = 0.5;
    [UnityEngine.Tooltip("Значимое изменение курса (направление манёвра), °")]
    public double significantTurnDeg = 5.0;
    [UnityEngine.Tooltip("Привилегированное судно свободно в действиях (правило 17 а ii), когда TCPA ≤ …, мин")]
    public double standOnFreeTcpaMin = 3.0;
    [UnityEngine.Tooltip("… или дистанция ≤ …, мили")]
    public double standOnFreeRangeNm = 1.0;
    [UnityEngine.Tooltip("Привилегированное: допустимое изменение курса относительно маршрута, °")]
    public double standOnHeadingTolDeg = 10.0;
    [UnityEngine.Tooltip("Привилегированное: допустимое снижение скорости, доля")]
    public double standOnSpeedTol = 0.2;
    [UnityEngine.Tooltip("Маршрутные критерии R1–R3, R8a не учитывают манёвр расхождения: от первого действия до возврата в коридор, но не дольше … мин после окончания сближения")]
    public double routeReturnAllowanceMin = 10.0;
    [UnityEngine.Tooltip("Руль для расчёта рекомендуемого момента перекладки на повороте маршрута, °")]
    public double wheelOverRudderDeg = 20.0;
    [UnityEngine.Tooltip("Проходной балл (0–100)")]
    public double passScore = 70.0;
    [UnityEngine.Tooltip("Веса групп: расхождение, время реакции, маршрут")]
    public double weightEncounters = 0.6, weightReaction = 0.1, weightRoute = 0.3;
    public List<CriterionSetting> criteria = new List<CriterionSetting>();

    public AssessmentSettings() { }

    /// <summary>Значения по умолчанию (предварительные) со всеми критериями каталога.</summary>
    public static AssessmentSettings Default()
    {
        var s = new AssessmentSettings();
        foreach (CriterionInfo c in AssessmentCatalog.All) s.criteria.Add(AssessmentCatalog.DefaultSetting(c));
        return s;
    }

    /// <summary>Снимок настроек ассета (null — по умолчанию).</summary>
    public static AssessmentSettings From(AssessmentCriteria asset) =>
        asset != null && asset.settings != null ? asset.settings.Clone() : Default();

    public AssessmentSettings Clone()
    {
        var c = (AssessmentSettings)MemberwiseClone();
        c.criteria = new List<CriterionSetting>();
        if (criteria != null)
            foreach (CriterionSetting x in criteria)
                if (x != null) c.criteria.Add(x.Clone());
        return c;
    }

    /// <summary>Настройка критерия; если в ассете его нет (добавлен позже) — по умолчанию.</summary>
    public CriterionSetting Get(string code)
    {
        if (criteria != null)
            foreach (CriterionSetting x in criteria)
                if (x != null && x.code == code) return x;
        CriterionInfo info = AssessmentCatalog.Find(code);
        return info != null ? AssessmentCatalog.DefaultSetting(info) : new CriterionSetting { code = code, enabled = false };
    }

    public double GroupWeight(AssessmentGroup g) =>
        g == AssessmentGroup.Encounters ? weightEncounters : g == AssessmentGroup.Reaction ? weightReaction :
        g == AssessmentGroup.Route ? weightRoute : 0.0;

    /// <summary>Добавить недостающие критерии каталога (после обновления методики).</summary>
    public int AddMissing()
    {
        if (criteria == null) criteria = new List<CriterionSetting>();
        int n = 0;
        foreach (CriterionInfo c in AssessmentCatalog.All)
            if (!criteria.Exists(x => x != null && x.code == c.Code)) { criteria.Add(AssessmentCatalog.DefaultSetting(c)); n++; }
        return n;
    }
}

/// <summary>Перечень критериев (C2). Пороги — ПРЕДВАРИТЕЛЬНЫЕ.</summary>
public static class AssessmentCatalog
{
    public const string E1 = "E1", E2 = "E2", E3 = "E3", E4 = "E4", E5 = "E5", E6 = "E6", E7 = "E7", E8 = "E8", E9 = "E9";
    public const string R1 = "R1", R2 = "R2", R3 = "R3", R4 = "R4", R5 = "R5", R6 = "R6", R7 = "R7", R8a = "R8a", R8b = "R8b";
    public const string A1 = "A1", A2 = "A2";

    private static CriterionInfo C(string code, AssessmentGroup g, string title, string rule, string unit, bool higher,
                                   bool mandatory, double full, double pass, double zero, double weight, string basis) =>
        new CriterionInfo
        {
            Code = code, Group = g, Title = title, Rule = rule, Unit = unit, HigherIsBetter = higher,
            Mandatory = mandatory, Full = full, Pass = pass, Zero = zero, Weight = weight, Basis = basis
        };

    public static readonly IReadOnlyList<CriterionInfo> All = new List<CriterionInfo>
    {
        // ── Расхождение с судами ──
        C(E1, AssessmentGroup.Encounters, "Манёвр уступающего судна достаточен", "правила 8 б, 16", "доля порога", true,
          true, 1, 1, 0, 10,
          "max(|Δψ|/Δψ_мин; ΔU/ΔU_мин), Δψ_мин = 30°, ΔU_мин = 50 % (в значении — доля); нормы нет: правило 8 б — «достаточно большое, чтобы быть сразу замеченным»; ориентир 30° — практика (Cockcroft & Lameijer), сверить"),
        C(E2, AssessmentGroup.Encounters, "Манёвр заблаговременный (TCPA в начале)", "правила 8 а, 16", "мин", true,
          false, 3, 2, 1, 6, "нормы нет: правило 8 а — «заблаговременно»; порог — предварительный"),
        C(E3, AssessmentGroup.Encounters, "На встречных курсах — поворот вправо", "правило 14 а", "1 — вправо, 0 — влево", true,
          true, 1, 1, 0, 10, "норма (направление)"),
        C(E4, AssessmentGroup.Encounters, "Не пересекать курс впереди судна справа", "правило 15", "1 — нет, 0 — пересёк", true,
          true, 1, 1, 0, 10, "норма («избегать пересечения курса впереди»)"),
        C(E5, AssessmentGroup.Encounters, "Не поворачивать влево на судно справа (замечание)", "правило 15, практика", "1 — нет, 0 — влево", true,
          false, 1, 1, 0, 3, "не норма: поворот влево ведёт к пересечению курса впереди; замечание"),
        C(E6, AssessmentGroup.Encounters, "Привилегированное судно сохраняет курс и скорость", "правило 17 а(i)", "доля допуска", false,
          true, 1, 1, 2, 10, "max(|Δψ_м|/10°; ΔU/20 %) до TCPA ≤ 3 мин или D ≤ 1 мили; допуски — предварительные"),
        C(E7, AssessmentGroup.Encounters, "Привилегированное: не влево на судно слева", "правило 17 в", "1 — нет, 0 — влево", true,
          true, 1, 1, 0, 6, "норма (направление)"),
        C(E8, AssessmentGroup.Encounters, "Наименьшая дистанция не меньше допустимой", "правило 8 г", "доля CPA_доп", true,
          true, 1, 1, 0.5, 10, "CPA_доп — параметр сценария (по умолчанию 0,5 мили; в фарватере B3-01 — 0,05 мили); нормы нет"),
        C(R8b, AssessmentGroup.Encounters, "Расхождение в узкости у правой стороны", "правило 9 а", "доля XTE_доп", true,
          true, 0.5, 0.3, 0, 6, "XTE своего судна в момент наименьшей дистанции со встречным; 0,3·XTE_доп — предварительно"),
        // ── Время реакции ──
        C(E9, AssessmentGroup.Reaction, "Время реакции на опасность", "—", "с", false,
          false, 30, 60, 120, 1, "от возникновения опасности (цель обнаружена, CPA < k·CPA_доп) до первого действия уступающего; нормы нет, порог — предварительный"),
        // ── Маршрут ──
        C(R1, AssessmentGroup.Route, "Наибольшее боковое отклонение", "маршрут", "доля XTE_доп", false,
          true, 0.5, 1, 1.5, 8, "XTE_доп — параметр сценария"),
        C(R2, AssessmentGroup.Route, "Среднеквадратичное боковое отклонение", "маршрут", "доля XTE_доп", false,
          false, 0.25, 0.5, 0.75, 5, "предварительно"),
        C(R3, AssessmentGroup.Route, "Время вне коридора", "маршрут", "с", false,
          false, 0, 30, 60, 5, "предварительно"),
        C(R4, AssessmentGroup.Route, "Пройдены все путевые точки", "маршрут", "доля", true,
          true, 1, 1, 0, 8, "—"),
        C(R5, AssessmentGroup.Route, "Уложился в лимит времени", "маршрут", "доля лимита", false,
          true, 1, 1, 1.0001, 4, "лимит — параметр сценария"),
        C(R6, AssessmentGroup.Route, "Вынос на повороте", "маршрут", "доля XTE_доп", false,
          false, 0.25, 0.5, 1, 6, "max |XTE − XTE_вх| в зоне поворота (XTE_вх — на входе в зону); основание — табл. 6.4 (без упреждения 147 м, с упреждением 23 м)"),
        C(R7, AssessmentGroup.Route, "Момент перекладки на повороте", "маршрут", "доля d_рек", false,
          false, 0.25, 0.5, 1, 5, "|d_факт − d_рек|/d_рек; d_рек = R·tg(Δχ/2) + U·δ/ω_δ (п. 8.4)"),
        C(R8a, AssessmentGroup.Route, "В узкости — правее оси", "правило 9 а", "доля времени", true,
          false, 0.9, 0.75, 0.5, 5, "доля времени на участках узкости с XTE ≥ 0; предварительно"),
        // ── Аварии ──
        C(A1, AssessmentGroup.Accidents, "Посадка на мель", "провал", "число", false,
          true, 0, 0, 1, 0, "посадка — провал упражнения независимо от баллов"),
        C(A2, AssessmentGroup.Accidents, "Столкновение", "провал", "число", false,
          true, 0, 0, 1, 0, "столкновение — провал упражнения независимо от баллов")
    };

    public static CriterionInfo Find(string code)
    {
        foreach (CriterionInfo c in All) if (c.Code == code) return c;
        return null;
    }

    public static CriterionSetting DefaultSetting(CriterionInfo c) => new CriterionSetting
    {
        code = c.Code, enabled = true, mandatory = c.Mandatory, full = c.Full, pass = c.Pass, zero = c.Zero, weight = c.Weight
    };

    public static string GroupTitle(AssessmentGroup g)
    {
        switch (g)
        {
            case AssessmentGroup.Encounters: return "Расхождение с судами";
            case AssessmentGroup.Reaction: return "Время реакции";
            case AssessmentGroup.Route: return "Маршрут";
            default: return "Аварии";
        }
    }
}
