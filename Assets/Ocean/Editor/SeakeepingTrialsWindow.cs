// ============================================================
// SeakeepingTrialsWindow.cs — Стенд мореходности (запрос 4)
// Меню: Tools/VR Bridge/Seakeeping Trials
//
// Без Play Mode: судно идёт прямым курсом с постоянной скоростью по тем же
// волнам, что в сцене (OceanWaves: длины, амплитуды, веер, ветер), при
// интенсивности и скорости волн из пресетов WeatherController. Перебор
// курсовых углов волны и скоростей; результат — ThesisData/Seakeeping/.
// Проверяет: характер качки по курсовому углу (лагом — бортовая, носом —
// килевая), изменение периода встречи с ходом и курсом.
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SeakeepingTrialsWindow : EditorWindow
{
    private ShipParameters parameters;
    private bool presetModerate = true, presetStorm = true, presetCalm;
    private bool atStop = true, atService = true;
    private float headingStep = 15f;
    private float duration = 600f, transient = 100f;
    private bool saveFiles = true;

    private List<SeakeepingResult> results;
    private string savedDir;
    private double runMs;
    private Vector2 scroll;

    [MenuItem("Tools/VR Bridge/Seakeeping Trials")]
    private static void Open()
    {
        var w = GetWindow<SeakeepingTrialsWindow>("Мореходность");
        w.minSize = new Vector2(560, 500);
    }

    private void OnEnable()
    {
        var nav = FindAnyObjectByType<ShipNavigation>();
        if (nav != null) parameters = nav.parameters;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Судно и условия", EditorStyles.boldLabel);
        parameters = (ShipParameters)EditorGUILayout.ObjectField("Параметры судна", parameters, typeof(ShipParameters), false);
        if (parameters != null)
            EditorGUILayout.HelpBox($"L {parameters.length} м, B {parameters.beam} м; T_φ {parameters.rollPeriod:F2} с, " +
                                    $"T_z {parameters.heavePeriod:F2} с, T_θ {parameters.pitchPeriod:F2} с; " +
                                    $"ζ {parameters.rollDamping:F2} / {parameters.heaveDamping:F2} / {parameters.pitchDamping:F2}", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Пресеты погоды");
        presetCalm = GUILayout.Toggle(presetCalm, "штиль");
        presetModerate = GUILayout.Toggle(presetModerate, "умеренное");
        presetStorm = GUILayout.Toggle(presetStorm, "шторм");
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Скорость");
        atStop = GUILayout.Toggle(atStop, "стоп");
        atService = GUILayout.Toggle(atService, parameters != null ? $"V_эксп = {parameters.serviceSpeedKnots} уз" : "V_эксп");
        EditorGUILayout.EndHorizontal();
        headingStep = EditorGUILayout.Slider("Шаг курсового угла, °", headingStep, 5f, 45f);
        duration = EditorGUILayout.FloatField("Длительность, с", duration);
        transient = EditorGUILayout.FloatField("Переходный процесс, с", transient);
        saveFiles = EditorGUILayout.Toggle("Сохранить в ThesisData", saveFiles);

        using (new EditorGUI.DisabledScope(parameters == null))
            if (GUILayout.Button("Запустить", GUILayout.Height(28)))
                RunAll();

        if (!string.IsNullOrEmpty(savedDir))
        {
            EditorGUILayout.HelpBox($"Сохранено: {savedDir}", MessageType.Info);
            if (GUILayout.Button("Открыть папку")) EditorUtility.RevealInFinder(savedDir);
        }

        if (results != null && results.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Результаты ({results.Count} случаев, {runMs / 1000.0:F1} с; σ — СКО, без комфорта)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("пресет   q, °   V, уз   σ_z, м   σ_φ, °   σ_θ, °   max φ, °   T_e1, с   T_пик z, с", EditorStyles.miniBoldLabel);
            foreach (SeakeepingResult r in results)
                EditorGUILayout.LabelField($"{r.Case.Preset,-9} {r.Case.HeadingDeg,5:F0} {r.Case.SpeedKnots,6:F1} {r.SigmaHeave,8:F2} " +
                                           $"{r.SigmaRollDeg,8:F2} {r.SigmaPitchDeg,8:F2} {r.MaxRollDeg,9:F1} {r.EncounterPeriods[0],8:F1} {r.PeakPeriodWaveHeave,9:F1}",
                                           EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void RunAll()
    {
        // Волны — из сцены (OceanWaves), иначе значения по умолчанию
        OceanWaves ocean = FindAnyObjectByType<OceanWaves>();
        WeatherController weather = FindAnyObjectByType<WeatherController>();
        ShipMotion motion = FindAnyObjectByType<ShipMotion>();
        if (weather == null)
        {
            EditorUtility.DisplayDialog("Мореходность", "В сцене нет WeatherController — интенсивность волн пресетов взять неоткуда.", "OK");
            return;
        }

        var field = new OceanWaveField();
        OceanWaves.ConfigureField(field, ocean != null ? ocean.waves : OceanWaves.DefaultWaves(),
                                  ocean != null ? ocean.windFromDeg : 160f, 1f);
        float speedInfluence = ocean != null ? ocean.speedInfluence : 0.25f;
        field.FadeEnd = 0f;   // судно в центре океана — затухание и подавление по сетке не влияют
        field.MeshMinStep = 0f; field.MeshAngleStep = 0f;

        var settings = new SeakeepingTrialSettings { Duration = duration, Transient = transient };
        if (motion != null) { settings.SamplesAlong = motion.samplesAlong; settings.SamplesAcross = motion.samplesAcross; }

        var presets = new List<string>();
        if (presetCalm) presets.Add("calm");
        if (presetModerate) presets.Add("moderate");
        if (presetStorm) presets.Add("storm");
        var speeds = new List<double>();
        if (atStop) speeds.Add(0.0);
        if (atService) speeds.Add(parameters.serviceSpeedKnots);

        var cases = new List<SeakeepingCase>();
        foreach (string name in presets)
        {
            weather.TryGetPreset(name, out WeatherController.WeatherPreset wp);
            double s = 1.0 + (wp.windSpeed - 1.0) * speedInfluence;   // как OceanWaves: s = 1 + (_WaveSpeed − 1)·speedInfluence
            foreach (double v in speeds)
                for (float q = 0f; q <= 180f + 1e-3f; q += headingStep)
                    cases.Add(new SeakeepingCase { Preset = name, Intensity = wp.seaState, WaveSpeedFactor = s, HeadingDeg = q, SpeedKnots = v });
        }

        results = new List<SeakeepingResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < cases.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Мореходность", cases[i].Id, (float)i / cases.Count)) break;
                results.Add(SeakeepingTrials.Run(parameters, field, cases[i], settings));
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        runMs = sw.Elapsed.TotalMilliseconds;

        string conditions =
            $"Волны — из сцены (OceanWaves{(ocean == null ? ", значения по умолчанию" : "")}): ветер с {(ocean != null ? ocean.windFromDeg : 160f):F0}°, " +
            $"λ = {Lambdas(field)} м; интенсивность и скорость волн — пресеты WeatherController. " +
            $"Сетка точек {settings.SamplesAlong}×{settings.SamplesAcross}, шаг {settings.Dt} с, длительность {duration} с, переходный процесс {transient} с.";
        savedDir = saveFiles ? SeakeepingReport.Save(results, parameters, conditions) : null;
        if (savedDir != null) Debug.Log($"[Мореходность] {results.Count} случаев за {runMs / 1000.0:F1} с → {savedDir}");
    }

    private static string Lambdas(OceanWaveField f)
    {
        var parts = new List<string>();
        for (int i = 0; i < OceanWaveField.WaveCount; i++)
            if (f.Steepness[i] > 0f) parts.Add((2.0 * System.Math.PI / f.K[i]).ToString("F0"));
        return string.Join("/", parts);
    }
}
