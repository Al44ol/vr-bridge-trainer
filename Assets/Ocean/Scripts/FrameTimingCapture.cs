// ============================================================
// FrameTimingCapture.cs — Замер времени кадра CPU/GPU для ВКР
// Ставится скриптом Tools/VR Bridge/Setup Frame Timing (объект PerfCapture).
//
// F8 — начать замер (durationSeconds), повторное F8 — прервать.
// Источник: FrameTimingManager (время кадра CPU и GPU, мс) +
// счётчики рендера ProfilerRecorder (Draw Calls, SetPass, Batches —
// есть в редакторе и в Development Build).
//
// Исключение из правила «время — симуляционное»: здесь измеряется
// производительность, поэтому используется реальное время кадра.
//
// Файлы (ThesisData/Performance/, формат CSV по CLAUDE.md):
//   frametiming_<режим>_<дата>.csv — покадрово;
//   frametiming_summary.csv        — строка на замер (дописывается).
// Бюджет кадра при 90 Гц: 1000/90 = 11.1 мс.
// C2: примечание (столбец note сводки) собирается само — сцена, сценарий и фаза,
// погода и время суток в начале и в конце замера, был ли переход освещения и сколько
// раз перерисовывалась проба отражений (B4), идёт ли запись; extraNote — дописывается.
// До C2 note было фиксированным («BridgeScene, moderate, stop») и условия не отражало.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

public class FrameTimingCapture : MonoBehaviour
{
    [Tooltip("Длительность замера, с (реальное время)")]
    public float durationSeconds = 30f;
    [Tooltip("Пропустить первые кадры после нажатия (переходные процессы), с")]
    public float warmupSeconds = 2f;
    [Tooltip("Устарело (до C2): фиксированное примечание, больше не пишется — условия собираются сами")]
    public string note = "";
    [Tooltip("Дополнительное примечание к замеру (дописывается к условиям)")]
    public string extraNote = "";
    [Tooltip("Частота шлема, Гц — для доли кадров, не уложившихся в бюджет")]
    public float targetHz = 90f;

    private struct Sample
    {
        public float t, dt, cpu, gpu;
        public long draws, setPass, batches;
    }

    private readonly List<Sample> samples = new List<Sample>(4096);
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private ProfilerRecorder drawRec, setPassRec, batchRec;
    private bool capturing;
    private float startTime;
    private string conditionsAtStart = "";
    private bool sawTransition;
    private int probeRendersAtStart;
    private EnvironmentReflection probe;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void OnEnable()
    {
        drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        batchRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
    }

    void OnDisable()
    {
        drawRec.Dispose();
        setPassRec.Dispose();
        batchRec.Dispose();
    }

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();

        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if (capturing) Finish(true);
            else Begin();
        }

        if (!capturing) return;

        float t = Time.realtimeSinceStartup - startTime;
        if (WeatherController.Instance != null && WeatherController.Instance.IsTransitioning) sawTransition = true;
        if (t < warmupSeconds) return;

        // Тайминги приходят с задержкой в несколько кадров — это нормально для статистики
        uint n = FrameTimingManager.GetLatestTimings(1, timings);
        samples.Add(new Sample
        {
            t = t - warmupSeconds,
            dt = Time.unscaledDeltaTime * 1000f,
            cpu = n > 0 ? (float)timings[0].cpuFrameTime : float.NaN,
            gpu = n > 0 ? (float)timings[0].gpuFrameTime : float.NaN,
            draws = drawRec.Valid ? drawRec.LastValue : -1,
            setPass = setPassRec.Valid ? setPassRec.LastValue : -1,
            batches = batchRec.Valid ? batchRec.LastValue : -1,
        });

        if (t >= warmupSeconds + durationSeconds) Finish(false);
    }

    private void Begin()
    {
        if (!FrameTimingManager.IsFeatureEnabled())
            Debug.LogWarning("[FrameTiming] FrameTimingManager выключен — включите Frame Timing Stats " +
                             "(Tools/VR Bridge/Setup Frame Timing). GPU-время будет 0.");
        samples.Clear();
        startTime = Time.realtimeSinceStartup;
        if (probe == null) probe = FindAnyObjectByType<EnvironmentReflection>();
        probeRendersAtStart = probe != null ? probe.Renders : 0;
        sawTransition = WeatherController.Instance != null && WeatherController.Instance.IsTransitioning;
        conditionsAtStart = Lighting();
        capturing = true;
        Debug.Log($"[FrameTiming] Замер начат: {RenderModeName()}, {durationSeconds:F0} с " +
                  $"(+{warmupSeconds:F0} с прогрев). Не двигайте головой. F8 — прервать.");
    }

    private void Finish(bool aborted)
    {
        capturing = false;
        if (samples.Count < 10)
        {
            Debug.LogWarning("[FrameTiming] Слишком мало кадров — замер не сохранён.");
            return;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Performance"));
        Directory.CreateDirectory(dir);
        string mode = RenderModeName();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);

        // Покадровый файл
        var sb = new StringBuilder();
        sb.AppendLine("t_s;frame_dt_ms;cpu_frame_ms;gpu_frame_ms;draw_calls;setpass_calls;batches");
        foreach (Sample s in samples)
            sb.Append(F(s.t, 4)).Append(';').Append(F(s.dt, 3)).Append(';')
              .Append(F(s.cpu, 3)).Append(';').Append(F(s.gpu, 3)).Append(';')
              .Append(s.draws.ToString(Inv)).Append(';').Append(s.setPass.ToString(Inv)).Append(';')
              .Append(s.batches.ToString(Inv)).AppendLine();
        string rawPath = Path.Combine(dir, $"frametiming_{mode}_{stamp}.csv");
        File.WriteAllText(rawPath, sb.ToString(), new UTF8Encoding(true));

        // Сводка
        var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>();
        double draws = 0, setPass = 0;
        foreach (Sample s in samples)
        {
            dt.Add(s.dt);
            if (!float.IsNaN(s.cpu) && s.cpu > 0f) cpu.Add(s.cpu);
            if (!float.IsNaN(s.gpu) && s.gpu > 0f) gpu.Add(s.gpu);
            draws += s.draws; setPass += s.setPass;
        }
        float budget = 1000f / targetHz;
        int over = 0;
        foreach (float d in dt) if (d > budget * 1.05f) over++;   // 5 % допуск на дрожание
        float duration = samples[samples.Count - 1].t;

        string summaryPath = Path.Combine(dir, "frametiming_summary.csv");
        bool newFile = !File.Exists(summaryPath);
        var line = new StringBuilder();
        if (newFile)
            line.AppendLine("date;render_mode;editor;aborted;duration_s;frames;fps_mean;" +
                            "frame_dt_mean_ms;cpu_mean_ms;cpu_p95_ms;gpu_mean_ms;gpu_median_ms;gpu_p95_ms;gpu_p99_ms;" +
                            "over_budget_pct;draw_calls_mean;setpass_mean;note");
        line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)).Append(';')
            .Append(mode).Append(';')
            .Append(Application.isEditor ? "1" : "0").Append(';')
            .Append(aborted ? "1" : "0").Append(';')
            .Append(F(duration, 1)).Append(';')
            .Append(samples.Count.ToString(Inv)).Append(';')
            .Append(F(samples.Count / Mathf.Max(0.001f, duration), 1)).Append(';')
            .Append(F(Mean(dt), 3)).Append(';')
            .Append(F(Mean(cpu), 3)).Append(';').Append(F(Pct(cpu, 0.95f), 3)).Append(';')
            .Append(F(Mean(gpu), 3)).Append(';').Append(F(Pct(gpu, 0.5f), 3)).Append(';')
            .Append(F(Pct(gpu, 0.95f), 3)).Append(';').Append(F(Pct(gpu, 0.99f), 3)).Append(';')
            .Append(F(100f * over / samples.Count, 2)).Append(';')
            .Append(F((float)(draws / samples.Count), 1)).Append(';')
            .Append(F((float)(setPass / samples.Count), 1)).Append(';')
            .Append(Conditions().Replace(';', ',')).AppendLine();
        if (newFile) File.WriteAllText(summaryPath, line.ToString(), new UTF8Encoding(true));
        else File.AppendAllText(summaryPath, line.ToString(), new UTF8Encoding(false));

        Debug.Log($"[FrameTiming] {mode}: {samples.Count} кадров, GPU ср. {F(Mean(gpu), 2)} мс " +
                  $"(p95 {F(Pct(gpu, 0.95f), 2)}), CPU ср. {F(Mean(cpu), 2)} мс, " +
                  $"draw calls {F((float)(draws / samples.Count), 0)}, вне бюджета {F(100f * over / samples.Count, 1)} %.\n" +
                  $"Условия: {Conditions()}\n" +
                  $"Файлы: {rawPath}\n{summaryPath}");
    }

    // Условия замера (столбец note): сцена, сценарий, освещение в начале и в конце, переход, проба, запись
    private string Conditions()
    {
        var b = new StringBuilder();
        b.Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        ScenarioRunner r = ScenarioRunner.Instance;
        if (r != null && r.Current != null) b.Append(", сценарий ").Append(r.Current.id).Append(" (").Append(PhaseName(r.CurrentPhase)).Append(')');
        else b.Append(", без сценария");
        string end = Lighting();
        b.Append(", ").Append(conditionsAtStart == end ? end : conditionsAtStart + " -> " + end);
        b.Append(", переход освещения: ").Append(sawTransition ? "да" : "нет");
        int renders = probe != null ? probe.Renders - probeRendersAtStart : 0;
        b.Append(", перерисовок пробы: ").Append(renders.ToString(Inv));
        ExerciseRecorder rec = ExerciseRecorder.Instance;
        b.Append(", запись: ").Append(rec != null && rec.IsRecording ? "идёт" : "нет");
        if (!string.IsNullOrWhiteSpace(extraNote)) b.Append(", ").Append(extraNote.Trim());
        return b.ToString();
    }

    private static string Lighting()
    {
        WeatherController w = WeatherController.Instance;
        if (w == null) return "погода ?";
        return $"погода {w.CurrentPresetId}, время суток {w.CurrentTimeOfDay}";
    }

    private static string PhaseName(ScenarioRunner.Phase p) =>
        p == ScenarioRunner.Phase.Running ? "выполнение" : p == ScenarioRunner.Phase.Briefing ? "брифинг" : p == ScenarioRunner.Phase.Finished ? "результаты" : "свободное плавание";

    // Фактический режим: при активном загрузчике OpenXR берётся из рантайма
    private static string RenderModeName()
    {
        if (!XRSettings.isDeviceActive) return "NoXR";
        var settings = OpenXRSettings.Instance;
        return settings != null ? settings.renderMode.ToString() : XRSettings.stereoRenderingMode.ToString();
    }

    private static string F(float v, int digits) =>
        float.IsNaN(v) ? "" : v.ToString("F" + digits, Inv);

    private static float Mean(List<float> v)
    {
        if (v.Count == 0) return float.NaN;
        double s = 0; foreach (float x in v) s += x;
        return (float)(s / v.Count);
    }

    // Перцентиль (ближайший ранг)
    private static float Pct(List<float> v, float p)
    {
        if (v.Count == 0) return float.NaN;
        var a = new List<float>(v); a.Sort();
        int i = Mathf.Clamp(Mathf.CeilToInt(p * a.Count) - 1, 0, a.Count - 1);
        return a[i];
    }
}
