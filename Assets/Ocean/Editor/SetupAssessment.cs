// ============================================================
// SetupAssessment.cs — Оценка действий обучающегося в сцене (C2) и кнопка тифона
//
// Tools/VR Bridge/Setup C2 Assessment (идемпотентно):
//   1. ассет критериев Assets/Ocean/Scenarios/Assessment_Default.asset (значения
//      по умолчанию — предварительные); назначается всем сценариям без набора;
//   2. сценарии B2-01 и B3-01: участки фарватера — «узкость» (правило 9), фраза
//      брифинга B2-01 про правую сторону; B3-01 — CPA_доп 0,05 мили у T1 и T3;
//   3. ExerciseAssessmentMonitor на объекте Scenario (рядом с ScenarioRunner и
//      ExerciseRecorder), ссылка в ScenarioPanel.
// Tools/VR Bridge/Setup Horn Button — кнопка тифона «как кнопка»: корпус, ободок,
//   грибовидный колпачок, табличка «ТИФОН»; старый диск (Cylinder) отключается.
// После запуска — Ctrl+S.
// ============================================================
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupAssessment
{
    private const string AssetPath = "Assets/Ocean/Scenarios/Assessment_Default.asset";
    private const string MaterialDir = "Assets/Ocean/Materials/Bridge";

    [MenuItem("Tools/VR Bridge/Setup C2 Assessment")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup C2] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (runner == null || nav == null)
        {
            Debug.LogError("[Setup C2] Не найдены ScenarioRunner или ShipNavigation — сначала Setup B2 Scenarios.");
            return;
        }
        var log = new StringBuilder($"[Setup C2] Сцена «{scene.name}»:\n");

        // ── 1. Ассет критериев ──
        AssessmentCriteria criteria = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(AssetPath);
        if (criteria == null)
        {
            criteria = ScriptableObject.CreateInstance<AssessmentCriteria>();
            AssetDatabase.CreateAsset(criteria, AssetPath);
            log.AppendLine($"  + {AssetPath}: {AssessmentCatalog.All.Count} критериев, значения по умолчанию (предварительные)");
        }
        else
        {
            int added = criteria.settings.AddMissing();
            EditorUtility.SetDirty(criteria);
            log.AppendLine($"  = {AssetPath} уже есть" + (added > 0 ? $", добавлено критериев: {added}" : ""));
        }

        // ── 2. Сценарии ──
        foreach (string guid in AssetDatabase.FindAssets("t:ScenarioDefinition"))
        {
            var s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (s == null) continue;
            Undo.RecordObject(s, "Setup C2 scenario");
            var changes = new StringBuilder();
            if (s.assessment == null) { s.assessment = criteria; changes.Append(" набор критериев;"); }
            if (s.id == DemoScenario.Id || s.id == DemoTrafficScenario.Id)
            {
                int narrow = 0;
                foreach (RouteWaypoint w in s.route)
                    if (w != null && (w.name == "Поворот" || w.name == "Выход из фарватера") && !w.narrowChannel) { w.narrowChannel = true; narrow++; }
                if (narrow > 0) changes.Append($" узкость (правило 9) на {narrow} участках;");
                if (s.briefing != null && s.briefing.Contains(DemoScenario.LegacyKeepAxisSentence))
                {
                    s.briefing = s.briefing.Replace(DemoScenario.LegacyKeepAxisSentence, DemoScenario.KeepRightSentence);
                    changes.Append(" брифинг — правая сторона фарватера;");
                }
            }
            if (s.id == DemoTrafficScenario.Id && s.traffic != null)
                foreach (TrafficShipDefinition t in s.traffic)
                    if (t != null && (t.id == "T1" || t.id == "T3") && t.requiredCpaNm <= 0f)
                    {
                        t.requiredCpaNm = DemoTrafficScenario.FairwayRequiredCpaNm;
                        changes.Append($" {t.id}: CPA_доп {DemoTrafficScenario.FairwayRequiredCpaNm} мили;");
                    }
            if (changes.Length > 0)
            {
                EditorUtility.SetDirty(s);
                log.AppendLine($"  + {s.id}:{changes}");
            }
        }
        AssetDatabase.SaveAssets();

        // ── 3. Монитор оценки ──
        ExerciseAssessmentMonitor monitor = FindInScene<ExerciseAssessmentMonitor>(scene);
        if (monitor == null)
        {
            monitor = Undo.AddComponent<ExerciseAssessmentMonitor>(runner.gameObject);
            log.AppendLine($"  + ExerciseAssessmentMonitor на «{runner.name}»");
        }
        else log.AppendLine("  = ExerciseAssessmentMonitor уже есть");
        Undo.RecordObject(monitor, "Setup ExerciseAssessmentMonitor");
        monitor.runner = runner;
        monitor.navigation = nav;
        monitor.recorder = FindInScene<ExerciseRecorder>(scene);
        if (monitor.recorder == null) log.AppendLine("  ! ExerciseRecorder не найден (Setup C1) — частота срезов 5 Гц, оценка в файлы не пишется");
        EditorUtility.SetDirty(monitor);

        ScenarioPanel panel = FindInScene<ScenarioPanel>(scene);
        if (panel != null)
        {
            Undo.RecordObject(panel, "Setup ScenarioPanel assessment");
            panel.assessment = monitor;
            EditorUtility.SetDirty(panel);
            log.AppendLine("  + ScenarioPanel → ExerciseAssessmentMonitor (итог на результатах, «Текущий балл» в меню Y)");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Отчёт для ВКР — Tools/VR Bridge/Assessment Report; пересчёт по записи — Session Replay.");
        Debug.Log(log.ToString());
    }

    // ─────────── Кнопка тифона ───────────
    [MenuItem("Tools/VR Bridge/Setup Horn Button")]
    private static void SetupHornButton()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Horn] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ShipHorn horn = FindInScene<ShipHorn>(scene);
        if (horn == null)
        {
            Debug.LogError("[Setup Horn] Кнопка гудка (ShipHorn) не найдена.");
            return;
        }
        var log = new StringBuilder($"[Setup Horn] «{horn.name}»:\n");
        Transform root = horn.transform;

        // Старый вид — плоский диск: отключить (не удалять)
        Transform oldDisc = root.Find("Cylinder");
        if (oldDisc != null && oldDisc.gameObject.activeSelf)
        {
            Undo.RecordObject(oldDisc.gameObject, "Disable old horn disc");
            oldDisc.gameObject.SetActive(false);
            log.AppendLine("  - старый диск «Cylinder» отключён");
        }

        Transform visual = root.Find("HornButtonModel");
        if (visual != null)
        {
            Undo.DestroyObjectImmediate(visual.gameObject);
            log.AppendLine("  = прежняя модель пересоздана");
        }
        Directory.CreateDirectory(MaterialDir);
        Material housing = Lit("Bridge_SwitchBase", new Color(0.12f, 0.13f, 0.15f), 0.4f, false, log);
        Material metal = Lit("Bridge_SwitchLever", new Color(0.7f, 0.72f, 0.75f), 0.75f, false, log);
        Material capMat = Lit("Bridge_HornCap", new Color(0.95f, 0.52f, 0.08f), 0.55f, true, log);
        Material labelBack = Lit("Bridge_SwitchLabel", new Color(0.05f, 0.05f, 0.06f), 0.3f, false, log);

        // Локальная ось Y кнопки — нормаль к наклонному пульту; +Z — к оператору (как у тумблера «СВЕТ»).
        // Корпус занимает высоту прежнего диска (−0,059…−0,019 м), колпачок — над ним.
        var model = new GameObject("HornButtonModel").transform;
        Undo.RegisterCreatedObjectUndo(model.gameObject, "Create horn button model");
        model.SetParent(root, false);
        Part(PrimitiveType.Cube, "Housing", model, new Vector3(0f, -0.039f, 0.005f), new Vector3(0.09f, 0.04f, 0.11f), housing);
        Part(PrimitiveType.Cylinder, "Bezel", model, new Vector3(0f, -0.015f, -0.012f), new Vector3(0.072f, 0.004f, 0.072f), metal);
        var cap = new GameObject("Cap").transform;
        cap.SetParent(model, false);
        cap.localPosition = new Vector3(0f, -0.002f, -0.012f);
        Renderer capBody = Part(PrimitiveType.Cylinder, "CapBody", cap, Vector3.zero, new Vector3(0.058f, 0.009f, 0.058f), capMat).GetComponent<Renderer>();
        Renderer capDome = Part(PrimitiveType.Sphere, "CapDome", cap, new Vector3(0f, 0.009f, 0f), new Vector3(0.058f, 0.014f, 0.058f), capMat).GetComponent<Renderer>();
        Part(PrimitiveType.Cube, "LabelPlate", model, new Vector3(0f, -0.0175f, 0.042f), new Vector3(0.07f, 0.003f, 0.018f), labelBack);
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(model, false);
        labelGo.transform.localPosition = new Vector3(0f, -0.0155f, 0.042f);
        labelGo.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);   // текстом вверх по наклону пульта
        var tmp = labelGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "ТИФОН";
        tmp.rectTransform.sizeDelta = new Vector2(0.064f, 0.014f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.01f;
        tmp.fontSizeMax = 2f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.85f, 0.85f, 0.8f);
        log.AppendLine("  + модель: корпус 9 × 11 см, ободок, янтарный колпачок ⌀ 58 мм, табличка «ТИФОН»");

        // Коллайдер взаимодействия — по корпусу и колпачку
        BoxCollider col = root.GetComponent<BoxCollider>();
        if (col != null)
        {
            Undo.RecordObject(col, "Horn collider");
            col.center = new Vector3(0f, -0.026f, 0f);
            col.size = new Vector3(0.1f, 0.07f, 0.12f);
            log.AppendLine("  = BoxCollider кнопки подогнан под корпус и колпачок");
        }

        HornButtonVisual vis = root.GetComponent<HornButtonVisual>();
        if (vis == null) vis = Undo.AddComponent<HornButtonVisual>(root.gameObject);
        Undo.RecordObject(vis, "Setup HornButtonVisual");
        vis.cap = cap;
        vis.glowRenderers = new[] { capBody, capDome };
        vis.horn = horn;
        EditorUtility.SetDirty(vis);
        log.AppendLine("  + HornButtonVisual: колпачок утапливается при нажатии, подсветка при наведении и гудке, тлеет ночью");
        if (root.GetComponent<XRSimpleInteractable>() == null) log.AppendLine("  ! на кнопке нет XRSimpleInteractable");

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Положение — как у прежней кнопки; если корпус «висит» над пультом или утоплен — подвинуть HornButtonModel по Y в Scene View.");
        Debug.Log(log.ToString());
    }

    private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static Material Lit(string name, Color color, float smoothness, bool emission, StringBuilder log)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { log.AppendLine($"  ! шейдер URP Lit не найден для {name}"); return null; }
        m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine($"  + материал {path}");
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
