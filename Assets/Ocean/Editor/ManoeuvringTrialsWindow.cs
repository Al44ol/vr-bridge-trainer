// ============================================================
// ManoeuvringTrialsWindow.cs — Стенд манёвренных испытаний (A3, часть 1)
// Меню: Tools/VR Bridge/Manoeuvring Trials
//
// Прогоняет испытания ManoeuvringTrials на чистой модели без Play Mode
// (шаг dt = 0.02 с) и сохраняет CSV, сводку (CSV + Markdown) и графики SVG
// в ThesisData/Trials/. В окне — таблица результатов и быстрый график.
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ManoeuvringTrialsWindow : EditorWindow
{
    private const string PrefParams = "VRBridge.Trials.Params";

    private ShipParameters parameters;
    private bool imoSpeed = true;
    private float testSpeedKn = 13.5f;
    private float maxDuration = 1800f;
    private float stopThresholdKn = 0.1f;
    private float sampleInterval = 0.5f;
    private bool saveFiles = true;

    private bool doTurning = true, doInitial = true, doZigzag10 = true, doZigzag20 = true;
    private bool doCrash = true, doPassive = true, doAccel = true;

    private List<TrialResult> results;
    private int selected;
    private string savedDir;
    private double runMs;
    private Vector2 scroll;

    [MenuItem("Tools/VR Bridge/Manoeuvring Trials")]
    private static void Open()
    {
        var w = GetWindow<ManoeuvringTrialsWindow>("Манёвренные испытания");
        w.minSize = new Vector2(520, 600);
    }

    private void OnEnable()
    {
        string path = EditorPrefs.GetString(PrefParams, "");
        if (!string.IsNullOrEmpty(path)) parameters = AssetDatabase.LoadAssetAtPath<ShipParameters>(path);
        if (parameters == null)
        {
            // По умолчанию — параметры ShipNavigation открытой сцены
            var nav = FindFirstObjectByType<ShipNavigation>();
            if (nav != null) parameters = nav.parameters;
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Модель и условия", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        parameters = (ShipParameters)EditorGUILayout.ObjectField("Параметры судна", parameters, typeof(ShipParameters), false);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(PrefParams, parameters != null ? AssetDatabase.GetAssetPath(parameters) : "");

        if (parameters == null)
        {
            EditorGUILayout.HelpBox("Выберите ассет ShipParameters (Assets/Ocean/Settings).", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        float vService = parameters.serviceSpeedKnots;
        imoSpeed = EditorGUILayout.ToggleLeft($"Скорость испытаний по IMO: 0.9·V_эксп = {0.9f * vService:F2} уз " +
                                              "(MSC.137(76) 4.2.1)", imoSpeed);
        using (new EditorGUI.DisabledScope(imoSpeed))
            testSpeedKn = EditorGUILayout.Slider("Скорость испытаний, уз", imoSpeed ? 0.9f * vService : testSpeedKn, 0.5f, vService);
        maxDuration = EditorGUILayout.FloatField("Предельная длительность, с", maxDuration);
        stopThresholdKn = EditorGUILayout.FloatField("Порог остановки (пассивное), уз", stopThresholdKn);
        sampleInterval = EditorGUILayout.FloatField("Интервал записи в CSV, с", sampleInterval);
        EditorGUILayout.LabelField("Шаг интегрирования", "0.02 с");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Испытания", EditorStyles.boldLabel);
        doTurning = EditorGUILayout.ToggleLeft("Циркуляция 35° на ПБ и ЛБ", doTurning);
        doInitial = EditorGUILayout.ToggleLeft("Начальная поворотливость (руль 10°)", doInitial);
        doZigzag10 = EditorGUILayout.ToggleLeft("Зигзаг 10°/10°", doZigzag10);
        doZigzag20 = EditorGUILayout.ToggleLeft("Зигзаг 20°/20°", doZigzag20);
        doCrash = EditorGUILayout.ToggleLeft("Торможение: полный вперёд → полный назад", doCrash);
        doPassive = EditorGUILayout.ToggleLeft("Пассивное торможение: полный вперёд → стоп", doPassive);
        doAccel = EditorGUILayout.ToggleLeft("Разгон: стоп → полный вперёд", doAccel);
        saveFiles = EditorGUILayout.ToggleLeft("Сохранить в ThesisData/Trials (CSV, сводка, SVG)", saveFiles);

        if (GUILayout.Button("Выполнить испытания", GUILayout.Height(30)))
            RunTrials();

        if (results != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Результаты ({runMs:F0} мс)", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(savedDir))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel(savedDir, GUILayout.Height(18));
                if (GUILayout.Button("Открыть", GUILayout.Width(80))) EditorUtility.RevealInFinder(savedDir);
                EditorGUILayout.EndHorizontal();
            }
            DrawTable();

            var names = new string[results.Count];
            for (int i = 0; i < results.Count; i++) names[i] = results[i].Title;
            selected = Mathf.Clamp(EditorGUILayout.Popup("График", selected, names), 0, results.Count - 1);
            DrawPlot(results[selected]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void RunTrials()
    {
        var s = new TrialSettings
        {
            Parameters = parameters,
            TestSpeedMs = (imoSpeed ? 0.9f * parameters.serviceSpeedKnots : testSpeedKn) * ManoeuvringTrials.KnotsToMs,
            Dt = 0.02,
            MaxDuration = Mathf.Max(10f, maxDuration),
            StopThresholdKn = Mathf.Max(0.001f, stopThresholdKn),
            SampleInterval = Mathf.Max(0.02f, sampleInterval),
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        results = new List<TrialResult>();
        if (doTurning) { results.Add(ManoeuvringTrials.Turning(s, true)); results.Add(ManoeuvringTrials.Turning(s, false)); }
        if (doInitial) results.Add(ManoeuvringTrials.InitialTurning(s));
        if (doZigzag10) results.Add(ManoeuvringTrials.Zigzag(s, 10, 10));
        if (doZigzag20) results.Add(ManoeuvringTrials.Zigzag(s, 20, 20));
        if (doCrash) results.Add(ManoeuvringTrials.CrashStop(s));
        if (doPassive) results.Add(ManoeuvringTrials.PassiveStop(s));
        if (doAccel) results.Add(ManoeuvringTrials.Acceleration(s));
        runMs = sw.Elapsed.TotalMilliseconds;
        selected = 0;
        savedDir = null;
        if (results.Count == 0) { results = null; return; }

        if (saveFiles)
        {
            savedDir = TrialsReport.Save(results, s);
            Debug.Log($"[Манёвренные испытания] {parameters.name}: {results.Count} испытаний за {runMs:F0} мс. Файлы: {savedDir}");
        }
    }

    private void DrawTable()
    {
        var bold = new GUIStyle(EditorStyles.miniBoldLabel);
        var mini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        foreach (TrialResult r in results)
        {
            EditorGUILayout.LabelField(r.Completed ? r.Title : $"{r.Title} — не завершено: {r.Message}", bold);
            foreach (TrialMetric m in r.Metrics)
            {
                string v = double.IsNaN(m.Value) ? "—" : $"{m.Value:F1} {m.Unit}";
                if (!double.IsNaN(m.ValueInL)) v += $" = {m.ValueInL:F2} L";
                string crit = m.Criterion.Length > 0 ? $"  [{m.Criterion}: {(m.Pass == true ? "соответствует" : m.Pass == false ? "НЕ соответствует" : "—")}]" : "";
                var old = GUI.color;
                if (m.Pass == false) GUI.color = new Color(1f, 0.6f, 0.6f);
                EditorGUILayout.LabelField("   " + m.Name, v + crit, mini);
                GUI.color = old;
            }
        }
    }

    // Быстрый график: траектория (вид сверху) для циркуляций, торможения; курс и руль для зигзага
    private void DrawPlot(TrialResult r)
    {
        Rect rect = GUILayoutUtility.GetRect(position.width - 30, 320);
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
        if (r.Samples.Count < 2) return;

        bool timeSeries = r.Id.StartsWith("zigzag") || r.Id.StartsWith("passive") || r.Id.StartsWith("acceleration") || r.Id.StartsWith("crash");
        var a = new List<Vector2>(); var b = new List<Vector2>();
        foreach (TrialSample p in r.Samples)
        {
            if (!timeSeries) a.Add(new Vector2((float)p.X, (float)p.Y));
            else if (r.Id.StartsWith("zigzag"))
            {
                a.Add(new Vector2((float)p.T, (float)p.DeltaPsiDeg));
                b.Add(new Vector2((float)p.T, (float)p.RudderActualDeg));
            }
            else a.Add(new Vector2((float)p.T, (float)(p.U / ManoeuvringTrials.KnotsToMs)));
        }

        Vector2 min = a[0], max = a[0];
        foreach (Vector2 v in a) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
        foreach (Vector2 v in b) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
        Vector2 size = Vector2.Max(max - min, new Vector2(1e-3f, 1e-3f));
        Rect inner = new Rect(rect.x + 40, rect.y + 10, rect.width - 50, rect.height - 30);
        float sx = inner.width / size.x, sy = inner.height / size.y;
        if (!timeSeries) { float k = Mathf.Min(sx, sy); sx = sy = k; }   // одинаковый масштаб для траектории
        Vector3 ToGui(Vector2 v) => new Vector3(inner.x + (v.x - min.x) * sx, inner.yMax - (v.y - min.y) * sy, 0);

        // Нулевая линия и начало
        Handles.color = new Color(0.4f, 0.4f, 0.4f);
        if (min.y < 0 && max.y > 0) Handles.DrawLine(ToGui(new Vector2(min.x, 0)), ToGui(new Vector2(max.x, 0)));
        if (!timeSeries && min.x < 0 && max.x > 0) Handles.DrawLine(ToGui(new Vector2(0, min.y)), ToGui(new Vector2(0, max.y)));

        Handles.color = new Color(0.4f, 0.85f, 1f);
        Handles.DrawAAPolyLine(2.5f, a.ConvertAll(ToGui).ToArray());
        if (b.Count > 1)
        {
            Handles.color = new Color(1f, 0.75f, 0.3f);
            Handles.DrawAAPolyLine(1.5f, b.ConvertAll(ToGui).ToArray());
        }

        var label = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
        string legend = !timeSeries ? $"вид сверху, м: X {min.x:F0}…{max.x:F0}, Y {min.y:F0}…{max.y:F0}"
            : r.Id.StartsWith("zigzag") ? $"голубой — Δψ, оранжевый — δ; t 0…{max.x:F0} с, {min.y:F0}…{max.y:F0}°"
            : $"скорость, уз: {min.y:F1}…{max.y:F1}; t 0…{max.x:F0} с";
        GUI.Label(new Rect(rect.x + 6, rect.yMax - 18, rect.width, 16), legend, label);
    }
}
