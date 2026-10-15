// ============================================================
// SetupBerth.cs — Сцена швартовки, часть 1 (запрос 19.2б)
// Меню: Tools/VR Bridge/Setup 19.2 Berth → затем Ctrl+S
//
// Делает (идемпотентно):
//   1. MooringController на ShipRoot (рядом с ShipNavigation): причал, кранцы, концы, подруливающее;
//   2. причал в акватории: если в ассете WaterAreaView.area нет причалов — добавляет демо-причал
//      (DemoWaterArea.Berth; то же делает Rebuild Demo Water Area);
//   3. материалы причала (Assets/Ocean/Materials/Berth_*) → WaterAreaView;
//   4. рычаг подруливающего ThrusterHead на пульте — слева от TelegraphHead (подвинуть в Scene View):
//      корпус, шкала ЛБ 100 · 50 · 0 · 50 · 100 ПБ, ось LeverPivot (качается вбок), рукоятка Knob —
//      единственный коллайдер взаимодействия; ThrusterLever + XRSimpleInteractable + GrabHighlight;
//      масштаб модели 0,5 (решение пользователя; уже построенный в масштабе 1 — уменьшается);
//   5. Ship/ControlMenu — меню «Управление» на кнопке B правого контроллера (BridgeMenu + ShipControlOrders);
//   6. проверка: корпус Hull в сцене против L × B ассета (контакт с кранцами считается по ассету),
//      кнопка B не занята панелью погоды (PanelToggle.controllerButton).
// Чтобы перестроить рычаг — удалите ThrusterHead и запустите пункт снова.
// ============================================================
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupBerth
{
    private const string HeadName = "ThrusterHead";
    private const string MenuObjectName = "ControlMenu";
    private const string MaterialFolder = "Assets/Ocean/Materials";
    /// <summary>Рычаг — слева от телеграфа с места оператора (оператор смотрит в −Z, его левая сторона — +X), м.</summary>
    private static readonly Vector3 OffsetFromTelegraph = new Vector3(0.28f, 0f, 0f);

    // Размеры модели рычага (м)
    private const float BaseWidth = 0.20f, BaseDepth = 0.12f, BaseHeight = 0.07f;
    private const float PivotHeight = 0.075f, LeverLength = 0.17f, FullAngle = 40f;

    [MenuItem("Tools/VR Bridge/Setup 19.2 Berth")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup 19.2] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (nav == null)
        {
            Debug.LogError("[Setup 19.2] В сцене нет ShipNavigation.");
            return;
        }
        Undo.SetCurrentGroupName("Setup 19.2 Berth");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup 19.2] Сцена «{scene.name}»:\n");

        // ── 1. MooringController ──
        MooringController mc = nav.GetComponent<MooringController>();
        if (mc == null)
        {
            mc = Undo.AddComponent<MooringController>(nav.gameObject);
            log.AppendLine($"  + {nav.name}: MooringController (причал, кранцы, концы, подруливающее; только модель 3 СС)");
        }
        else log.AppendLine("  = MooringController уже есть");
        Undo.RecordObject(mc, "Setup MooringController");
        mc.navigation = nav;
        if (mc.grounding == null) mc.grounding = FindInScene<GroundingMonitor>(scene);

        // ── 2. Причал в акватории ──
        WaterAreaView view = FindInScene<WaterAreaView>(scene);
        WaterArea area = view != null ? view.area : null;
        if (area == null) log.AppendLine("  ! нет WaterAreaView с ассетом акватории — причала не будет (Setup B1 Water Area)");
        else
        {
            if (area.berths == null) area.berths = new System.Collections.Generic.List<BerthArea>();
            if (area.berths.Count == 0)
            {
                Undo.RecordObject(area, "Add demo berth");
                area.berths.Add(DemoWaterArea.Berth());
                area.MarkChanged();
                EditorUtility.SetDirty(area);
                log.AppendLine($"  + в «{area.name}» добавлен демо-причал");
            }
            foreach (BerthArea b in area.berths)
                log.AppendLine($"  = причал «{b.name}»: A ({b.a.East:F0}; {b.a.North:F0}) → B ({b.b.East:F0}; {b.b.North:F0}), {b.Length:F0} м, курс кромки {b.BearingDeg:F0}°, " +
                               $"кранцев {b.FenderPositions().Count} (D {b.fenderOuterDiameterM:F2} м, R_н {b.fenderRatedReactionKn:F0} кН), кнехтов {b.BollardPositions().Count}, кромка {b.quayHeightM:F1} м над водой");
            mc.area = area;
        }
        EditorUtility.SetDirty(mc);

        // ── 3. Материалы причала ──
        if (view != null)
        {
            Undo.RecordObject(view, "Berth materials");
            if (view.berthConcreteMaterial == null) view.berthConcreteMaterial = GetMaterial("Berth_Concrete", new Color(0.55f, 0.55f, 0.53f), 0.15f);
            if (view.berthEdgeMaterial == null) view.berthEdgeMaterial = GetMaterial("Berth_Edge", new Color(0.9f, 0.75f, 0.1f), 0.3f);
            if (view.fenderMaterial == null) view.fenderMaterial = GetMaterial("Berth_Fender", new Color(0.06f, 0.06f, 0.06f), 0.35f);
            if (view.bollardMaterial == null) view.bollardMaterial = GetMaterial("Berth_Bollard", new Color(0.18f, 0.2f, 0.22f), 0.5f);
            EditorUtility.SetDirty(view);
            log.AppendLine("  = материалы причала: Assets/Ocean/Materials/Berth_*");
        }

        // ── 4. Рычаг подруливающего ──
        ThrusterLever lever = FindInScene<ThrusterLever>(scene);
        if (lever == null)
        {
            Transform telegraphHead = nav.telegraph != null ? FindTelegraphHead(nav.telegraph) : null;
            Transform parent = telegraphHead != null ? telegraphHead.parent : (nav.telegraph != null ? nav.telegraph.transform.parent : null);
            if (parent == null)
                log.AppendLine("  ! не найден TelegraphHead — рычаг подруливающего не построен (меню B управляет подруливающим и без него)");
            else
            {
                Vector3 pos = (telegraphHead != null ? telegraphHead.localPosition : nav.telegraph.transform.localPosition) + OffsetFromTelegraph;
                lever = BuildLever(parent, pos, log);
            }
        }
        else log.AppendLine("  = рычаг подруливающего уже есть (" + lever.name + ")");
        if (lever != null) ApplyHeadScale(lever.transform, log);
        if (lever != null)
        {
            Undo.RecordObject(mc, "Link thruster lever");
            mc.thrusterLever = lever;
            EditorUtility.SetDirty(mc);
        }

        // ── 5. Меню «Управление» (B) ──
        BridgeMenu control = null, main = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BridgeMenu m in root.GetComponentsInChildren<BridgeMenu>(true))
            {
                if (m.menuId == ShipControlOrders.MenuId) control = m;
                else if (string.IsNullOrEmpty(m.menuId)) main = m;
            }
        if (control == null)
        {
            var go = new GameObject(MenuObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create ControlMenu");
            Transform ship = main != null ? main.transform.parent : (nav.worldOrigin != null && nav.worldOrigin.ship != null ? nav.worldOrigin.ship : nav.transform);
            go.transform.SetParent(ship, false);
            control = go.AddComponent<BridgeMenu>();
            control.menuId = ShipControlOrders.MenuId;
            control.menuTitle = "Управление (команды)";
            control.toggleBinding = ShipControlOrders.ToggleBinding;
            if (main != null)
            {
                control.distanceFromHead = main.distanceFromHead;
                control.downOffset = main.downOffset;
                control.canvasScale = main.canvasScale;
            }
            log.AppendLine($"  + {ship.name}/{MenuObjectName} — меню «Управление» на кнопке B правого контроллера");
        }
        else log.AppendLine("  = меню «Управление» уже есть");
        ShipControlOrders orders = control.GetComponent<ShipControlOrders>();
        if (orders == null)
        {
            orders = Undo.AddComponent<ShipControlOrders>(control.gameObject);
            log.AppendLine("  + ShipControlOrders: руль, машина, подруливающее");
        }
        Undo.RecordObject(orders, "Setup ShipControlOrders");
        orders.navigation = nav;
        orders.mooring = mc;
        EditorUtility.SetDirty(orders);

        // ── 6. Проверки ──
        PanelToggle weather = FindInScene<PanelToggle>(scene);
        if (weather != null && weather.controllerButton)
            log.AppendLine("  ! PanelToggle.controllerButton включён — кнопка B откроет и панель погоды; выключите (погода — меню Y)");
        CheckHull(scene, nav, log);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        log.Append("Готово. Сохраните сцену (Ctrl+S). Проверка: кнопка B (меню «Управление») → «Швартовка» → «Судно к причалу».");
        Debug.Log(log.ToString());
    }

    // ─────────── Рычаг ───────────

    private static ThrusterLever BuildLever(Transform parent, Vector3 localPos, StringBuilder log)
    {
        Material housing = GetMaterial("Telegraph_Housing", new Color(0.16f, 0.18f, 0.21f), 0.4f);
        Material plate = GetMaterial("Telegraph_Drum", new Color(0.82f, 0.84f, 0.86f), 0.5f);
        Material port = GetMaterial("Telegraph_Astern", new Color(0.75f, 0.12f, 0.12f), 0.3f);
        Material stbd = GetMaterial("Telegraph_Ahead", new Color(0.10f, 0.55f, 0.25f), 0.3f);
        Material zero = GetMaterial("Telegraph_Stop", new Color(0.95f, 0.95f, 0.95f), 0.3f);
        Material leverMat = GetMaterial("Telegraph_Lever", new Color(0.62f, 0.65f, 0.68f), 0.7f);
        Material knobMat = GetMaterial("Telegraph_Knob", new Color(0.08f, 0.08f, 0.10f), 0.6f);

        var headGo = new GameObject(HeadName);
        Undo.RegisterCreatedObjectUndo(headGo, "Create ThrusterHead");
        Transform head = headGo.transform;
        head.SetParent(parent, false);
        head.localPosition = localPos;
        head.localRotation = Quaternion.identity;

        Part(PrimitiveType.Cube, "Housing", head, new Vector3(0f, 0.5f * BaseHeight, 0f), Quaternion.identity,
             new Vector3(BaseWidth, BaseHeight, BaseDepth), housing);

        // Шкала-дуга за рычагом: сектор на каждое положение; правый борт оператора — −X (он смотрит в −Z)
        var scale = new GameObject("Scale").transform;
        scale.SetParent(head, false);
        scale.localPosition = new Vector3(0f, PivotHeight, 0f);
        const float scaleRadius = 0.09f;
        for (int i = 0; i < ThrusterOrders.Count; i++)
        {
            double cmd = ThrusterOrders.Command(i);
            float angle = FullAngle * (float)cmd;                                    // > 0 — на ПБ
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.forward);
            Vector3 radial = rot * Vector3.up;
            Material mat = cmd > 0 ? stbd : cmd < 0 ? port : zero;
            Part(PrimitiveType.Cube, "Sector_" + i, scale, radial * scaleRadius + new Vector3(0f, 0f, -0.025f), rot,
                 new Vector3(0.028f, 0.004f, 0.02f), mat);
            var labelGo = new GameObject("Label_" + i);
            labelGo.transform.SetParent(scale, false);
            labelGo.transform.localPosition = radial * (scaleRadius + 0.022f) + new Vector3(0f, 0f, -0.025f);
            labelGo.transform.localRotation = Quaternion.LookRotation(Vector3.back, radial);   // лицом к оператору (он со стороны +Z)
            var text = labelGo.AddComponent<TextMeshPro>();
            text.text = ThrusterOrders.ScaleLabel(i);
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 0.12f;
            text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(0.04f, 0.02f);
            text.raycastTarget = false;
        }
        foreach (var (label, x) in new[] { ("ЛБ", 1f), ("ПБ", -1f) })
        {
            var go = new GameObject("Side_" + label);
            go.transform.SetParent(head, false);
            go.transform.localPosition = new Vector3(x * 0.075f, BaseHeight + 0.004f, 0.045f);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);   // лежит на корпусе, читается с места оператора
            var t = go.AddComponent<TextMeshPro>();
            t.text = label;
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = 0.16f;
            t.color = x > 0 ? new Color(1f, 0.45f, 0.4f) : new Color(0.45f, 1f, 0.55f);
            t.rectTransform.sizeDelta = new Vector2(0.05f, 0.025f);
            t.raycastTarget = false;
        }
        var caption = new GameObject("Caption");
        caption.transform.SetParent(head, false);
        caption.transform.localPosition = new Vector3(0f, BaseHeight + 0.004f, 0.05f);
        caption.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);
        var ct = caption.AddComponent<TextMeshPro>();
        ct.text = "ПОДРУЛ.";
        ct.alignment = TextAlignmentOptions.Center;
        ct.fontSize = 0.12f;
        ct.color = Color.white;
        ct.rectTransform.sizeDelta = new Vector2(0.08f, 0.02f);
        ct.raycastTarget = false;

        // Рычаг: ось — продольная (Z), качается вбок
        var pivotGo = new GameObject("LeverPivot");
        Transform pivot = pivotGo.transform;
        pivot.SetParent(head, false);
        pivot.localPosition = new Vector3(0f, PivotHeight, 0f);
        pivot.localRotation = Quaternion.identity;
        Transform hub = Part(PrimitiveType.Cylinder, "Hub", pivot, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.04f, 0.012f, 0.04f), leverMat);
        Transform arm = Part(PrimitiveType.Cylinder, "Arm", pivot, new Vector3(0f, 0.5f * LeverLength, 0f), Quaternion.identity,
                             new Vector3(0.016f, 0.5f * LeverLength, 0.016f), leverMat);
        Transform knob = Part(PrimitiveType.Sphere, "Knob", pivot, new Vector3(0f, LeverLength, 0f), Quaternion.identity,
                              Vector3.one * 0.045f, knobMat, keepCollider: true);
        var sphere = knob.GetComponent<SphereCollider>();
        sphere.radius = 0.9f;   // ~4 см — легко попасть рукой
        _ = hub;

        var interactable = Undo.AddComponent<XRSimpleInteractable>(headGo);
        var so = new SerializedObject(interactable);
        SerializedProperty colliders = so.FindProperty("m_Colliders");
        if (colliders != null)
        {
            colliders.arraySize = 1;
            colliders.GetArrayElementAtIndex(0).objectReferenceValue = sphere;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        var lever = Undo.AddComponent<ThrusterLever>(headGo);
        lever.leverVisual = pivot;
        lever.leverRotationAxis = Vector3.forward;
        lever.fullAngle = FullAngle;
        var highlight = Undo.AddComponent<GrabHighlight>(headGo);
        highlight.interactable = interactable;
        highlight.renderers = new Renderer[] { knob.GetComponent<Renderer>(), arm.GetComponent<Renderer>() };
        head.localScale = Vector3.one * HeadScale;
        sphere.radius = 0.9f / HeadScale * 0.75f;
        log.AppendLine($"  + {parent.name}/{HeadName} (масштаб {HeadScale}) — рычаг подруливающего слева от телеграфа (локально {localPos.x:F2}; {localPos.y:F2}; {localPos.z:F2}) — подвиньте в Scene View, если мешает");
        return lever;
    }

    /// <summary>
    /// Масштаб модели рычага (решение пользователя 15.10.2026: вдвое меньше первой постройки).
    /// Применяется, только пока ThrusterHead в масштабе 1 (как построен) — ручная правка масштаба не затирается.
    /// Коллайдер рукоятки — с запасом: ≈ 3 см радиусом в мире, чтобы за маленькую рукоятку было легко взяться.
    /// </summary>
    private const float HeadScale = 0.5f;

    private static void ApplyHeadScale(Transform head, StringBuilder log)
    {
        if ((head.localScale - Vector3.one).sqrMagnitude > 1e-6f)
        {
            log.AppendLine($"  = масштаб {head.name}: {head.localScale.x:0.##} (не меняется)");
            return;
        }
        Undo.RecordObject(head, "Scale ThrusterHead");
        head.localScale = Vector3.one * HeadScale;
        Transform knob = head.Find("LeverPivot/Knob");
        var sphere = knob != null ? knob.GetComponent<SphereCollider>() : null;
        if (sphere != null)
        {
            Undo.RecordObject(sphere, "Knob collider");
            sphere.radius = 0.9f / HeadScale * 0.75f;   // 0,9 при масштабе 1 → 1,35: в мире ≈ 3 см вместо 2 см
        }
        log.AppendLine($"  + {head.name}: масштаб {HeadScale} (вдвое меньше), коллайдер рукоятки увеличен до ≈ 3 см");
    }

    // ─────────── Проверка корпуса ───────────

    private static void CheckHull(Scene scene, ShipNavigation nav, StringBuilder log)
    {
        ShipParameters p = nav.parameters;
        Transform hull = FindByName(scene, "Hull");
        if (p == null || hull == null) { log.AppendLine("  ? корпус Hull или ShipParameters не найден — сверка размеров пропущена"); return; }
        Transform ship = hull.parent != null ? hull.parent : hull;
        // Габарит Hull в системе Ship: углы ограничивающего ящика меша
        var mf = hull.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { log.AppendLine("  ? у Hull нет меша — сверка размеров пропущена"); return; }
        Bounds mb = mf.sharedMesh.bounds;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
            Vector3 l = ship.InverseTransformPoint(hull.TransformPoint(c));
            min = Vector3.Min(min, l);
            max = Vector3.Max(max, l);
        }
        float width = max.x - min.x, length = max.z - min.z;
        log.AppendLine($"  = корпус Hull в сцене: ширина {width:F2} м (B ассета {p.beam:F2}), длина {length:F1} м (L ассета {p.length:F1}; нос — BowTip отдельно). " +
                       "Контакт с кранцами считается по L × B ассета; если борт Hull в шлеме не доходит до кранцев или входит в них — подогнать Hull или beam");
    }

    // ─────────── Общее ───────────

    private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Quaternion localRot,
                                  Vector3 localScale, Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    private static Material GetMaterial(string name, Color color, float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Ocean", "Materials");
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // TelegraphHead — родитель Telegraph (после A2.2) или его сосед
    private static Transform FindTelegraphHead(EngineTelegraph telegraph)
    {
        Transform p = telegraph.transform.parent;
        if (p == null) return null;
        if (p.name == "TelegraphHead") return p;
        return p.Find("TelegraphHead");
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    private static Transform FindByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }
}
