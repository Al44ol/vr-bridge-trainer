// ============================================================
// BridgeStationSwitch.cs — Переход «К штурвалу / Крыло ЛБ / Крыло ПБ» (запрос 19.2б-2)
//
// У модели МОСТИКИ крыльев нет — вокруг рубки галерея с леером; точки крыльев ставит Setup 19.2 Berth
// (на галерее у борта, при необходимости — площадка до борта с полом-коллайдером). Точки — дочерние
// объекты Ship (качаются вместе с судном), «вперёд» точки — в нос.
// Переход мгновенный: XR Origin поворачивается вокруг головы так, чтобы взгляд (по горизонтали) смотрел
// вперёд точки, затем сдвигается так, чтобы голова встала над точкой, а основание Rig — на её уровень
// (пол). Рост игрока (Camera Offset, HeightAdjust) и положение в комнате не меняются. CharacterController
// на время переноса выключается (иначе вернёт прежнее положение).
// Меню «Управление» (кнопка B) → подменю «Переход»; событие StationChanged в шину.
// ============================================================
using Unity.XR.CoreUtils;
using UnityEngine;

public class BridgeStationSwitch : MonoBehaviour
{
    [Header("Точки (ставит Setup 19.2 Berth; двигать в Scene View)")]
    public Transform helm;
    public Transform wingPort;
    public Transform wingStarboard;

    [Header("Сцена (пусто — найти)")]
    public XROrigin xrOrigin;
    public ShipNavigation navigation;

    public BridgeStation Current { get; private set; } = BridgeStation.Helm;

    private readonly System.Collections.Generic.List<BridgeMenuCommand> commands = new System.Collections.Generic.List<BridgeMenuCommand>();

    void Awake()
    {
        if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
    }

    void OnEnable()
    {
        foreach (BridgeStation s in new[] { BridgeStation.Helm, BridgeStation.WingPort, BridgeStation.WingStarboard })
        {
            BridgeStation st = s;
            string title = st == BridgeStation.Helm ? "К штурвалу" : st == BridgeStation.WingPort ? "Крыло ЛБ (левый борт)" : "Крыло ПБ (правый борт)";
            commands.Add(BridgeMenu.Register(ShipControlOrders.StationsGroup, () => Current == st ? $"{title} <size=70%>(здесь)</size>" : title,
                () => MoveTo(st), () => Anchor(st) != null && xrOrigin != null && !ShipTypeSwitcher.OnTug, hideWhenUnavailable: true).InMenu(ShipControlOrders.MenuId));   // 19.4а: у буксира крыльев нет
        }
        BridgeMenu.SetGroupOrder(ShipControlOrders.StationsGroup, 40);
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in commands) BridgeMenu.Unregister(c);
        commands.Clear();
    }

    public Transform Anchor(BridgeStation s) => s == BridgeStation.WingPort ? wingPort : s == BridgeStation.WingStarboard ? wingStarboard : helm;

    /// <summary>Перейти в точку (меню; в части 3 — сценарий швартовки может начинать на крыле).</summary>
    public void MoveTo(BridgeStation s)
    {
        Transform anchor = Anchor(s);
        if (anchor == null || xrOrigin == null || xrOrigin.Camera == null) return;
        Transform origin = xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
        Transform cam = xrOrigin.Camera.transform;
        Vector3 up = anchor.up;

        var cc = origin.GetComponent<CharacterController>();
        bool ccWasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;

        // 1. Взгляд — вперёд точки (поворот вокруг головы)
        Vector3 look = Vector3.ProjectOnPlane(cam.forward, up), target = Vector3.ProjectOnPlane(anchor.forward, up);
        if (look.sqrMagnitude > 1e-6f && target.sqrMagnitude > 1e-6f)
            origin.RotateAround(cam.position, up, Vector3.SignedAngle(look, target, up));
        // 2. Голова — над точкой; 3. основание Rig — на уровне точки (пол)
        Vector3 d = anchor.position - cam.position;
        origin.position += d - up * Vector3.Dot(d, up);
        origin.position += up * Vector3.Dot(anchor.position - origin.position, up);

        Physics.SyncTransforms();
        if (cc != null) cc.enabled = ccWasEnabled;

        BridgeStation old = Current;
        Current = s;
        var e = new ExerciseEvent(ExerciseEventType.StationChanged) { Index = (int)s, Value = (int)old };
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Переход] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }
}
