// ============================================================
// FrameTimingCapture.cs — Замер времени кадра CPU/GPU для ВКР
// Ставится скриптом Tools/VR Bridge/Setup Frame Timing (объект PerfCapture).
//
// Меню Y «Замер» (до D1 — F8) — начать замер (durationSeconds), повторно — прервать.
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
// D1: в note — режим монитора (окно инструктора / с PiP / зеркало шлема). Запуск — меню Y «Замер»
// (клавиша F8 — только при keyboardShortcuts).
// Индикатор (после D1): в шлеме надпись «ЗАМЕР N с» с обратным отсчётом (прогрев + замер) и
// режимом монитора — над штурвалом или над indicatorAnchor; по окончании — итог на summarySeconds
// (кадров/с, доля > 11,1 мс, кадров > 20 мс, наибольший кадр, сохранён ли). Короткий тон в начале
// (звучит в прогреве) и в конце (после последнего кадра) — в измеряемое окно звук не попадает.
// Текст меняется раз в секунду (перестраивается меш одной строки), положение — тогда же; холст
// дочерний к судну, поэтому качается с мостиком без пересчёта в каждом кадре. В note — «индикатор:
// вкл/выкл». Выход из Play Mode во время замера — набранное сохраняется (aborted = 1), а не теряется.
// В сводку добавлены столбцы frames_over_20ms и max_frame_dt_ms (в конец, после note).
// D2: в note — навигационный планшет (скрыт / север вверх / курс вверх).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
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
    [Tooltip("Клавиша F8 (D1: замер запускается из меню Y «Замер»; клавиша — по желанию)")]
    public bool keyboardShortcuts;

    [Header("Индикатор в шлеме")]
    [Tooltip("Надпись «ЗАМЕР N с» во время замера и итог после него (выкл — для контрольного замера без надписи)")]
    public bool showIndicator = true;
    [Tooltip("Над каким объектом показывать надпись (пусто — над штурвалом; можно перетащить, например, экран на пульте)")]
    public Transform indicatorAnchor;
    [Tooltip("Высота надписи над объектом привязки, м")]
    public float indicatorHeight = 0.35f;
    [Tooltip("Масштаб холста надписи, м на единицу UI")]
    public float indicatorScale = 0.0008f;
    [Tooltip("Сколько секунд виден итог после замера")]
    public float summarySeconds = 4f;
    [Tooltip("Короткий тон в начале и в конце замера")]
    public bool tones = true;
    [Range(0f, 1f)] public float toneVolume = 0.25f;

    // Кадр длиннее этого — пропуск кадра при 90 Гц (табл. 13.2: столбец «кадров > 20 мс»)
    private const float LongFrameMs = 20f;
    // Ниже этой частоты итог показывается жёлтым (порог табл. 13.2)
    private const float FpsOk = 89.5f;
    private static readonly Color CaptureColor = new Color(1f, 0.22f, 0.15f, 0.75f);
    private static readonly Color SummaryOkColor = new Color(0.55f, 1f, 0.6f, 0.9f);
    private static readonly Color SummaryWarnColor = new Color(1f, 0.82f, 0.25f, 0.9f);

    private BridgeMenuCommand menuCommand;
    private Canvas indicator;
    private CanvasGroup indicatorGroup;
    private TextMeshProUGUI indicatorLabel;
    private int shownSeconds = -1;
    private float summaryHideAt;
    private AudioSource toneSource;
    private AudioClip toneStart, toneEnd, toneAbort;

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
        BuildTones();
        // D1: без клавиатуры — из меню «Параметры» (Y), раздел «Замер»
        menuCommand = BridgeMenu.Register("Замер",
            () => capturing ? $"Прервать замер (осталось {RemainingSeconds()} с)" : $"Замер частоты кадров ({durationSeconds:F0} с)", ToggleCapture)
            .OpensWindow()   // замер — без открытого меню перед глазами (как обычный вид)
            .InMenu(BridgeMenu.SettingsMenuId);
    }

    void OnDisable()
    {
        // Выход из Play Mode во время замера: сохранить набранное (aborted = 1), без надписи и тона
        if (capturing)
        {
            try { Finish(true, true); }
            catch (Exception ex) { Debug.LogWarning("[FrameTiming] Замер при выходе не сохранён: " + ex.Message); }
        }
        if (indicator != null) indicator.enabled = false;
        drawRec.Dispose();
        setPassRec.Dispose();
        batchRec.Dispose();
        BridgeMenu.Unregister(menuCommand);
        menuCommand = null;
    }

    /// <summary>Начать замер или прервать идущий (меню Y; F8 — если включены клавиши).</summary>
    public void ToggleCapture()
    {
        if (capturing) Finish(true);
        else Begin();
    }

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();

        if (keyboardShortcuts && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            ToggleCapture();

        UpdateIndicator();
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
        shownSeconds = -1;
        if (showIndicator) ShowIndicator();
        PlayTone(toneStart);   // звучит в прогреве — в замер не попадает
        Debug.Log($"[FrameTiming] Замер начат: {RenderModeName()}, {durationSeconds:F0} с " +
                  $"(+{warmupSeconds:F0} с прогрев). Не двигайте головой. Прервать — меню Y «Замер».");
    }

    /// <param name="quitting">выход из Play Mode: только сохранить, без надписи и тона</param>
    private void Finish(bool aborted, bool quitting = false)
    {
        capturing = false;
        if (samples.Count < 10)
        {
            Debug.LogWarning("[FrameTiming] Слишком мало кадров — замер не сохранён.");
            if (!quitting)
            {
                ShowSummary("ЗАМЕР ПРЕРВАН", "мало кадров — не сохранён", SummaryWarnColor);
                PlayTone(toneAbort);
            }
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
        int longFrames = 0;
        float maxDt = 0f;
        foreach (float d in dt)
        {
            if (d > budget * 1.05f) over++;   // 5 % допуск на дрожание
            if (d > LongFrameMs) longFrames++;
            if (d > maxDt) maxDt = d;
        }
        float duration = samples[samples.Count - 1].t;
        float fps = samples.Count / Mathf.Max(0.001f, duration);
        float overPct = 100f * over / samples.Count;

        string summaryPath = Path.Combine(dir, "frametiming_summary.csv");
        bool newFile = !File.Exists(summaryPath);
        if (!newFile) UpgradeSummaryHeader(summaryPath);
        var line = new StringBuilder();
        if (newFile) line.AppendLine(SummaryHeader);
        line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)).Append(';')
            .Append(mode).Append(';')
            .Append(Application.isEditor ? "1" : "0").Append(';')
            .Append(aborted ? "1" : "0").Append(';')
            .Append(F(duration, 1)).Append(';')
            .Append(samples.Count.ToString(Inv)).Append(';')
            .Append(F(fps, 1)).Append(';')
            .Append(F(Mean(dt), 3)).Append(';')
            .Append(F(Mean(cpu), 3)).Append(';').Append(F(Pct(cpu, 0.95f), 3)).Append(';')
            .Append(F(Mean(gpu), 3)).Append(';').Append(F(Pct(gpu, 0.5f), 3)).Append(';')
            .Append(F(Pct(gpu, 0.95f), 3)).Append(';').Append(F(Pct(gpu, 0.99f), 3)).Append(';')
            .Append(F(overPct, 2)).Append(';')
            .Append(F((float)(draws / samples.Count), 1)).Append(';')
            .Append(F((float)(setPass / samples.Count), 1)).Append(';')
            .Append(Conditions().Replace(';', ',')).Append(';')
            .Append(longFrames.ToString(Inv)).Append(';')
            .Append(F(maxDt, 1)).AppendLine();
        if (newFile) File.WriteAllText(summaryPath, line.ToString(), new UTF8Encoding(true));
        else File.AppendAllText(summaryPath, line.ToString(), new UTF8Encoding(false));

        Debug.Log($"[FrameTiming] {mode}: {samples.Count} кадров, GPU ср. {F(Mean(gpu), 2)} мс " +
                  $"(p95 {F(Pct(gpu, 0.95f), 2)}), CPU ср. {F(Mean(cpu), 2)} мс, " +
                  $"draw calls {F((float)(draws / samples.Count), 0)}, вне бюджета {F(overPct, 1)} %, " +
                  $"кадров > {LongFrameMs:F0} мс: {longFrames}, наибольший {F(maxDt, 1)} мс.\n" +
                  $"Условия: {Conditions()}\n" +
                  $"Файлы: {rawPath}\n{summaryPath}");

        if (quitting) return;
        ShowSummary(aborted ? "ЗАМЕР ПРЕРВАН" : "ЗАМЕР ОКОНЧЕН",
            $"{R(fps, 1)} кадр/с · > {R(budget, 1)} мс: {R(overPct, 1)} % · > {LongFrameMs:F0} мс: {longFrames} (наиб. {R(maxDt, 0)} мс)\n" +
            $"{MonitorMode(false)} · {(aborted ? $"сохранено {R(duration, 0)} с" : "сохранён")}",
            fps < FpsOk || aborted ? SummaryWarnColor : SummaryOkColor);
        PlayTone(aborted ? toneAbort : toneEnd);   // после последнего кадра — в замер не попадает
    }

    private const string SummaryHeader =
        "date;render_mode;editor;aborted;duration_s;frames;fps_mean;" +
        "frame_dt_mean_ms;cpu_mean_ms;cpu_p95_ms;gpu_mean_ms;gpu_median_ms;gpu_p95_ms;gpu_p99_ms;" +
        "over_budget_pct;draw_calls_mean;setpass_mean;note;frames_over_20ms;max_frame_dt_ms";

    // Сводка, начатая до индикатора: дописать в заголовок новые столбцы (у старых строк они пустые)
    private static void UpgradeSummaryHeader(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0].Contains("frames_over_20ms")) return;
        lines[0] = lines[0].TrimEnd() + ";frames_over_20ms;max_frame_dt_ms";
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    // ─────────── Индикатор в шлеме ───────────

    /// <summary>Сколько секунд осталось до конца замера (прогрев + замер), с округлением вверх.</summary>
    private int RemainingSeconds()
    {
        float left = warmupSeconds + durationSeconds - (Time.realtimeSinceStartup - startTime);
        return Mathf.Max(0, Mathf.CeilToInt(left));
    }

    private void UpdateIndicator()
    {
        if (indicator == null || !indicator.enabled) return;
        if (capturing)
        {
            // Текст и положение — только при смене секунды (меш строки перестраивается раз в секунду)
            int left = RemainingSeconds();
            if (left == shownSeconds) return;
            shownSeconds = left;
            bool warmup = Time.realtimeSinceStartup - startTime < warmupSeconds;
            indicatorLabel.text = $"<b>ЗАМЕР {left} с</b>\n<size=55%>{(warmup ? "прогрев" : "не двигайте головой")} · {MonitorMode(false)}</size>";
            PlaceIndicator();
            return;
        }
        float rest = summaryHideAt - Time.unscaledTime;
        indicatorGroup.alpha = Mathf.Clamp01(rest / 0.8f);   // итог гаснет за последние 0,8 с
        if (rest <= 0f) indicator.enabled = false;
    }

    private void ShowIndicator()
    {
        BuildIndicator();
        if (indicator == null) return;
        indicatorLabel.color = CaptureColor;
        indicatorGroup.alpha = 1f;
        indicator.enabled = true;
        UpdateIndicator();
    }

    private void ShowSummary(string title, string detail, Color color)
    {
        if (!showIndicator) return;
        BuildIndicator();
        if (indicator == null) return;
        indicatorLabel.color = color;
        indicatorLabel.text = $"<b>{title}</b>\n<size=50%>{detail}</size>";
        indicatorGroup.alpha = 1f;
        indicator.enabled = true;
        PlaceIndicator();
        summaryHideAt = Time.unscaledTime + summarySeconds;
    }

    // Объект привязки: заданный в инспекторе или штурвал
    private Transform Anchor()
    {
        if (indicatorAnchor != null) return indicatorAnchor;
        SteeringWheel wheel = FindAnyObjectByType<SteeringWheel>();
        return wheel != null ? wheel.transform : null;
    }

    private void BuildIndicator()
    {
        Transform anchor = Anchor();
        Camera cam = Camera.main;
        // Холст — дочерний к родителю привязки (у штурвала — Ship: сам штурвал вращается и масштабирован
        // неравномерно); без привязки — к Camera Offset (движется с судном), перед глазами
        Transform parent = anchor != null ? (anchor.parent != null ? anchor.parent : anchor)
                         : cam != null ? cam.transform.parent : null;
        if (indicator == null)
        {
            var go = new GameObject("FrameTimingIndicator", typeof(RectTransform));
            indicator = go.AddComponent<Canvas>();
            indicator.renderMode = RenderMode.WorldSpace;
            indicator.sortingOrder = 10;
            indicatorGroup = go.AddComponent<CanvasGroup>();
            indicatorGroup.interactable = false;
            indicatorGroup.blocksRaycasts = false;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 170f);

            var text = new GameObject("Text", typeof(RectTransform));
            text.transform.SetParent(go.transform, false);
            indicatorLabel = text.AddComponent<TextMeshProUGUI>();
            indicatorLabel.fontSize = 60f;
            indicatorLabel.alignment = TextAlignmentOptions.Center;
            indicatorLabel.raycastTarget = false;
            var rt = text.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            indicator.enabled = false;
        }
        if (indicator.transform.parent != parent) indicator.transform.SetParent(parent, false);
        Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
        indicator.transform.localScale = new Vector3(indicatorScale / Mathf.Max(1e-6f, Mathf.Abs(ps.x)),
            indicatorScale / Mathf.Max(1e-6f, Mathf.Abs(ps.y)), indicatorScale / Mathf.Max(1e-6f, Mathf.Abs(ps.z)));
    }

    // Над объектом привязки (вверх — по судну), лицом к голове по горизонтали
    private void PlaceIndicator()
    {
        if (indicator == null) return;
        Transform parent = indicator.transform.parent;
        Vector3 up = parent != null ? parent.up : Vector3.up;
        Transform anchor = Anchor();
        Camera cam = Camera.main;
        Vector3 pos;
        if (anchor != null) pos = anchor.position + up * indicatorHeight;
        else if (cam != null)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            pos = cam.transform.position + fwd.normalized * 1.1f + up * 0.25f;
        }
        else return;
        Vector3 look = cam != null ? Vector3.ProjectOnPlane(pos - cam.transform.position, up) : Vector3.zero;
        if (look.sqrMagnitude < 1e-4f) look = parent != null ? parent.forward : Vector3.forward;
        indicator.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, up));
    }

    // ─────────── Тоны ───────────

    private void PlayTone(AudioClip clip)
    {
        if (!tones || toneSource == null || clip == null) return;
        toneSource.PlayOneShot(clip, toneVolume);
    }

    // Клипы — заранее (OnEnable), не во время замера
    private void BuildTones()
    {
        if (toneSource != null) return;
        toneSource = gameObject.AddComponent<AudioSource>();
        toneSource.playOnAwake = false;
        toneSource.spatialBlend = 0f;
        toneStart = Beeps("FrameTimingStart", (880f, 0.15f));
        toneEnd = Beeps("FrameTimingEnd", (880f, 0.12f), (0f, 0.08f), (1320f, 0.12f));
        toneAbort = Beeps("FrameTimingAbort", (440f, 0.25f));
    }

    // Последовательность тонов (частота 0 — пауза), огибающая 10 мс — без щелчков
    private static AudioClip Beeps(string name, params (float hz, float s)[] parts)
    {
        const int rate = 44100;
        int total = 0;
        foreach (var p in parts) total += (int)(p.s * rate);
        var data = new float[total];
        int fade = (int)(0.01f * rate), at = 0;
        foreach (var p in parts)
        {
            int n = (int)(p.s * rate);
            if (p.hz > 0f)
                for (int i = 0; i < n; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i, n - i) / (float)fade);
                    data[at + i] = 0.8f * env * Mathf.Sin(2f * Mathf.PI * p.hz * i / rate);
                }
            at += n;
        }
        AudioClip clip = AudioClip.Create(name, total, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Режим монитора: полный — для note, краткий — для надписи
    private static string MonitorMode(bool full)
    {
        InstructorStation st = InstructorStation.Instance;
        if (st == null) return full ? "" : "монитор ?";
        if (st.Mode != InstructorStation.DisplayMode.Instructor) return full ? "зеркало шлема" : "зеркало";
        bool pip = st.Pip != null && st.Pip.Active;
        if (!full) return pip ? "окно + PiP" : "окно, PiP выкл";
        return "окно инструктора" + (pip ? $" + PiP {st.Pip.width}x{st.Pip.height} {st.Pip.rateHz.ToString("0", Inv)} Гц" : "");
    }

    // Число для надписи (десятичная запятая)
    private static string R(float v, int digits) => v.ToString("F" + digits, Inv).Replace('.', ',');

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
        // D1: что на мониторе — окно инструктора (и PiP) или зеркало из шлема
        string monitor = MonitorMode(true);
        if (monitor.Length > 0) b.Append(", монитор: ").Append(monitor);
        b.Append(", индикатор: ").Append(showIndicator ? "вкл" : "выкл");
        // D2: навигационный планшет в руке — показан ли и в каком режиме (в конце замера)
        NavPlotter plotter = NavPlotter.Instance;
        if (plotter != null) b.Append(", планшет: ").Append(plotter.Shown ? (plotter.headUp ? "курс вверх" : "север вверх") : "скрыт");
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
