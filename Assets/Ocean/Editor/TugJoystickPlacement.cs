// ============================================================
// TugJoystickPlacement.cs — Положение пульта (и рукоятки джойстика) своего буксира (запросы 19.4а-2, 19.4а-5)
//
// 19.4а-5: на буксире — свой пульт (Ship/TugConsole: рукоятка, тумблер, ТИФОН, СВЕТ, экраны), он строится в Play Mode
// на месте стойки МОСТИКИ со сдвигом ShipTypeSwitcher.tugConsoleOffset. Правка мышью:
//   1. Play → тип судна «буксир» → в Hierarchy выделить Ship/TugConsole и подвинуть в Scene View;
//   2. Tools/VR Bridge/Tug — запомнить положение пульта (только в Play Mode): сдвиг запоминается;
//   3. выйти из Play Mode — сдвиг записывается в ShipTypeSwitcher (сцена помечается изменённой) → Ctrl+S.
// Без пульта (стойка не найдена) — прежнее: сдвиг рукоятки Ship/TugJoystick от TelegraphHead (tugJoystickOffset, 19.4а-2).
// Сдвиг можно и просто ввести в инспекторе Ship → ShipTypeSwitcher → «Буксир (19.4а)» вне Play Mode.
// ============================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TugJoystickPlacement
{
    private const string Key = "VRBridge.TugJoystickOffset";
    private const string ConsoleKey = "VRBridge.TugConsoleOffset";

    static TugJoystickPlacement()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/VR Bridge/Tug — запомнить положение пульта")]
    private static void Remember()
    {
        ShipTypeSwitcher sw = Object.FindAnyObjectByType<ShipTypeSwitcher>();
        TugConsole console = TugConsole.Instance;
        if (sw != null && console != null)
        {
            Vector3 c = console.transform.localPosition - console.BaseLocalPosition;
            EditorPrefs.SetString(ConsoleKey, Format(c));
            Debug.Log($"[Буксир] Сдвиг пульта от места стойки МОСТИКИ запомнен: ({c.x:F3}; {c.y:F3}; {c.z:F3}) м. " +
                      "После выхода из Play Mode он запишется в ShipTypeSwitcher — сохраните сцену (Ctrl+S).");
            return;
        }
        GameObject joy = GameObject.Find("TugJoystick");
        ShipNavigation nav = Object.FindAnyObjectByType<ShipNavigation>();
        Transform head = nav != null && nav.telegraph != null ? nav.telegraph.transform : null;
        if (head != null && head.parent != null && head.parent.name == "TelegraphHead") head = head.parent;
        if (sw == null || joy == null || head == null || joy.transform.parent != head.parent)
        {
            Debug.LogWarning("[Буксир] Нет пульта и рукоятки: Play Mode → меню A «Судно » → «Тип судна: буксир», затем подвинуть Ship/TugConsole.");
            return;
        }
        Vector3 offset = joy.transform.localPosition - head.localPosition;
        EditorPrefs.SetString(Key, Format(offset));
        Debug.Log($"[Буксир] Сдвиг рукоятки от TelegraphHead запомнен: ({offset.x:F3}; {offset.y:F3}; {offset.z:F3}) м. " +
                  "После выхода из Play Mode он запишется в ShipTypeSwitcher — сохраните сцену (Ctrl+S).");
    }

    [MenuItem("Tools/VR Bridge/Tug — запомнить положение пульта", true)]
    private static bool RememberValidate() => EditorApplication.isPlaying;

    private static string Format(Vector3 v)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return v.x.ToString("R", inv) + ";" + v.y.ToString("R", inv) + ";" + v.z.ToString("R", inv);
    }

    private static bool Take(string key, out Vector3 v)
    {
        v = Vector3.zero;
        if (!EditorPrefs.HasKey(key)) return false;
        string[] p = EditorPrefs.GetString(key).Split(';');
        EditorPrefs.DeleteKey(key);
        if (p.Length != 3) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out float x) ||
            !float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float y) ||
            !float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float z)) return false;
        v = new Vector3(x, y, z);
        return true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode) return;
        bool joy = Take(Key, out Vector3 j), con = Take(ConsoleKey, out Vector3 c);
        if (!joy && !con) return;
        ShipTypeSwitcher sw = Object.FindAnyObjectByType<ShipTypeSwitcher>();
        if (sw == null) return;
        Undo.RecordObject(sw, "Tug Console Offset");
        if (joy) sw.tugJoystickOffset = j;
        if (con) sw.tugConsoleOffset = c;
        EditorUtility.SetDirty(sw);
        EditorSceneManager.MarkSceneDirty(sw.gameObject.scene);
        Debug.Log(con ? $"[Буксир] ShipTypeSwitcher.tugConsoleOffset = ({c.x:F3}; {c.y:F3}; {c.z:F3}) м — сохраните сцену (Ctrl+S)."
                      : $"[Буксир] ShipTypeSwitcher.tugJoystickOffset = ({j.x:F3}; {j.y:F3}; {j.z:F3}) м — сохраните сцену (Ctrl+S).");
    }
}
