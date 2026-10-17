// ============================================================
// PilotOrderNotice.cs — Команда лоцмана в шлеме (запрос 19.4б-2)
//
// Подписчик шины ExerciseEvents: команда лоцмана (PilotOrder, упражнение на буксире 19-03) — синяя надпись
// «Лоцман: …» перед обучающимся на showSeconds и тон «двойной сигнал» (как вызов по УКВ; синтез 1 кГц, 2 × 0,12 с).
// Во всех режимах тренажёра: это команда упражнения, а не подсказка. Ниже оповещения о провале
// (ExerciseFailureNotice — выше уровня глаз). Строится кодом — объект «PilotOrderNotice», дочерний к Ship
// (качается с мостиком), Setup не нужен.
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PilotOrderNotice : MonoBehaviour
{
    [Tooltip("Сколько секунд видна надпись")]
    public float showSeconds = 6f;
    [Tooltip("19.4б-3: обстановка («До причала … м, идёт … уз») — секунд, без сигнала")]
    public float infoSeconds = 4f;
    public float distanceFromHead = 1.1f;
    [Tooltip("Над уровнем глаз, м (оповещение о провале — 0,22 м)")]
    public float upOffset = 0.05f;
    public float canvasScale = 0.001f;
    [Tooltip("Тон: частота, Гц; громкость 0…1")]
    public float toneHz = 1000f;
    [Range(0f, 1f)] public float toneVolume = 0.35f;

    public static PilotOrderNotice Instance { get; private set; }

    private Canvas canvas;
    private TextMeshProUGUI label;
    private CanvasGroup group;
    private float hideAt, lastCommandAt = -100f;
    private AudioSource audioSource;
    private AudioClip tone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<PilotOrderNotice>() != null || FindAnyObjectByType<ScenarioRunner>() == null) return;
        ShipTypeSwitcher ship = FindAnyObjectByType<ShipTypeSwitcher>();
        var go = new GameObject("PilotOrderNotice");
        if (ship != null) go.transform.SetParent(ship.transform, false);
        go.AddComponent<PilotOrderNotice>();
    }

    void Awake() => Instance = this;

    void OnEnable() => ExerciseEvents.Raised += OnEvent;

    void OnDisable() => ExerciseEvents.Raised -= OnEvent;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (tone != null) Destroy(tone);
    }

    private void OnEvent(ExerciseEvent e)
    {
        if (e.Type != ExerciseEventType.PilotOrder || !e.InExercise) return;
        // 19.4б-3: обстановка («До причала …») — короче и без сигнала; команды — с сигналом
        bool info = e.Index == (int)PilotOrderKind.Info;
        if (info && Time.unscaledTime - lastCommandAt < 3f) return;   // не перекрывать только что поданную команду (обстановка — и на мониторе)
        if (!info) lastCommandAt = Time.unscaledTime;
        Show(e.Subject, info ? infoSeconds : showSeconds, !info);
    }

    public void Show(string order) => Show(order, showSeconds, true);

    public void Show(string order, float seconds, bool withTone)
    {
        Build();
        label.text = $"<size=70%>ЛОЦМАН</size>\n<b>«{order}»</b>";
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = cam.transform.position + fwd * distanceFromHead + Vector3.up * upOffset;
            canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }
        canvas.enabled = true;
        group.alpha = 1f;
        hideAt = Time.unscaledTime + seconds;
        if (withTone && toneVolume > 0f) audioSource.PlayOneShot(tone, toneVolume);
    }

    void Update()
    {
        if (canvas == null || !canvas.enabled) return;
        float left = hideAt - Time.unscaledTime;
        group.alpha = Mathf.Clamp01(left / 0.8f);   // гаснет за последние 0,8 с
        if (left <= 0f) canvas.enabled = false;
    }

    private void Build()
    {
        if (canvas != null) return;
        var go = new GameObject("PilotOrderCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 9;
        go.transform.localScale = Vector3.one * canvasScale;
        group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 120f);

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        var img = bg.AddComponent<Image>();
        img.color = new Color(0.04f, 0.16f, 0.36f, 0.92f);
        img.raycastTarget = false;
        Stretch(bg.GetComponent<RectTransform>());

        var text = new GameObject("Text", typeof(RectTransform));
        text.transform.SetParent(go.transform, false);
        label = text.AddComponent<TextMeshProUGUI>();
        label.fontSize = 36f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        Stretch(text.GetComponent<RectTransform>());
        canvas.enabled = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        tone = DoubleBeep(toneHz);
    }

    // Два коротких сигнала 0,12 с с паузой 0,08 с, мягкие фронты 10 мс
    private static AudioClip DoubleBeep(float hz)
    {
        const int rate = 44100;
        const float beep = 0.12f, gap = 0.08f, ramp = 0.01f;
        int n = Mathf.CeilToInt((2f * beep + gap) * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float local = t < beep ? t : t >= beep + gap ? t - beep - gap : -1f;
            if (local < 0f || local > beep) continue;
            float env = Mathf.Clamp01(Mathf.Min(local, beep - local) / ramp);
            data[i] = env * Mathf.Sin(2f * Mathf.PI * hz * t);
        }
        AudioClip clip = AudioClip.Create("PilotOrderTone", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
