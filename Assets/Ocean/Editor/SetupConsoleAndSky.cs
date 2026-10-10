// ============================================================
// SetupConsoleAndSky.cs — Пульт и небо (A2.1)
// Меню: Tools/VR Bridge/Setup Console and Sky
//
// Делает (идемпотентно):
//   1. панель погоды: 4 кнопки сеткой 2×2 — Штиль | Шторм / Волнение | Ночь
//      (кнопка «Ночь» была смещена далеко за пределы панели);
//   2. телеграф: уменьшает модель в 2 раза и ставит на пульт справа от кнопки
//      «Шторм», основанием на плоскость пульта; рукоятку делает удобнее для хвата;
//   3. SkyWeather на WeatherSystem: затемнение неба ночью/в шторм и тучи в шторм.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SetupConsoleAndSky
{
    private const float TelegraphScale = 0.5f;
    private const float TelegraphGap = 0.06f;    // от края кнопки до центра телеграфа, м (+ половина ширины кнопки)

    [MenuItem("Tools/VR Bridge/Setup Console and Sky")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Console] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Setup Console and Sky");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup Console] Сцена «{scene.name}»:\n");

        // ── 1. Кнопки погоды ──
        PanelToggle panel = FindInScene<PanelToggle>(scene);
        RectTransform storm = null;
        if (panel != null)
        {
            RectTransform calm = FindButton(panel, "Штиль");
            storm = FindButton(panel, "Шторм");
            RectTransform moderate = FindButton(panel, "Волнение");
            RectTransform night = FindButton(panel, "Ночь");
            bool changed = false;
            changed |= Place(calm,     new Vector3(-90f,   0f, -2f));
            changed |= Place(storm,    new Vector3( 90f,   0f, -2f));
            changed |= Place(moderate, new Vector3(-90f, -80f, -2f));
            changed |= Place(night,    new Vector3( 90f, -80f, -2f));
            log.AppendLine(changed ? "  + кнопки погоды: Штиль | Шторм / Волнение | Ночь"
                                   : "  = кнопки погоды уже на местах");
            if (night == null) log.AppendLine("  ! кнопка «Ночь» не найдена");
        }
        else log.AppendLine("  ! панель погоды (PanelToggle) не найдена");

        // ── 2. Телеграф на пульт ──
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        Transform tParent = telegraph != null ? telegraph.transform.parent : null;
        Transform head = tParent == null ? null : tParent.name == "TelegraphHead" ? tParent : tParent.Find("TelegraphHead");
        if (head == null)
            log.AppendLine("  ! TelegraphHead не найден — сначала Tools/VR Bridge/Setup Telegraph Model");
        else if (storm == null)
            log.AppendLine("  ! кнопка «Шторм» не найдена — телеграф не перемещён");
        else
            PlaceTelegraph(telegraph, head, panel.transform, storm, log);

        // ── 3. Небо по погоде ──
        WeatherController weather = FindInScene<WeatherController>(scene);
        if (weather != null)
        {
            var sky = weather.GetComponent<SkyWeather>();
            if (sky == null)
            {
                sky = Undo.AddComponent<SkyWeather>(weather.gameObject);
                log.AppendLine("  + SkyWeather на " + weather.name + " (ночь темнее, тучи в шторм)");
            }
            OceanWaves waves = FindInScene<OceanWaves>(scene);
            if (sky.sunLight != weather.sunLight || sky.shipMotion != weather.shipMotion || sky.oceanWaves != waves)
            {
                Undo.RecordObject(sky, "Setup SkyWeather");
                sky.sunLight = weather.sunLight;
                sky.shipMotion = weather.shipMotion;
                sky.oceanWaves = waves;
                log.AppendLine("  + SkyWeather: солнце, качка, ветер");
            }
            else log.AppendLine("  = SkyWeather уже настроен");
        }
        else log.AppendLine("  ! WeatherController не найден");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    private static void PlaceTelegraph(EngineTelegraph telegraph, Transform head, Transform panel, RectTransform button, StringBuilder log)
    {
        // Плоскость пульта = плоскость панели. Нормаль — та, что смотрит вверх.
        Vector3 normal = -panel.forward;
        if (normal.y < 0f) normal = -normal;
        Vector3 right = panel.right;                                    // направление чтения кнопок
        Vector3 upSlope = Vector3.ProjectOnPlane(panel.up, normal).normalized;   // «верх» текста — к носу

        Vector3 center = button.TransformPoint(button.rect.center);
        float halfWidth = button.TransformVector(new Vector3(button.rect.width * 0.5f, 0f, 0f)).magnitude;
        Vector3 targetPos = center + right * (halfWidth + TelegraphGap) + normal * 0.003f;
        // −Z телеграфа (рычаг «вперёд») — к носу вдоль пульта, вверх — по нормали пульта
        Quaternion targetRot = Quaternion.LookRotation(-upSlope, normal);

        bool samePlace = (head.position - targetPos).sqrMagnitude < 1e-6f
                         && Quaternion.Angle(head.rotation, targetRot) < 0.5f
                         && Mathf.Abs(head.localScale.x - TelegraphScale) < 1e-4f;
        if (samePlace)
        {
            log.AppendLine("  = телеграф уже на пульте");
            return;
        }

        Undo.RecordObject(head, "Place telegraph");
        head.localScale = Vector3.one * TelegraphScale;
        head.SetPositionAndRotation(targetPos, targetRot);

        // Рукоятка: после уменьшения модели коллайдер побольше (~4.5 см)
        Transform knob = head.Find("LeverPivot/Knob");
        var sphere = knob != null ? knob.GetComponent<SphereCollider>() : null;
        if (sphere != null)
        {
            Undo.RecordObject(sphere, "Bigger knob collider");
            sphere.radius = 1.6f;
        }

        // Источник щелчка телеграфа — рядом с моделью
        Undo.RecordObject(telegraph.transform, "Move telegraph root");
        telegraph.transform.position = targetPos;

        log.AppendLine($"  + телеграф: масштаб ×{TelegraphScale}, на пульте справа от кнопки «Шторм»");
    }

    private static bool Place(RectTransform rt, Vector3 anchored3D)
    {
        if (rt == null || (rt.anchoredPosition3D - anchored3D).sqrMagnitude < 1e-4f)
            return false;
        Undo.RecordObject(rt, "Place weather button");
        rt.anchoredPosition3D = anchored3D;
        return true;
    }

    private static RectTransform FindButton(PanelToggle panel, string label)
    {
        foreach (Button b in panel.GetComponentsInChildren<Button>(true))
        {
            var t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null && t.text.Trim() == label)
                return b.GetComponent<RectTransform>();
        }
        return null;
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
