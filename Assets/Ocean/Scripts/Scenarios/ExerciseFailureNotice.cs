// ============================================================
// ExerciseFailureNotice.cs — Оповещение «Упражнение провалено» (B3)
//
// Подписчик шины ExerciseEvents: посадка на мель (Grounded) или столкновение
// (Collision) во время упражнения — провал независимо от остальных результатов
// (так же будет в оценке C2). Красная надпись появляется перед обучающимся
// (выше панели результатов) на showSeconds и гаснет; на экране результатов
// причина провала выделяется красным (ScenarioPanel).
// Оповещение срабатывает и когда условие завершения в сценарии выключено
// (например, при отработке съёма с мели упражнение продолжается).
// Переключатель — меню «Параметры» (Y), подменю «Оповещения» (до D1 — меню A, «Сценарий»):
// «Оповещение о провале: вкл/выкл». Объект — дочерний к Ship (качается с мостиком).
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ExerciseFailureNotice : MonoBehaviour
{
    [Tooltip("Показывать оповещение о провале (переключается из меню мостика)")]
    public bool showNotice = true;
    [Tooltip("Сколько секунд видна надпись")]
    public float showSeconds = 6f;
    public float distanceFromHead = 1.1f;
    [Tooltip("Выше уровня глаз, м (панель результатов — ниже)")]
    public float upOffset = 0.22f;
    public float canvasScale = 0.001f;

    public static ExerciseFailureNotice Instance { get; private set; }
    /// <summary>Выделять провал (надпись и строка на экране результатов).</summary>
    public static bool Enabled => Instance == null || Instance.showNotice;

    private Canvas canvas;
    private TextMeshProUGUI label;
    private CanvasGroup group;
    private float hideAt;
    private BridgeMenuCommand toggle;

    void Awake() => Instance = this;

    void OnEnable()
    {
        ExerciseEvents.Raised += OnEvent;
        // D1: переключатель — в меню «Параметры» (Y), подменю «Оповещения»
        toggle = BridgeMenu.Register("Оповещения",
            () => showNotice ? "Оповещение о провале: вкл" : "Оповещение о провале: выкл",
            () => showNotice = !showNotice, instructorOnly: true).InMenu(BridgeMenu.SettingsMenuId);
        toggle.CloseOnExecute = false;
    }

    void OnDisable()
    {
        ExerciseEvents.Raised -= OnEvent;
        BridgeMenu.Unregister(toggle);
        toggle = null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEvent(ExerciseEvent e)
    {
        if (!showNotice || !e.InExercise) return;
        if (e.Type == ExerciseEventType.Collision) Show($"столкновение с «{e.Subject}»");
        else if (e.Type == ExerciseEventType.Grounded) Show($"посадка на мель «{e.Subject}»");
    }

    public void Show(string reason)
    {
        Build();
        label.text = $"<b>УПРАЖНЕНИЕ ПРОВАЛЕНО</b>\n<size=70%>{reason}</size>";
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
        hideAt = Time.unscaledTime + showSeconds;
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
        var go = new GameObject("FailureNoticeCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        go.transform.localScale = Vector3.one * canvasScale;
        group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(620f, 130f);

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        var img = bg.AddComponent<Image>();
        img.color = new Color(0.45f, 0.04f, 0.03f, 0.92f);
        img.raycastTarget = false;
        Stretch(bg.GetComponent<RectTransform>());

        var text = new GameObject("Text", typeof(RectTransform));
        text.transform.SetParent(go.transform, false);
        label = text.AddComponent<TextMeshProUGUI>();
        label.fontSize = 40f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        Stretch(text.GetComponent<RectTransform>());
        canvas.enabled = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
