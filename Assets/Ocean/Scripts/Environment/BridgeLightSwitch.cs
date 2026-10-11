// ============================================================
// BridgeLightSwitch.cs — Тумблер «СВЕТ» на пульте (B4)
//
// Нажатие (луч или рука, XRSimpleInteractable, как кнопка гудка) включает и
// выключает свет рубки (BridgeLighting.ToggleCabin); цвет — в меню «Параметры» (Y).
// Рычажок перекидывается вверх (вкл) / вниз (выкл). Индикатор на тумблере
// горит цветом света рубки, когда свет включён, и слабо тлеет, когда выключен, —
// тумблер можно найти в темноте.
// Геометрию и место (рядом с кнопкой гудка) создаёт Tools/VR Bridge/Setup B4 Night;
// место можно поправить мышью в Scene View.
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class BridgeLightSwitch : MonoBehaviour
{
    public BridgeLighting lighting;
    [Tooltip("Ось рычажка (вращение по X)")]
    public Transform lever;
    [Tooltip("Угол рычажка от середины, градусы")]
    public float leverAngle = 28f;
    public Renderer indicator;
    [Tooltip("Яркость индикатора, когда свет выключен (чтобы тумблер был виден в темноте)")]
    [Range(0f, 1f)] public float idleGlow = 0.12f;

    private XRSimpleInteractable interactable;
    private MaterialPropertyBlock block;
    private float shown;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        block = new MaterialPropertyBlock();
    }

    void OnEnable() => interactable.selectEntered.AddListener(OnSelect);
    void OnDisable() => interactable.selectEntered.RemoveListener(OnSelect);

    void Start()
    {
        if (lighting == null) lighting = BridgeLighting.Instance != null ? BridgeLighting.Instance : FindAnyObjectByType<BridgeLighting>();
        shown = lighting != null && lighting.cabinOn ? 1f : -1f;
    }

    private void OnSelect(SelectEnterEventArgs args)
    {
        if (lighting != null) lighting.ToggleCabin();
    }

    void Update()
    {
        if (lighting == null) return;
        BridgeLighting.CabinMode mode = lighting.Mode;
        float target = mode != BridgeLighting.CabinMode.Off ? 1f : -1f;
        shown = Mathf.MoveTowards(shown, target, Time.deltaTime * 12f);
        if (lever != null) lever.localRotation = Quaternion.Euler(-leverAngle * shown, 0f, 0f);

        if (indicator != null)
        {
            Color c = lighting.cabinRed ? new Color(1f, 0.15f, 0.06f) : new Color(1f, 0.9f, 0.7f);
            float k = mode != BridgeLighting.CabinMode.Off ? 1.2f : idleGlow;
            indicator.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c * 0.3f);
            block.SetColor(EmissionId, c * k);
            indicator.SetPropertyBlock(block);
        }
    }
}
