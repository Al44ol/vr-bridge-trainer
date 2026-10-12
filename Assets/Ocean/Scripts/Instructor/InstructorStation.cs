// ============================================================
// InstructorStation.cs — Окно инструктора на мониторе ПК (D1)
//
// До D1 окно приложения на мониторе (Game View в редакторе) показывало
// изображение из шлема (зеркало XR). Теперь режимов два; переключатель — меню
// «Параметры» (Y) → «Монитор инструктора» (без клавиатуры: станция автономна;
// клавиша F2 — только при keyboardShortcuts):
//   • «окно инструктора» (по умолчанию): зеркало XR выключено, на монитор рисует
//     своя камера инструктора — без сцены (cullingMask = 0, не XR), только очистка
//     фона; поверх — интерфейс InstructorWindow (Screen Space Overlay: в шлем не попадает);
//   • «зеркало из шлема»: прежний вид, окно инструктора скрыто, подсказка.
// Зеркало URP рисует по XRDisplaySubsystem.GetPreferredMirrorBlitMode() — его и
// выключаем (SetPreferredMirrorBlitMode(None)); XRSettings.gameViewRenderMode — дополнительно.
// Подсистема дисплея может запуститься позже Start — режим проверяется раз в секунду.
// Шлем рисует сцену как раньше в обоих режимах. Нагрузка окна на кадр: интерфейс
// перестраивается с частотой uiRateHz (5 Гц), неподвижный слой карты — только
// при смене масштаба; 3D окно не рисует (кроме PiP — InstructorPip, тоже из меню Y).
// Без сети: всё в одном процессе (Play Mode / сборка на ПК станции).
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

public class InstructorStation : MonoBehaviour
{
    public enum DisplayMode { Instructor, Mirror }

    [Tooltip("Режим при запуске")]
    public DisplayMode startMode = DisplayMode.Instructor;
    [Tooltip("Клавиша «окно инструктора ⇄ зеркало из шлема» (только при keyboardShortcuts)")]
    public Key toggleKey = Key.F2;
    [Tooltip("Клавиши клавиатуры (D1: по умолчанию выкл — режим и PiP из меню Y «Монитор инструктора»)")]
    public bool keyboardShortcuts;
    [Tooltip("Обновление текстов и подвижного слоя карты, раз в секунду (реальное время)")]
    [Range(1f, 30f)] public float uiRateHz = 5f;
    [Tooltip("Цвет фона окна")]
    public Color background = new Color(0.04f, 0.06f, 0.08f, 1f);

    public static InstructorStation Instance { get; private set; }
    /// <summary>В окне инструктора набирают текст (ID обучающегося): клавиши сцены (погода 1–4) не действуют.</summary>
    public static bool TextInputFocused { get; set; }

    public DisplayMode Mode { get; private set; }
    public InstructorWindow Window { get; private set; }
    public InstructorPip Pip { get; private set; }

    private const string MenuGroup = "Монитор инструктора";
    private Camera cam;
    private GameViewRenderMode mirrorMode = GameViewRenderMode.LeftEye;
    private int mirrorBlitMode = XRMirrorViewBlitMode.Default;
    private bool mirrorBlitSaved;
    private float nextMirrorCheck;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => TextInputFocused = false;

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Инструктор] В сцене несколько InstructorStation — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TextInputFocused = false;
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder(MenuGroup, 20);
        BridgeMenuCommand mode = BridgeMenu.Register(MenuGroup,
            () => Mode == DisplayMode.Instructor ? "Монитор: окно инструктора" : "Монитор: зеркало шлема",
            () => SetMode(Mode == DisplayMode.Instructor ? DisplayMode.Mirror : DisplayMode.Instructor),
            instructorOnly: true).InMenu(BridgeMenu.SettingsMenuId);
        menuCommands.Add(mode);
        BridgeMenuCommand pip = BridgeMenu.Register(MenuGroup,
            () => Pip != null && Pip.Active ? "Вид из шлема на мониторе (PiP): вкл" : "Вид из шлема на мониторе (PiP): выкл",
            TogglePip, () => Mode == DisplayMode.Instructor && Pip != null, instructorOnly: true).InMenu(BridgeMenu.SettingsMenuId);
        menuCommands.Add(pip);
    }

    void Start()
    {
        mirrorMode = XRSettings.gameViewRenderMode == GameViewRenderMode.None ? GameViewRenderMode.LeftEye : XRSettings.gameViewRenderMode;
        BuildCamera();
        Pip = GetComponent<InstructorPip>();
        if (Pip == null) Pip = gameObject.AddComponent<InstructorPip>();
        Window = GetComponent<InstructorWindow>();
        if (Window == null) Window = gameObject.AddComponent<InstructorWindow>();
        Window.Init(this);
        SetMode(startMode);
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
        // Выход из Play Mode / отключение — вернуть зеркало
        XRSettings.gameViewRenderMode = mirrorMode;
        XRDisplaySubsystem d = Display();
        if (d != null && mirrorBlitSaved) d.SetPreferredMirrorBlitMode(mirrorBlitMode);
    }

    void Update()
    {
        if (keyboardShortcuts && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame && !TextInputFocused)
            SetMode(Mode == DisplayMode.Instructor ? DisplayMode.Mirror : DisplayMode.Instructor);
        // Подсистема дисплея XR может запуститься позже — режим зеркала проверяется раз в секунду
        if (Time.unscaledTime >= nextMirrorCheck)
        {
            nextMirrorCheck = Time.unscaledTime + 1f;
            ApplyMirror(false);
        }
    }

    public void SetMode(DisplayMode mode)
    {
        Mode = mode;
        bool instructor = mode == DisplayMode.Instructor;
        if (cam != null) cam.enabled = instructor;
        if (Window != null) Window.SetVisible(instructor);
        if (!instructor && Pip != null) Pip.SetActive(false);
        ApplyMirror(true);
    }

    public void TogglePip()
    {
        if (Pip == null || Mode != DisplayMode.Instructor) return;
        Pip.SetActive(!Pip.Active);
        Debug.Log($"[Инструктор] Вид из шлема на мониторе (PiP): {(Pip.Active ? $"вкл, {Pip.width}×{Pip.height}, {Pip.rateHz:F0} Гц" : "выкл")}.");
    }

    // Зеркало XR на мониторе: выключено в режиме окна, прежнее — в режиме зеркала
    private void ApplyMirror(bool log)
    {
        bool instructor = Mode == DisplayMode.Instructor;
        XRSettings.gameViewRenderMode = instructor ? GameViewRenderMode.None : mirrorMode;
        XRDisplaySubsystem d = Display();
        if (d != null)
        {
            if (!mirrorBlitSaved)
            {
                int current = d.GetPreferredMirrorBlitMode();
                mirrorBlitMode = current == XRMirrorViewBlitMode.None ? XRMirrorViewBlitMode.Default : current;
                mirrorBlitSaved = true;
            }
            int want = instructor ? XRMirrorViewBlitMode.None : mirrorBlitMode;
            if (d.GetPreferredMirrorBlitMode() != want)
            {
                d.SetPreferredMirrorBlitMode(want);
                log = true;
            }
        }
        if (!log) return;
        string blit = d == null ? "XR не запущен" : $"режим зеркала XR {d.GetPreferredMirrorBlitMode()}";
        Debug.Log(instructor
            ? $"[Инструктор] Монитор — окно инструктора (зеркало шлема выключено; {blit}). Меню Y → «Монитор инструктора»."
            : $"[Инструктор] Монитор — зеркало из шлема ({blit}). Меню Y → «Монитор инструктора».");
    }

    private static XRDisplaySubsystem Display()
    {
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        foreach (XRDisplaySubsystem d in displays) if (d.running) return d;
        return null;
    }

    private void BuildCamera()
    {
        if (cam != null) return;
        var go = new GameObject("InstructorCamera");
        go.transform.SetParent(transform, false);
        cam = go.AddComponent<Camera>();
        cam.stereoTargetEye = StereoTargetEyeMask.None;   // не XR: рисует только на монитор
        cam.cullingMask = 0;                               // сцену не рисует — фон и интерфейс
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.depth = 100;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.useOcclusionCulling = false;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;
        data.allowXRRendering = false;
    }
}
