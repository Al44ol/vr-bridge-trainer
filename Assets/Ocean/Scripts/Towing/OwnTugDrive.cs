// ============================================================
// OwnTugDrive.cs — Свой буксир: джойстик → две азимутальные колонки в модели судна (запрос 19.4а)
//
// Ставится сам (ShipTypeSwitcher) на ShipRoot рядом с ShipNavigation. Работает, пока тип судна — «буксир»
// (SetActive): на каждом шаге модели (ShipNavigation.BeforeStep, до model.Step) AzimuthDrive (Core,
// TugJoystick.cs) раскладывает положение джойстика на колонки и прикладывает силы; штурвал и телеграф
// в модель не идут (ShipNavigation.SetAzimuthDrive).
// Рукоятка джойстика (TugJoystickHandle) строится кодом при первом включении — на месте TelegraphHead,
// из материалов телеграфа. Меню «Управление» (кнопка B) → группа «Джойстик (буксир)» на главной: состояние
// колонок, «Ход / Лагом / Разворот: < … >» ступенями 25 %, «Джойстик в ноль» — для крыла и без рук.
// Событие JoystickChanged — установившееся положение (держится settleSeconds, сдвиг ≥ minChange по какой-либо
// оси), как TelegraphChanged у телеграфа (BridgeActionMonitor).
// 19.4а-2: удержание курса (AzimuthDrive.HeadingHold, по умолчанию вкл) — тумблер «УДЕРЖ. КУРС» справа от рукоятки
// (TugHeadingSwitch) и меню B «Удержание курса»; событие HeadingHoldChanged. Сдвиг рукоятки от TelegraphHead —
// ShipTypeSwitcher.tugJoystickOffset (Tools/VR Bridge/Tug — запомнить положение джойстика).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-89)]
public class OwnTugDrive : MonoBehaviour
{
    public const string Group = "Джойстик (буксир)";

    [Tooltip("Тяга на гаке своего буксира, т (TugSpec)")]
    public float bollardPullTonnes = (float)TugSpec.DefaultBollardPullTonnes;
    [Tooltip("Положение считается установившимся, если не меняется, с")]
    public float settleSeconds = 0.5f;
    [Tooltip("Наименьший сдвиг по оси для события JoystickChanged (доля хода)")]
    public float minChange = 0.05f;

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;

    public static OwnTugDrive Instance { get; private set; }
    public bool Active { get; private set; }
    public AzimuthDrive Drive => drive;
    public TugJoystickHandle Handle => handle;
    public JoystickState State => handle != null ? handle.State : menuState;

    private AzimuthDrive drive;
    private ShipParameters driveFor;
    private TugJoystickHandle handle;
    private JoystickState menuState, published;
    private float lastChangeTime = -1f;
    private string lastSource = "рукой";
    private TugHeadingSwitch headingSwitch;

    /// <summary>19.4а-2: удержание курса (тумблер «УДЕРЖ. КУРС», меню B) — по умолчанию включено.</summary>
    public bool HeadingHold => drive == null || drive.HeadingHold;

    /// <summary>Включить/выключить удержание курса; событие HeadingHoldChanged.</summary>
    public void SetHeadingHold(bool on, string source)
    {
        if (drive == null || drive.HeadingHold == on) return;
        drive.HeadingHold = on;
        var e = new ExerciseEvent(ExerciseEventType.HeadingHoldChanged) { Value = on ? 1 : 0, Subject = source };
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Буксир] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
        BridgeMenu.RebuildOpen();
    }
    private readonly List<BridgeMenuCommand> commands = new List<BridgeMenuCommand>();

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Буксир] В сцене несколько OwnTugDrive — используется первый.", this);
        else Instance = this;
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (navigation != null) navigation.BeforeStep += OnBeforeStep;
        RegisterCommands();
    }

    void OnDisable()
    {
        if (navigation != null) navigation.BeforeStep -= OnBeforeStep;
        foreach (BridgeMenuCommand c in commands) BridgeMenu.Unregister(c);
        commands.Clear();
    }

    /// <summary>Включить/выключить движитель (ShipTypeSwitcher). Выключение и включение ставят джойстик в ноль.</summary>
    public void SetActive(bool on, ShipParameters tug)
    {
        if (on && tug != null && (drive == null || driveFor != tug))
        {
            drive = new AzimuthDrive(TugSpec.For(tug, bollardPullTonnes));
            driveFor = tug;
            Debug.Log($"[Буксир] Свой буксир {tug.name}: колонки x {drive.Spec.ThrusterX:F1} м, y ±{drive.Spec.ThrusterY:F1} м, " +
                      $"тяга на гаке {bollardPullTonnes:0} т; пределы джойстика: ход {TugJoystick.MaxSurge(drive.Spec) / 1000:F0} кН, " +
                      $"лагом {TugJoystick.MaxSway(drive.Spec) / 1000:F0} кН, разворот {TugJoystick.MaxYaw(drive.Spec) / 1000:F0} кН·м.", this);
        }
        if (on && handle == null) BuildHandle();
        Active = on && drive != null;
        drive?.Reset();
        if (drive != null) drive.HeadingHold = true;   // 19.4а-2: при каждом включении буксира — удержание курса включено
        menuState = published = JoystickState.Zero;
        if (handle != null)
        {
            handle.SetState(JoystickState.Zero);
            handle.gameObject.SetActive(Active);
        }
        lastChangeTime = -1f;
        BridgeMenu.RebuildOpen();
    }

    private void OnBeforeStep(ShipDynamicsModel m, double dt)
    {
        if (!Active || drive == null) return;
        drive.Joystick = State;
        drive.Apply(m, dt);
    }

    void Update()
    {
        if (!Active || lastChangeTime < 0f || Time.time - lastChangeTime < settleSeconds) return;
        lastChangeTime = -1f;
        JoystickState s = State;
        if (s.DistanceTo(published) < minChange && !(s.IsZero && !published.IsZero)) return;
        published = s;
        Publish(lastSource);
    }

    private void OnHandleChanged(bool fromCode)
    {
        if (!fromCode) lastSource = "рукой";
        lastChangeTime = Time.time;
    }

    /// <summary>Положение кодом (меню, сброс сценария); source — Subject события.</summary>
    public void SetState(JoystickState s, string source)
    {
        lastSource = source;
        menuState = s;
        if (handle != null) handle.SetState(s);
        else lastChangeTime = Time.time;
    }

    private void Publish(string source)
    {
        JoystickState s = State;
        var e = new ExerciseEvent(ExerciseEventType.JoystickChanged)
        {
            Value = System.Math.Round(s.Surge * 100.0), Value2 = System.Math.Round(s.Sway * 100.0),
            Index = (int)System.Math.Round(s.Yaw * 100.0), Subject = source
        };
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Буксир] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }

    // ─────────── Рукоятка на месте телеграфа ───────────

    private void BuildHandle()
    {
        EngineTelegraph tel = navigation != null ? navigation.telegraph : null;
        Transform head = tel != null ? tel.transform : null;
        if (head != null && head.parent != null && head.parent.name == "TelegraphHead") head = head.parent;
        if (head == null || head.parent == null)
        {
            Debug.LogWarning("[Буксир] Нет TelegraphHead — рукоятка джойстика не построена, управление — меню B «Джойстик».", this);
            return;
        }
        Material body = null, knob = null;
        foreach (Renderer r in head.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            if (body == null && r.name == "Housing") body = r.sharedMaterial;
            if (knob == null && r.name == "Knob") knob = r.sharedMaterial;
        }
        if (body == null || knob == null)
            foreach (Renderer r in head.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null) continue;
                if (body == null) body = r.sharedMaterial;
                if (knob == null) knob = r.sharedMaterial;
            }

        var go = new GameObject("TugJoystick");
        go.SetActive(false);
        Transform t = go.transform;
        t.SetParent(head.parent, false);
        // 19.4а-2: сдвиг рукоятки от TelegraphHead — ShipTypeSwitcher.tugJoystickOffset (Tools/VR Bridge/Tug — запомнить положение джойстика)
        t.localPosition = head.localPosition + (ShipTypeSwitcher.Instance != null ? ShipTypeSwitcher.Instance.tugJoystickOffset : Vector3.zero);
        // +Z рукоятки — к носу судна, в плоскости палубы
        WorldOrigin origin = FindAnyObjectByType<WorldOrigin>();
        Transform ship = origin != null && origin.ship != null ? origin.ship : head.parent;
        Vector3 bow = head.parent.InverseTransformDirection(ship.TransformDirection(origin != null ? origin.bowLocalAxis : Vector3.back));
        bow.y = 0f;
        t.localRotation = Quaternion.LookRotation(bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back, Vector3.up);
        handle = go.AddComponent<TugJoystickHandle>();
        handle.Build(body, knob);
        handle.Changed += OnHandleChanged;
        // 19.4а-2: тумблер «УДЕРЖ. КУРС» — справа от рукоятки (в её системе +X — к ПБ), двигается вместе с ней
        headingSwitch = TugHeadingSwitch.Build(t, new Vector3(0.15f, 0f, 0f), this, "УДЕРЖ. КУРС");
        Debug.Log($"[Буксир] Рукоятка джойстика построена на месте {head.name} ({head.parent.name}).", this);
    }

    // ─────────── Меню «Управление» (кнопка B) ───────────

    private void RegisterCommands()
    {
        string menu = ShipControlOrders.MenuId;
        BridgeMenuCommand status = BridgeMenu.Register(Group, () => $"<size=85%>{StatusLine()}</size>", null, () => Active, hideWhenUnavailable: true).InMenu(menu);
        status.CloseOnExecute = false;
        commands.Add(status);
        commands.Add(BridgeMenu.RegisterStepper(Group, () => $"Ход: {TugJoystick.SurgeTitle(State.Surge)}",
            () => Step(0, -1), () => Step(0, +1), () => Active, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.RegisterStepper(Group, () => $"Лагом: {TugJoystick.SwayTitle(State.Sway)}",
            () => Step(1, -1), () => Step(1, +1), () => Active, hideWhenUnavailable: true).InMenu(menu));
        commands.Add(BridgeMenu.RegisterStepper(Group, () => $"Разворот: {TugJoystick.YawTitle(State.Yaw)}",
            () => Step(2, -1), () => Step(2, +1), () => Active, hideWhenUnavailable: true).InMenu(menu));
        BridgeMenuCommand zero = BridgeMenu.Register(Group, "Джойстик в ноль", () => SetState(JoystickState.Zero, "меню"),
            () => Active && !State.IsZero, hideWhenUnavailable: true).InMenu(menu);
        zero.CloseOnExecute = false;
        commands.Add(zero);
        commands.Add(BridgeMenu.RegisterStepper(Group, () => $"Удержание курса: {(HeadingHold ? "вкл" : "выкл")}",
            () => SetHeadingHold(!HeadingHold, "меню"), () => SetHeadingHold(!HeadingHold, "меню"), () => Active, hideWhenUnavailable: true).InMenu(menu));
        BridgeMenu.SetGroupExpanded(Group, true);
        BridgeMenu.SetGroupOrder(Group, 5);
    }

    private void Step(int axis, int dir)
    {
        JoystickState s = State;
        if (axis == 0) s.Surge = TugJoystick.Step(s.Surge, dir);
        else if (axis == 1) s.Sway = TugJoystick.Step(s.Sway, dir);
        else s.Yaw = TugJoystick.Step(s.Yaw, dir);
        SetState(s, "меню");
    }

    private string StatusLine()
    {
        if (drive == null) return "Колонки: —";
        string hold = !drive.HeadingHold ? "" : double.IsNaN(drive.HoldHeadingDeg) ? " · курс: разворот" : $" · держу курс {drive.HoldHeadingDeg % 360 + (drive.HoldHeadingDeg < 0 ? 360 : 0):000}°";
        return $"Колонки: ПБ {drive.UnitDirectionDeg(1):000}° {drive.UnitLoad(1) * 100:0} % · ЛБ {drive.UnitDirectionDeg(2):000}° {drive.UnitLoad(2) * 100:0} %{hold}";
    }
}
