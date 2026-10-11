// ============================================================
// ScenarioDefinitionEditor.cs — Инспектор сценария (B2)
//
// Поверх обычных полей:
//   • проблемы описания (ScenarioDefinition.Validate);
//   • подсказка по начальной скорости: установившаяся скорость при выбранном
//     положении телеграфа (судно будет разгоняться или тормозить к ней);
//   • «Маршрут по оси фарватера» — старт + точки оси выбранного фарватера акватории;
//   • «Старт — в первую точку маршрута» и «Курс — на вторую точку»;
//   • B3: для других судов — рассчитанный старт, пеленг, дистанция, CPA/TCPA.
// Маршрут и старт двигаются мышью в Scene View: выделить объект Scenario
// (ScenarioRunner) — редактируется загруженный / выбранный для предпросмотра сценарий.
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScenarioDefinition))]
public class ScenarioDefinitionEditor : Editor
{
    private int fairwayIndex;

    public override void OnInspectorGUI()
    {
        var s = (ScenarioDefinition)target;

        List<string> problems = s.Validate();
        if (problems.Count == 0) EditorGUILayout.HelpBox("Сценарий корректен.", MessageType.None);
        else EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);

        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Справка", EditorStyles.boldLabel);
        if (s.initial != null)
        {
            ShipParameters p = s.shipParameters;
            if (p != null)
            {
                double vSet = ShipDynamicsModel.SteadySpeed(p, ScenarioText.SpeedFactor(s.initial.telegraph)) / ShipParameters.KnotsToMs;
                string msg = $"Установившаяся скорость при «{ScenarioText.Telegraph(s.initial.telegraph)}»: {vSet:F1} уз.";
                if (Mathf.Abs((float)vSet - s.initial.speedKnots) > 0.5f)
                    msg += $" Начальная {s.initial.speedKnots:F1} уз — судно будет {(vSet > s.initial.speedKnots ? "разгоняться" : "тормозить")}.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
            }
            if (s.waterArea != null)
                EditorGUILayout.LabelField("Глубина в точке старта", $"{s.waterArea.DepthAt(s.initial.position):F1} м");
        }
        EditorGUILayout.LabelField("Длина маршрута", $"{s.RouteLength:F0} м = {s.RouteLength / 1852.0:F2} мили");
        if (s.shipParameters != null && s.initial != null && s.initial.speedKnots > 0.1f)
            EditorGUILayout.LabelField("Время на начальной скорости", ScenarioText.Clock(s.RouteLength / (s.initial.speedKnots * ShipParameters.KnotsToMs)));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Маршрут", EditorStyles.boldLabel);
        if (s.waterArea != null && s.waterArea.fairways.Count > 0)
        {
            var names = new string[s.waterArea.fairways.Count];
            for (int i = 0; i < names.Length; i++) names[i] = $"{i}: {s.waterArea.fairways[i].name} ({s.waterArea.fairways[i].width:F0} м)";
            EditorGUILayout.BeginHorizontal();
            fairwayIndex = EditorGUILayout.Popup(Mathf.Clamp(fairwayIndex, 0, names.Length - 1), names);
            if (GUILayout.Button("Маршрут по оси фарватера", GUILayout.Width(190))) RouteFromFairway(s, s.waterArea.fairways[fairwayIndex]);
            EditorGUILayout.EndHorizontal();
        }
        else EditorGUILayout.LabelField("В акватории нет фарватеров — точки задаются вручную или ручками в Scene View.", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = s.route.Count > 0 && s.initial != null;
        if (GUILayout.Button("Старт — в первую точку маршрута"))
        {
            Undo.RecordObject(s, "Старт в первую точку");
            s.initial.position = s.route[0].position;
            EditorUtility.SetDirty(s);
        }
        GUI.enabled = s.route.Count > 1 && s.initial != null;
        if (GUILayout.Button("Курс — на вторую точку"))
        {
            Undo.RecordObject(s, "Курс на вторую точку");
            WorldPosition d = s.route[1].position - s.initial.position;
            s.initial.headingDeg = Mathf.Round((Mathf.Atan2((float)d.East, (float)d.North) * Mathf.Rad2Deg + 360f) % 360f * 10f) / 10f;
            EditorUtility.SetDirty(s);
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Ручки маршрута в Scene View — выделите объект Scenario в сцене (поле Preview Index выбирает сценарий).", MessageType.None);

        TrafficInfo(s);
    }

    // Другие суда (B3): рассчитанный старт, дистанция и пеленг со старта своего судна,
    // CPA/TCPA при начальных курсах и скоростях (как увидит обучающийся в начале)
    private static void TrafficInfo(ScenarioDefinition s)
    {
        if (s.traffic == null || s.traffic.Count == 0 || s.initial == null) return;
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Другие суда (B3)", EditorStyles.boldLabel);
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            if (d == null) continue;
            TrafficStart st = TrafficPlanner.Resolve(d, s.initial);
            CpaResult c = CpaCalculator.FromCourses(s.initial.position, s.initial.headingDeg, s.initial.speedKnots,
                                                    st.Position, st.HeadingDeg, st.SpeedKnots);
            string tcpa = c.Tcpa >= 0 ? $"{c.Tcpa / 60.0:F1} мин" : "уже прошли";
            EditorGUILayout.HelpBox(
                $"{d.TargetId(i)} «{d.name}» — {ScenarioText.VesselType(d.type)}, {(d.parameters != null ? d.parameters.name : "параметры по умолчанию для типа")}\n" +
                $"Старт: E {st.Position.East:F0}, N {st.Position.North:F0} м; курс {st.HeadingDeg:000}°, {st.SpeedKnots:F1} уз" +
                $"{(st.Path != null && st.Path.Count > 0 ? $"; путь — {st.Path.Count} т." : "")}\n" +
                $"Со старта своего судна: пеленг {c.BearingDeg:000}°, дистанция {c.Range / 1852.0:F2} мили; " +
                $"CPA {c.Cpa / 1852.0:F2} мили, TCPA {tcpa} (при начальных курсах и скоростях)",
                MessageType.None);
        }
    }

    private static void RouteFromFairway(ScenarioDefinition s, FairwayLine f)
    {
        if (f.axis.Count < 2) return;
        Undo.RecordObject(s, "Маршрут по оси фарватера");
        float xte = Mathf.Round(Mathf.Max(10f, f.width * 0.5f - 35f));   // полуширина минус запас (как в первом сценарии)
        var route = new List<RouteWaypoint>();
        if (s.initial != null && (s.initial.position - f.axis[0]).Magnitude > 1.0)
            route.Add(new RouteWaypoint("Старт", s.initial.position, xte));
        for (int i = 0; i < f.axis.Count; i++)
        {
            string name = i == 0 ? "Вход в фарватер" : i == f.axis.Count - 1 ? "Выход из фарватера" : $"Поворот {i}";
            route.Add(new RouteWaypoint(name, f.axis[i], xte));
        }
        s.route = route;
        EditorUtility.SetDirty(s);
        Debug.Log($"[Сценарий] «{s.title}»: маршрут по оси «{f.name}», {route.Count} точек, XTE доп. {xte:F0} м (полуширина − 35 м; подход — при необходимости увеличьте вручную).");
    }
}
