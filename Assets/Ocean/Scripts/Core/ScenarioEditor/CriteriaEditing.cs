// ============================================================
// CriteriaEditing.cs — Галочки критериев сценария в редакторе (запрос 21.4б)
//
// Сценарий ссылается на набор оценки (assessment: Assessment_Default, _Berthing, _TugBerthing, _TugWork);
// в редакторе у каждого критерия две галочки — «включён» и «обязательный». В сценарий пишутся только
// отличия от набора (ScenarioDefinition.criteriaSwitches): совпала галочка с набором — запись убирается.
// Пороги и веса не правятся (методика, глава 2): у упражнения — копия набора с галочками
// (ScenarioDefinition.EffectiveAssessment; она же — снимок assessment_settings в session.json).
// «Обязательный» имеет смысл только у включённого: выключили — обязательность снимается.
// ============================================================
using System.Collections.Generic;

public static class CriteriaEditing
{
    /// <summary>Настройка критерия в наборе сценария (без галочек).</summary>
    public static CriterionSetting Base(ScenarioDefinition s, string code) => AssessmentSettings.From(s.assessment).Get(code);

    /// <summary>Итоговая настройка критерия (набор + галочки).</summary>
    public static CriterionSetting Effective(ScenarioDefinition s, string code) => s.EffectiveAssessment().Get(code);

    /// <summary>Отличается ли критерий от набора.</summary>
    public static bool Differs(ScenarioDefinition s, string code) => Find(s, code) != null;

    public static CriterionSwitch Find(ScenarioDefinition s, string code) =>
        s.criteriaSwitches != null ? s.criteriaSwitches.Find(x => x != null && x.code == code) : null;

    /// <summary>Поставить галочки; совпадают с набором — отличие убирается. true — что-то изменилось.</summary>
    public static bool Set(ScenarioDefinition s, string code, bool enabled, bool mandatory)
    {
        if (s.criteriaSwitches == null) s.criteriaSwitches = new List<CriterionSwitch>();
        mandatory = enabled && mandatory;
        CriterionSetting b = Base(s, code);
        bool same = b.enabled == enabled && (b.enabled && b.mandatory) == mandatory;
        CriterionSwitch w = Find(s, code);
        if (same)
        {
            if (w == null) return false;
            s.criteriaSwitches.Remove(w);
            return true;
        }
        if (w != null && w.enabled == enabled && w.mandatory == mandatory) return false;
        if (w == null) { w = new CriterionSwitch { code = code }; s.criteriaSwitches.Add(w); }
        w.enabled = enabled;
        w.mandatory = mandatory;
        return true;
    }

    public static bool ToggleEnabled(ScenarioDefinition s, string code)
    {
        CriterionSetting e = Effective(s, code);
        return Set(s, code, !e.enabled, e.mandatory);
    }

    public static bool ToggleMandatory(ScenarioDefinition s, string code)
    {
        CriterionSetting e = Effective(s, code);
        if (!e.enabled) return false;   // обязательным бывает только включённый
        return Set(s, code, true, !e.mandatory);
    }

    /// <summary>Сменить набор: галочки, совпавшие с новым набором, убираются; неизвестные коды — тоже.</summary>
    public static void SetBase(ScenarioDefinition s, AssessmentCriteria set)
    {
        var keep = new List<CriterionSwitch>();
        AssessmentSettings eff = s.EffectiveAssessment();
        var wanted = new List<(string code, bool en, bool man)>();
        if (s.criteriaSwitches != null)
            foreach (CriterionSwitch w in s.criteriaSwitches)
                if (w != null && AssessmentCatalog.Find(w.code) != null) wanted.Add((w.code, w.enabled, w.mandatory));
        s.assessment = set;
        s.criteriaSwitches = keep;
        foreach (var x in wanted) Set(s, x.code, x.en, x.man);
    }

    /// <summary>Убрать все отличия — как в наборе.</summary>
    public static bool Reset(ScenarioDefinition s)
    {
        if (s.criteriaSwitches == null || s.criteriaSwitches.Count == 0) return false;
        s.criteriaSwitches.Clear();
        return true;
    }

    /// <summary>Включённых и обязательных критериев (итог).</summary>
    public static (int enabled, int mandatory) Count(ScenarioDefinition s)
    {
        AssessmentSettings a = s.EffectiveAssessment();
        int en = 0, man = 0;
        foreach (CriterionInfo c in AssessmentCatalog.All)
        {
            CriterionSetting x = a.Get(c.Code);
            if (!x.enabled) continue;
            en++;
            if (x.mandatory) man++;
        }
        return (en, man);
    }

    public static string GroupTitle(AssessmentGroup g)
    {
        switch (g)
        {
            case AssessmentGroup.Encounters: return "Расхождение с судами";
            case AssessmentGroup.Reaction: return "Время реакции, вводные";
            case AssessmentGroup.Route: return "Маршрут";
            case AssessmentGroup.Accidents: return "Аварии";
            case AssessmentGroup.Visibility: return "Ограниченная видимость";
            case AssessmentGroup.Mooring: return "Швартовка";
            case AssessmentGroup.Towing: return "Буксир";
            default: return g.ToString();
        }
    }
}
