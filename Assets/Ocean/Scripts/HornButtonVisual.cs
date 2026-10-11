// ============================================================
// HornButtonVisual.cs — Вид кнопки тифона (гудка) на пульте (C2, по замечанию)
//
// До C2 кнопка была плоским диском без корпуса и на кнопку не походила.
// Теперь (Tools/VR Bridge/Setup Horn Button): корпус на пульте, металлический
// ободок, янтарный грибовидный колпачок, табличка «ТИФОН». Этот компонент:
//   • колпачок утапливается, пока кнопку держат (select луча или руки);
//   • при наведении колпачок подсвечивается, пока звучит гудок — светится;
//   • в темноте (WeatherController.Darkness) колпачок слабо тлеет — кнопку
//     видно ночью, как тумблер «СВЕТ» (B4).
// Сам гудок по-прежнему — ShipHorn.PlayHorn по selectEntered (фиксированные 3 с);
// «гудок, пока держат» и тип сигнала по правилу 32 — запрос C4.
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class HornButtonVisual : MonoBehaviour
{
    [Tooltip("Колпачок (двигается вдоль локальной оси Y)")]
    public Transform cap;
    public Renderer[] glowRenderers;
    public ShipHorn horn;

    [Tooltip("Ход колпачка при нажатии, м")]
    public float pressDepth = 0.008f;
    [Tooltip("Постоянная времени хода, с")]
    public float pressTime = 0.04f;

    [Header("Свечение колпачка")]
    public Color glowColor = new Color(1f, 0.55f, 0.1f);
    [Tooltip("В темноте (кнопку видно ночью)")]
    [Range(0f, 1f)] public float idleGlow = 0.1f;
    [Range(0f, 1f)] public float hoverGlow = 0.35f;
    [Range(0f, 2f)] public float soundingGlow = 1.2f;

    private XRSimpleInteractable interactable;
    private MaterialPropertyBlock block;
    private Vector3 capRest;
    private float press, glow;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        block = new MaterialPropertyBlock();
        if (cap != null) capRest = cap.localPosition;
        if (horn == null) horn = GetComponent<ShipHorn>();
    }

    void Update()
    {
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.005f, pressTime));
        press = Mathf.Lerp(press, interactable.isSelected ? 1f : 0f, k);
        if (cap != null) cap.localPosition = capRest + Vector3.down * (pressDepth * press);

        float darkness = WeatherController.Instance != null ? WeatherController.Instance.Darkness : 0f;
        float target = idleGlow * darkness;
        if (interactable.isHovered) target = Mathf.Max(target, hoverGlow);
        if (horn != null && horn.IsSounding) target = soundingGlow * (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
        glow = Mathf.Lerp(glow, target, 1f - Mathf.Exp(-Time.deltaTime / 0.08f));

        if (glowRenderers == null) return;
        block.SetColor(EmissionId, glowColor * glow);
        foreach (Renderer r in glowRenderers)
            if (r != null) r.SetPropertyBlock(block);
    }
}
