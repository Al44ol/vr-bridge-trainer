// ============================================================
// WaterAreaView.cs — Учебная акватория в сцене (B1)
//
// По ассету WaterArea создаёт модели знаков (WaterAreaMeshes) и суши
// и каждый кадр ставит их в координаты сцены через плавающее начало
// координат: p_сцены = p_мира − p_судна (в double, WorldOrigin.WorldToScene),
// поэтому знаки стоят на месте, когда судно идёт.
//
// Качание буёв: высота — вода в точке знака в текущем кадре
// (OceanWaves.SampleHeight — те же волны, что видит обучающийся, тот же
// момент времени отображения); наклон — по уклону воды из двух дополнительных
// точек на расстоянии r: s_E = (h(E+r) − h)/r, s_N = (h(N+r) − h)/r,
// вертикаль буя n = norm(−k·s_E, 1, −k·s_N), k — доля следования уклону,
// сглаживание наклона с постоянной τ (буй инерционен).
// Знаки дальше nearDistance обновляют высоту раз в farUpdateInterval кадров
// (позиция — каждый кадр); волны там и так гаснут (затухание 1,5…6 км).
//
// Мели на воде не видны. Отладочный слой (команда меню A «Подсказки» → «Показать мели»; до 19.2 — «Отладка»)
// рисует их контуры над водой: красные — опасные для своего судна
// (глубина < d + Δ, по GroundingMonitor), жёлтые — прочие; голубым — фарватер.
// Причалы (запрос 19.2б): пирс (кромка, тыл structureWidthM, до berthBottomDepth под водой), полоса
// по кромке, кранцы (цилиндры вдоль кромки; обжатие из MooringController — кранец сплющивается)
// и кнехты. Пирс неподвижен на среднем уровне; корень — в точке A кромки (точность float у кранцев).
// Порядок выполнения −60: после WorldOrigin, ShipNavigation, OceanWaves, ShipMotion.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-60)]
public class WaterAreaView : MonoBehaviour
{
    public WaterArea area;
    public WorldOrigin worldOrigin;
    public OceanWaves ocean;
    [Tooltip("Для цвета мелей на отладочном слое (опасная глубина d + Δ)")]
    public GroundingMonitor grounding;

    [Header("Материалы (порядок MarkColor: красный, зелёный, жёлтый, чёрный, белый)")]
    public Material[] markMaterials = new Material[5];
    public Material landTopMaterial;
    public Material landSlopeMaterial;
    public Material debugDangerMaterial;
    public Material debugSafeMaterial;
    public Material debugFairwayMaterial;

    [Header("Знаки")]
    [Tooltip("Масштаб моделей знаков (1 — натуральная величина)")]
    public float buoyScale = 1f;
    [Tooltip("Доля следования уклону воды k (0 — буй не наклоняется)")]
    [Range(0f, 1.5f)] public float tiltFactor = 0.8f;
    [Tooltip("Расстояние между точками сэмплирования уклона r, м")]
    public float slopeSampleDistance = 1.5f;
    [Tooltip("Постоянная сглаживания наклона τ, с")]
    public float tiltSmoothing = 0.3f;

    [Header("Производительность")]
    [Tooltip("Ближе этой дистанции высота знака обновляется каждый кадр, м")]
    public float nearDistance = 3500f;
    [Tooltip("Дальние знаки: раз в N кадров")]
    [Range(1, 60)] public int farUpdateInterval = 10;

    [Header("Суша")]
    public bool showLand = true;
    [Tooltip("Глубина основания откоса под средним уровнем, м")]
    public float landBaseDepth = 6f;
    [Tooltip("Ширина откоса в высотах берега")]
    public float landSlopeFactor = 2.5f;

    [Header("Причалы (19.2б; материалы ставит Setup 19.2 Berth, пусто — простые)")]
    public Material berthConcreteMaterial;
    public Material berthEdgeMaterial;
    public Material fenderMaterial;
    public Material bollardMaterial;
    [Tooltip("Пирс уходит под воду на, м")]
    public float berthBottomDepth = 4f;
    [Tooltip("Верх кранца ниже кромки на, м")]
    public float fenderDropM = 0.25f;

    [Header("Отладка")]
    [Tooltip("Контуры мелей и фарватера на воде (переключается из меню мостика)")]
    public bool showShoalsOnWater;
    [Tooltip("Высота линий над средним уровнем, м")]
    public float debugLineHeight = 0.6f;
    public float debugLineWidth = 3f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private int buoyCount;
    [SerializeField] private float updateTimeUs;

    private struct Buoy
    {
        public Transform transform;
        public float yaw;
        public Vector3 up;
        public float height;
        public bool initialized;
    }

    private readonly List<Buoy> buoys = new List<Buoy>();
    private readonly Dictionary<AidType, WaterAreaMeshes.BuoyMesh> meshCache = new Dictionary<AidType, WaterAreaMeshes.BuoyMesh>();
    private readonly List<Mesh> landMeshes = new List<Mesh>();
    private Transform buoyRoot, landRoot, debugRoot, berthRoot;

    private sealed class BerthView
    {
        public BerthArea Berth;
        public Transform Root;
        public Transform[] Fenders;
        public float Diameter;
        public bool Deflected;
    }
    private readonly List<BerthView> berthViews = new List<BerthView>();
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private int builtRevision = int.MinValue;
    private WaterArea builtArea;
    private bool builtDebug;
    private float builtDangerDepth = float.NaN;
    private BridgeMenuCommand debugCommand;
    private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

    /// <summary>Модели знаков (B3: огни знаков — AidLightsView); номер = номер в WaterArea.aids.</summary>
    public int BuoyCount => buoys.Count;
    public Transform BuoyTransform(int i) => i >= 0 && i < buoys.Count ? buoys[i].transform : null;

    public WorldOrigin Origin => worldOrigin != null ? worldOrigin : WorldOrigin.Instance;
    public OceanWaves Ocean => ocean != null ? ocean : OceanWaves.Instance;
    /// <summary>Средний уровень воды в сцене (y океана), м.</summary>
    public float MeanLevel => Ocean != null ? Ocean.MeanLevel : 0f;
    /// <summary>Глубина, меньше которой мель опасна для своего судна, м.</summary>
    public float DangerDepth => grounding != null ? grounding.DangerDepth : 3.5f;

    void OnEnable()
    {
        debugCommand = BridgeMenu.Register(TrainerModeSwitch.HintsGroup,   // перед 19.2 (п. B): подсказка — не в «экзамене»
            () => showShoalsOnWater ? "Скрыть мели на воде" : "Показать мели на воде",
            () => showShoalsOnWater = !showShoalsOnWater);
        // Быстрая проверка без долгого перехода: переставить судно (на стопе) по первому фарватеру
        teleportCommands.Add(BridgeMenu.Register("Отладка", "Судно: старт демо (2,5 км до входа)",
            () => Teleport(DemoWaterArea.StartPosition, DemoWaterArea.StartHeadingDeg), () => Navigation != null, instructorOnly: true));
        teleportCommands.Add(BridgeMenu.Register("Отладка", "Судно: вход в фарватер",
            () => TeleportAlongFairway(0, 300.0), () => Navigation != null && HasFairway, instructorOnly: true));
        teleportCommands.Add(BridgeMenu.Register("Отладка", "Судно: 800 м до поворота",
            () => TeleportAlongFairway(1, 800.0), () => Navigation != null && HasFairway, instructorOnly: true));
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(debugCommand);
        debugCommand = null;
        foreach (BridgeMenuCommand c in teleportCommands) BridgeMenu.Unregister(c);
        teleportCommands.Clear();
    }

    private readonly List<BridgeMenuCommand> teleportCommands = new List<BridgeMenuCommand>();
    private ShipNavigation navigationCache;
    private ShipNavigation Navigation
    {
        get
        {
            if (navigationCache == null) navigationCache = FindAnyObjectByType<ShipNavigation>();
            return navigationCache;
        }
    }
    private bool HasFairway => area != null && area.fairways.Count > 0 && area.fairways[0].axis.Count >= 2;

    private void Teleport(WorldPosition p, double headingDeg)
    {
        if (Navigation == null) return;
        Navigation.TeleportTo(p.East, p.North, headingDeg);
        if (grounding != null) grounding.ResetState();
    }

    // На отрезке оси, ведущем к точке index (для index = 0 — первый отрезок), за distance м до неё
    private void TeleportAlongFairway(int index, double distance)
    {
        if (!HasFairway) return;
        List<WorldPosition> axis = area.fairways[0].axis;
        int i = Mathf.Clamp(index, 0, axis.Count - 1);
        WorldPosition a = i == 0 ? axis[0] : axis[i - 1], b = i == 0 ? axis[1] : axis[i];
        WorldPosition d = b - a;
        double len = System.Math.Max(1e-6, d.Magnitude);
        WorldPosition dir = d * (1.0 / len);
        WorldPosition target = axis[i] - dir * distance;
        Teleport(target, System.Math.Atan2(dir.East, dir.North) * Mathf.Rad2Deg);
    }

    void Start() => Rebuild();

    void OnDestroy()
    {
        foreach (var m in meshCache.Values) if (m.Mesh != null) Destroy(m.Mesh);
        meshCache.Clear();
        foreach (Mesh m in landMeshes) if (m != null) Destroy(m);
        landMeshes.Clear();
        foreach (Material m in runtimeMaterials) if (m != null) Destroy(m);
        runtimeMaterials.Clear();
    }

    private int BerthCount => area != null && area.berths != null ? area.berths.Count : 0;

    void Update()
    {
        if (area == null || Origin == null) return;
        if (area != builtArea || area.revision != builtRevision || buoys.Count != area.aids.Count || berthViews.Count != BerthCount) Rebuild();
        if (showShoalsOnWater != builtDebug || (showShoalsOnWater && !Mathf.Approximately(builtDangerDepth, DangerDepth))) RebuildDebug();
        if (landRoot != null && landRoot.gameObject.activeSelf != showLand) landRoot.gameObject.SetActive(showLand);

        watch.Restart();
        UpdateBuoys();
        PlaceAnchored(landRoot);
        PlaceAnchored(debugRoot, debugLineHeight);
        UpdateBerths();
        watch.Stop();
        updateTimeUs = Mathf.Lerp(updateTimeUs, (float)(watch.Elapsed.TotalMilliseconds * 1000.0), 0.05f);
    }

    // ─────────── Знаки ───────────
    private void UpdateBuoys()
    {
        WorldOrigin origin = Origin;
        OceanWaves water = Ocean;
        float mean = MeanLevel;
        float dt = Time.deltaTime;
        float tiltBlend = tiltSmoothing > 0f ? 1f - Mathf.Exp(-dt / tiltSmoothing) : 1f;
        float near2 = nearDistance * nearDistance;
        int frame = Time.frameCount;
        int interval = Mathf.Max(1, farUpdateInterval);
        double r = Mathf.Max(0.1f, slopeSampleDistance);

        for (int i = 0; i < buoys.Count; i++)
        {
            Buoy b = buoys[i];
            if (b.transform == null) continue;
            WorldPosition wp = area.aids[i].position;
            Vector3 p = origin.WorldToScene(wp);

            bool sample = !b.initialized || p.x * p.x + p.z * p.z < near2 || (frame + i) % interval == 0;
            if (sample)
            {
                float h = 0f;
                Vector3 up = Vector3.up;
                if (water != null)
                {
                    double h0 = water.SampleHeight(wp);
                    double hE = water.SampleHeight(new WorldPosition(wp.East + r, wp.North));
                    double hN = water.SampleHeight(new WorldPosition(wp.East, wp.North + r));
                    h = (float)h0;
                    float sE = (float)((hE - h0) / r), sN = (float)((hN - h0) / r);
                    up = new Vector3(-tiltFactor * sE, 1f, -tiltFactor * sN).normalized;
                }
                b.height = h;
                b.up = b.initialized ? Vector3.Slerp(b.up, up, tiltBlend) : up;
                b.initialized = true;
                buoys[i] = b;
            }
            p.y = mean + b.height;
            b.transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, b.up) * Quaternion.Euler(0f, b.yaw, 0f));
        }
    }

    // Корень суши/отладки: локальные вершины — относительно центра района
    private void PlaceAnchored(Transform root, float yOffset = 0f)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        Vector3 p = Origin.WorldToScene(area.Center);
        p.y = MeanLevel + yOffset;
        root.position = p;
        root.rotation = Quaternion.identity;
    }

    // ─────────── Построение ───────────
    [ContextMenu("Пересобрать акваторию")]
    public void Rebuild()
    {
        if (!Application.isPlaying) return;
        ClearChildren();
        builtArea = area;
        builtRevision = area != null ? area.revision : int.MinValue;
        if (area == null) return;

        buoyRoot = new GameObject("Buoys").transform;
        buoyRoot.SetParent(transform, false);
        for (int i = 0; i < area.aids.Count; i++)
        {
            AidToNavigation aid = area.aids[i];
            if (!meshCache.TryGetValue(aid.type, out WaterAreaMeshes.BuoyMesh bm))
            {
                bm = WaterAreaMeshes.BuildBuoy(aid.type);
                meshCache[aid.type] = bm;
            }
            var go = new GameObject(string.IsNullOrEmpty(aid.name) ? aid.type.ToString() : aid.name);
            go.transform.SetParent(buoyRoot, false);
            go.transform.localScale = Vector3.one * Mathf.Max(0.1f, buoyScale);
            go.AddComponent<MeshFilter>().sharedMesh = bm.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[bm.Colors.Length];
            for (int c = 0; c < mats.Length; c++) mats[c] = MarkMaterial(bm.Colors[c]);
            mr.sharedMaterials = mats;
            // Поворот вокруг вертикали — детерминированный, чтобы буи не стояли «строем»
            float yaw = (i * 137.5f) % 360f;
            buoys.Add(new Buoy { transform = go.transform, yaw = yaw, up = Vector3.up });
        }
        buoyCount = buoys.Count;

        landRoot = new GameObject("Land").transform;
        landRoot.SetParent(transform, false);
        WorldPosition anchor = area.Center;
        foreach (LandArea land in area.land)
        {
            if (land == null || land.vertices.Count < 3) continue;
            Mesh mesh = WaterAreaMeshes.BuildLand(land, anchor, landBaseDepth, landSlopeFactor);
            landMeshes.Add(mesh);
            var go = new GameObject(land.name);
            go.transform.SetParent(landRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { landTopMaterial, landSlopeMaterial };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        landRoot.gameObject.SetActive(showLand);

        berthRoot = new GameObject("Berths").transform;
        berthRoot.SetParent(transform, false);
        for (int i = 0; i < BerthCount; i++) berthViews.Add(BuildBerth(area.berths[i]));

        RebuildDebug();
    }

    // ─────────── Причалы (19.2б) ───────────
    // Локальные оси корня: +X — вдоль кромки (t, от A к B), +Y — вверх от среднего уровня,
    // +Z — от кромки в сторону берега (−n). Корень — в точке A, поворот LookRotation(−n).
    private BerthView BuildBerth(BerthArea b)
    {
        var view = new BerthView { Berth = b, Diameter = b != null ? b.fenderOuterDiameterM : 0f };
        var root = new GameObject(b != null && !string.IsNullOrEmpty(b.name) ? b.name : "Причал").transform;
        root.SetParent(berthRoot, false);
        view.Root = root;
        if (b == null || b.Length < 1.0) { view.Fenders = new Transform[0]; return view; }

        float len = (float)b.Length, h = b.quayHeightM, w = Mathf.Max(2f, b.structureWidthM), bottom = Mathf.Max(0.5f, berthBottomDepth);
        Material concrete = BerthMaterial(berthConcreteMaterial, "Berth_Concrete_rt", new Color(0.55f, 0.55f, 0.53f));
        Material edge = BerthMaterial(berthEdgeMaterial, "Berth_Edge_rt", new Color(0.9f, 0.75f, 0.1f));
        Material rubber = BerthMaterial(fenderMaterial, "Berth_Fender_rt", new Color(0.06f, 0.06f, 0.06f));
        Material iron = BerthMaterial(bollardMaterial, "Berth_Bollard_rt", new Color(0.18f, 0.2f, 0.22f));

        BerthPart(PrimitiveType.Cube, "Pier", root, new Vector3(0.5f * len, 0.5f * (h - bottom), 0.5f * w),
                  Quaternion.identity, new Vector3(len, h + bottom, w), concrete);
        BerthPart(PrimitiveType.Cube, "EdgeStripe", root, new Vector3(0.5f * len, h + 0.02f, 0.15f),
                  Quaternion.identity, new Vector3(len, 0.04f, 0.3f), edge);

        // Кранцы: цилиндр по кромке (ось цилиндра Unity — Y → поворот на 90° вокруг Z)
        float d = Mathf.Max(0.05f, b.fenderOuterDiameterM), fl = Mathf.Max(0.2f, b.fenderLengthM);
        float yF = Mathf.Max(0.5f * d + 0.2f, h - fenderDropM - 0.5f * d);
        List<double> ft = b.FenderPositions();
        view.Fenders = new Transform[ft.Count];
        for (int i = 0; i < ft.Count; i++)
            view.Fenders[i] = BerthPart(PrimitiveType.Cylinder, $"Fender_{i + 1}", root, new Vector3((float)ft[i], yF, -0.5f * d),
                                        Quaternion.Euler(0f, 0f, 90f), new Vector3(d, 0.5f * fl, d), rubber);

        // Кнехты: тумба и шляпка, в bollardSetbackM от кромки
        List<double> bt = b.BollardPositions();
        for (int i = 0; i < bt.Count; i++)
        {
            float t = (float)bt[i], z = b.bollardSetbackM;
            BerthPart(PrimitiveType.Cylinder, $"Bollard_{i + 1}", root, new Vector3(t, h + 0.25f, z), Quaternion.identity, new Vector3(0.35f, 0.25f, 0.35f), iron);
            BerthPart(PrimitiveType.Cylinder, $"BollardCap_{i + 1}", root, new Vector3(t, h + 0.52f, z), Quaternion.identity, new Vector3(0.5f, 0.03f, 0.5f), iron);
        }
        return view;
    }

    private static Transform BerthPart(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    private Material BerthMaterial(Material assigned, string name, Color color)
    {
        if (assigned != null) return assigned;
        foreach (Material m in runtimeMaterials) if (m != null && m.name == name) return m;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(lit != null ? lit : Shader.Find("Standard")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        runtimeMaterials.Add(mat);
        return mat;
    }

    private void UpdateBerths()
    {
        if (berthViews.Count == 0) return;
        WorldOrigin origin = Origin;
        float mean = MeanLevel;
        MooringController mc = MooringController.Instance;
        int active = mc != null ? mc.ActiveBerthIndex : -1;
        double[] defl = active >= 0 && mc.System != null ? mc.System.FenderDeflection : null;
        for (int i = 0; i < berthViews.Count; i++)
        {
            BerthView v = berthViews[i];
            if (v.Root == null || v.Berth == null) continue;
            Vector3 p = origin.WorldToScene(v.Berth.a);
            p.y = mean;
            WorldPosition n = v.Berth.NAxis;
            v.Root.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3((float)-n.East, 0f, (float)-n.North), Vector3.up));

            // Обжатие кранцев: наружная сторона сдвигается к кромке, кранец сплющивается
            bool any = i == active && defl != null && defl.Length == v.Fenders.Length;
            if (!any && !v.Deflected) continue;
            for (int k = 0; k < v.Fenders.Length; k++)
            {
                Transform f = v.Fenders[k];
                if (f == null) continue;
                float d = v.Diameter;
                float e = any ? Mathf.Clamp(d - (float)defl[k], 0.2f * d, d) : d;
                Vector3 lp = f.localPosition;
                f.localPosition = new Vector3(lp.x, lp.y, -0.5f * e);
                Vector3 ls = f.localScale;
                f.localScale = new Vector3(ls.x, ls.y, e);
            }
            v.Deflected = any;
        }
    }

    private void RebuildDebug()
    {
        builtDebug = showShoalsOnWater;
        builtDangerDepth = DangerDepth;
        if (debugRoot != null) Destroy(debugRoot.gameObject);
        debugRoot = null;
        if (!showShoalsOnWater || area == null) return;

        debugRoot = new GameObject("DebugShoals").transform;
        debugRoot.SetParent(transform, false);
        WorldPosition anchor = area.Center;
        foreach (ShoalArea s in area.shoals)
        {
            if (s == null || s.vertices.Count < 2) continue;
            Line(s.name, s.vertices, anchor, true, s.depth < DangerDepth ? debugDangerMaterial : debugSafeMaterial);
        }
        foreach (FairwayLine f in area.fairways)
        {
            if (f == null || f.axis.Count < 2) continue;
            Line(f.name + " ось", f.axis, anchor, false, debugFairwayMaterial);
            Line(f.name + " кромка Л", DemoWaterArea.OffsetPolyline(f.axis, f.width * 0.5), anchor, false, debugFairwayMaterial);
            Line(f.name + " кромка П", DemoWaterArea.OffsetPolyline(f.axis, -f.width * 0.5), anchor, false, debugFairwayMaterial);
        }
    }

    private void Line(string name, IList<WorldPosition> pts, WorldPosition anchor, bool loop, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(debugRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = loop;
        lr.positionCount = pts.Count;
        lr.widthMultiplier = debugLineWidth;
        lr.sharedMaterial = mat;
        // Лента лежит на воде: TransformZ — плоскость ленты ⟂ локальной Z; поворот на 90°
        // вокруг X переводит локальную XY в XZ сцены (локальная +Y → север), точки — в XY
        lr.alignment = LineAlignment.TransformZ;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        for (int i = 0; i < pts.Count; i++)
            lr.SetPosition(i, new Vector3((float)(pts[i].East - anchor.East), (float)(pts[i].North - anchor.North), 0f));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private Material MarkMaterial(MarkColor c)
    {
        int i = (int)c;
        return markMaterials != null && i < markMaterials.Length ? markMaterials[i] : null;
    }

    private void ClearChildren()
    {
        buoys.Clear();
        berthViews.Clear();
        foreach (Mesh m in landMeshes) if (m != null) Destroy(m);
        landMeshes.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        buoyRoot = landRoot = debugRoot = berthRoot = null;
    }
}
