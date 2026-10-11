// ============================================================
// ScenarioRunnerEditor.cs — Маршрут сценария в Scene View и управление в Play Mode (B2)
//
// Гизмо (только Scene View, в шлеме маршрут не виден — он будет на планшете D2):
//   линия пути, коридор ±XTE доп. по участкам, биссектрисы точек поворота
//   (линии прохождения), линия финиша, старт со стрелкой курса; в Play Mode —
//   активный участок ярче, у судна — текущее XTE.
// Выделите объект Scenario: точки маршрута и старт перетаскиваются по воде
// (Ctrl — сетка 10 м), «+» на участке — вставить точку, Delete — удалить
// выделенную (не меньше двух). Правки — в ассет сценария, через Undo.
// В инспекторе в Play Mode — кнопки жизненного цикла (для проверки без шлема).
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScenarioRunner))]
public class ScenarioRunnerEditor : Editor
{
    private static int selWaypoint = -1;
    private static bool selStart;
    private const float Snap = 10f;

    private static readonly Color RouteColor = new Color(1f, 0.35f, 0.85f);
    private static readonly Color CorridorColor = new Color(1f, 0.35f, 0.85f, 0.5f);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var runner = (ScenarioRunner)target;
        ScenarioDefinition s = runner.PreviewScenario;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Маршрут в Scene View", s != null ? $"{s.id} «{s.title}», точек {s.route.Count}" : "—");
        if (s != null && GUILayout.Button("Открыть ассет сценария")) Selection.activeObject = s;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Библиотека сценариев собирается сама при входе в Play Mode (все ScenarioDefinition проекта).", MessageType.None);
            return;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Упражнение", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Фаза", runner.CurrentPhase.ToString());
        EditorGUILayout.LabelField("Время", ScenarioText.Clock(runner.ExerciseTime));
        if (runner.Route.Valid && runner.CurrentPhase == ScenarioRunner.Phase.Running)
        {
            EditorGUILayout.LabelField("Участок", $"{runner.Route.Leg} → {runner.Route.Leg + 1}, курс {runner.Route.LegCourseDeg:F1}°");
            EditorGUILayout.LabelField("XTE", $"{runner.Route.Xte:+0.0;−0.0} м (доп. {runner.Route.XteLimit:F0} м){(runner.Route.OutsideCorridor ? "  ВНЕ КОРИДОРА" : "")}");
            EditorGUILayout.LabelField("Пройдено точек", $"{runner.Route.PassedCount} из {runner.Route.WaypointCount - 1}");
        }
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = s != null;
        if (GUILayout.Button("Загрузить")) runner.Load(s);
        GUI.enabled = runner.CurrentPhase == ScenarioRunner.Phase.Briefing;
        if (GUILayout.Button("Начать")) runner.StartExercise();
        GUI.enabled = runner.CurrentPhase == ScenarioRunner.Phase.Running;
        if (GUILayout.Button("Завершить")) runner.Abort();
        GUI.enabled = runner.Current != null;
        if (GUILayout.Button("Перезапуск")) runner.Restart();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        Repaint();
    }

    // ─────────── Ручки ───────────
    private void OnSceneGUI()
    {
        var runner = (ScenarioRunner)target;
        ScenarioDefinition s = runner.PreviewScenario;
        WorldOrigin origin = FindOrigin();
        if (s == null || origin == null) return;
        float y = WaterLevel(runner);

        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            && selWaypoint >= 0 && selWaypoint < s.route.Count)
        {
            if (s.route.Count > 2)
            {
                Undo.RecordObject(s, "Удалить путевую точку");
                s.route.RemoveAt(selWaypoint);
                selWaypoint = -1;
                EditorUtility.SetDirty(s);
            }
            else Debug.LogWarning("[Сценарий] В маршруте должно быть не меньше двух точек.");
            e.Use();
        }

        Draw(s, origin, y, runner, true);

        for (int i = 0; i < s.route.Count; i++)
        {
            WorldPosition w = s.route[i].position;
            Vector3 p = ToScene(origin, w, y);
            Handles.color = selWaypoint == i ? Color.white : RouteColor;
            if (DragPoint(s, ref w, origin, y, HandleUtility.GetHandleSize(p) * 0.1f, Handles.SphereHandleCap, out bool grabbed))
            {
                s.route[i].position = w;
                EditorUtility.SetDirty(s);
            }
            if (grabbed) { selWaypoint = i; selStart = false; }
        }

        if (s.initial != null)
        {
            WorldPosition w = s.initial.position;
            Vector3 p = ToScene(origin, w, y);
            Handles.color = selStart ? Color.white : new Color(0.3f, 1f, 0.4f);
            if (DragPoint(s, ref w, origin, y, HandleUtility.GetHandleSize(p) * 0.12f, Handles.CubeHandleCap, out bool grabbed))
            {
                s.initial.position = w;
                EditorUtility.SetDirty(s);
            }
            if (grabbed) { selStart = true; selWaypoint = -1; }
        }

        // «+» на серединах участков
        Handles.color = Color.white;
        for (int i = 0; i + 1 < s.route.Count; i++)
        {
            WorldPosition mid = (s.route[i].position + s.route[i + 1].position) * 0.5;
            Vector3 mp = ToScene(origin, mid, y);
            float ms = HandleUtility.GetHandleSize(mp) * 0.05f;
            if (Handles.Button(mp, Quaternion.identity, ms, ms * 1.4f, Handles.RectangleHandleCap))
            {
                Undo.RecordObject(s, "Вставить путевую точку");
                s.route.Insert(i + 1, new RouteWaypoint($"Точка {i + 1}", mid, s.route[i + 1].xteLimit));
                selWaypoint = i + 1;
                EditorUtility.SetDirty(s);
                break;
            }
        }
    }

    private static bool DragPoint(ScenarioDefinition s, ref WorldPosition w, WorldOrigin origin, float y, float size, Handles.CapFunction cap, out bool grabbed)
    {
        Vector3 p = ToScene(origin, w, y);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        EditorGUI.BeginChangeCheck();
        Vector3 np = Handles.Slider2D(id, p, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, size, cap, Vector2.zero, false);
        grabbed = GUIUtility.hotControl == id;
        if (!EditorGUI.EndChangeCheck()) return false;
        WorldPosition nw = origin.SceneToWorld(np);
        if (Event.current.control)
            nw = new WorldPosition(System.Math.Round(nw.East / Snap) * Snap, System.Math.Round(nw.North / Snap) * Snap);
        Undo.RecordObject(s, "Перемещение точки маршрута");
        w = nw;
        return true;
    }

    // ─────────── Отрисовка ───────────
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
    private static void DrawGizmo(ScenarioRunner runner, GizmoType type)
    {
        ScenarioDefinition s = runner.PreviewScenario;
        WorldOrigin origin = FindOrigin();
        if (s == null || origin == null) return;
        Draw(s, origin, WaterLevel(runner), runner, false);
    }

    private static void Draw(ScenarioDefinition s, WorldOrigin origin, float y, ScenarioRunner runner, bool selected)
    {
        List<RouteWaypoint> r = s.route;
        bool running = Application.isPlaying && runner.Current == s && runner.CurrentPhase == ScenarioRunner.Phase.Running;
        GUIStyle label = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = RouteColor } };

        for (int i = 0; i + 1 < r.Count; i++)
        {
            WorldPosition a = r[i].position, b = r[i + 1].position;
            WorldPosition d = b - a;
            double len = d.Magnitude;
            if (len < 1e-6) continue;
            double te = d.East / len, tn = d.North / len;
            double lim = Mathf.Max(0f, r[i + 1].xteLimit);
            var n = new WorldPosition(tn * lim, -te * lim);   // вправо на XTE доп.
            bool active = running && runner.Route.Leg == i;

            Vector3[] box =
            {
                ToScene(origin, a + n, y), ToScene(origin, b + n, y),
                ToScene(origin, b - n, y), ToScene(origin, a - n, y)
            };
            Handles.color = new Color(CorridorColor.r, CorridorColor.g, CorridorColor.b, active ? 0.16f : selected ? 0.08f : 0.05f);
            Handles.DrawAAConvexPolygon(box);
            Handles.color = new Color(CorridorColor.r, CorridorColor.g, CorridorColor.b, active ? 0.9f : CorridorColor.a);
            Handles.DrawDottedLine(box[0], box[1], 5f);
            Handles.DrawDottedLine(box[2], box[3], 5f);
            Handles.color = new Color(RouteColor.r, RouteColor.g, RouteColor.b, active ? 1f : 0.85f);
            Handles.DrawAAPolyLine(active ? 5f : 3f, ToScene(origin, a, y), ToScene(origin, b, y));
            Handles.Label(ToScene(origin, (a + b) * 0.5, y), $"{(Mathf.Atan2((float)te, (float)tn) * Mathf.Rad2Deg + 360f) % 360f:000}°  ±{lim:F0} м", label);
        }

        // Линии прохождения: биссектрисы во внутренних точках, перпендикуляр в последней
        for (int i = 1; i < r.Count; i++)
        {
            WorldPosition w = r[i].position;
            Dir(r[i - 1].position, w, out double t1e, out double t1n);
            double be = t1e, bn = t1n;
            if (i + 1 < r.Count)
            {
                Dir(w, r[i + 1].position, out double t2e, out double t2n);
                if ((t1e + t2e) * (t1e + t2e) + (t1n + t2n) * (t1n + t2n) > 1e-12) { be = t1e + t2e; bn = t1n + t2n; }
            }
            double bl = System.Math.Sqrt(be * be + bn * bn);
            double half = Mathf.Max(r[i].xteLimit, i + 1 < r.Count ? r[i + 1].xteLimit : 0f) * 1.3;
            var along = new WorldPosition(bn / bl * half, -be / bl * half);   // линия ⟂ нормали прохождения
            Handles.color = new Color(1f, 1f, 1f, 0.7f);
            Handles.DrawAAPolyLine(2f, ToScene(origin, w - along, y), ToScene(origin, w + along, y));
            Handles.color = RouteColor;
            Vector3 p = ToScene(origin, w, y);
            Handles.DrawSolidDisc(p, Vector3.up, HandleUtility.GetHandleSize(p) * 0.06f);
            Handles.Label(p + Vector3.up * HandleUtility.GetHandleSize(p) * 0.3f, $"WP{i} {r[i].name}", label);
        }
        if (r.Count > 0)
            Handles.Label(ToScene(origin, r[0].position, y), "WP0 " + r[0].name, label);

        // Старт и курс
        if (s.initial != null)
        {
            Vector3 p = ToScene(origin, s.initial.position, y);
            float size = HandleUtility.GetHandleSize(p);
            float h = s.initial.headingDeg * Mathf.Deg2Rad;
            Handles.color = new Color(0.3f, 1f, 0.4f);
            Handles.DrawAAPolyLine(4f, p, p + new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * size * 1.2f);
            Handles.Label(p + Vector3.up * size * 0.2f, $"Старт {s.initial.headingDeg:000}° {s.initial.speedKnots:F1} уз",
                          new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.3f, 1f, 0.4f) } });
        }

        // Текущее XTE у судна
        if (running && runner.navigation != null && runner.navigation.Model != null)
        {
            Vector3 ship = ToScene(origin, runner.navigation.PositionEastNorth, y);
            Handles.Label(ship + Vector3.up * HandleUtility.GetHandleSize(ship) * 0.5f,
                          $"XTE {runner.Route.Xte:+0;−0;0} м / ±{runner.Route.XteLimit:F0}",
                          new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = runner.Route.OutsideCorridor ? Color.red : Color.white } });
        }
    }

    private static void Dir(WorldPosition a, WorldPosition b, out double te, out double tn)
    {
        WorldPosition d = b - a;
        double len = d.Magnitude;
        if (len < 1e-9) { te = 0; tn = 1; return; }
        te = d.East / len; tn = d.North / len;
    }

    private static float WaterLevel(ScenarioRunner runner)
    {
        WaterAreaView v = runner.waterArea;
        return (v != null ? v.MeanLevel : 0f) + 1.0f;
    }

    private static WorldOrigin FindOrigin() =>
        WorldOrigin.Instance != null ? WorldOrigin.Instance : Object.FindAnyObjectByType<WorldOrigin>();

    private static Vector3 ToScene(WorldOrigin origin, WorldPosition p, float y)
    {
        Vector3 s = origin.WorldToScene(p);
        s.y = y;
        return s;
    }
}
