// ============================================================
// InstructorStation.cs — Окно инструктора на мониторе ПК (D1)
//
// До D1 окно приложения на мониторе (Game View в редакторе) показывало
// изображение из шлема (зеркало XR). Теперь режимов два, переключатель — F2:
//   • «окно инструктора» (по умолчанию): зеркало XR выключено
//     (XRSettings.gameViewRenderMode = None), на монитор рисует своя камера
//     инструктора — без сцены (cullingMask = 0, не XR), только очистка фона;
//     поверх — интерфейс InstructorWindow (Screen Space Overlay: в шлем не попадает);
//   • «зеркало из шлема»: прежний вид, окно инструктора скрыто, подсказка «F2».
// Шлем рисует сцену как раньше в обоих режимах. Нагрузка окна на кадр: интерфейс
// перестраивается с частотой uiRateHz (5 Гц), неподвижный слой карты — только
// при смене масштаба; 3D окно не рисует (кроме PiP по кнопке — InstructorPip).
// Без сети: всё в одном процессе (Play Mode / сборка на ПК станции).
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

public class InstructorStation : MonoBehaviour
{
    public enum DisplayMode { Instructor, Mirror }

    [Tooltip("Режим при запуске")]
    public DisplayMode startMode = DisplayMode.Instructor;
    [Tooltip("Клавиша «окно инструктора ⇄ зеркало из шлема»")]
    public Key toggleKey = Key.F2;
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

    private Camera cam;
    private GameViewRenderMode mirrorMode = GameViewRenderMode.LeftEye;

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
        // Выход из Play Mode / отключение — вернуть зеркало
        XRSettings.gameViewRenderMode = mirrorMode;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame && !TextInputFocused)
            SetMode(Mode == DisplayMode.Instructor ? DisplayMode.Mirror : DisplayMode.Instructor);
    }

    public void SetMode(DisplayMode mode)
    {
        Mode = mode;
        bool instructor = mode == DisplayMode.Instructor;
        XRSettings.gameViewRenderMode = instructor ? GameViewRenderMode.None : mirrorMode;
        if (cam != null) cam.enabled = instructor;
        if (Window != null) Window.SetVisible(instructor);
        if (!instructor && Pip != null) Pip.SetActive(false);
        Debug.Log(instructor
            ? $"[Инструктор] Окно инструктора на мониторе (зеркало шлема выключено). {toggleKey} — зеркало из шлема."
            : $"[Инструктор] Монитор — зеркало из шлема. {toggleKey} — окно инструктора.");
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
