// ============================================================
// WaterAreaViewEditor.cs — Редактирование учебной акватории в Scene View (B1)
//
// Выделите объект WaterArea в иерархии:
//   • знаки, вершины мелей/суши, точки оси фарватера, углы района — перетаскиваются
//     мышью по плоскости воды (Ctrl — привязка к сетке 10 м);
//   • кружок в центре многоугольника — перенос целиком;
//   • «+» на серединах рёбер выделенного многоугольника — вставить вершину;
//   • Delete — удалить выделенную вершину (или объект, если вершина не выбрана);
//   • в инспекторе — свойства выделенного объекта (тип знака, название, глубина…)
//     и кнопки «Добавить …» (в центр вида Scene View).
// Правки идут через Undo и сохраняются в ассет WaterArea (в том числе в Play Mode —
// знак переезжает сразу). Без выделения акватория рисуется гизмо.
// Координаты мира ↔ сцены — через WorldOrigin (позиция судна из сцены).
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaterAreaView))]
public class WaterAreaViewEditor : Editor
{
    private enum Kind { None, Aid, Shoal, Land, Fairway, Bounds }

    private static Kind selKind = Kind.None;
    private static int selIndex = -1;
    private static int selVertex = -1;
    private static bool editInScene = true;
    private static AidType newAidType = AidType.LateralPort;
    private const float Snap = 10f;

    private static readonly string[] AidTitles = BuildAidTitles();

    private static string[] BuildAidTitles()
    {
        var names = System.Enum.GetValues(typeof(AidType));
        var t = new string[names.Length];
        for (int i = 0; i < t.Length; i++) t[i] = IalaMarks.Get((AidType)i).TitleRu;
        return t;
    }

    // ─────────── Инспектор ───────────
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var view = (WaterAreaView)target;
        WaterArea area = view.area;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Редактирование акватории", EditorStyles.boldLabel);
        if (area == null)
        {
            EditorGUILayout.HelpBox("Не назначен ассет WaterArea (Tools/VR Bridge/Setup B1 Water Area).", MessageType.Info);
            return;
        }
        EditorGUILayout.HelpBox("Положение объекта WaterArea не влияет на акваторию: знаки и мели заданы в ассете " +
                                "в мировых координатах и ставятся относительно судна. Буи-модели появляются в Play Mode, " +
                                "без Play — гизмо (акватория в 1,5–8 км от судна: отдалите вид или нажмите кнопку ниже).", MessageType.Info);
        if (GUILayout.Button("Показать акваторию в Scene View")) FrameArea(view);
        editInScene = EditorGUILayout.Toggle("Ручки в Scene View", editInScene);
        EditorGUILayout.HelpBox("Перетаскивайте знаки и вершины мышью (Ctrl — сетка 10 м). Кружок в центре — перенос " +
                                "многоугольника, «+» на ребре — новая вершина, Delete — удалить выделенное.", MessageType.None);
        EditorGUILayout.LabelField($"Знаков: {area.aids.Count}, мелей: {area.shoals.Count}, суши: {area.land.Count}, фарватеров: {area.fairways.Count}");

        EditorGUILayout.BeginHorizontal();
        newAidType = (AidType)EditorGUILayout.Popup((int)newAidType, AidTitles);
        if (GUILayout.Button("Добавить знак", GUILayout.Width(120))) AddAid(area, view);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Добавить мель")) AddPolygon(area, view, Kind.Shoal);
        if (GUILayout.Button("Добавить сушу")) AddPolygon(area, view, Kind.Land);
        if (GUILayout.Button("Добавить фарватер")) AddPolygon(area, view, Kind.Fairway);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        DrawSelectionInspector(area);
    }

    private void DrawSelectionInspector(WaterArea area)
    {
        if (!SelectionValid(area))
        {
            EditorGUILayout.LabelField("Выделите объект на сцене.", EditorStyles.miniLabel);
            return;
        }
        EditorGUILayout.LabelField("Выделено", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        string name = null; int type = -1; float value = 0f; string light = null;
        switch (selKind)
        {
            case Kind.Aid:
            {
                AidToNavigation a = area.aids[selIndex];
                name = EditorGUILayout.TextField("Название", a.name);
                type = EditorGUILayout.Popup("Тип", (int)a.type, AidTitles);
                light = EditorGUILayout.TextField(new GUIContent("Огонь", "Пусто — по умолчанию: " + IalaMarks.Get(a.type).DefaultLight), a.lightCharacter);
                EditorGUILayout.LabelField("Позиция", a.position.ToString());
                break;
            }
            case Kind.Shoal:
            {
                ShoalArea s = area.shoals[selIndex];
                name = EditorGUILayout.TextField("Название", s.name);
                value = EditorGUILayout.FloatField("Глубина, м", s.depth);
                EditorGUILayout.LabelField("Вершин", s.vertices.Count.ToString());
                break;
            }
            case Kind.Land:
            {
                LandArea l = area.land[selIndex];
                name = EditorGUILayout.TextField("Название", l.name);
                value = EditorGUILayout.FloatField("Высота берега, м", l.height);
                EditorGUILayout.LabelField("Вершин", l.vertices.Count.ToString());
                break;
            }
            case Kind.Fairway:
            {
                FairwayLine f = area.fairways[selIndex];
                name = EditorGUILayout.TextField("Название", f.name);
                value = EditorGUILayout.FloatField("Ширина, м", f.width);
                EditorGUILayout.LabelField("Ширина, кбт", (f.width / WaterArea.CableMeters).ToString("F2"));
                break;
            }
            case Kind.Bounds:
                EditorGUILayout.LabelField("Границы района", $"{area.boundsMin} … {area.boundsMax}");
                break;
        }
        if (selVertex >= 0) EditorGUILayout.LabelField("Вершина", selVertex.ToString());

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(area, "Правка акватории");
            switch (selKind)
            {
                case Kind.Aid:
                    area.aids[selIndex].name = name; area.aids[selIndex].type = (AidType)type; area.aids[selIndex].lightCharacter = light; break;
                case Kind.Shoal:
                    area.shoals[selIndex].name = name; area.shoals[selIndex].depth = Mathf.Max(0f, value); break;
                case Kind.Land:
                    area.land[selIndex].name = name; area.land[selIndex].height = Mathf.Max(0.5f, value); break;
                case Kind.Fairway:
                    area.fairways[selIndex].name = name; area.fairways[selIndex].width = Mathf.Max(10f, value); break;
            }
            Changed(area, true);
        }

        EditorGUILayout.BeginHorizontal();
        if (selVertex >= 0 && GUILayout.Button("Удалить вершину")) DeleteVertex(area);
        if (selKind != Kind.Bounds && GUILayout.Button("Удалить объект")) DeleteObject(area);
        EditorGUILayout.EndHorizontal();
    }

    // ─────────── Scene View ───────────
    private void OnSceneGUI()
    {
        var view = (WaterAreaView)target;
        WaterArea area = view.area;
        WorldOrigin origin = FindOrigin(view);
        if (area == null || origin == null) return;
        float y = view.MeanLevel + 0.5f;

        Event e = Event.current;
        if (editInScene && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && SelectionValid(area))
        {
            if (selVertex >= 0) DeleteVertex(area); else if (selKind != Kind.Bounds) DeleteObject(area);
            e.Use();
        }

        DrawArea(area, origin, y, view.DangerDepth, true);
        if (!editInScene) return;

        // Знаки
        for (int i = 0; i < area.aids.Count; i++)
        {
            AidToNavigation a = area.aids[i];
            Vector3 p = ToScene(origin, a.position, y);
            float size = HandleUtility.GetHandleSize(p) * 0.12f;
            Handles.color = GizmoColor(IalaMarks.Get(a.type).MainColor);
            if (DragPoint(area, ref a.position, origin, y, size, Handles.SphereHandleCap, out bool grabbed))
                Changed(area, false);
            if (grabbed) Select(Kind.Aid, i, -1);
        }

        // Многоугольники и фарватеры
        for (int i = 0; i < area.shoals.Count; i++)
            EditPolygon(area, origin, y, Kind.Shoal, i, area.shoals[i].vertices, true);
        for (int i = 0; i < area.land.Count; i++)
            EditPolygon(area, origin, y, Kind.Land, i, area.land[i].vertices, true);
        for (int i = 0; i < area.fairways.Count; i++)
            EditPolygon(area, origin, y, Kind.Fairway, i, area.fairways[i].axis, false);

        // Границы района: два угла
        Handles.color = new Color(1f, 1f, 1f, 0.8f);
        float bs = HandleUtility.GetHandleSize(ToScene(origin, area.boundsMin, y)) * 0.1f;
        if (DragPoint(area, ref area.boundsMin, origin, y, bs, Handles.CubeHandleCap, out bool gMin)) Changed(area, false);
        if (gMin) Select(Kind.Bounds, 0, -1);
        bs = HandleUtility.GetHandleSize(ToScene(origin, area.boundsMax, y)) * 0.1f;
        if (DragPoint(area, ref area.boundsMax, origin, y, bs, Handles.CubeHandleCap, out bool gMax)) Changed(area, false);
        if (gMax) Select(Kind.Bounds, 0, -1);
    }

    private void EditPolygon(WaterArea area, WorldOrigin origin, float y, Kind kind, int index, List<WorldPosition> pts, bool closed)
    {
        if (pts == null || pts.Count == 0) return;
        bool selected = selKind == kind && selIndex == index;
        Color baseColor = kind == Kind.Land ? new Color(0.55f, 0.75f, 0.35f) : kind == Kind.Fairway ? new Color(0.3f, 0.85f, 1f) : new Color(1f, 0.6f, 0.2f);

        // Вершины
        for (int v = 0; v < pts.Count; v++)
        {
            WorldPosition w = pts[v];
            Vector3 p = ToScene(origin, w, y);
            float size = HandleUtility.GetHandleSize(p) * (selected && selVertex == v ? 0.09f : 0.06f);
            Handles.color = selected && selVertex == v ? Color.white : baseColor;
            if (DragPoint(area, ref w, origin, y, size, Handles.DotHandleCap, out bool grabbed))
            {
                pts[v] = w;
                Changed(area, false);
            }
            if (grabbed) Select(kind, index, v);
        }

        // Перенос целиком — кружок в центре
        WorldPosition c = closed ? Polygon2D.Centroid(pts) : Average(pts);
        Vector3 cp = ToScene(origin, c, y);
        float cs = HandleUtility.GetHandleSize(cp) * 0.15f;
        Handles.color = selected ? Color.white : new Color(baseColor.r, baseColor.g, baseColor.b, 0.7f);
        WorldPosition moved = c;
        if (DragPoint(area, ref moved, origin, y, cs, Handles.CircleHandleCap, out bool grabbedC))
        {
            WorldPosition d = moved - c;
            for (int v = 0; v < pts.Count; v++) pts[v] = pts[v] + d;
            Changed(area, false);
        }
        if (grabbedC) Select(kind, index, -1);

        // «+» на серединах рёбер выделенного
        if (!selected) return;
        int edges = closed ? pts.Count : pts.Count - 1;
        Handles.color = Color.white;
        for (int v = 0; v < edges; v++)
        {
            WorldPosition a = pts[v], b = pts[(v + 1) % pts.Count];
            WorldPosition mid = (a + b) * 0.5;
            Vector3 mp = ToScene(origin, mid, y);
            float ms = HandleUtility.GetHandleSize(mp) * 0.05f;
            if (Handles.Button(mp, Quaternion.identity, ms, ms * 1.4f, Handles.RectangleHandleCap))
            {
                Undo.RecordObject(area, "Вставить вершину");
                pts.Insert(v + 1, mid);
                Select(kind, index, v + 1);
                Changed(area, true);
                break;
            }
        }
    }

    // Перетаскивание точки по плоскости воды; true — точка сдвинута
    private static bool DragPoint(WaterArea area, ref WorldPosition w, WorldOrigin origin, float y, float size, Handles.CapFunction cap, out bool grabbed)
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
        Undo.RecordObject(area, "Перемещение в акватории");
        w = nw;
        return true;
    }

    // ─────────── Отрисовка (и для гизмо без выделения) ───────────
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
    private static void DrawGizmo(WaterAreaView view, GizmoType type)
    {
        if (view.area == null) return;
        WorldOrigin origin = FindOrigin(view);
        if (origin == null) return;
        DrawArea(view.area, origin, view.MeanLevel + 0.5f, view.DangerDepth, false);
    }

    private static void DrawArea(WaterArea area, WorldOrigin origin, float y, float dangerDepth, bool selected)
    {
        // Границы района
        Handles.color = new Color(1f, 1f, 1f, selected ? 0.6f : 0.3f);
        WorldPosition a = area.boundsMin, b = area.boundsMax;
        Handles.DrawDottedLines(new[]
        {
            ToScene(origin, a, y), ToScene(origin, new WorldPosition(b.East, a.North), y),
            ToScene(origin, new WorldPosition(b.East, a.North), y), ToScene(origin, b, y),
            ToScene(origin, b, y), ToScene(origin, new WorldPosition(a.East, b.North), y),
            ToScene(origin, new WorldPosition(a.East, b.North), y), ToScene(origin, a, y)
        }, 6f);

        foreach (LandArea l in area.land)
            if (l != null) Polygon(l.vertices, origin, y, new Color(0.45f, 0.6f, 0.3f), $"{l.name}\nберег {l.height:F0} м", selected);
        foreach (ShoalArea s in area.shoals)
        {
            if (s == null) continue;
            bool danger = s.depth < dangerDepth;
            Polygon(s.vertices, origin, y, danger ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.85f, 0.2f),
                    $"{s.name}\n{s.depth:F1} м", selected);
        }
        foreach (FairwayLine f in area.fairways)
        {
            if (f == null || f.axis.Count < 2) continue;
            Handles.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            Handles.DrawAAPolyLine(3f, ToSceneArray(f.axis, origin, y, false));
            Handles.color = new Color(0.3f, 0.85f, 1f, 0.5f);
            Handles.DrawAAPolyLine(2f, ToSceneArray(DemoWaterArea.OffsetPolyline(f.axis, f.width * 0.5), origin, y, false));
            Handles.DrawAAPolyLine(2f, ToSceneArray(DemoWaterArea.OffsetPolyline(f.axis, -f.width * 0.5), origin, y, false));
        }

        GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
        foreach (AidToNavigation aid in area.aids)
        {
            Vector3 p = ToScene(origin, aid.position, y);
            float size = HandleUtility.GetHandleSize(p) * 0.1f;
            MarkAppearance look = IalaMarks.Get(aid.type);
            // Столбик: низ — нижний пояс, верх — верхний (кардинальные видны двухцветными)
            Handles.color = GizmoColor(look.VerticalStripes ? look.StripeColorB : look.Bands[0].Color);
            Handles.DrawSolidDisc(p, Vector3.up, size * 0.7f);
            Handles.color = GizmoColor(look.MainColor);
            Handles.DrawAAPolyLine(4f, p, p + Vector3.up * size * 3f);
            Handles.DrawSolidDisc(p + Vector3.up * size * 3f, Vector3.up, size * 0.45f);
            Handles.Label(p + Vector3.up * size * 4f, $"{look.ShortLabel} {aid.name}", style);
        }
    }

    private static void Polygon(List<WorldPosition> pts, WorldOrigin origin, float y, Color color, string label, bool selected)
    {
        if (pts == null || pts.Count < 2) return;
        Vector3[] scene = ToSceneArray(pts, origin, y, true);
        if (pts.Count >= 3)
        {
            Handles.color = new Color(color.r, color.g, color.b, selected ? 0.18f : 0.1f);
            List<int> tris = Polygon2D.Triangulate(pts);
            for (int t = 0; t + 2 < tris.Count; t += 3)
                Handles.DrawAAConvexPolygon(scene[tris[t]], scene[tris[t + 1]], scene[tris[t + 2]]);
        }
        Handles.color = new Color(color.r, color.g, color.b, 0.95f);
        Handles.DrawAAPolyLine(3f, scene);
        WorldPosition c = Polygon2D.Centroid(pts);
        Handles.Label(ToScene(origin, c, y), label, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color }, alignment = TextAnchor.MiddleCenter });
    }

    // ─────────── Добавление и удаление ───────────
    private static void AddAid(WaterArea area, WaterAreaView view)
    {
        WorldPosition c = ViewCenter(view);
        Undo.RecordObject(area, "Добавить знак");
        area.aids.Add(new AidToNavigation(IalaMarks.Get(newAidType).TitleRu, newAidType, Mathf.Round((float)c.East), Mathf.Round((float)c.North)));
        Select(Kind.Aid, area.aids.Count - 1, -1);
        Changed(area, true);
    }

    private static void AddPolygon(WaterArea area, WaterAreaView view, Kind kind)
    {
        WorldPosition c = ViewCenter(view);
        Undo.RecordObject(area, "Добавить объект");
        double h = kind == Kind.Land ? 500 : 150;
        var square = new List<WorldPosition>
        {
            new WorldPosition(c.East - h, c.North - h), new WorldPosition(c.East + h, c.North - h),
            new WorldPosition(c.East + h, c.North + h), new WorldPosition(c.East - h, c.North + h)
        };
        switch (kind)
        {
            case Kind.Shoal:
                area.shoals.Add(new ShoalArea { name = "Мель", depth = 2f, vertices = square });
                Select(kind, area.shoals.Count - 1, -1); break;
            case Kind.Land:
                area.land.Add(new LandArea { name = "Берег", height = 10f, vertices = square });
                Select(kind, area.land.Count - 1, -1); break;
            case Kind.Fairway:
                area.fairways.Add(new FairwayLine { name = "Фарватер", axis = new List<WorldPosition> { new WorldPosition(c.East, c.North - 500), new WorldPosition(c.East, c.North + 500) } });
                Select(kind, area.fairways.Count - 1, -1); break;
        }
        Changed(area, true);
    }

    private static void DeleteVertex(WaterArea area)
    {
        List<WorldPosition> pts = SelectedPoints(area, out int min);
        if (pts == null || selVertex < 0 || selVertex >= pts.Count) return;
        if (pts.Count <= min)
        {
            Debug.LogWarning($"[Акватория] Нельзя удалить: нужно не меньше {min} вершин.");
            return;
        }
        Undo.RecordObject(area, "Удалить вершину");
        pts.RemoveAt(selVertex);
        selVertex = -1;
        Changed(area, true);
    }

    private static void DeleteObject(WaterArea area)
    {
        if (!SelectionValid(area)) return;
        Undo.RecordObject(area, "Удалить объект");
        switch (selKind)
        {
            case Kind.Aid: area.aids.RemoveAt(selIndex); break;
            case Kind.Shoal: area.shoals.RemoveAt(selIndex); break;
            case Kind.Land: area.land.RemoveAt(selIndex); break;
            case Kind.Fairway: area.fairways.RemoveAt(selIndex); break;
        }
        Select(Kind.None, -1, -1);
        Changed(area, true);
    }

    private static List<WorldPosition> SelectedPoints(WaterArea area, out int minCount)
    {
        minCount = 3;
        if (!SelectionValid(area)) return null;
        switch (selKind)
        {
            case Kind.Shoal: return area.shoals[selIndex].vertices;
            case Kind.Land: return area.land[selIndex].vertices;
            case Kind.Fairway: minCount = 2; return area.fairways[selIndex].axis;
        }
        return null;
    }

    // ─────────── Вспомогательное ───────────
    private static bool SelectionValid(WaterArea area)
    {
        switch (selKind)
        {
            case Kind.Aid: return selIndex >= 0 && selIndex < area.aids.Count;
            case Kind.Shoal: return selIndex >= 0 && selIndex < area.shoals.Count;
            case Kind.Land: return selIndex >= 0 && selIndex < area.land.Count;
            case Kind.Fairway: return selIndex >= 0 && selIndex < area.fairways.Count;
            case Kind.Bounds: return true;
        }
        return false;
    }

    private static void Select(Kind kind, int index, int vertex)
    {
        if (selKind == kind && selIndex == index && selVertex == vertex) return;
        selKind = kind; selIndex = index; selVertex = vertex;
        foreach (var ed in ActiveEditorTracker.sharedTracker.activeEditors)
            if (ed is WaterAreaViewEditor) ed.Repaint();
    }

    // structural — изменился состав (WaterAreaView пересоберёт модели); иначе только позиции
    private static void Changed(WaterArea area, bool structural)
    {
        if (structural) area.MarkChanged();
        EditorUtility.SetDirty(area);
        SceneView.RepaintAll();
    }

    private static WorldOrigin FindOrigin(WaterAreaView view)
    {
        if (view.worldOrigin != null) return view.worldOrigin;
        if (WorldOrigin.Instance != null) return WorldOrigin.Instance;
        return Object.FindAnyObjectByType<WorldOrigin>();
    }

    // Вид Scene View сверху на весь район
    private static void FrameArea(WaterAreaView view)
    {
        WorldOrigin origin = FindOrigin(view);
        SceneView sv = SceneView.lastActiveSceneView;
        if (origin == null || sv == null || view.area == null) return;
        Vector3 a = ToScene(origin, view.area.boundsMin, view.MeanLevel);
        Vector3 b = ToScene(origin, view.area.boundsMax, view.MeanLevel);
        var bounds = new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x), 10f, Mathf.Abs(b.z - a.z)));
        sv.orthographic = false;
        sv.rotation = Quaternion.Euler(90f, 0f, 0f);   // сверху, север вверх
        sv.Frame(bounds, false);
        sv.Repaint();
    }

    private static WorldPosition ViewCenter(WaterAreaView view)
    {
        WorldOrigin origin = FindOrigin(view);
        SceneView sv = SceneView.lastActiveSceneView;
        Vector3 pivot = sv != null ? sv.pivot : Vector3.zero;
        return origin != null ? origin.SceneToWorld(pivot) : new WorldPosition(pivot.x, pivot.z);
    }

    private static Vector3 ToScene(WorldOrigin origin, WorldPosition p, float y)
    {
        Vector3 s = origin.WorldToScene(p);
        s.y = y;
        return s;
    }

    private static Vector3[] ToSceneArray(IList<WorldPosition> pts, WorldOrigin origin, float y, bool closed)
    {
        var a = new Vector3[pts.Count + (closed ? 1 : 0)];
        for (int i = 0; i < pts.Count; i++) a[i] = ToScene(origin, pts[i], y);
        if (closed) a[pts.Count] = a[0];
        return a;
    }

    private static WorldPosition Average(IList<WorldPosition> pts)
    {
        double e = 0, n = 0;
        foreach (WorldPosition p in pts) { e += p.East; n += p.North; }
        return new WorldPosition(e / pts.Count, n / pts.Count);
    }

    private static Color GizmoColor(MarkColor c)
    {
        switch (c)
        {
            case MarkColor.Red: return new Color(0.95f, 0.15f, 0.12f);
            case MarkColor.Green: return new Color(0.1f, 0.8f, 0.25f);
            case MarkColor.Yellow: return new Color(1f, 0.85f, 0.1f);
            case MarkColor.Black: return new Color(0.08f, 0.08f, 0.08f);
            default: return Color.white;
        }
    }
}
