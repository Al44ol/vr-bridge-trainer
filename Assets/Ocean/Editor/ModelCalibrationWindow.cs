// ============================================================
// ModelCalibrationWindow.cs — Калибровка модели по судну-прототипу (A3.2)
// Меню: Tools/VR Bridge/Model Calibration
//
// Без Play Mode: идентификация K', T', α', C_r' (ShipCalibration) для
//   Mariner — по эталонной модели MarinerModel (основное судно);
//   KCS     — по опубликованным показателям EFD (независимая проверка).
// Отчёт — ThesisData/Calibration/<судно>_<дата>/ (CalibrationReport).
// «Сохранить ассет» создаёт или обновляет Assets/Ocean/Settings/ShipParameters_<судно>.asset
// параметрами выбранного этапа. Сцену окно не меняет.
// ============================================================
using System;
using UnityEditor;
using UnityEngine;

public class ModelCalibrationWindow : EditorWindow
{
    private const string SettingsDir = "Assets/Ocean/Settings";

    private CalibrationResult result;
    private int stage;
    private bool saveReport = true;
    private string savedDir, savedAsset;
    private double runSeconds;
    private Vector2 scroll;

    [MenuItem("Tools/VR Bridge/Model Calibration")]
    private static void Open()
    {
        var w = GetWindow<ModelCalibrationWindow>("Калибровка модели");
        w.minSize = new Vector2(560, 560);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Судно-прототип", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Mariner — сухогруз L = 160,93 м: эталон — нелинейная модель Chislett & Strøm-Tejsen (1965), MSS mariner.m; " +
            "подбор по записям ψ(t), r(t), u(t).\n" +
            "KCS — контейнеровоз L = 230 м: эталон — показатели свободноходной модели MARIN (Nath et al. 2025; Yun et al. 2025); " +
            "независимая проверка метода.\nРасчёт — несколько секунд, Play Mode не нужен.", MessageType.None);
        saveReport = EditorGUILayout.ToggleLeft("Сохранить отчёт в ThesisData/Calibration (CSV, Markdown, SVG)", saveReport);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Калибровка Mariner", GUILayout.Height(30))) Run(() => ShipCalibration.CalibrateMariner());
        if (GUILayout.Button("Калибровка KCS (проверка)", GUILayout.Height(30))) Run(() => ShipCalibration.CalibrateKcs());
        EditorGUILayout.EndHorizontal();

        if (result != null) DrawResult();
        EditorGUILayout.EndScrollView();
    }

    private void Run(Func<CalibrationResult> calibrate)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Калибровка модели", "Идентификация параметров (Нелдер–Мид)…", 0.5f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            result = calibrate();
            runSeconds = sw.Elapsed.TotalSeconds;
            stage = result.Recommended;
            savedDir = saveReport ? CalibrationReport.Save(result) : null;
            savedAsset = null;
            Debug.Log($"[Калибровка] {result.Title}: {result.Stages.Count} этапов за {runSeconds:F1} с" +
                      (savedDir != null ? $". Отчёт: {savedDir}" : ""));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void DrawResult()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{result.Title} ({runSeconds:F1} с)", EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(savedDir))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(savedDir, GUILayout.Height(18));
            if (GUILayout.Button("Открыть", GUILayout.Width(80))) EditorUtility.RevealInFinder(savedDir);
            EditorGUILayout.EndHorizontal();
        }

        var mini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        for (int i = 0; i < result.Stages.Count; i++)
        {
            CalibrationStage st = result.Stages[i];
            ShipParameters p = st.Parameters;
            string a = p.yawModel == YawModel.Norrbin ? $", α' = {p.norrbinAlphaPrime:F2}" : "";
            EditorGUILayout.LabelField($"{(i == result.Recommended ? "★ " : "   ")}{st.Title}",
                $"K' = {p.nomotoKPrime:F3}, T' = {p.nomotoTPrime:F3}{a}; J = {st.Cost:G4}", mini);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Сравнение с эталоном (погрешность, %)", EditorStyles.boldLabel);
        foreach (ComparisonRow r in result.Rows)
        {
            string v = $"эталон {r.Reference:F2} {r.Unit}; модель {r.Values[stage]:F2} ({r.ErrorPercent(stage):+0.0;-0.0} %)";
            var old = GUI.color;
            if (Math.Abs(r.ErrorPercent(stage)) > 20) GUI.color = new Color(1f, 0.7f, 0.5f);
            EditorGUILayout.LabelField($"   {r.TrialTitle}: {r.Metric}{(r.UseInFit ? "" : " (спр.)")}", v, mini);
            GUI.color = old;
        }
        foreach (string n in result.Notes) EditorGUILayout.HelpBox(n, MessageType.None);

        EditorGUILayout.Space();
        var names = result.Stages.ConvertAll(s => s.Title).ToArray();
        stage = Mathf.Clamp(EditorGUILayout.Popup("Этап для ассета и таблицы", stage, names), 0, names.Length - 1);
        string path = $"{SettingsDir}/{result.Base.name}.asset";
        if (GUILayout.Button($"Сохранить ассет {path}", GUILayout.Height(26))) SaveAsset(path);
        if (!string.IsNullOrEmpty(savedAsset))
            EditorGUILayout.HelpBox($"Сохранено: {savedAsset}. Проверка: Tools/VR Bridge/Manoeuvring Trials с этим ассетом. " +
                                    "Сцена не изменена (ShipNavigation по-прежнему на своём ассете).", MessageType.Info);
    }

    private void SaveAsset(string path)
    {
        ShipParameters src = result.Stages[stage].Parameters.Clone();
        src.name = result.Base.name;
        var existing = AssetDatabase.LoadAssetAtPath<ShipParameters>(path);
        if (existing != null)
        {
            Undo.RecordObject(existing, "Калибровка ShipParameters");
            EditorUtility.CopySerialized(src, existing);
            existing.name = result.Base.name;
            EditorUtility.SetDirty(existing);
            DestroyImmediate(src);
        }
        else
        {
            AssetDatabase.CreateAsset(src, path);
        }
        AssetDatabase.SaveAssets();
        savedAsset = path;
        Debug.Log($"[Калибровка] Ассет {path}: этап «{result.Stages[stage].Title}»");
    }
}
