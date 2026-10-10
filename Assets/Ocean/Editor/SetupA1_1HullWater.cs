// ============================================================
// SetupA1_1HullWater.cs — Настройка сцены для шага A1.1
// Меню: Tools/VR Bridge/Setup A1.1 Hull Water
//
// Делает (идемпотентно):
//   1. удлиняет кубы Hull и BowTip вниз — корпус уходит под воду
//      на draft метров (верх корпуса остаётся на месте);
//   2. добавляет ShipWaterInteraction на OceanRadial (маска корпуса,
//      след, бурун) и связывает его с Hull, BowTip и WorldOrigin;
//   3. сбрасывает параметры волн OceanWaves к новым значениям
//      по умолчанию (направление от ветра, рябь) — один раз.
// Требует, чтобы перед этим был выполнен Setup A1 Ocean.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupA1_1HullWater
{
    private const float Draft = 3f;   // осадка: на сколько метров корпус уходит под воду

    [MenuItem("Tools/VR Bridge/Setup A1.1 Hull Water")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup A1.1] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Setup A1.1 Hull Water");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup A1.1] Сцена «{scene.name}»:\n");

        OceanRadialMesh ocean = FindInScene<OceanRadialMesh>(scene);
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        if (ocean == null || origin == null)
        {
            Debug.LogError("[Setup A1.1] Не найден OceanRadial или WorldOrigin — сначала выполните Tools/VR Bridge/Setup A1 Ocean.");
            return;
        }
        float waterLevel = ocean.transform.position.y;

        // ── 1. Корпус под воду ──
        Transform hull = FindByName(scene, "Hull");
        Transform bowTip = FindByName(scene, "BowTip");
        ExtendDown(hull, waterLevel - Draft, log);
        ExtendDown(bowTip, waterLevel - Draft, log);

        // ── 2. Взаимодействие корпуса с водой ──
        var interaction = ocean.GetComponent<ShipWaterInteraction>();
        if (interaction == null)
        {
            interaction = Undo.AddComponent<ShipWaterInteraction>(ocean.gameObject);
            log.AppendLine("  + добавлен ShipWaterInteraction на " + ocean.name);
        }
        if (interaction.hull != hull || interaction.bowTip != bowTip || interaction.worldOrigin != origin)
        {
            Undo.RecordObject(interaction, "Setup ShipWaterInteraction");
            interaction.hull = hull;
            interaction.bowTip = bowTip;
            interaction.worldOrigin = origin;
            log.AppendLine($"  + ShipWaterInteraction: hull = {Name(hull)}, bowTip = {Name(bowTip)}, worldOrigin = {origin.name}");
        }
        else log.AppendLine("  = ShipWaterInteraction уже настроен");

        // ── 3. Новые параметры волн (один раз: признак — у всех волн нулевое смещение от ветра — данные от A1) ──
        var waves = ocean.GetComponent<OceanWaves>();
        if (waves != null)
        {
            bool oldLayout = true;
            if (waves.waves != null)
                foreach (var w in waves.waves)
                    if (Mathf.Abs(w.angleOffsetDeg) > 0.01f) oldLayout = false;
            if (oldLayout)
            {
                Undo.RecordObject(waves, "Reset OceanWaves");
                waves.ResetWavesToDefaults();
                log.AppendLine($"  + OceanWaves: параметры волн сброшены к новым значениям (ветер с {waves.windFromDeg}°, рябь)");
            }
            else log.AppendLine("  = OceanWaves: параметры волн уже новые");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    // Удлинить куб вниз до bottomY (мировая), сохранив верхнюю грань
    private static void ExtendDown(Transform box, float bottomY, StringBuilder log)
    {
        if (box == null)
        {
            log.AppendLine("  ! объект корпуса не найден (Hull/BowTip) — пропущен");
            return;
        }

        float top = box.position.y + Mathf.Abs(box.lossyScale.y) * 0.5f;
        float bottom = box.position.y - Mathf.Abs(box.lossyScale.y) * 0.5f;
        if (bottom <= bottomY + 0.01f)
        {
            log.AppendLine($"  = {box.name}: уже уходит под воду (низ {bottom:F2} м)");
            return;
        }

        Undo.RecordObject(box, "Extend hull down");
        float parentScaleY = box.parent != null ? box.parent.lossyScale.y : 1f;
        float newHeight = top - bottomY;
        Vector3 s = box.localScale;
        s.y = newHeight / Mathf.Max(parentScaleY, 1e-4f);
        box.localScale = s;
        Vector3 p = box.position;
        p.y = (top + bottomY) * 0.5f;
        box.position = p;
        log.AppendLine($"  + {box.name}: низ {bottom:F2} → {bottomY:F2} м (осадка {Draft} м, верх {top:F2} м без изменений)");
    }

    private static string Name(Object o) => o != null ? o.name : "нет";

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
                if (t.name.Trim() == name)
                    return t;
        return null;
    }
}
