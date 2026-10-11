// ============================================================
// AssessmentCriteria.cs — Набор критериев оценки (заготовка B2, заполняется в C2)
//
// ScenarioDefinition уже ссылается на этот тип: когда в C2 появятся критерии,
// пороги и веса, ссылки в существующих сценариях сохранятся.
// ============================================================
using UnityEngine;

[CreateAssetMenu(menuName = "VR Bridge/Assessment Criteria", fileName = "AssessmentCriteria", order = 11)]
public class AssessmentCriteria : ScriptableObject
{
    [TextArea(2, 6)] public string notes = "Критерии оценки — запрос C2.";
}
