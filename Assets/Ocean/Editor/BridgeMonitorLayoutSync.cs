// ============================================================
// BridgeMonitorLayoutSync.cs — Раскладка экранов пульта при входе в Play Mode (запрос 16а, по проверке 23.10.2026)
//
// Сам, без пункта меню: перед Play ищет в открытой сцене Ship и экраны МОСТИКИ (BridgeMonitors.ScreenNames +
// BigPanelName), считает плоскость каждого по граням сетки (BridgeMonitors.FaceFromMesh → Core ScreenFace; в редакторе
// вершины читаются и при выключенном Read/Write) и пишет Assets/Ocean/Resources/BridgeMonitorLayout.asset —
// только если изменилось. Сборка берёт плоскости из ассета (в ней вершины модели не прочитать).
// ============================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BridgeMonitorLayoutSync
{
    private const string AssetPath = "Assets/Ocean/Resources/BridgeMonitorLayout.asset";

    static BridgeMonitorLayoutSync()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Sync();
        };
    }

    [MenuItem("Tools/VR Bridge/Bridge Monitors — пересчитать экраны")]
    public static void Sync()
    {
        ShipTypeSwitcher sw = Object.FindAnyObjectByType<ShipTypeSwitcher>();
        if (sw == null) return;   // не сцена тренажёра
        Transform ship = sw.transform;
        Vector3 viewer = BridgeMonitors.ViewerFor(ship);
        var entries = new List<BridgeMonitorLayout.Entry>();
        var log = new StringBuilder();
        var names = new List<string>(BridgeMonitors.ScreenNames) { BridgeMonitors.BigPanelName };
        foreach (string name in names)
        {
            Transform t = BridgeMonitors.FindScreen(ship, name);
            BridgeMonitorLayout.Entry e = t != null ? BridgeMonitors.FaceFromMesh(ship, t, viewer) : null;
            if (e == null) { log.Append($"\n  {name} — {(t == null ? "не найден" : "плоскость не найдена")}"); continue; }
            entries.Add(e);
        }

        var layout = AssetDatabase.LoadAssetAtPath<BridgeMonitorLayout>(AssetPath);
        bool created = layout == null;
        if (created)
        {
            layout = ScriptableObject.CreateInstance<BridgeMonitorLayout>();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
            AssetDatabase.CreateAsset(layout, AssetPath);
        }
        string before = EditorJsonUtility.ToJson(layout);
        layout.screens = entries;
        if (!created && EditorJsonUtility.ToJson(layout) == before) return;
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Мониторы] Раскладка экранов {(created ? "создана" : "обновлена")}: {entries.Count} из {names.Count} — {AssetPath}{log}");
    }
}
