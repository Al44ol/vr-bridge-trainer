// ============================================================
// AssessmentCriteria.cs — Набор критериев оценки (заготовка B2, C2)
//
// Ассет (Create → VR Bridge → Assessment Criteria) хранит числа методики
// оценки: параметры ситуаций сближения, пороги, веса, обязательность
// критериев (AssessmentSettings). Что измеряется и по какому правилу —
// AssessmentCatalog (код). Сценарий ссылается на ассет полем assessment;
// пусто — значения по умолчанию. Все значения по умолчанию ПРЕДВАРИТЕЛЬНЫЕ
// (обосновываются в главе 2 ВКР; список — ThesisNotes, раздел 10).
// Снимок настроек пишется в session.json (assessment_settings): офлайн-оценка
// по записи берёт те же числа, что оценка в шлеме.
// ============================================================
using UnityEngine;

[CreateAssetMenu(menuName = "VR Bridge/Assessment Criteria", fileName = "AssessmentCriteria", order = 11)]
public class AssessmentCriteria : ScriptableObject
{
    [TextArea(2, 6)] public string notes =
        "Критерии оценки (C2). Значения по умолчанию — предварительные, обосновываются в главе 2 ВКР.";

    public AssessmentSettings settings = AssessmentSettings.Default();

    [ContextMenu("Сбросить к значениям по умолчанию")]
    public void ResetToDefaults() => settings = AssessmentSettings.Default();

    [ContextMenu("Добавить недостающие критерии")]
    public void AddMissing() => settings.AddMissing();
}
