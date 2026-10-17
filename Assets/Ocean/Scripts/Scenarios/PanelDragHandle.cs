// ============================================================
// PanelDragHandle.cs — Ручка «переместить» над world-space панелью (17.10.2026, по проверке 19-03)
//
// Полоска над верхним краем панели (UI Image и подпись без raycastTarget + BoxCollider, XRSimpleInteractable):
// взять лучом (курок) или рукой — панель идёт за контроллером жёстко (смещение в системе контроллера — как при
// хвате) и разворачивается к голове; отпустили — остаётся, где оставили (в системе судна: панель — дочерняя к Ship).
// UI-луч ручку не видит (raycastTarget выкл), поэтому кнопки панели и ручка не мешают друг другу.
// Строится кодом (ScenarioPanel), сцену не правит. Пока панель скрыта — ручка выключена (коллайдер тоже).
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class PanelDragHandle : MonoBehaviour
{
    public const float Height = 34f;
    private static readonly Color Idle = new Color(0.33f, 0.35f, 0.4f, 0.95f), Hover = new Color(0.3f, 0.52f, 0.8f, 0.98f);

    private Transform target;     // двигаемая панель (корень холста)
    private Transform hand;       // контроллер, который держит
    private Vector3 localPos;     // точка панели в системе контроллера
    private Image image;
    private RectTransform rect;
    private BoxCollider box;

    public bool IsGrabbed => hand != null;

    /// <summary>Ручка на холсте panel (world-space, масштаб — м на пиксель) шириной width пикселей.</summary>
    public static PanelDragHandle Create(Transform panel, float width, string label = "• • •   переместить   • • •")
    {
        var go = new GameObject("DragHandle", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var h = go.AddComponent<PanelDragHandle>();
        h.target = panel;
        h.rect = go.GetComponent<RectTransform>();
        h.rect.sizeDelta = new Vector2(width, Height);
        h.image = go.AddComponent<Image>();
        h.image.color = Idle;
        h.image.raycastTarget = false;

        var text = new GameObject("Text", typeof(RectTransform));
        text.transform.SetParent(go.transform, false);
        var tmp = text.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 20;
        tmp.color = new Color(0.85f, 0.92f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        var tr = text.GetComponent<RectTransform>();
        tr.sizeDelta = new Vector2(width, Height);
        tr.anchoredPosition3D = new Vector3(0f, 0f, -1f);

        // Коллайдер — в пикселях холста (масштаб панели переводит в метры); толщина 20 px ≈ 2 см при 0,001 м/px
        h.box = go.AddComponent<BoxCollider>();
        h.box.size = new Vector3(width, Height, 20f);
        var interactable = go.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(h.OnGrab);
        interactable.selectExited.AddListener(_ => h.hand = null);
        interactable.hoverEntered.AddListener(_ => h.image.color = Hover);
        interactable.hoverExited.AddListener(_ => { if (h.hand == null) h.image.color = Idle; });
        return h;
    }

    /// <summary>Над верхним краем панели: top — y верхнего края в пикселях холста (от центра).</summary>
    public void PlaceAbove(float top, float gap = 6f)
    {
        rect.anchoredPosition = new Vector2(0f, top + gap + 0.5f * Height);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        hand = args.interactorObject.transform;
        localPos = hand.InverseTransformPoint(target.position);
        image.color = Hover;
    }

    void OnDisable()
    {
        hand = null;
        if (image != null) image.color = Idle;
    }

    void LateUpdate()
    {
        if (hand == null) return;
        Vector3 p = hand.TransformPoint(localPos);
        Camera cam = Camera.main;
        Vector3 look = cam != null ? p - cam.transform.position : target.forward;
        target.position = p;
        if (look.sqrMagnitude > 1e-4f) target.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
    }
}
