// ============================================================
// StartMenuPlayToggle.cs — Tools/VR Bridge/Start Menu in Play (запрос 21.2а)
//
// В сборке на ПК при запуске — начальное меню «Обучение / Редактор сценариев / Выход».
// В Unity по умолчанию Play — сразу тренажёр; этот пункт (галочка) показывает меню и при Play,
// чтобы проверить его без сборки. Хранится в EditorPrefs (только на этом компьютере).
// ============================================================
using UnityEditor;

public static class StartMenuPlayToggle
{
    private const string Item = "Tools/VR Bridge/Start Menu in Play";

    [MenuItem(Item)]
    private static void Toggle()
    {
        bool on = !EditorPrefs.GetBool(TrainerScenes.StartMenuPref, false);
        EditorPrefs.SetBool(TrainerScenes.StartMenuPref, on);
        UnityEngine.Debug.Log($"[Меню] Начальное меню при Play: {(on ? "вкл" : "выкл (сразу тренажёр)")}.");
    }

    [MenuItem(Item, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(Item, EditorPrefs.GetBool(TrainerScenes.StartMenuPref, false));
        return true;
    }
}
