// ============================================================
// TugJoystickPlacement.cs — Положение рукоятки джойстика своего буксира (запрос 19.4а-2)
//
// Рукоятка (Ship/TugJoystick, с тумблером «УДЕРЖ. КУРС») строится в Play Mode на месте TelegraphHead со сдвигом
// ShipTypeSwitcher.tugJoystickOffset. Правка мышью:
//   1. Play → тип судна «буксир» → в Hierarchy выделить Ship/TugJoystick и подвинуть в Scene View;
//   2. Tools/VR Bridge/Tug — запомнить положение джойстика (только в Play Mode): сдвиг запоминается;
//   3. выйти из Play Mode — сдвиг записывается в ShipTypeSwitcher (сцена помечается изменённой) → Ctrl+S.
// Сдвиг можно и просто ввести в инспекторе Ship → ShipTypeSwitcher → «Буксир (19.4а)» вне Play Mode.
// ============================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TugJoystickPlacement
{
    private const string Key = "VRBridge.TugJoystickOffset";

    static TugJoystickPlacement()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/VR Bridge/Tug — запомнить положение джойстика")]
    private static void Remember()
    {
        ShipTypeSwitcher sw = Object.FindAnyObjectByType<ShipTypeSwitcher>();
        GameObject joy = GameObject.Find("TugJoystick");
        ShipNavigation nav = Object.FindAnyObjectByType<ShipNavigation>();
        Transform head = nav != null && nav.telegraph != null ? nav.telegraph.transform : null;
        if (head != null && head.parent != null && head.parent.name == "TelegraphHead") head = head.parent;
        if (sw == null || joy == null || head == null || joy.transform.parent != head.parent)
        {
            Debug.LogWarning("[Буксир] Нет рукоятки: Play Mode → меню A «Судно » → «Тип судна: буксир», затем подвинуть Ship/TugJoystick.");
            return;
        }
        Vector3 offset = joy.transform.localPosition - head.localPosition;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        EditorPrefs.SetString(Key, offset.x.ToString("R", inv) + ";" + offset.y.ToString("R", inv) + ";" + offset.z.ToString("R", inv));
        Debug.Log($"[Буксир] Сдвиг рукоятки от TelegraphHead запомнен: ({offset.x:F3}; {offset.y:F3}; {offset.z:F3}) м. " +
                  "После выхода из Play Mode он запишется в ShipTypeSwitcher — сохраните сцену (Ctrl+S).");
    }

    [MenuItem("Tools/VR Bridge/Tug — запомнить положение джойстика", true)]
    private static bool RememberValidate() => EditorApplication.isPlaying;

    private static void OnPlayModeChanged(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode || !EditorPrefs.HasKey(Key)) return;
        string[] p = EditorPrefs.GetString(Key).Split(';');
        EditorPrefs.DeleteKey(Key);
        if (p.Length != 3) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out float x) ||
            !float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float y) ||
            !float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float z)) return;
        ShipTypeSwitcher sw = Object.FindAnyObjectByType<ShipTypeSwitcher>();
        if (sw == null) return;
        Undo.RecordObject(sw, "Tug Joystick Offset");
        sw.tugJoystickOffset = new Vector3(x, y, z);
        EditorUtility.SetDirty(sw);
        EditorSceneManager.MarkSceneDirty(sw.gameObject.scene);
        Debug.Log($"[Буксир] ShipTypeSwitcher.tugJoystickOffset = ({x:F3}; {y:F3}; {z:F3}) м — сохраните сцену (Ctrl+S).");
    }
}
