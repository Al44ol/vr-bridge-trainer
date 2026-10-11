// ============================================================
// SetupTraffic.cs — Другие суда и навигационные огни в сцене (B3)
// Меню:
//   Tools/VR Bridge/Setup B3 Traffic             — настройка сцены (идемпотентно);
//   Tools/VR Bridge/Rebuild Demo Traffic Scenario — заново заполнить B3-01 (перезаписывает ассет).
//
// Setup B3 Traffic:
//   1. материалы моделей судов (Assets/Ocean/Materials/Traffic/, URP Lit) и огней
//      (NavLight.mat, шейдер VRBridgeTrainer/NavLight);
//   2. ширина и осадка в ассетах Mariner и KCS (нужны для контура столкновения и модели):
//      Mariner B = 23,16 м (MARAD, C4-S-1a), осадка — заглушка 8 м (не найдена);
//      KCS B = 32,2 м, d = 10,8 м ([7] раздела 3). На модель движения и калибровку
//      не влияют (B и d в ней не участвуют) — см. ShipParameters_Sources.md;
//   3. объект Traffic в корне сцены: TrafficController (параметры по умолчанию для
//      типов: сухогруз и танкер — Mariner, малое судно — Model45m; материалы);
//   4. объект NavLights в корне: NavLightField (огни судов и знаков, режим «авто/вкл/выкл»);
//   5. AidLightsView рядом с WaterAreaView — огни знаков МАМС;
//   6. ExerciseFailureNotice рядом с ScenarioPanel — оповещение «Упражнение провалено»;
//   7. ScenarioPanelAnchor (дочерний к Ship) — точка панели выбора при запуске: 1 м перед
//      местом появления игрока (XR Origin), на уровне поручней (1,15 м), с наклоном к глазам;
//      если уже есть — не трогается. Опустить имеющуюся точку —
//      Tools/VR Bridge/Scenario Panel To Handrail Height
//      (подвинуть мышью в Scene View);
//   8. сценарий Assets/Ocean/Scenarios/Scenario_B3-01_Traffic.asset (DemoTrafficScenario).
// Требует Setup B2 Scenarios. После запуска — Ctrl+S.
// ============================================================
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupTraffic
{
    private const string MaterialDir = "Assets/Ocean/Materials/Traffic";
    private const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_B3-01_Traffic.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string Model45mPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";
    private const string MarinerPath = "Assets/Ocean/Settings/ShipParameters_Mariner.asset";
    private const string KcsPath = "Assets/Ocean/Settings/ShipParameters_KCS.asset";

    [MenuItem("Tools/VR Bridge/Setup B3 Traffic")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup B3] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        if (runner == null || nav == null || motion == null)
        {
            Debug.LogError("[Setup B3] Не найдены ScenarioRunner, ShipNavigation или ShipMotion — сначала Setup B2 Scenarios.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup B3 Traffic");
        var log = new StringBuilder($"[Setup B3] Сцена «{scene.name}»:\n");

        // ── 1. Материалы ──
        Directory.CreateDirectory(MaterialDir);
        Material cargoHull = Lit("Traffic_Hull_Cargo", new Color(0.13f, 0.18f, 0.26f), 0.35f, log);
        Material tankerHull = Lit("Traffic_Hull_Tanker", new Color(0.12f, 0.12f, 0.13f), 0.35f, log);
        Material smallHull = Lit("Traffic_Hull_Small", new Color(0.78f, 0.25f, 0.08f), 0.4f, log);
        Material boot = Lit("Traffic_Boot", new Color(0.45f, 0.1f, 0.08f), 0.3f, log);
        Material deck = Lit("Traffic_Deck", new Color(0.3f, 0.36f, 0.3f), 0.2f, log);
        Material house = Lit("Traffic_House", new Color(0.88f, 0.88f, 0.84f), 0.35f, log);
        Material dark = Lit("Traffic_Dark", new Color(0.05f, 0.06f, 0.07f), 0.5f, log);
        Material accent = Lit("Traffic_Accent", new Color(0.24f, 0.36f, 0.44f), 0.3f, log);
        Material navLight = NavLightMaterial(log);

        // ── 2. Ширина и осадка Mariner, KCS ──
        ShipParameters model45 = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45mPath);
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>(MarinerPath);
        ShipParameters kcs = AssetDatabase.LoadAssetAtPath<ShipParameters>(KcsPath);
        if (mariner != null) SetBeamDraft(mariner, 23.16f, 8f, log, "B — MARAD (C4-S-1a); осадка — заглушка (не найдена)");
        else log.AppendLine($"  ! нет {MarinerPath}");
        if (kcs != null) SetBeamDraft(kcs, 32.2f, 10.8f, log, "B, d — Nath 2025, табл. I");
        if (model45 == null) log.AppendLine($"  ! нет {Model45mPath}");

        // ── 3. Traffic ──
        TrafficController traffic = FindInScene<TrafficController>(scene);
        if (traffic == null)
        {
            var go = new GameObject("Traffic");
            Undo.RegisterCreatedObjectUndo(go, "Create Traffic");
            traffic = Undo.AddComponent<TrafficController>(go);
            log.AppendLine("  + объект Traffic (TrafficController) в корне сцены");
        }
        else log.AppendLine("  = TrafficController уже есть");
        Undo.RecordObject(traffic, "Setup TrafficController");
        traffic.runner = runner;
        traffic.navigation = nav;
        traffic.grounding = FindInScene<GroundingMonitor>(scene);
        traffic.cameraShake = FindInScene<GroundingCameraShake>(scene);
        traffic.cargoParameters = mariner;
        traffic.tankerParameters = mariner;
        traffic.smallParameters = model45;
        traffic.cargoHull = cargoHull; traffic.tankerHull = tankerHull; traffic.smallHull = smallHull;
        traffic.bootMaterial = boot; traffic.deckMaterial = deck; traffic.houseMaterial = house;
        traffic.darkMaterial = dark; traffic.accentMaterial = accent;
        EditorUtility.SetDirty(traffic);

        // ── 4. NavLights ──
        NavLightField field = FindInScene<NavLightField>(scene);
        if (field == null)
        {
            var go = new GameObject("NavLights");
            Undo.RegisterCreatedObjectUndo(go, "Create NavLights");
            field = Undo.AddComponent<NavLightField>(go);
            log.AppendLine("  + объект NavLights (NavLightField) в корне сцены — все огни одним мешем");
        }
        else log.AppendLine("  = NavLightField уже есть");
        Undo.RecordObject(field, "Setup NavLightField");
        field.material = navLight;
        EditorUtility.SetDirty(field);

        // ── 5. Огни знаков ──
        WaterAreaView areaView = FindInScene<WaterAreaView>(scene);
        if (areaView != null)
        {
            AidLightsView aid = areaView.GetComponent<AidLightsView>();
            if (aid == null)
            {
                aid = Undo.AddComponent<AidLightsView>(areaView.gameObject);
                log.AppendLine($"  + AidLightsView на «{areaView.name}» — огни знаков МАМС");
            }
            Undo.RecordObject(aid, "Setup AidLightsView");
            aid.view = areaView;
            EditorUtility.SetDirty(aid);
        }
        else log.AppendLine("  ! WaterAreaView не найден — огней знаков не будет");

        // ── 6. Оповещение о провале и 7. точка панели выбора ──
        ScenarioPanel panel = FindInScene<ScenarioPanel>(scene);
        if (panel != null)
        {
            if (panel.GetComponent<ExerciseFailureNotice>() == null)
            {
                Undo.AddComponent<ExerciseFailureNotice>(panel.gameObject);
                log.AppendLine("  + ExerciseFailureNotice (оповещение «Упражнение провалено»; меню A → «Сценарий»)");
            }
            Transform anchor = motion.transform.Find("ScenarioPanelAnchor");
            if (anchor == null)
            {
                var go = new GameObject("ScenarioPanelAnchor");
                Undo.RegisterCreatedObjectUndo(go, "Create ScenarioPanelAnchor");
                anchor = go.transform;
                PlaceAnchor(scene, anchor, out string where);
                anchor.SetParent(motion.transform, true);
                log.AppendLine($"  + ScenarioPanelAnchor (дочерний к {motion.name}) {where} — подвинуть можно в Scene View");
            }
            else log.AppendLine("  = ScenarioPanelAnchor уже есть (положение не меняется)");
            Undo.RecordObject(panel, "Setup ScenarioPanel anchor");
            panel.startAnchor = anchor;
            EditorUtility.SetDirty(panel);
        }
        else log.AppendLine("  ! ScenarioPanel не найден — оповещение и точка панели не настроены");

        // ── 8. Сценарий B3-01 ──
        ScenarioDefinition demo = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (demo == null)
        {
            demo = ScriptableObject.CreateInstance<ScenarioDefinition>();
            if (FillDemo(demo, log))
            {
                AssetDatabase.CreateAsset(demo, ScenarioPath);
                log.AppendLine($"  + {ScenarioPath}: «{demo.title}», судов {demo.traffic.Count}");
            }
        }
        else log.AppendLine($"  = {ScenarioPath} уже есть");
        AssetDatabase.SaveAssets();
        Undo.RecordObject(runner, "Scenario library");
        runner.scenarios = ScenarioLibrarySync.FindAll();
        EditorUtility.SetDirty(runner);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). В Play Mode: выбор сценария → B3-01; меню A → «Отладка» → «Проверка огней».");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Traffic Scenario")]
    private static void RebuildDemo()
    {
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s == null)
        {
            Debug.LogWarning($"[Сценарий] Нет {ScenarioPath} — запустите Tools/VR Bridge/Setup B3 Traffic.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Сценарий B3-01",
                $"Заполнить {ScenarioPath} заново? Все правки (суда, маршрут, брифинг) будут заменены исходными.", "Заполнить", "Отмена"))
            return;
        Undo.RecordObject(s, "Rebuild Demo Traffic Scenario");
        var log = new StringBuilder();
        if (!FillDemo(s, log)) return;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Сценарий] {ScenarioPath}: «{s.title}», судов {s.traffic.Count}. {log}");
    }

    private static bool FillDemo(ScenarioDefinition s, StringBuilder log)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters own = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45mPath);
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>(MarinerPath);
        if (area == null || own == null || mariner == null)
        {
            Debug.LogError($"[Сценарий] Нужны {DemoAreaPath}, {Model45mPath}, {MarinerPath} (Setup B1, калибровка A3.2).");
            return false;
        }
        DemoTrafficScenario.Fill(s, area, own, mariner, mariner, own);
        foreach (string p in s.Validate()) log.AppendLine($"  ! {p}");
        return true;
    }

    // ─────────── Вспомогательное ───────────
    private static void SetBeamDraft(ShipParameters p, float beam, float draft, StringBuilder log, string source)
    {
        if (Mathf.Approximately(p.beam, beam) && Mathf.Approximately(p.draft, draft))
        {
            log.AppendLine($"  = {p.name}: B {beam} м, d {draft} м уже заданы");
            return;
        }
        Undo.RecordObject(p, "Beam and draft");
        p.beam = beam;
        p.draft = draft;
        EditorUtility.SetDirty(p);
        log.AppendLine($"  + {p.name}: B = {beam} м, d = {draft} м ({source})");
    }

    // Центр панели над полом — примерно на уровне поручней (после первой проверки в шлеме; было 1,5 м)
    private const float AnchorHeight = 1.15f;
    private const float EyeHeight = 1.6f;

    // 1 м перед местом появления игрока (XR Origin), на уровне поручней, наклон — к глазам
    private static void PlaceAnchor(Scene scene, Transform anchor, out string where)
    {
        Transform rig = FindRig(scene);
        if (rig == null)
        {
            anchor.SetPositionAndRotation(new Vector3(0f, AnchorHeight, 1f), Quaternion.identity);
            where = $"в (0; {AnchorHeight}; 1) — XR Origin не найден";
            return;
        }
        Vector3 fwd = rig.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 pos = rig.position + fwd * 1.0f + Vector3.up * AnchorHeight;
        anchor.SetPositionAndRotation(pos, FacingEye(rig, pos));
        where = $"в 1 м перед «{rig.name}» на высоте {AnchorHeight} м";
    }

    /// <summary>Опустить точку панели выбора на уровень поручней (положение по горизонтали сохраняется).</summary>
    [MenuItem("Tools/VR Bridge/Scenario Panel To Handrail Height")]
    private static void AnchorToHandrail()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Панель] Выйдите из Play Mode."); return; }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioPanel panel = FindInScene<ScenarioPanel>(scene);
        Transform rig = FindRig(scene);
        if (panel == null || panel.startAnchor == null || rig == null)
        {
            Debug.LogError("[Панель] Нет ScenarioPanel.startAnchor или XR Origin — сначала Setup B3 Traffic.");
            return;
        }
        Transform a = panel.startAnchor;
        Undo.RecordObject(a, "Scenario Panel To Handrail Height");
        Vector3 pos = a.position;
        float was = pos.y - rig.position.y;
        pos.y = rig.position.y + AnchorHeight;
        a.SetPositionAndRotation(pos, FacingEye(rig, pos));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Панель] Точка панели выбора: {was:F2} → {AnchorHeight:F2} м над полом, наклон к глазам ({EyeHeight} м). Сохраните сцену (Ctrl+S).");
    }

    // Панель смотрит «от глаз»: ось +Z — из точки глаз игрока на панель (как ScenarioPanel.PositionInFrontOfPlayer)
    private static Quaternion FacingEye(Transform rig, Vector3 panelPos)
    {
        Vector3 eye = rig.position + Vector3.up * EyeHeight;
        Vector3 dir = panelPos - eye;
        if (dir.sqrMagnitude < 1e-4f) dir = rig.forward;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private static Transform FindRig(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "XROrigin") return mb.transform;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                if (c.CompareTag("MainCamera")) return c.transform;
        return null;
    }

    private static Material Lit(string name, Color color, float smoothness, StringBuilder log)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { log.AppendLine($"  ! шейдер URP Lit не найден для {name}"); return null; }
        m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine($"  + материал {path}");
        return m;
    }

    private static Material NavLightMaterial(StringBuilder log)
    {
        string path = $"{MaterialDir}/NavLight.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find("VRBridgeTrainer/NavLight");
        if (shader == null) { log.AppendLine("  ! шейдер VRBridgeTrainer/NavLight не найден (Assets/Ocean/Shaders/NavLight.shader)"); return null; }
        m = new Material(shader) { name = "NavLight", renderQueue = 3050 };
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine($"  + материал {path} (огни, очередь 3050 — после воды)");
        return m;
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
}
