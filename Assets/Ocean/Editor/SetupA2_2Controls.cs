// ============================================================
// SetupA2_2Controls.cs — Доводка органов управления (A2.2)
// Меню: Tools/VR Bridge/Setup A2.2 Controls
//
// Делает (идемпотентно):
//   1. телеграф — один объект: Telegraph (логика, звук) становится дочерним
//      к TelegraphHead (модель); двигать теперь нужно только TelegraphHead;
//   2. рукоятка телеграфа: коллайдер ~5.5 см, подсветка при наведении/захвате;
//      штурвал: подсветка обода;
//   3. (хват лучом: точка хвата — там, куда попал луч; так делает NearFarInteractor
//      по умолчанию, SteeringWheel/EngineTelegraph следят за этой точкой);
//   4. панель навигации NavDisplay ложится на пульт на место панели погоды
//      (панель погоды по-прежнему вызывается кнопкой B);
//   5. параметры судна под модель на сцене (L ≈ 45 м): ShipParameters_Model45m.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupA2_2Controls
{
    private const string Model45Path = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";
    private const float KnobColliderRadius = 2.0f;   // в единицах шара (×0.0275 м) ≈ 5.5 см

    [MenuItem("Tools/VR Bridge/Setup A2.2 Controls")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup A2.2] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Setup A2.2 Controls");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup A2.2] Сцена «{scene.name}»:\n");

        // ── 1–3. Телеграф ──
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        Transform head = FindTelegraphHead(telegraph);
        if (telegraph == null || head == null)
        {
            log.AppendLine("  ! телеграф или TelegraphHead не найдены — сначала Setup Telegraph Model");
        }
        else
        {
            if (telegraph.transform.parent != head)
            {
                Undo.SetTransformParent(telegraph.transform, head, "Telegraph under TelegraphHead");
                telegraph.transform.localPosition = Vector3.zero;
                telegraph.transform.localRotation = Quaternion.identity;
                log.AppendLine("  + Telegraph (логика) перенесён внутрь TelegraphHead — двигайте только TelegraphHead");
            }
            else log.AppendLine("  = Telegraph уже внутри TelegraphHead");

            Transform knob = head.Find("LeverPivot/Knob");
            var sphere = knob != null ? knob.GetComponent<SphereCollider>() : null;
            if (sphere != null && !Mathf.Approximately(sphere.radius, KnobColliderRadius))
            {
                Undo.RecordObject(sphere, "Knob collider");
                sphere.radius = KnobColliderRadius;
                log.AppendLine("  + рукоятка: коллайдер ~5.5 см");
            }
            if (knob != null)
                AddHighlight(telegraph.gameObject, new[] { knob.GetComponent<Renderer>() }, "рукоятка телеграфа", log);
        }

        // Штурвал
        SteeringWheel wheel = FindInScene<SteeringWheel>(scene);
        if (wheel != null)
        {
            Transform visual = wheel.wheelVisual != null ? wheel.wheelVisual : wheel.transform;
            AddHighlight(wheel.gameObject, visual.GetComponentsInChildren<Renderer>(true), "обод штурвала", log);
        }

        // ── 4. NavDisplay на пульт вместо панели погоды ──
        PanelToggle panel = FindInScene<PanelToggle>(scene);
        Transform display = FindByName(scene, "NavDisplay");
        if (panel != null && display != null)
            PlaceDisplayOnDesk(display as RectTransform, panel.GetComponent<RectTransform>(), log);
        else
            log.AppendLine("  ! NavDisplay или панель погоды не найдены");

        // ── 5. Параметры судна под модель ──
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (nav != null)
        {
            var model45 = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45Path);
            if (model45 == null)
            {
                model45 = ScriptableObject.CreateInstance<ShipParameters>();
                model45.length = 45f;
                model45.accelerationTimeConstant = 30f;
                model45.decelerationTimeConstant = 60f;
                AssetDatabase.CreateAsset(model45, Model45Path);
                AssetDatabase.SaveAssets();
                log.AppendLine("  + создан " + Model45Path + " (L = 45 м, τ 30/60 с; заглушка, калибровка в A3)");
            }
            if (nav.parameters != model45)
            {
                Undo.RecordObject(nav, "Ship parameters");
                nav.parameters = model45;
                log.AppendLine("  + ShipNavigation использует параметры модели 45 м (ShipParameters_Default — среднее торговое судно 150 м — сохранён)");
            }
            else log.AppendLine("  = ShipNavigation уже на параметрах модели 45 м");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    private static void PlaceDisplayOnDesk(RectTransform display, RectTransform panel, StringBuilder log)
    {
        // Та же плоскость и ориентация, что у панели погоды; чуть выше пульта
        Vector3 toViewer = -panel.forward;
        if (toViewer.y < 0f) toViewer = -toViewer;
        Vector3 targetPos = panel.position + toViewer * 0.005f;
        Quaternion targetRot = panel.rotation;

        // Масштаб — чтобы панель навигации заняла место панели погоды (без искажения пропорций)
        Vector2 panelWorld = new Vector2(panel.rect.width * panel.lossyScale.x, panel.rect.height * panel.lossyScale.y);
        float s = Mathf.Min(panelWorld.x / display.rect.width, panelWorld.y / display.rect.height);
        float parentScale = display.parent != null ? display.parent.lossyScale.x : 1f;
        Vector3 targetScale = Vector3.one * (s / Mathf.Max(parentScale, 1e-6f));

        if ((display.position - targetPos).sqrMagnitude < 1e-6f && Quaternion.Angle(display.rotation, targetRot) < 0.1f
            && (display.localScale - targetScale).sqrMagnitude < 1e-10f)
        {
            log.AppendLine("  = NavDisplay уже лежит на пульте");
            return;
        }
        Undo.RecordObject(display, "Place NavDisplay on desk");
        display.SetPositionAndRotation(targetPos, targetRot);
        display.localScale = targetScale;
        log.AppendLine("  + NavDisplay лежит на пульте на месте панели погоды (погода — кнопка B)");
    }

    private static void AddHighlight(GameObject target, Renderer[] renderers, string what, StringBuilder log)
    {
        var list = new List<Renderer>();
        foreach (var r in renderers) if (r != null) list.Add(r);
        var h = target.GetComponent<GrabHighlight>();
        if (h == null)
        {
            h = Undo.AddComponent<GrabHighlight>(target);
            log.AppendLine("  + подсветка: " + what);
        }
        Undo.RecordObject(h, "Setup highlight");
        h.interactable = target.GetComponent<XRBaseInteractable>();
        h.renderers = list.ToArray();
    }

    // TelegraphHead — либо родитель Telegraph (после A2.2), либо его сосед (до A2.2)
    private static Transform FindTelegraphHead(EngineTelegraph telegraph)
    {
        if (telegraph == null) return null;
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
                if (t.name == name)
                    return t;
        return null;
    }
}
