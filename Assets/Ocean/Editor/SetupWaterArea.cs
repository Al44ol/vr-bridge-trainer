// ============================================================
// SetupWaterArea.cs — Учебная акватория в сцене (B1)
// Меню:
//   Tools/VR Bridge/Setup B1 Water Area   — настройка сцены (идемпотентно);
//   Tools/VR Bridge/Rebuild Demo Water Area — заново построить демо (перезаписывает ассет);
//   Tools/VR Bridge/Ship To Demo Start      — судно на старт демо (E 0, N −2500, курс 000°);
//   Tools/VR Bridge/Weaker Ripple           — рябь воды слабее (detailStrength → 0,12).
//
// Setup B1 Water Area:
//   1. материалы знаков, суши и отладочных линий (URP) — Assets/Ocean/Materials/WaterArea/;
//   2. ассет Assets/Ocean/Settings/WaterArea_Demo.asset (если его нет — демо);
//   3. объект WaterArea с WaterAreaView (ассет, WorldOrigin, OceanWaves, материалы);
//   4. GroundingMonitor на ShipRoot (ShipNavigation, ShipMotion, акватория);
//   5. GroundingCameraShake на «Camera Offset» XR-рига;
//   6. полоса глубины и сигнализации над NavDisplay;
//   7. BridgeMenu (дочерний к Ship) — меню на кнопке A;
//   8. отключает JumpProvider шаблона XR (прыжок тоже на кнопке A; компонент не удаляется).
// Требует Setup A1 Ocean, A2 Navigation, Setup Seakeeping. После запуска — Ctrl+S.
// ============================================================
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SetupWaterArea
{
    private const string MaterialDir = "Assets/Ocean/Materials/WaterArea";
    private const string DemoPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";

    [MenuItem("Tools/VR Bridge/Setup B1 Water Area")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup B1] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        OceanWaves ocean = FindInScene<OceanWaves>(scene);
        if (origin == null || nav == null || motion == null || ocean == null)
        {
            Debug.LogError("[Setup B1] Не найдены WorldOrigin, ShipNavigation, ShipMotion или OceanWaves — сначала Setup A1 Ocean и A2 Navigation.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup B1 Water Area");
        var log = new StringBuilder($"[Setup B1] Сцена «{scene.name}»:\n");

        // ── 1. Материалы ──
        Directory.CreateDirectory(MaterialDir);
        var marks = new Material[5];
        marks[(int)MarkColor.Red] = EnsureMaterial("Mark_Red", new Color(0.78f, 0.07f, 0.05f), false, 0.35f, log);
        marks[(int)MarkColor.Green] = EnsureMaterial("Mark_Green", new Color(0.04f, 0.5f, 0.15f), false, 0.35f, log);
        marks[(int)MarkColor.Yellow] = EnsureMaterial("Mark_Yellow", new Color(0.98f, 0.78f, 0.06f), false, 0.35f, log);
        marks[(int)MarkColor.Black] = EnsureMaterial("Mark_Black", new Color(0.04f, 0.04f, 0.045f), false, 0.3f, log);
        marks[(int)MarkColor.White] = EnsureMaterial("Mark_White", new Color(0.92f, 0.92f, 0.9f), false, 0.35f, log);
        Material landTop = EnsureMaterial("Land_Top", new Color(0.24f, 0.32f, 0.17f), false, 0.05f, log);
        Material landSlope = EnsureMaterial("Land_Slope", new Color(0.47f, 0.42f, 0.33f), false, 0.08f, log);
        Material dbgDanger = EnsureMaterial("Debug_ShoalDanger", new Color(1f, 0.15f, 0.1f), true, 0f, log);
        Material dbgSafe = EnsureMaterial("Debug_ShoalSafe", new Color(1f, 0.85f, 0.15f), true, 0f, log);
        Material dbgFairway = EnsureMaterial("Debug_Fairway", new Color(0.25f, 0.85f, 1f), true, 0f, log);

        // ── 2. Ассет демо ──
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoPath);
        if (area == null)
        {
            area = ScriptableObject.CreateInstance<WaterArea>();
            DemoWaterArea.Fill(area);
            AssetDatabase.CreateAsset(area, DemoPath);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  + {DemoPath}: {area.aids.Count} знаков, {area.shoals.Count} мелей, {area.land.Count} участка суши");
        }
        else log.AppendLine($"  = {DemoPath} уже есть (перестроить — Tools/VR Bridge/Rebuild Demo Water Area)");

        // ── 3. WaterAreaView ──
        WaterAreaView view = FindInScene<WaterAreaView>(scene);
        if (view == null)
        {
            var go = new GameObject("WaterArea");
            Undo.RegisterCreatedObjectUndo(go, "Create WaterArea");
            SceneManager.MoveGameObjectToScene(go, scene);
            view = Undo.AddComponent<WaterAreaView>(go);
            log.AppendLine("  + объект WaterArea (WaterAreaView)");
        }
        Undo.RecordObject(view, "Setup B1");
        if (view.area == null) view.area = area;
        view.worldOrigin = origin;
        view.ocean = ocean;
        view.markMaterials = marks;
        view.landTopMaterial = landTop;
        view.landSlopeMaterial = landSlope;
        view.debugDangerMaterial = dbgDanger;
        view.debugSafeMaterial = dbgSafe;
        view.debugFairwayMaterial = dbgFairway;

        // ── 4. GroundingMonitor ──
        GroundingMonitor monitor = nav.GetComponent<GroundingMonitor>();
        if (monitor == null)
        {
            monitor = Undo.AddComponent<GroundingMonitor>(nav.gameObject);
            log.AppendLine($"  + GroundingMonitor на {nav.name}");
        }
        Undo.RecordObject(monitor, "Setup B1");
        monitor.navigation = nav;
        monitor.waterArea = view;
        monitor.shipMotion = motion;
        monitor.ComputeHullCenter();
        view.grounding = monitor;
        ShipParameters p = nav.parameters;
        log.AppendLine(p != null
            ? $"  + корпус: L {p.length:F1} м × B {p.beam:F2} м, d {p.draft:F1} м; центр от ShipRoot: к носу {monitor.hullCenterForward:F2} м, к ПБ {monitor.hullCenterStarboard:F2} м; опасно < {p.draft + monitor.underKeelClearance:F1} м"
            : "  ! у ShipNavigation нет ShipParameters");

        // ── 5. Тряска камеры ──
        Camera cam = FindMainCamera(scene);
        Transform camOffset = cam != null ? cam.transform.parent : null;
        if (camOffset != null)
        {
            GroundingCameraShake shake = camOffset.GetComponent<GroundingCameraShake>();
            if (shake == null)
            {
                shake = Undo.AddComponent<GroundingCameraShake>(camOffset.gameObject);
                log.AppendLine($"  + GroundingCameraShake на «{camOffset.name}»");
            }
            Undo.RecordObject(shake, "Setup B1");
            shake.target = camOffset;
            monitor.cameraShake = shake;
        }
        else log.AppendLine("  ! камера XR (MainCamera) не найдена — тряски не будет");

        // ── 6. Полоса глубины над NavDisplay ──
        BridgeNavDisplay display = FindInScene<BridgeNavDisplay>(scene);
        if (display != null)
        {
            Undo.RecordObject(display, "Setup B1");
            display.grounding = monitor;
            if (display.alarmText == null)
            {
                var rt = (RectTransform)display.transform;
                RectTransform strip = UiChild("DepthAlarm", rt, new Vector2(0f, 190f), new Vector2(420f, 70f));
                Undo.RegisterCreatedObjectUndo(strip.gameObject, "Create DepthAlarm");
                var img = strip.gameObject.AddComponent<Image>();
                img.color = new Color(0.05f, 0.07f, 0.09f, 0.85f);
                img.raycastTarget = false;
                RectTransform textRt = UiChild("Text", strip, Vector2.zero, new Vector2(400f, 64f));
                var text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
                text.fontSize = 24f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(0.75f, 0.85f, 0.95f);
                text.raycastTarget = false;
                text.text = "ГЛУБИНА —";
                display.alarmText = text;
                display.alarmBackground = img;
                log.AppendLine("  + полоса «ГЛУБИНА / ОПАСНО: МЕЛЬ» над NavDisplay");
            }
            else log.AppendLine("  = полоса глубины на NavDisplay уже есть");
        }
        else log.AppendLine("  ! NavDisplay не найден — сигнализация только звуком и в консоли");

        // ── 7. Меню мостика ──
        BridgeMenu menu = FindInScene<BridgeMenu>(scene);
        if (menu == null)
        {
            var go = new GameObject("BridgeMenu");
            Undo.RegisterCreatedObjectUndo(go, "Create BridgeMenu");
            go.transform.SetParent(motion.transform, false);
            Undo.AddComponent<BridgeMenu>(go);
            log.AppendLine($"  + BridgeMenu (дочерний к {motion.name}) — кнопка A");
        }
        else log.AppendLine("  = BridgeMenu уже есть");

        // ── 8. Прыжок шаблона XR (кнопка A) ──
        int jumps = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Behaviour b in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null || b.GetType().Name != "JumpProvider" || !b.enabled) continue;
                Undo.RecordObject(b, "Disable JumpProvider");
                b.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(b);
                jumps++;
                log.AppendLine($"  + JumpProvider отключён ({b.gameObject.name}) — кнопка A свободна для меню");
            }
        }
        if (jumps == 0) log.AppendLine("  = JumpProvider уже отключён или отсутствует");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Судно на старт демо — Tools/VR Bridge/Ship To Demo Start.");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Water Area")]
    private static void RebuildDemo()
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoPath);
        bool create = area == null;
        if (!create && !EditorUtility.DisplayDialog("Демонстрационная акватория",
                $"Перестроить {DemoPath}? Все правки этого ассета (перемещённые буи, мели) будут заменены исходными.",
                "Перестроить", "Отмена"))
            return;
        if (create)
        {
            area = ScriptableObject.CreateInstance<WaterArea>();
            DemoWaterArea.Fill(area);
            AssetDatabase.CreateAsset(area, DemoPath);
        }
        else
        {
            Undo.RecordObject(area, "Rebuild Demo Water Area");
            DemoWaterArea.Fill(area);
            EditorUtility.SetDirty(area);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Акватория] {DemoPath}: {area.aids.Count} знаков, {area.shoals.Count} мелей, {area.land.Count} участка суши, {area.fairways.Count} фарватер.");
    }

    // Рябь (детальные волны только в нормалях): по отзыву из шлема после B1 — слабее
    private const float RippleStrength = 0.12f;   // 0,6 (A1) → 0,35 → 0,12 («рябь как плитка» в шлеме); вернуть — 0,35

    [MenuItem("Tools/VR Bridge/Weaker Ripple")]
    private static void WeakerRipple()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Рябь] Выйдите из Play Mode (в Play правка не сохранится).");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        OceanWaves ocean = FindInScene<OceanWaves>(scene);
        if (ocean == null)
        {
            Debug.LogError("[Рябь] OceanWaves не найден.");
            return;
        }
        float old = ocean.detailStrength;
        Undo.RecordObject(ocean, "Weaker Ripple");
        ocean.detailStrength = RippleStrength;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Рябь] OceanWaves.detailStrength: {old:F2} → {RippleStrength:F2}. Сохраните сцену (Ctrl+S). " +
                  "Подобрать на глаз можно и в Play Mode (поле Detail Strength на OceanRadial), затем записать значение без Play.");
    }

    [MenuItem("Tools/VR Bridge/Ship To Demo Start")]
    private static void ShipToDemoStart()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Акватория] Выйдите из Play Mode.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (origin == null || nav == null)
        {
            Debug.LogError("[Акватория] Не найдены WorldOrigin или ShipNavigation.");
            return;
        }
        var so = new SerializedObject(origin);
        so.FindProperty("east").doubleValue = DemoWaterArea.StartPosition.East;
        so.FindProperty("north").doubleValue = DemoWaterArea.StartPosition.North;
        so.ApplyModifiedProperties();

        // Курс → поворот ShipRoot (как в ShipNavigation.Start: курс = yaw + курс носа при yaw 0)
        Vector3 bowAxis = origin.bowLocalAxis;
        Transform ship = origin.ship;
        if (ship != null && ship.parent == nav.transform) bowAxis = ship.localRotation * bowAxis;
        float bowYawOffset = Mathf.Atan2(bowAxis.x, bowAxis.z) * Mathf.Rad2Deg;
        Undo.RecordObject(nav.transform, "Ship To Demo Start");
        nav.transform.rotation = Quaternion.Euler(0f, (float)DemoWaterArea.StartHeadingDeg - bowYawOffset, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Акватория] Судно: {DemoWaterArea.StartPosition}, курс {DemoWaterArea.StartHeadingDeg:000}°. Сохраните сцену (Ctrl+S).");
    }

    // ─────────── Вспомогательное ───────────
    private static Material EnsureMaterial(string name, Color color, bool unlit, float smoothness, StringBuilder log)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null)
        {
            log.AppendLine($"  ! шейдер URP не найден для {name}");
            return null;
        }
        m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        if (!unlit) m.SetFloat("_Smoothness", smoothness);
        else if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // ленты отладки видны с обеих сторон
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine($"  + материал {path}");
        return m;
    }

    private static Camera FindMainCamera(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                if (c.CompareTag("MainCamera")) return c;
        return null;
    }

    private static RectTransform UiChild(string name, RectTransform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        return rt;
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
