// ============================================================
// SetupA2Navigation.cs — Настройка сцены для шага A2 (навигация)
// Меню: Tools/VR Bridge/Setup A2 Navigation
//
// Делает (идемпотентно):
//   1. ShipRoot — новый родитель Ship в начале координат: ShipNavigation
//      поворачивает его по курсу, ShipMotion качает Ship внутри;
//   2. ассет ShipParameters (заглушка) и компонент ShipNavigation;
//   3. штурвал: кольцо коллайдеров по ободу (WheelGrip) — взяться можно
//      в любой точке обода, не за центр; старый BoxCollider выключен;
//      режим «штурвал за рукой», автовозврат выключен;
//   4. временная панель индикации NavDisplay над штурвалом.
// Требует выполненных Setup A1 Ocean. После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupA2Navigation
{
    private const string ParametersPath = "Assets/Ocean/Settings/ShipParameters_Default.asset";
    private const int GripCount = 16;

    [MenuItem("Tools/VR Bridge/Setup A2 Navigation")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup A2] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        ShipMotion shipMotion = FindInScene<ShipMotion>(scene);
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        SteeringWheel wheel = FindInScene<SteeringWheel>(scene);
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        if (shipMotion == null || origin == null)
        {
            Debug.LogError("[Setup A2] Не найден Ship (ShipMotion) или WorldOrigin — сначала выполните Setup A1 Ocean.");
            return;
        }

        Undo.SetCurrentGroupName("Setup A2 Navigation");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup A2] Сцена «{scene.name}»:\n");
        Transform ship = shipMotion.transform;

        // ── 1. ShipRoot ──
        Transform shipRoot = ship.parent;
        if (shipRoot == null || shipRoot.GetComponent<ShipNavigation>() == null && shipRoot.name != "ShipRoot")
        {
            var rootGo = new GameObject("ShipRoot");
            Undo.RegisterCreatedObjectUndo(rootGo, "Create ShipRoot");
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (ship.parent != null)
                Undo.SetTransformParent(rootGo.transform, ship.parent, "Parent ShipRoot");
            rootGo.transform.SetSiblingIndex(ship.GetSiblingIndex());
            Undo.SetTransformParent(ship, rootGo.transform, "Move Ship under ShipRoot");
            shipRoot = rootGo.transform;
            log.AppendLine("  + создан ShipRoot, Ship перенесён внутрь (курс — ShipRoot, качка — Ship)");
        }
        else log.AppendLine("  = ShipRoot уже есть");

        // ── 2. Параметры и навигация ──
        var parameters = AssetDatabase.LoadAssetAtPath<ShipParameters>(ParametersPath);
        if (parameters == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Ocean/Settings"))
                AssetDatabase.CreateFolder("Assets/Ocean", "Settings");
            parameters = ScriptableObject.CreateInstance<ShipParameters>();
            AssetDatabase.CreateAsset(parameters, ParametersPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("  + создан " + ParametersPath + " (заглушка, калибруется в A3)");
        }

        var nav = shipRoot.GetComponent<ShipNavigation>();
        if (nav == null)
        {
            nav = Undo.AddComponent<ShipNavigation>(shipRoot.gameObject);
            log.AppendLine("  + добавлен ShipNavigation на ShipRoot");
        }
        if (nav.parameters != parameters || nav.wheel != wheel || nav.telegraph != telegraph || nav.worldOrigin != origin)
        {
            Undo.RecordObject(nav, "Setup ShipNavigation");
            if (nav.parameters == null) nav.parameters = parameters;
            nav.wheel = wheel;
            nav.telegraph = telegraph;
            nav.worldOrigin = origin;
            log.AppendLine("  + ShipNavigation: штурвал, телеграф, WorldOrigin, параметры");
        }

        // ── 3. Штурвал ──
        if (wheel != null)
            SetupWheel(wheel, ship, log);
        else
            log.AppendLine("  ! штурвал (SteeringWheel) не найден");

        // ── 4. Панель индикации ──
        SetupDisplay(nav, ship, wheel, log);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Тест циркуляции — F5 в Play Mode.");
        Debug.Log(log.ToString());
    }

    private static void SetupWheel(SteeringWheel wheel, Transform ship, StringBuilder log)
    {
        Undo.RecordObject(wheel, "Setup SteeringWheel");
        wheel.rimTracking = true;
        wheel.autoReturn = false;
        EditorUtility.SetDirty(wheel);

        Transform existing = ship.Find("WheelGrip");
        Transform grip = existing;
        if (grip == null)
        {
            Transform visual = wheel.wheelVisual != null ? wheel.wheelVisual : wheel.transform;
            Vector3 center = visual.position;
            Vector3 axis = wheel.WorldAxis;

            // Радиус обода — по вершинам мешей штурвала (максимальное расстояние от оси)
            float rimRadius = 0f;
            foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 d = mf.transform.TransformPoint(v) - center;
                    d -= axis * Vector3.Dot(d, axis);
                    rimRadius = Mathf.Max(rimRadius, d.magnitude);
                }
            }
            if (rimRadius < 0.05f) rimRadius = 0.25f;
            float ring = rimRadius * 0.92f;
            float sphere = Mathf.Max(0.035f, rimRadius * 0.14f);

            var gripGo = new GameObject("WheelGrip");
            Undo.RegisterCreatedObjectUndo(gripGo, "Create WheelGrip");
            grip = gripGo.transform;
            grip.SetParent(ship, false);
            grip.SetPositionAndRotation(center, Quaternion.LookRotation(axis));
            for (int i = 0; i < GripCount; i++)
            {
                float a = 2f * Mathf.PI * i / GripCount;
                var g = new GameObject("Grip_" + i);
                g.transform.SetParent(grip, false);
                g.transform.localPosition = new Vector3(ring * Mathf.Cos(a), ring * Mathf.Sin(a), 0f);
                var sc = g.AddComponent<SphereCollider>();
                sc.radius = sphere;
                sc.isTrigger = false;   // как у рукоятки телеграфа — ближний хват XRI находит наверняка
            }
            log.AppendLine($"  + WheelGrip: {GripCount} коллайдеров по ободу (радиус обода {rimRadius:F2} м)");
        }
        else log.AppendLine("  = WheelGrip уже есть");

        var interactable = wheel.GetComponent<XRSimpleInteractable>();
        if (interactable != null)
        {
            var colliders = grip.GetComponentsInChildren<SphereCollider>(true);
            var so = new SerializedObject(interactable);
            SerializedProperty list = so.FindProperty("m_Colliders");
            bool same = list != null && list.arraySize == colliders.Length;
            for (int i = 0; same && i < colliders.Length; i++)
                same = list.GetArrayElementAtIndex(i).objectReferenceValue == colliders[i];
            if (list != null && !same)
            {
                list.arraySize = colliders.Length;
                for (int i = 0; i < colliders.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
                so.ApplyModifiedProperties();
                log.AppendLine("  + штурвал: взяться можно только за обод");
            }
        }

        foreach (var box in wheel.GetComponents<BoxCollider>())
        {
            if (!box.enabled) continue;
            Undo.RecordObject(box, "Disable wheel collider");
            box.enabled = false;
            log.AppendLine("  + старый BoxCollider штурвала выключен (не удалён)");
        }
    }

    private static void SetupDisplay(ShipNavigation nav, Transform ship, SteeringWheel wheel, StringBuilder log)
    {
        if (ship.Find("NavDisplay") != null)
        {
            log.AppendLine("  = NavDisplay уже есть");
            return;
        }

        // Над штурвалом, ближе к носу (нос — −Z Ship), наклон к оператору
        Vector3 basePos = wheel != null ? ship.InverseTransformPoint(wheel.transform.position) : new Vector3(2.6f, 0.6f, -2f);
        var go = new GameObject("NavDisplay", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create NavDisplay");
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(ship, false);
        rt.localPosition = basePos + new Vector3(0f, 0.62f, -0.35f);
        Vector3 forward = new Vector3(0f, -Mathf.Sin(15f * Mathf.Deg2Rad), -Mathf.Cos(15f * Mathf.Deg2Rad));
        rt.localRotation = Quaternion.LookRotation(forward, Vector3.up);
        rt.sizeDelta = new Vector2(420f, 300f);
        rt.localScale = Vector3.one * 0.001f;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var bg = UiChild("Background", rt, Vector2.zero, new Vector2(420f, 300f));
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.07f, 0.09f, 0.85f);
        bgImage.raycastTarget = false;

        var textRt = UiChild("Values", rt, new Vector2(0f, 40f), new Vector2(390f, 210f));
        var text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 24f;
        text.color = new Color(0.85f, 0.95f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        text.text = "КУРС —";

        // Шкала руля ±35°
        var scale = UiChild("RudderScale", rt, new Vector2(0f, -105f), new Vector2(360f, 12f));
        var scaleImg = scale.gameObject.AddComponent<Image>();
        scaleImg.color = new Color(0.3f, 0.33f, 0.36f);
        scaleImg.raycastTarget = false;
        var center = UiChild("Center", scale, Vector2.zero, new Vector2(3f, 26f));
        center.gameObject.AddComponent<Image>().color = Color.white;
        Label(scale, "ЛБ 35", new Vector2(-150f, -26f));
        Label(scale, "35 ПБ", new Vector2(150f, -26f));

        var ordered = UiChild("OrderedMarker", scale, Vector2.zero, new Vector2(5f, 40f));
        ordered.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
        var actual = UiChild("ActualMarker", scale, Vector2.zero, new Vector2(12f, 22f));
        var actualImg = actual.gameObject.AddComponent<Image>();
        actualImg.color = new Color(0.2f, 0.85f, 0.35f);

        var display = go.AddComponent<BridgeNavDisplay>();
        display.navigation = nav;
        display.valuesText = text;
        display.rudderScale = scale;
        display.orderedMarker = ordered;
        display.actualMarker = actual;
        display.actualMarkerImage = actualImg;

        log.AppendLine("  + NavDisplay: панель курса/скорости/руля над штурвалом (можно передвинуть вручную)");
    }

    private static RectTransform UiChild(string name, RectTransform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private static void Label(RectTransform parent, string s, Vector2 pos)
    {
        var rt = UiChild("Label", parent, pos, new Vector2(90f, 24f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = s;
        t.fontSize = 18f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.8f, 0.85f, 0.9f);
        t.raycastTarget = false;
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
