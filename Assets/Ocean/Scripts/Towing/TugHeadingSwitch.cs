// ============================================================
// TugHeadingSwitch.cs — Тумблер «УДЕРЖ. КУРС» своего буксира (запрос 19.4а-2)
//
// Строится кодом (OwnTugDrive) рядом с рукояткой джойстика — копия тумблера «СВЕТ» (BridgeLightSwitch, Setup B4:
// основание, рычажок, индикатор, табличка), без его компонента; если тумблера «СВЕТ» в сцене нет — простая модель.
// Нажатие (рука или луч) — удержание курса вкл/выкл (OwnTugDrive.SetHeadingHold). Рычажок вверх — включено,
// индикатор горит зелёным; выключено — рычажок вниз, индикатор тлеет. По умолчанию — включено.
// Дочерний к рукоятке (Ship/TugJoystick): двигается вместе с ней.
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class TugHeadingSwitch : MonoBehaviour
{
    public OwnTugDrive drive;
    public Transform lever;
    public Renderer indicator;
    public float leverAngle = 28f;
    [Range(0f, 1f)] public float idleGlow = 0.12f;

    private XRSimpleInteractable interactable;
    private MaterialPropertyBlock block;
    private float shown = 1f;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Построить тумблер в системе parent (точка localPos), подписать label.</summary>
    public static TugHeadingSwitch Build(Transform parent, Vector3 localPos, OwnTugDrive drive, string label)
    {
        BridgeLightSwitch src = FindAnyObjectByType<BridgeLightSwitch>(FindObjectsInactive.Include);
        GameObject go;
        Transform leverT = null;
        Renderer ind = null;
        if (src != null)
        {
            var holder = new GameObject("TugHeadingSwitch_Build");
            holder.SetActive(false);   // копия не просыпается, пока не убран BridgeLightSwitch
            go = Instantiate(src.gameObject, holder.transform);
            DestroyImmediate(go.GetComponent<BridgeLightSwitch>());
            leverT = go.transform.Find("LeverPivot");
            Transform it = go.transform.Find("Indicator");
            if (it != null) ind = it.GetComponent<Renderer>();
            var tmp = go.GetComponentInChildren<TMPro.TextMeshPro>(true);
            if (tmp != null) tmp.text = label;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Inverse(parent.rotation) * src.transform.rotation;   // как «СВЕТ» относительно судна
            go.transform.localScale = src.transform.localScale;
            Destroy(holder);
        }
        else
        {
            go = new GameObject("TugHeadingSwitch");
            go.transform.SetParent(parent, false);
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.transform.SetParent(go.transform, false);
            b.transform.localPosition = new Vector3(0f, 0.006f, 0f);
            b.transform.localScale = new Vector3(0.05f, 0.012f, 0.07f);
            DestroyImmediate(b.GetComponent<Collider>());
            leverT = new GameObject("LeverPivot").transform;
            leverT.SetParent(go.transform, false);
            leverT.localPosition = new Vector3(0f, 0.012f, 0f);
            var l = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            l.transform.SetParent(leverT, false);
            l.transform.localPosition = new Vector3(0f, 0.018f, 0f);
            l.transform.localScale = new Vector3(0.008f, 0.018f, 0.008f);
            DestroyImmediate(l.GetComponent<Collider>());
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.025f, 0.01f);
            col.size = new Vector3(0.06f, 0.05f, 0.09f);
            go.AddComponent<XRSimpleInteractable>();
        }
        go.name = "TugHeadingSwitch";
        go.transform.localPosition = localPos;
        var sw = go.AddComponent<TugHeadingSwitch>();
        sw.drive = drive;
        sw.lever = leverT;
        sw.indicator = ind;
        return sw;
    }

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        block = new MaterialPropertyBlock();
    }

    void OnEnable() { if (interactable != null) interactable.selectEntered.AddListener(OnSelect); }
    void OnDisable() { if (interactable != null) interactable.selectEntered.RemoveListener(OnSelect); }

    private void OnSelect(SelectEnterEventArgs args)
    {
        if (drive != null) drive.SetHeadingHold(!drive.HeadingHold, "тумблер");
    }

    void Update()
    {
        bool on = drive == null || drive.HeadingHold;
        shown = Mathf.MoveTowards(shown, on ? 1f : -1f, Time.deltaTime * 12f);
        if (lever != null) lever.localRotation = Quaternion.Euler(-leverAngle * shown, 0f, 0f);
        if (indicator != null)
        {
            Color c = new Color(0.2f, 1f, 0.35f);
            indicator.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c * 0.3f);
            block.SetColor(EmissionId, c * (on ? 1.2f : idleGlow));
            indicator.SetPropertyBlock(block);
        }
    }
}
