// ============================================================
// SetupSeakeeping.cs — Настройка качки на волнах (запрос 4)
// Меню: Tools/VR Bridge/Setup Seakeeping
//
// Делает (идемпотентно):
//   1. ShipMotion: режим Waves, ссылки на OceanWaves и ShipNavigation;
//      центр качки pivotLocal — середина корпуса (Hull + BowTip) по длине
//      и ширине на уровне воды (y объекта океана);
//   2. ShipParameters_Model45m (если он на ShipNavigation): ширина и осадка
//      по корпусу сцены, допущения GM, C_B, C_WP, затухания, периоды — по формулам.
// Требует выполненных Setup A1 Ocean, A1.1 Hull Water, A2 Navigation.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupSeakeeping
{
    // Допущения для судна сцены (мостик 45 м) — данных формуляра нет
    private const float AssumedGM = 0.8f;
    private const float AssumedCb = 0.6f;
    private const float AssumedCwp = 0.75f;

    [MenuItem("Tools/VR Bridge/Setup Seakeeping")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Seakeeping] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        OceanWaves ocean = FindInScene<OceanWaves>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        ShipWaterInteraction water = FindInScene<ShipWaterInteraction>(scene);
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        if (motion == null || ocean == null || water == null || water.hull == null)
        {
            Debug.LogError("[Setup Seakeeping] Не найдены ShipMotion, OceanWaves или корпус (ShipWaterInteraction.hull) — " +
                           "сначала Setup A1 Ocean и Setup A1.1 Hull Water.");
            return;
        }

        var log = new StringBuilder($"[Setup Seakeeping] Сцена «{scene.name}»:\n");
        Transform ship = motion.transform;

        // ── Геометрия корпуса в системе Ship: нос, правый борт, вверх ──
        Vector3 bow = origin != null ? origin.bowLocalAxis : Vector3.back;
        bow.y = 0f; bow.Normalize();
        Vector3 starboard = Vector3.Cross(Vector3.up, bow);
        float fore = float.MinValue, aft = float.MaxValue, stbd = float.MinValue, port = float.MaxValue, bottom = float.MaxValue;
        foreach (Transform box in new[] { water.hull, water.bowTip })
        {
            if (box == null) continue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                Vector3 c = ship.InverseTransformPoint(box.TransformPoint(corner));
                float a = Vector3.Dot(c, bow), s = Vector3.Dot(c, starboard);
                fore = Mathf.Max(fore, a); aft = Mathf.Min(aft, a);
                stbd = Mathf.Max(stbd, s); port = Mathf.Min(port, s);
                bottom = Mathf.Min(bottom, c.y);
            }
        }
        float waterline = ship.InverseTransformPoint(new Vector3(0f, ocean.transform.position.y, 0f)).y;
        float lengthWl = fore - aft, beam = stbd - port, draft = waterline - bottom;
        Vector3 pivot = bow * (0.5f * (fore + aft)) + starboard * (0.5f * (stbd + port));
        pivot.y = waterline;
        log.AppendLine($"  корпус (Hull + BowTip): длина {lengthWl:F2} м, ширина {beam:F2} м, осадка {draft:F2} м, " +
                       $"центр качки (локально Ship) {pivot.ToString("F2")}");

        // ── 1. ShipMotion ──
        Undo.RecordObject(motion, "Setup Seakeeping");
        motion.mode = ShipMotion.MotionMode.Waves;
        motion.ocean = ocean;
        motion.navigation = nav;
        motion.pivotLocal = pivot;
        EditorUtility.SetDirty(motion);
        log.AppendLine($"  + ShipMotion: режим Waves, OceanWaves, ShipNavigation, сетка {motion.samplesAlong}×{motion.samplesAcross}, " +
                       $"комфорт {motion.comfortScale:F2}, пределы крена/дифферента {motion.maxRollDeg:F0}°/{motion.maxPitchDeg:F0}°");

        // ── 2. Параметры судна ──
        ShipParameters p = nav != null ? nav.parameters : null;
        if (p == null)
            log.AppendLine("  ! у ShipNavigation нет ShipParameters — периоды качки не заданы");
        else if (!p.name.Contains("Model45m"))
            log.AppendLine($"  = {p.name}: не судно сцены (Model45m) — мореходные параметры не трогаю: " +
                           $"B {p.beam} м, d {p.draft} м, T_φ {p.rollPeriod:F1} с, T_z {p.heavePeriod:F1} с, T_θ {p.pitchPeriod:F1} с");
        else
        {
            Undo.RecordObject(p, "Setup Seakeeping parameters");
            p.beam = Mathf.Round(beam * 100f) / 100f;
            p.draft = Mathf.Round(draft * 100f) / 100f;
            p.metacentricHeight = AssumedGM;
            p.blockCoefficient = AssumedCb;
            p.waterplaneCoefficient = AssumedCwp;
            p.rollDamping = 0.10f;
            p.heaveDamping = 0.25f;
            p.pitchDamping = 0.25f;
            p.EstimateMotionPeriods();
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  + {p.name}: B {p.beam} м, d {p.draft} м (по корпусу); допущения GM {AssumedGM} м, C_B {AssumedCb}, C_WP {AssumedCwp}; " +
                           $"T_φ {p.rollPeriod:F2} с, T_z = T_θ {p.heavePeriod:F2} с; ζ 0,10/0,25/0,25");
            if (Mathf.Abs(p.length - lengthWl) > 0.05f * p.length)
                log.AppendLine($"  ! длина в параметрах {p.length} м отличается от корпуса сцены {lengthWl:F1} м");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Режим качки — ShipMotion.mode (Waves / Sinusoidal).");
        Debug.Log(log.ToString());
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
