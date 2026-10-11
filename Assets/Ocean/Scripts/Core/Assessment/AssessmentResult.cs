// ============================================================
// AssessmentResult.cs — Результат оценки (C2)
//
// По каждому критерию и объекту (цель, точка поворота) — значение, пороги,
// зачёт, балл и пояснение на русском; итог: балл 0–100, зачёт/незачёт, провал.
// Выход: панель результатов в шлеме (ToPanelText), assessment.csv в папке сессии
// (ToCsv), итог в session.json (SessionAssessmentSummary). Разбор и отчёт — C3.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class CriterionResult
{
    public CriterionInfo Info;
    public CriterionSetting Setting;
    public string Code => Info.Code;
    /// <summary>Объект критерия: id цели, название точки поворота; пусто — упражнение в целом.</summary>
    public string Subject = "";
    /// <summary>Оценён (false — не оценивался: нет данных, упражнение завершено досрочно и т. п.).</summary>
    public bool Evaluated;
    public double Value = double.NaN;
    public bool Passed;
    public double Score;
    /// <summary>Пояснение (что измерено), на русском.</summary>
    public string Note = "";

    public bool Mandatory => Setting.mandatory;
}

/// <summary>Сближение с целью — для разбора и окна инструктора.</summary>
public sealed class EncounterSummary
{
    /// <summary>TargetId — подпись (при нескольких сближениях с одной целью — «T3 (2)»), TargetKey — id цели в targets.csv.</summary>
    public string TargetId = "", TargetName = "", TargetKey = "";
    public EncounterSituation Situation;
    public double DetectTime = double.NaN, DetectRangeNm = double.NaN, DetectionRangeNm = double.NaN;
    public double OnsetTime = double.NaN, OnsetCpaNm = double.NaN, OnsetTcpaMin = double.NaN, BearingRateDegMin = double.NaN;
    public bool SteadyBearing, NarrowChannel;
    public double ActionTime = double.NaN;
    public string Action = "";
    public double MinDistanceM = double.NaN, MinTime = double.NaN, RequiredCpaM;
    public bool Complete, Collided;
}

[Serializable]
public class SessionAssessmentSummary
{
    /// <summary>Оценка выполнена (false — сессия до C2 или запись прервана).</summary>
    public bool assessed;
    public double score;
    public bool passed, failed;
    public string failure = "";
    public int criteria_evaluated, criteria_failed, mandatory_failed;

    public static SessionAssessmentSummary From(AssessmentResult r) => r == null ? new SessionAssessmentSummary() : new SessionAssessmentSummary
    {
        assessed = true, score = double.IsNaN(r.Score) ? 0 : Math.Round(r.Score, 1), passed = r.Passed, failed = r.Failed,
        failure = r.FailureText, criteria_evaluated = r.EvaluatedCount, criteria_failed = r.FailedCount, mandatory_failed = r.MandatoryFailed.Count
    };
}

public sealed class AssessmentResult
{
    public bool Final;
    /// <summary>Время упражнения на момент оценки, с.</summary>
    public double Time;
    public ScenarioEndReason EndReason;
    public readonly List<CriterionResult> Items = new List<CriterionResult>();
    public readonly List<EncounterSummary> Encounters = new List<EncounterSummary>();
    /// <summary>Балл 0–100 (NaN — нечего оценивать).</summary>
    public double Score = double.NaN;
    public readonly Dictionary<AssessmentGroup, double> GroupScores = new Dictionary<AssessmentGroup, double>();
    public bool Failed;
    public string FailureText = "";
    public readonly List<string> MandatoryFailed = new List<string>();
    public bool Passed;
    public double PassScore;

    public int EvaluatedCount { get { int n = 0; foreach (CriterionResult c in Items) if (c.Evaluated) n++; return n; } }
    public int FailedCount { get { int n = 0; foreach (CriterionResult c in Items) if (c.Evaluated && !c.Passed) n++; return n; } }

    public CriterionResult Find(string code, string subject = null)
    {
        foreach (CriterionResult c in Items)
            if (c.Code == code && (subject == null || c.Subject == subject)) return c;
        return null;
    }

    // ─────────── Балл ───────────
    public static bool Meets(double v, double threshold, bool higherIsBetter) =>
        higherIsBetter ? v >= threshold - 1e-9 : v <= threshold + 1e-9;

    /// <summary>Балл 0…1: 1 не хуже «полного», 0 не лучше «нулевого», между — линейно.</summary>
    public static double ScoreOf(double v, CriterionSetting s, bool higherIsBetter)
    {
        if (double.IsNaN(v)) return 0.0;
        if (Meets(v, s.full, higherIsBetter)) return 1.0;
        if (!higherIsBetter ? v >= s.zero : v <= s.zero) return 0.0;
        double span = s.full - s.zero;
        if (Math.Abs(span) < 1e-12) return 0.0;
        double k = (v - s.zero) / span;
        return k < 0 ? 0 : k > 1 ? 1 : k;
    }

    /// <summary>Итог по критериям: баллы групп, общий балл, обязательные, зачёт.</summary>
    public void Aggregate(AssessmentSettings settings)
    {
        PassScore = settings.passScore;
        var byCode = new Dictionary<string, List<CriterionResult>>();
        foreach (CriterionResult c in Items)
        {
            if (!c.Evaluated) continue;
            if (!byCode.TryGetValue(c.Code, out var list)) byCode[c.Code] = list = new List<CriterionResult>();
            list.Add(c);
        }
        var sumW = new Dictionary<AssessmentGroup, double>();
        var sumWs = new Dictionary<AssessmentGroup, double>();
        MandatoryFailed.Clear();
        foreach (KeyValuePair<string, List<CriterionResult>> kv in byCode)
        {
            CriterionInfo info = kv.Value[0].Info;
            CriterionSetting set = kv.Value[0].Setting;
            double mean = 0; bool allPassed = true;
            foreach (CriterionResult c in kv.Value) { mean += c.Score; allPassed &= c.Passed; }
            mean /= kv.Value.Count;
            if (set.mandatory && !allPassed && info.Group != AssessmentGroup.Accidents) MandatoryFailed.Add(info.Code);
            if (info.Group == AssessmentGroup.Accidents || set.weight <= 0) continue;
            sumW.TryGetValue(info.Group, out double w);
            sumWs.TryGetValue(info.Group, out double ws);
            sumW[info.Group] = w + set.weight;
            sumWs[info.Group] = ws + set.weight * mean;
        }
        GroupScores.Clear();
        double tw = 0, tws = 0;
        foreach (KeyValuePair<AssessmentGroup, double> kv in sumW)
        {
            double gs = sumWs[kv.Key] / kv.Value;
            GroupScores[kv.Key] = 100.0 * gs;
            double gw = settings.GroupWeight(kv.Key);
            tw += gw; tws += gw * gs;
        }
        Score = tw > 0 ? 100.0 * tws / tw : double.NaN;
        Passed = !Failed && MandatoryFailed.Count == 0 && !double.IsNaN(Score) && Score >= settings.passScore - 1e-9;
    }

    // ─────────── Текст ───────────
    public static string Num(double v, int digits = 0) => ScenarioText.Num(v, digits);

    /// <summary>Значение в единицах критерия (для таблиц).</summary>
    public static string ValueText(CriterionResult c)
    {
        if (!c.Evaluated || double.IsNaN(c.Value)) return "—";
        switch (c.Info.Unit)
        {
            case "мин": return $"{Num(c.Value, 1)} мин";
            case "с": return $"{Num(c.Value)} с";
            case "число": return Num(c.Value);
            default:
                if (c.Info.Unit.StartsWith("1 —")) return c.Value >= 0.5 ? "да" : "нет";
                return Num(c.Value, 2);
        }
    }

    /// <summary>Порог зачёта словами.</summary>
    public static string PassText(CriterionResult c)
    {
        CriterionInfo i = c.Info;
        if (i.Unit.StartsWith("1 —")) return "";
        string op = i.HigherIsBetter ? "≥" : "≤";
        switch (i.Unit)
        {
            case "мин": return $"{op} {Num(c.Setting.pass, 1)} мин";
            case "с": return $"{op} {Num(c.Setting.pass)} с";
            case "число": return $"{op} {Num(c.Setting.pass)}";
            default: return $"{op} {Num(c.Setting.pass, 2)}";
        }
    }

    public string Headline()
    {
        string score = double.IsNaN(Score) ? "—" : Num(Score);
        string verdict = Failed ? "ПРОВАЛ" : Passed ? "ЗАЧЁТ" : "НЕЗАЧЁТ";
        return Final ? $"Оценка: {score} из 100 — {verdict}" : $"Текущая оценка: {score} из 100 (предварительно)";
    }

    /// <summary>Текст для VR-панели (TextMeshPro, rich text).</summary>
    public string ToPanelText(bool showFailure = true)
    {
        var b = new StringBuilder();
        string color = Failed ? "#FF5A4A" : Passed ? "#6FE08A" : "#FFC85A";
        b.AppendLine($"<b><color={color}>{Headline()}</color></b>");
        if (showFailure && Failed) b.AppendLine($"<color=#FF5A4A>Провал: {FailureText}</color>");
        if (Final && !Failed && !Passed)
        {
            if (MandatoryFailed.Count > 0) b.AppendLine($"<color=#FFC85A>Не выполнены обязательные критерии: {string.Join(", ", MandatoryFailed)}</color>");
            else if (!double.IsNaN(Score)) b.AppendLine($"<color=#FFC85A>Балл ниже проходного ({Num(PassScore)})</color>");
        }
        if (GroupScores.Count > 0)
        {
            var parts = new List<string>();
            foreach (AssessmentGroup g in new[] { AssessmentGroup.Encounters, AssessmentGroup.Reaction, AssessmentGroup.Route })
                if (GroupScores.TryGetValue(g, out double gs)) parts.Add($"{AssessmentCatalog.GroupTitle(g).ToLowerInvariant()} {Num(gs)}");
            b.AppendLine($"<size=85%><color=#9AA6B2>{string.Join(" · ", parts)}</color></size>");
        }
        foreach (EncounterSummary e in Encounters)
        {
            string state = e.Collided ? "столкновение" : e.Complete ? $"мин. {Num(e.MinDistanceM)} м" : "идёт";
            b.AppendLine($"<size=85%>{e.TargetId}: {EncounterClassifier.Title(e.Situation)} — {state}</size>");
        }
        b.Append("<size=85%>");
        foreach (CriterionResult c in Items)
        {
            string subj = string.IsNullOrEmpty(c.Subject) ? "" : $" ({c.Subject})";
            if (!c.Evaluated)
            {
                b.AppendLine($"<color=#7F8A96>–  {c.Info.Title}{subj}: {c.Note}</color>");
                continue;
            }
            string mark = c.Passed ? "<color=#6FE08A>+</color>" : c.Mandatory ? "<color=#FF5A4A>×</color>" : "<color=#FFC85A>×</color>";
            b.AppendLine($"{mark}  {c.Info.Title}{subj}: {c.Note}");
        }
        b.Append("</size>");
        return b.ToString();
    }

    public const string CsvHeader =
        "code;group;title;rule;subject;evaluated;value;unit;full;pass;zero;passed;score;mandatory;weight;note";

    /// <summary>assessment.csv: строка на критерий и объект, затем строка итога (code = TOTAL).</summary>
    public string ToCsv()
    {
        var b = new StringBuilder();
        b.Append(CsvHeader).Append('\n');
        foreach (CriterionResult c in Items)
        {
            CsvFormat.Text(b, c.Code); CsvFormat.Sep(b);
            b.Append(c.Info.Group.ToString()); CsvFormat.Sep(b);
            CsvFormat.Text(b, c.Info.Title); CsvFormat.Sep(b);
            CsvFormat.Text(b, c.Info.Rule); CsvFormat.Sep(b);
            CsvFormat.Text(b, c.Subject); CsvFormat.Sep(b);
            b.Append(c.Evaluated ? '1' : '0'); CsvFormat.Sep(b);
            if (c.Evaluated) CsvFormat.Num(b, c.Value, 4);
            CsvFormat.Sep(b);
            CsvFormat.Text(b, c.Info.Unit); CsvFormat.Sep(b);
            CsvFormat.Num(b, c.Setting.full, 4); CsvFormat.Sep(b);
            CsvFormat.Num(b, c.Setting.pass, 4); CsvFormat.Sep(b);
            CsvFormat.Num(b, c.Setting.zero, 4); CsvFormat.Sep(b);
            if (c.Evaluated) b.Append(c.Passed ? '1' : '0');
            CsvFormat.Sep(b);
            if (c.Evaluated) CsvFormat.Num(b, c.Score, 4);
            CsvFormat.Sep(b);
            b.Append(c.Mandatory ? '1' : '0'); CsvFormat.Sep(b);
            CsvFormat.Num(b, c.Setting.weight, 2); CsvFormat.Sep(b);
            CsvFormat.Text(b, c.Note);
            b.Append('\n');
        }
        b.Append("TOTAL;;");
        CsvFormat.Text(b, Headline()); CsvFormat.Sep(b);
        b.Append(";;1;");
        CsvFormat.Num(b, Score, 2);
        b.Append(";балл;;");
        CsvFormat.Num(b, PassScore, 2);
        b.Append(";;").Append(Passed ? '1' : '0').Append(";;;;");
        CsvFormat.Text(b, Failed ? "провал: " + FailureText : MandatoryFailed.Count > 0 ? "не выполнены: " + string.Join(", ", MandatoryFailed) : "");
        b.Append('\n');
        return b.ToString();
    }
}
