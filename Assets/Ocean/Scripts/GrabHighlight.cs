// ============================================================
// GrabHighlight.cs — Подсветка органа управления при наведении руки/луча
//
// Слушает события hover/select XR-интерактивного объекта (на этом же
// GameObject или указанного явно) и подкрашивает заданные рендереры
// через MaterialPropertyBlock (ассеты материалов не меняются):
//   наведение — hoverColor, захват — selectColor.
// Нужна, чтобы обучающийся видел, за что можно взяться (рукоятка
// телеграфа, обод штурвала).
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GrabHighlight : MonoBehaviour
{
    [Tooltip("Интерактивный объект. Если пусто — берётся с этого GameObject.")]
    public XRBaseInteractable interactable;
    [Tooltip("Что подсвечивать")]
    public Renderer[] renderers;
    public Color hoverColor = new Color(1f, 0.8f, 0.25f);
    public Color selectColor = new Color(0.35f, 0.9f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock block;
    private int hoverCount;
    private bool selected;

    void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
        block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        if (interactable == null) return;
        interactable.hoverEntered.AddListener(OnHoverEnter);
        interactable.hoverExited.AddListener(OnHoverExit);
        interactable.selectEntered.AddListener(OnSelectEnter);
        interactable.selectExited.AddListener(OnSelectExit);
    }

    void OnDisable()
    {
        if (interactable == null) return;
        interactable.hoverEntered.RemoveListener(OnHoverEnter);
        interactable.hoverExited.RemoveListener(OnHoverExit);
        interactable.selectEntered.RemoveListener(OnSelectEnter);
        interactable.selectExited.RemoveListener(OnSelectExit);
        hoverCount = 0;
        selected = false;
        Apply();
    }

    private void OnHoverEnter(HoverEnterEventArgs _) { hoverCount++; Apply(); }
    private void OnHoverExit(HoverExitEventArgs _)   { hoverCount = Mathf.Max(0, hoverCount - 1); Apply(); }
    private void OnSelectEnter(SelectEnterEventArgs _) { selected = true; Apply(); }
    private void OnSelectExit(SelectExitEventArgs _)   { selected = false; Apply(); }

    private void Apply()
    {
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!selected && hoverCount == 0)
            {
                r.SetPropertyBlock(null);   // исходный цвет материала
                continue;
            }
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, selected ? selectColor : hoverColor);
            r.SetPropertyBlock(block);
        }
    }
}
