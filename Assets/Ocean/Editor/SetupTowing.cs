// ============================================================
// SetupTowing.cs — Ассеты буксировки (запрос 19.3)
//
// Сцену не правит. При входе в Play Mode (ScenarioLibrarySync → EnsureCreated) создаёт, если их нет:
//   • 19.3б: Assets/Ocean/Resources/ShipParameters_Tug.asset — буксир из ShipPresets.FillTug (единственный источник
//     значений): его берёт TowingController (Resources.Load), если в инспекторе не задан свой; правка значений —
//     в инспекторе ассета. Без ассета (сборка без него) TowingController строит те же параметры сам.
// Существующие ассеты не меняются.
// ============================================================
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupTowing
{
    public const string TugPath = "Assets/Ocean/Resources/ShipParameters_Tug.asset";

    /// <summary>Создать недостающие ассеты буксировки (при входе в Play Mode).</summary>
    public static void EnsureCreated()
    {
        if (AssetDatabase.LoadAssetAtPath<ShipParameters>(TugPath) != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TugPath)));
        AssetDatabase.Refresh();
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        ShipPresets.FillTug(p);
        AssetDatabase.CreateAsset(p, TugPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Setup 19.3] Буксир создан автоматически (при входе в Play Mode): {TugPath} — L {p.length} м, B {p.beam} м, d {p.draft} м, " +
                  $"тяга на гаке {TugSpec.DefaultBollardPullTonnes:0} т (TowingController.bollardPullTonnes).");
    }
}
