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
    Accidents,    // аварии — провал, в балл не входят
    Visibility,   // ограниченная видимость (C4): туманные сигналы, безопасная скорость, правило 19
    Mooring,      // швартовка (19.2б-3): задание сценария швартовки
    Towing        // буксир (19.3в): трос и толкание, если буксир вызывался
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

    // ── Ограниченная видимость (C4). В снимках session.json до C4 этих полей нет (там 0) — тогда
    //    берутся значения по умолчанию (свойства ниже); группа в таких сессиях и не оценивается.
    [UnityEngine.Tooltip("Вес группы «Ограниченная видимость» (C4)")]
    public double weightVisibility = 0.3;
    [UnityEngine.Tooltip("Ограниченная видимость (правила 19, 35): V меньше, мили")]
    public double restrictedVisibilityNm = 2.0;
    [UnityEngine.Tooltip("Допуск длительности звука (правило 32: продолжительный 4–6 с, короткий около 1 с), с")]
    public double blastToleranceS = SoundSignals.DefaultToleranceS;
    [UnityEngine.Tooltip("Звуки с паузой не длиннее … с — один сигнал")]
    public double signalGapS = SoundSignals.DefaultGroupGapS;
    [UnityEngine.Tooltip("Судно «имеет ход» (правило 35 а, б) при скорости не меньше, уз")]
    public double makingWayKnots = 0.5;
    [UnityEngine.Tooltip("Безопасная скорость (F3, F4) оценивается через … с после начала ограниченной видимости (время на сброс хода)")]
    public double safeSpeedGraceS = 120.0;
    [UnityEngine.Tooltip("«Минимальный ход, при котором судно удерживается на курсе» (правило 19 д): телеграф не выше SF")]
    public double minimumSteerageSf = 0.2;

    public double WeightVisibility => weightVisibility > 0 ? weightVisibility : 0.3;
    public double RestrictedVisibilityNm => restrictedVisibilityNm > 0 ? restrictedVisibilityNm : 2.0;
    public double BlastToleranceS => blastToleranceS > 0 ? blastToleranceS : 0.0;
    public double SignalGapS => signalGapS > 0 ? signalGapS : SoundSignals.DefaultGroupGapS;
    public double MakingWayKnots => makingWayKnots > 0 ? makingWayKnots : 0.5;
    public double SafeSpeedGraceS => safeSpeedGraceS > 0 ? safeSpeedGraceS : 120.0;
    public double MinimumSteerageSf => minimumSteerageSf > 0 ? minimumSteerageSf : 0.2;

    // ── Швартовка (19.2б-3). В снимках до 19.2б-3 поля нет (0) — вес по умолчанию; группа там и не оценивается.
    [UnityEngine.Tooltip("Вес группы «Швартовка» (19.2б-3)")]
    public double weightMooring = 1.0;
    public double WeightMooring => weightMooring > 0 ? weightMooring : 1.0;

    // ── Буксир (19.3в). В снимках до 19.3в поля нет (0) — вес по умолчанию; группа там и не оценивается.
    [UnityEngine.Tooltip("Вес группы «Буксир» (19.3в)")]
    public double weightTowing = 0.5;
    public double WeightTowing => weightTowing > 0 ? weightTowing : 0.5;
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
        g == AssessmentGroup.Route ? weightRoute : g == AssessmentGroup.Visibility ? WeightVisibility :
        g == AssessmentGroup.Mooring ? WeightMooring : g == AssessmentGroup.Towing ? WeightTowing : 0.0;

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
    public const string F1 = "F1", F2 = "F2", F3 = "F3", F4 = "F4", F5 = "F5", F6 = "F6";
    public const string N1 = "N1", N2 = "N2";
    public const string M1 = "M1", M2 = "M2", M3 = "M3", M4 = "M4", M5 = "M5", M6 = "M6", M7 = "M7", M8 = "M8", M9 = "M9";
    public const string G1 = "G1", G2 = "G2", G3 = "G3", G4 = "G4";
    public const string G5 = "G5", G6 = "G6", G7 = "G7", G8 = "G8", G9 = "G9", G10 = "G10", G11 = "G11";
    public const string G12 = "G12", G13 = "G13";

    /// <summary>Группы, входящие в балл (порядок вывода).</summary>
    public static readonly AssessmentGroup[] ScoredGroups =
        { AssessmentGroup.Encounters, AssessmentGroup.Reaction, AssessmentGroup.Route, AssessmentGroup.Visibility, AssessmentGroup.Mooring, AssessmentGroup.Towing };

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
        // D1: вводные инструктора (оцениваются, только если вводная была)
        C(N1, AssessmentGroup.Reaction, "Реакция на отказ рулевого устройства: снижение хода", "практика", "с", false,
          false, 15, 30, 60, 1, "от отказа до первой команды телеграфу на меньший ход (или ход уже «Стоп»); нормы нет — практика: при потере управления рулём сбросить ход; пороги предварительные"),
        C(N2, AssessmentGroup.Reaction, "Реакция на отказ главного двигателя: телеграф «Стоп»", "практика", "с", false,
          false, 15, 30, 60, 1, "от отказа до команды «Стоп» (или телеграф уже на «Стоп»); нормы нет — подтверждение отказа машине; сигнал «— · ·» в ограниченную видимость (правило 35 в) не оценивается; пороги предварительные"),
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
        // ── Ограниченная видимость (C4; оценивается только при V < restrictedVisibilityNm) ──
        C(F1, AssessmentGroup.Visibility, "Туманные сигналы подаются не реже чем через 2 мин", "правило 35 а, б", "с", false,
          true, 120, 120, 240,  8, "наибольший промежуток без своего сигнала Д или ДД (от начала ограниченной видимости, между сигналами, до конца); 2 мин — норма"),
        C(F2, AssessmentGroup.Visibility, "Тип туманного сигнала соответствует ходу", "правило 35 а, б", "доля", true,
          false, 1, 0.75, 0.5, 4, "доля верных среди своих сигналов из продолжительных звуков: Д — имея ход, ДД — без хода; порог доли — предварительный"),
        C(F3, AssessmentGroup.Visibility, "Безопасная скорость: средняя", "правила 6, 19 б", "доля U_без", false,
          true, 1, 1.1, 1.5, 8, "U_ср/U_без после времени на сброс хода; U_без — параметр сценария (нормы нет: правило 6 — перечень факторов); зачёт до 1,1 — запаздывание торможения после команды"),
        C(F4, AssessmentGroup.Visibility, "Безопасная скорость: наибольшая", "правила 6, 19 б", "доля U_без", false,
          false, 1.1, 1.25, 1.75, 4, "U_max/U_без после времени на сброс хода; пороги — предварительные"),
        C(F5, AssessmentGroup.Visibility, "Снижение хода, услышав сигнал впереди траверза", "правило 19 д", "с", false,
          true, 30, 60, 120, 8, "от первого слышимого сигнала невидимой цели впереди траверза до команды «Самый малый вперёд» или ниже; нормы времени нет — предварительно"),
        C(F6, AssessmentGroup.Visibility, "В тумане — не влево на судно впереди траверза", "правило 19 г", "1 — нет, 0 — нарушение", true,
          true, 1, 1, 0, 8, "норма (направление): нет перекладки влево на известную, но невидимую цель впереди траверза (кроме обгоняемой) и поворота в сторону цели на траверзе или позади него"),
        // ── Швартовка (19.2б-3; оценивается только в сценарии швартовки) ──
        C(M1, AssessmentGroup.Mooring, "Задание швартовки выполнено (ошвартован)", "задание сценария", "доля заданий", true,
          true, 1, 1, 0, 10, "выполнено заданий из 4 (лагом, у кранцев, концы с носа и кормы, ошвартован — концы поданы, у кранцев, ход ≤ 0,1 уз 30 с); как R4 «пройдены все путевые точки»: зачёт — только всё задание"),
        // 19.2б-3в: остальные критерии группы (по событиям навала и концов и mooring.csv); пороги предварительные
        C(M2, AssessmentGroup.Mooring, "Скорость навала на кранцы", "практика, PIANC", "уз", false,
          true, 0.15, 0.3, 0.5, 8, "наибольшая скорость точки касания к кромке при навале (BerthingImpact); учебная граница 0,1–0,3 уз (19.15), на 0,5 уз кранцы причала №1 выходят на номинал; нормы для обучения нет"),
        C(M3, AssessmentGroup.Mooring, "Обжатие кранцев не выше номинального", "каталог кранца", "доля δ_н", false,
          true, 0.5, 1.0, 1.5, 6, "наибольшее δ/δ_н за упражнение (навалы и mooring.csv); > 1 — перегрузка кранца (номинал каталога), энергия навала — в примечании"),
        C(M4, AssessmentGroup.Mooring, "Без удара корпусом о кромку причала", "практика", "число", false,
          true, 0, 0, 1, 8, "корпус зашёл за кромку (QuayImpact) — повреждение корпуса и причала"),
        C(M5, AssessmentGroup.Mooring, "Подход лагом: угол к кромке при касании", "практика, PIANC", "°", false,
          false, 3, 10, 20, 4, "|угол ДП к кромке| в момент первого касания кранцев; навал скулой сосредотачивает энергию на одном кранце (19.15); пороги предварительные"),
        C(M6, AssessmentGroup.Mooring, "Без обрыва швартовных концов", "OCIMF MEG4", "число", false,
          true, 0, 0, 1, 8, "обрыв (T > MBL) опасен для швартовной команды"),
        C(M7, AssessmentGroup.Mooring, "Концы не перегружены", "OCIMF MEG4", "с", false,
          false, 0, 10, 30, 4, "время с натяжением хотя бы одного конца выше рабочей нагрузки 0,5·MBL (LineOverload → LineOverloadCleared); пороги предварительные"),
        C(M8, AssessmentGroup.Mooring, "Время швартовки", "задание сценария", "доля T_контр", false,
          false, 1, 1.5, 2, 3, "время «ошвартован» / контрольное время сценария (19-01 — 10 мин)"),
        C(M9, AssessmentGroup.Mooring, "Подход к кранцам — с крыла со стороны причала", "практика", "1 — да, 0 — нет", true,
          false, 1, 1, 0, 2, "место обучающегося при первом касании кранцев (mooring.csv → station): с крыла виден борт и кранцы"),
        // ── Буксир (19.3в; оценивается, только если буксир вызывался; пороги предварительные) ──
        C(G1, AssessmentGroup.Towing, "Без обрыва буксирного троса", "практика", "число", false,
          true, 0, 0, 1, 8, "обрыв (T > MBL = 2,5·T_гак) опасен для команды на баке и буксира"),
        C(G2, AssessmentGroup.Towing, "Буксирный трос не перегружен", "практика", "с", false,
          false, 0, 10, 30, 4, "время с натяжением выше 0,5·MBL (TowLineOverload → …Cleared, отдача, обрыв), как M7 у концов"),
        C(G3, AssessmentGroup.Towing, "Тяга буксира задаётся ступенями", "практика", "%", false,
          false, 25, 50, 100, 4, "наибольший скачок заданной тяги между соседними командами буксиру (вызов и «стоп» — 0 %); 25 % против ветра 20 уз уже дают ≈ 1 уз лагом (19.24.4), полная тяга сразу — навал и рывок троса (до 1,7·P·T_гак)"),
        C(G4, AssessmentGroup.Towing, "Касание буксира о борт — мягкое", "практика, PIANC", "уз", false,
          false, 0.3, 0.6, 1.2, 3, "наибольшая скорость сближения носа буксира с бортом по нормали при касании (TugPushContact): подход буксира и снос судна на него"),
        // ── Работа своего буксира (19.4в; оценивается только в упражнении на буксире — 19-03; пороги предварительные) ──
        C(G5, AssessmentGroup.Towing, "Задание буксира выполнено", "задание сценария", "доля заданий", true,
          true, 1, 1, 0, 10, "выполнено заданий из 4 (подход к борту у миделя, прижать к причалу, держать до «концы закреплены», отойти); как M1: зачёт — только всё задание"),
        C(G6, AssessmentGroup.Towing, "Касание борта кранцем — мягкое", "практика, PIANC", "уз", false,
          false, 0.3, 0.6, 1.2, 6, "наибольшая скорость носа буксира к борту клиента по нормали среди касаний кранцем (TugWorkContact); пороги — как G4"),
        C(G7, AssessmentGroup.Towing, "Подход к борту — поперёк", "практика", "°", false,
          false, 10, 20, 40, 3, "отклонение ДП буксира от перпендикуляра к борту при касании, которым выполнен подход; под углом кранец скользит по борту, буксир разворачивает"),
        C(G8, AssessmentGroup.Towing, "Упор держится, пока заводят концы", "команда лоцмана", "доля времени", true,
          true, 0.95, 0.8, 0.5, 6, "доля времени от «Держите, заводим концы» до «концы закреплены» с упором кранцем в борт (mooring.csv); без упора судно отжимает ветром, концы перегружаются"),
        C(G9, AssessmentGroup.Towing, "Без касания корпусом", "практика", "уз", false,
          false, 0, 0.3, 0.6, 4, "наибольшая скорость сближения при касании клиента корпусом буксира, а не кранцем (TugWorkHullContact); касаний нет — 0"),
        C(G10, AssessmentGroup.Towing, "Навал судна на кранцы причала", "практика, PIANC", "уз", false,
          true, 0.15, 0.3, 0.5, 8, "наибольшая скорость навала судна-клиента на кранцы (BerthingImpact); пороги — как M2: судно к причалу ведёт буксир"),
        C(G11, AssessmentGroup.Towing, "Реакция на команду лоцмана упором", "команда лоцмана", "с", false,
          true, 15, 30, 60, 8, "наибольшее время от команды «Толкай / Держи … / Стоп толкать» до установившегося положения рукоятки на названной ступени (JoystickChanged); не выполнена до следующей команды — не меньше 60 с"),
        // 19.4г: «случайное событие» в 19-03 (только если было — TugWorkEvent; пороги предварительные)
        C(G12, AssessmentGroup.Towing, "Переход к отошедшему концу", "команда лоцмана", "с", false,
          false, 60, 90, 180, 4, "от команды «Буксир — к носу / корме» до упора кранцем в зоне этого конца (± 15 м около ± 0,3·L от миделя); не перешёл, пока судно не у кранцев, — не меньше 180 с"),
        C(G13, AssessmentGroup.Towing, "Угол судна не растёт сверх названного", "практика", "°", false,
          false, 2, 3, 6, 4, "наибольший |угол ДП судна к кромке| (mooring.csv) от команды до «судно у кранцев» минус названный лоцманом N; пока буксир не упёрся у конца, ветер разворачивает судно вокруг закреплённого конца"),
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
            case AssessmentGroup.Visibility: return "Ограниченная видимость";
            case AssessmentGroup.Mooring: return "Швартовка";
            case AssessmentGroup.Towing: return "Буксир";
            default: return "Аварии";
        }
    }
}
