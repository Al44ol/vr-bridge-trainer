// ============================================================
// NavLightField.cs — Все навигационные огни сцены одним мешем (B3)
//
// Источники (TrafficShipView — огни судов, AidLightsView — огни знаков МАМС)
// в Update сдают огни: позиция в сцене, цвет, уровень яркости 0…1 (Submit).
// В LateUpdate поле собирает один меш (4 вершины на огонь) и рисует его одним
// вызовом шейдером VRBridgeTrainer/NavLight (billboard постоянного углового
// размера, разворот к камере — в вершинном шейдере, отдельно для каждого глаза).
//
// Условия наблюдения (общие для всех огней):
//   • метеорологическая видимость V по туману сцены (ExponentialSquared,
//     V = 1,73/ρ — как в WeatherController); без тумана — ясно (K = 0,8);
//   • коэффициент пропускания K = 0,05^(1852/V) (NavigationLights);
//   • огни горят (режим «авто»): темно или ограниченная видимость V < 2 миль
//     (правило 20 б, в). Режим «вкл/выкл» — команда меню мостика «Отладка».
//     B4: «темно» — по времени суток (WeatherController.Darkness ≥ 0,5: сумерки
//     и ночь, EnvironmentModel.LightsRequired); без WeatherController — как в B3,
//     RenderSettings.ambientIntensity < 0,3. Шторм днём — огни по видимости (0,65 мили).
// Порядок −95: Update — раньше источников (условия кадра готовы), LateUpdate — после всех.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-95)]
public class NavLightField : MonoBehaviour
{
    public enum LightsMode { Auto, On, Off }

    public static NavLightField Instance { get; private set; }

    [Tooltip("Материал с шейдером VRBridgeTrainer/NavLight (Setup B3 Traffic)")]
    public Material material;

    [Header("Отображение")]
    [Tooltip("Угловой размер огня (с ореолом), градусы. 0,15° ≈ 3 пикселя шлема")]
    public float angularDiameterDeg = 0.15f;
    [Tooltip("Наименьший радиус огня вблизи, м (размер фонаря)")]
    public float minRadius = 0.15f;
    [Tooltip("Общая яркость (множитель шейдера)")]
    public float brightness = 1.5f;

    [Header("Когда горят огни")]
    public LightsMode mode = LightsMode.Auto;
    [Tooltip("Авто без WeatherController (как в B3): темно, если RenderSettings.ambientIntensity меньше порога")]
    public float darkAmbientThreshold = 0.3f;
    [Tooltip("Авто (B4): темно, если WeatherController.Darkness не меньше порога (сумерки 0,6, ночь 1)")]
    public float darknessThreshold = EnvironmentModel.DarknessForLights;
    [Tooltip("Авто: ограниченная видимость меньше, мили")]
    public float restrictedVisibilityNm = 2f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private bool lightsOn;
    [SerializeField] private float visibilityNm;
    [SerializeField] private float transmissivity;
    [SerializeField] private int lightsDrawn;

    /// <summary>Огни горят (режим и условия).</summary>
    public bool LightsOn => lightsOn;
    /// <summary>Коэффициент пропускания атмосферы K на милю.</summary>
    public double Transmissivity { get; private set; } = NavigationLights.ClearTransmissivity;

    private struct Entry { public Vector3 Position; public Color Color; }
    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
    private readonly List<int> indices = new List<int>();
    private Mesh mesh;
    private Transform meshRoot;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock block;
    private BridgeMenuCommand modeCommand;
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Огни] В сцене несколько NavLightField — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (mesh != null) Destroy(mesh);
    }

    void OnEnable()
    {
        modeCommand = BridgeMenu.Register("Отладка", () =>
            mode == LightsMode.Auto ? "Огни: авто → вкл" : mode == LightsMode.On ? "Огни: вкл → выкл" : "Огни: выкл → авто",
            () => mode = mode == LightsMode.Auto ? LightsMode.On : mode == LightsMode.On ? LightsMode.Off : LightsMode.Auto);
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(modeCommand);
        modeCommand = null;
    }

    void Start()
    {
        var go = new GameObject("NavLightsMesh");
        go.transform.SetParent(transform, false);
        meshRoot = go.transform;
        mesh = new Mesh { name = "NavLights" };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        meshRenderer = mr;
        block = new MaterialPropertyBlock();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (material == null) Debug.LogWarning("[Огни] Не назначен материал NavLight — Tools/VR Bridge/Setup B3 Traffic.", this);
    }

    void Update()
    {
        // Видимость по туману сцены (ExponentialSquared: V = √(−ln 0,05)/ρ ≈ 1,73/ρ)
        double vis = double.PositiveInfinity;
        if (RenderSettings.fog && RenderSettings.fogDensity > 1e-7f)
        {
            double k = RenderSettings.fogMode == FogMode.ExponentialSquared ? 1.7308 : RenderSettings.fogMode == FogMode.Exponential ? 2.9957 : 0.0;
            if (k > 0.0) vis = k / RenderSettings.fogDensity;
        }
        Transmissivity = NavigationLights.TransmissivityFromVisibility(vis);
        WeatherController weather = WeatherController.Instance;
        bool dark = weather != null ? weather.Darkness >= darknessThreshold : RenderSettings.ambientIntensity < darkAmbientThreshold;
        bool restricted = vis < restrictedVisibilityNm * NavigationLights.MetersPerNm;
        lightsOn = mode == LightsMode.On || (mode == LightsMode.Auto && (dark || restricted));
        visibilityNm = (float)(vis / NavigationLights.MetersPerNm);
        transmissivity = (float)Transmissivity;
    }

    /// <summary>Сдать огонь на этот кадр: позиция в сцене, цвет, уровень яркости 0…1.</summary>
    public void Submit(Vector3 scenePosition, NavLightColor color, float level)
    {
        if (level <= 0.002f) return;
        Color c = ColorOf(color);
        c.a = Mathf.Clamp01(level);
        entries.Add(new Entry { Position = scenePosition, Color = c });
    }

    /// <summary>Освещённость → уровень яркости, с учётом текущего K.</summary>
    public float Level(double intensityCd, double distanceM, double sectorFactor = 1.0)
    {
        if (!lightsOn || sectorFactor <= 0.0) return 0f;
        return (float)NavigationLights.DisplayLevel(NavigationLights.Illuminance(intensityCd * sectorFactor, distanceM, Transmissivity));
    }

    void LateUpdate()
    {
        if (mesh == null) return;
        meshRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        verts.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
        float tanHalf = Mathf.Tan(0.5f * angularDiameterDeg * Mathf.Deg2Rad);
        var size = new Vector2(tanHalf, minRadius);
        foreach (Entry e in entries)
        {
            int i = verts.Count;
            for (int k = 0; k < 4; k++)
            {
                verts.Add(e.Position);
                colors.Add(e.Color);
                uv1.Add(size);
            }
            uv0.Add(new Vector2(0, 0)); uv0.Add(new Vector2(1, 0)); uv0.Add(new Vector2(1, 1)); uv0.Add(new Vector2(0, 1));
            indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
            indices.Add(i); indices.Add(i + 2); indices.Add(i + 3);
        }
        lightsDrawn = entries.Count;
        entries.Clear();

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(indices, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 30000f);   // квадраты разворачивает шейдер
        // Яркость — через MaterialPropertyBlock: ассет материала в Play Mode не меняется
        block.SetFloat(IntensityId, brightness);
        meshRenderer.SetPropertyBlock(block);
    }

    /// <summary>Цвета огней (цветность по Приложению I, п. 7 — приближённо в sRGB).</summary>
    public static Color ColorOf(NavLightColor c)
    {
        switch (c)
        {
            case NavLightColor.Red: return new Color(1f, 0.13f, 0.08f);
            case NavLightColor.Green: return new Color(0.2f, 1f, 0.5f);
            case NavLightColor.Yellow: return new Color(1f, 0.78f, 0.15f);
            default: return new Color(1f, 0.95f, 0.85f);
        }
    }
}
