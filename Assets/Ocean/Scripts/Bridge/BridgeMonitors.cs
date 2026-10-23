// ============================================================
// BridgeMonitors.cs — Мониторы на пульте МОСТИКИ (запрос 16а)
//
// Ставится сам на Ship (AfterSceneLoad и TrainerScenes.OnSceneLoaded) — Setup и правка сцены не нужны.
// Экраны — части модели МОСТИКИ по именам (их назвал пользователь, слева направо):
//   M1 Cube.6310 — задание и сообщения (16в)   M6 Cube.6298 — маршрут (16б)
//   M2 Cube.6314 — машина                      M7 Cube.6302 — цели (16в)
//   M3 Cube.6319 — руль                        M8 Cube.6303 — ветер и погода (16в)
//   M4 Cube.6306 — курс                        M9 Cube.6286 — сигнализация и швартовка (16в)
//   M5 Cube.6292 — скорость и поворот (16б)    P  Cube.6297 — большая панель: карта (16б)
// На каждый экран — World Space Canvas точно по лицевой грани: по граням сетки (Core ScreenFace — самая большая плоская грань,
// обращённая к игроку; наклон мониторов у модели «запечён» в вершины, поэтому габариты сетки не годятся), в сборке —
// из ассета Resources/BridgeMonitorLayout (пишет редактор при входе в Play — BridgeMonitorLayoutSync).
// Прежний способ (16а) — запасной: самая тонкая ось габаритов сетки — нормаль,
// сторона — к игроку у штурвала (XR Origin при запуске), «верх» — ось, ближайшая к вертикали судна
// (у лежащего экрана — от игрока); поля по краю — screenInset. Холсты — дочерние к Ship (качаются вместе с пультом),
// без GraphicRaycaster (лучи XRI их не задевают). Ширина холста — canvasWidthPx пикселей при любом размере экрана.
// Обновление: стрелки и картушка — needleRateHz (поворот RectTransform, сетка не перестраивается), тексты — textRateHz.
// Только на модели 45 м: на катере и буксире мониторы скрыты (ShipTypeSwitcher.TypeChanged).
// Консоль — «[Мониторы] …»: какие экраны найдены, размер, куда смотрят.
// ============================================================
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;

public class BridgeMonitors : MonoBehaviour
{
    public static readonly string[] ScreenNames =
        { "Cube.6310", "Cube.6314", "Cube.6319", "Cube.6306", "Cube.6292", "Cube.6298", "Cube.6302", "Cube.6303", "Cube.6286" };
    public const string BigPanelName = "Cube.6297";
    public const string ModelName = "МОСТИКИ";

    [Header("Машина")]
    [Tooltip("Номинальные обороты ГД (телеграф «Полный»), об/мин — для прибора; модель движения не меняется")]
    public float ratedRpm = 750f;
    [Tooltip("Постоянная времени изменения оборотов, с")]
    public float engineTimeConstantS = 4f;

    [Header("Экраны")]
    [Tooltip("Поле по краю экрана (доля стороны) — под рамку монитора")]
    [Range(0f, 0.2f)] public float screenInset = 0.015f;   // рамку панели рисует сам экран
    [Tooltip("Холст над поверхностью экрана, м")]
    public float surfaceOffset = 0.003f;
    [Tooltip("Ширина холста, пикселей (высота — по пропорции экрана)")]
    public float canvasWidthPx = 800f;
    [Tooltip("Стрелки и картушка, раз в секунду")]
    public float needleRateHz = 30f;
    [Tooltip("Тексты, раз в секунду")]
    public float textRateHz = 5f;
    public bool logScreens = true;

    public ShipNavigation Nav { get; private set; }
    public ThrusterLever Thruster { get; private set; }
    public readonly EngineRpm Engine = new EngineRpm();
    public static BridgeMonitors Instance { get; private set; }

    private ShipTypeSwitcher switcher;
    private Transform root;
    private readonly List<MonitorScreen> screens = new List<MonitorScreen>();
    private float nextNeedle, nextText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Ensure()   // и при повторной загрузке сцены — TrainerScenes
    {
        if (FindAnyObjectByType<BridgeMonitors>() != null) return;
        if (FindAnyObjectByType<ScenarioRunner>() == null) return;   // не сцена тренажёра
        ShipTypeSwitcher sw = FindAnyObjectByType<ShipTypeSwitcher>();
        GameObject ship = sw != null ? sw.gameObject : GameObject.Find("Ship");
        if (ship != null) ship.AddComponent<BridgeMonitors>();
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (switcher != null) switcher.TypeChanged -= ApplyType;
    }

    private IEnumerator Start()
    {
        yield return null;   // XR Origin и стойка — на месте
        Nav = FindAnyObjectByType<ShipNavigation>();
        Thruster = FindAnyObjectByType<ThrusterLever>();
        switcher = GetComponent<ShipTypeSwitcher>() ?? FindAnyObjectByType<ShipTypeSwitcher>();
        Engine.RatedRpm = ratedRpm;
        Engine.TimeConstantS = engineTimeConstantS;
        Build();
        if (switcher != null) switcher.TypeChanged += ApplyType;
        ApplyType();
    }

    // ─────────── Экраны ───────────
    private void Build()
    {
        Transform model = FindDeep(transform, ModelName) ?? transform;
        root = new GameObject("BridgeMonitors").transform;
        root.SetParent(transform, false);
        Vector3 viewer = ViewerFor(transform);
        BridgeMonitorLayout layout = Resources.Load<BridgeMonitorLayout>(BridgeMonitorLayout.ResourcePath);
        var log = new StringBuilder();
        var missing = new List<string>();
        for (int i = 0; i <= ScreenNames.Length; i++)
        {
            bool big = i == ScreenNames.Length;
            string name = big ? BigPanelName : ScreenNames[i];
            Transform t = FindDeep(model, name);
            if (t == null) { missing.Add(name); continue; }
            // Плоскость: ассет раскладки (пишет редактор при входе в Play) → грани сетки на месте → габариты (запасной)
            string source = "из ассета";
            BridgeMonitorLayout.Entry e = layout != null ? layout.Find(name) : null;
            if (e == null) { e = FaceFromMesh(transform, t, viewer); source = "по граням"; }
            Vector3 c, n, up;
            float w, h;
            if (e != null)
            {
                c = transform.TransformPoint(e.center);
                n = transform.TransformDirection(e.normal).normalized;
                up = transform.TransformDirection(e.up).normalized;
                w = e.width * (1f - 2f * screenInset);
                h = e.height * (1f - 2f * screenInset);
            }
            else if (Face(t, viewer, transform.up, screenInset, out c, out n, out up, out w, out h)) source = "по габаритам (запасной)";
            else { missing.Add(name); continue; }
            string label = big ? "P" : $"M{i + 1}";
            RectTransform canvas = MakeCanvas($"Monitor {label} ({name})", c + n * surfaceOffset, n, up, w, h, out Vector2 size);
            MonitorScreen s = MonitorScreen.For(i, label, name, big);
            s.Setup(canvas, size, this);
            screens.Add(s);
            log.Append($"\n  {label} {name} — {w * 100f:F0} × {h * 100f:F0} см, {s.Title}, нормаль {Facing(n)}, {source}");
        }
        if (logScreens)
            Debug.Log($"[Мониторы] Экраны: {screens.Count} из {ScreenNames.Length + 1} (модель «{model.name}»){log}" +
                      (missing.Count > 0 ? $"\n  не найдены: {string.Join(", ", missing)}" : ""), this);
    }

    /// <summary>Игрок у штурвала: XR Origin (при запуске — у штурвала) + рост 1,6 м.</summary>
    public static Vector3 ViewerFor(Transform ship)
    {
        XROrigin origin = FindAnyObjectByType<XROrigin>();
        if (origin != null) return origin.transform.position + ship.up * 1.6f;
        return ship.position + ship.up * 6f;
    }

    /// <summary>Модель пульта в Ship (или сам Ship) и экран по имени — для раскладки в редакторе.</summary>
    public static Transform FindScreen(Transform ship, string name) => FindDeep(FindDeep(ship, ModelName) ?? ship, name);

    /// <summary>Плоскость экрана по граням сетки (Core ScreenFace), в осях Ship; null — вершины не читаются или граней нет.</summary>
    public static BridgeMonitorLayout.Entry FaceFromMesh(Transform ship, Transform screen, Vector3 viewer)
    {
        MeshFilter mf = screen.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || !(mesh.isReadable || Application.isEditor)) return null;
        Vector3[] vs;
        int[] tris;
        try
        {
            vs = mesh.vertices;
            tris = mesh.triangles;
        }
        catch (System.Exception) { return null; }
        if (vs == null || vs.Length == 0 || tris == null || tris.Length < 3) return null;
        var world = new List<V3>(vs.Length);
        foreach (Vector3 v in vs)
        {
            Vector3 p = screen.TransformPoint(v);
            world.Add(new V3(p.x, p.y, p.z));
        }
        Vector3 up = ship.up;
        ScreenFaceResult f = ScreenFace.Find(world, tris, new V3(viewer.x, viewer.y, viewer.z), new V3(up.x, up.y, up.z));
        if (f == null || f.Width < 0.01 || f.Height < 0.01) return null;
        return new BridgeMonitorLayout.Entry
        {
            name = screen.name,
            center = ship.InverseTransformPoint(ToV(f.Center)),
            normal = ship.InverseTransformDirection(ToV(f.Normal)).normalized,
            up = ship.InverseTransformDirection(ToV(f.Up)).normalized,
            width = (float)f.Width,
            height = (float)f.Height
        };
    }

    private static Vector3 ToV(V3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>Запасной вариант (16а): плоскость по габаритам сетки — верно только без «запечённого» поворота.</summary>
    public static bool Face(Transform t, Vector3 viewer, Vector3 shipUp, float inset,
                            out Vector3 center, out Vector3 normal, out Vector3 upAxis, out float width, out float height)
    {
        center = normal = upAxis = Vector3.zero;
        width = height = 0f;
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        Bounds b = mf.sharedMesh.bounds;
        Vector3 c = t.TransformPoint(b.center);
        Vector3[] e =
        {
            t.TransformVector(new Vector3(b.size.x, 0f, 0f)),
            t.TransformVector(new Vector3(0f, b.size.y, 0f)),
            t.TransformVector(new Vector3(0f, 0f, b.size.z))
        };
        int thin = 0;
        for (int k = 1; k < 3; k++) if (e[k].magnitude < e[thin].magnitude) thin = k;
        int a = (thin + 1) % 3, d = (thin + 2) % 3;
        if (e[a].sqrMagnitude < 1e-8f || e[d].sqrMagnitude < 1e-8f) return false;
        Vector3 n = e[thin].sqrMagnitude > 1e-10f ? e[thin].normalized : Vector3.Cross(e[a], e[d]).normalized;
        if (Vector3.Dot(n, viewer - c) < 0f) n = -n;
        // «Верх» экрана: ось, ближайшая к вертикали; у лежащего экрана — от игрока
        Vector3 refUp = shipUp;
        if (Mathf.Abs(Vector3.Dot(e[a].normalized, shipUp)) < 0.3f && Mathf.Abs(Vector3.Dot(e[d].normalized, shipUp)) < 0.3f)
            refUp = Vector3.ProjectOnPlane(c - viewer, n).normalized;
        int ui = Mathf.Abs(Vector3.Dot(e[a].normalized, refUp)) >= Mathf.Abs(Vector3.Dot(e[d].normalized, refUp)) ? a : d;
        int wi = ui == a ? d : a;
        Vector3 u = e[ui];
        if (Vector3.Dot(u, refUp) < 0f) u = -u;
        center = c + n * (e[thin].magnitude * 0.5f);
        normal = n;
        upAxis = Vector3.ProjectOnPlane(u, n).normalized;
        height = e[ui].magnitude * (1f - 2f * inset);
        width = e[wi].magnitude * (1f - 2f * inset);
        return true;
    }

    private RectTransform MakeCanvas(string name, Vector3 pos, Vector3 normal, Vector3 up, float w, float h, out Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(root, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = go.GetComponent<RectTransform>();
        size = new Vector2(canvasWidthPx, canvasWidthPx * h / Mathf.Max(1e-4f, w));
        rt.sizeDelta = size;
        rt.pivot = new Vector2(0.5f, 0.5f);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-normal, up));   // лицом к игроку
        float parentScale = Mathf.Max(1e-6f, root.lossyScale.x);
        go.transform.localScale = Vector3.one * (w / canvasWidthPx / parentScale);
        return rt;
    }

    private string Facing(Vector3 n)
    {
        Vector3 l = transform.InverseTransformDirection(n);
        return $"({l.x:+0.00;-0.00}, {l.y:+0.00;-0.00}, {l.z:+0.00;-0.00}) в осях Ship";
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    // Только на модели 45 м (на катере и буксире — свои органы управления)
    private void ApplyType()
    {
        if (root == null) return;
        bool on = switcher == null || switcher.Type == ShipType.Model45m;
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }

    // ─────────── Кадр ───────────
    void Update()
    {
        if (Nav != null && Nav.telegraph != null) Engine.Step(Nav.telegraph.SpeedFactor, Nav.EngineFailed, Time.deltaTime);
        if (root == null || !root.gameObject.activeInHierarchy) return;
        float now = Time.unscaledTime;
        if (now >= nextNeedle)
        {
            nextNeedle = now + 1f / Mathf.Max(1f, needleRateHz);
            foreach (MonitorScreen s in screens) s.Needles();
        }
        if (now >= nextText)
        {
            nextText = now + 1f / Mathf.Max(0.5f, textRateHz);
            foreach (MonitorScreen s in screens) s.Texts();
        }
    }

    /// <summary>Строка отказа (как на NavDisplay): «ОТКАЗ ГД», «ОТКАЗ РУЛЯ» — если оповещение включено.</summary>
    public static string FailureLine => InstructorInputs.Instance != null ? InstructorInputs.Instance.FailureLine : "";
    public static bool BlinkOn => Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
}
