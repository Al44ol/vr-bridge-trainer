// ============================================================
// RainEffect.cs — Дождь (частицы) вокруг наблюдателя
// Ставится скриптом Tools/VR Bridge/Setup Rain на объект Rain (корень сцены).
//
// Интенсивность (0..1) задаёт WeatherController: в пресете storm = 1,
// в остальных = 0, меняется вместе с погодой (SmoothStep за время перехода).
//
// Привязка. Плавающее начало координат: судно стоит в (0,0,0), движется
// «мир» (вода сдвигается в шейдере). Поэтому частицы считаются в мировом
// пространстве сцены, а излучатель каждый кадр ставится над камерой.
// Не дочерний к Ship — иначе дождь наклонялся бы с креном.
//
// Скорость капли относительно судна (сцена = система судна):
//   v = v_ветра − v_судна + (0, −w, 0),
//   v_ветра = U_w · d_ветра (капля быстро набирает скорость воздуха),
//   v_судна = ShipSpeed · e_нос (горизонтальная проекция оси носа),
//   w — установившаяся скорость падения капли (≈ 6.5 м/с при d = 2 мм,
//   Gunn & Kinzer, 1949).
// Излучатель смещается против относительного ветра на v_гор · h / w,
// чтобы капли проходили через место наблюдателя, а не мимо.
//
// Рубка. Капли, попавшие в коробки shelters (BoxCollider-триггеры,
// дочерние к Ship — качаются вместе с рубкой), уничтожаются модулем
// Trigger системы частиц: дождь бьёт в крышу и окна, но не идёт внутрь.
// ============================================================
using UnityEngine;

public class RainEffect : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("За кем следует дождь (камера XR). Пусто — Camera.main.")]
    public Transform followTarget;
    [Tooltip("Ход судна (ShipSpeed) и направление носа")]
    public WorldOrigin worldOrigin;
    [Tooltip("Направление ветра (windFromDeg)")]
    public OceanWaves oceanWaves;
    [Tooltip("Материал капель (URP Particles/Unlit, прозрачный). Ассет — чтобы шейдер не вырезался из сборки.")]
    public Material rainMaterial;
    [Tooltip("Объёмы, куда дождь не проникает (рубка). Триггеры, дочерние к Ship.")]
    public Collider[] shelters;

    [Header("Интенсивность (задаёт WeatherController)")]
    [Range(0f, 1f)] [SerializeField] private float intensity;

    [Header("Капли")]
    [Tooltip("Капель в секунду при интенсивности 1")]
    public float emissionRate = 2000f;
    public int maxParticles = 6000;
    [Tooltip("Сторона квадрата излучения над наблюдателем, м")]
    public float area = 30f;
    [Tooltip("Высота излучения над камерой, м")]
    public float spawnHeight = 10f;
    [Tooltip("Насколько ниже камеры живёт капля, м (дальше вниз не видно — палуба)")]
    public float fallBelowCamera = 8f;
    [Tooltip("Уровень воды в сцене (y OceanRadial), м — ниже капли не опускаются")]
    public float waterLevelY = -5f;
    [Tooltip("Скорость падения капли, м/с (установившаяся, d ≈ 2 мм)")]
    public float fallSpeed = 6.5f;
    [Tooltip("Скорость ветра в шторм, м/с (7 баллов Бофорта ≈ 14–17 м/с)")]
    public float windSpeed = 15f;
    [Tooltip("Толщина капли, м")]
    public float dropWidth = 0.012f;
    [Tooltip("Длина штриха = скорость · коэффициент (имитация смаза при выдержке)")]
    public float streakVelocityScale = 0.035f;
    [Tooltip("Яркость капли относительно цвета тумана")]
    public float brightness = 1.8f;
    [Range(0f, 1f)] public float alpha = 0.35f;

    private ParticleSystem rainSystem;
    private ParticleSystemRenderer rainRenderer;
    private Material runtimeMaterial;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Интенсивность дождя 0..1.</summary>
    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Clamp01(value);
    }

    void Awake()
    {
        CreateRainSystem();
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    void LateUpdate()
    {
        if (rainSystem == null) return;

        Transform target = followTarget;
        if (target == null && Camera.main != null) target = Camera.main.transform;
        if (target == null) return;

        // Относительный ветер (горизонтальный)
        Vector3 wind = Vector3.zero;
        if (oceanWaves != null)
        {
            Vector2 d = oceanWaves.WindDirection;            // куда дует: x = восток, y = север
            wind = new Vector3(d.x, 0f, d.y) * windSpeed;
        }
        Vector3 shipVel = Vector3.zero;
        if (worldOrigin != null)
        {
            Vector3 bow = worldOrigin.ship != null
                ? worldOrigin.ship.TransformDirection(worldOrigin.bowLocalAxis)
                : Vector3.back;
            bow.y = 0f;
            if (bow.sqrMagnitude > 1e-6f) shipVel = bow.normalized * worldOrigin.ShipSpeed;
        }
        Vector3 relH = wind - shipVel;

        // Излучатель над камерой, со сносом навстречу относительному ветру
        Vector3 cam = target.position;
        float fallTime = spawnHeight / Mathf.Max(0.1f, fallSpeed);
        rainSystem.transform.SetPositionAndRotation(
            cam + Vector3.up * spawnHeight - relH * fallTime, Quaternion.identity);

        // Время жизни — до max(камера − fallBelowCamera, вода)
        float killY = Mathf.Max(cam.y - fallBelowCamera, waterLevelY);
        float life = (cam.y + spawnHeight - killY) / Mathf.Max(0.1f, fallSpeed);
        var main = rainSystem.main;
        main.startLifetime = Mathf.Max(0.2f, life);

        var vel = rainSystem.velocityOverLifetime;
        vel.x = relH.x;
        vel.y = -fallSpeed;
        vel.z = relH.z;

        // Цвет — от цвета тумана: днём светлее, в шторм/ночью темнее
        if (runtimeMaterial != null)
        {
            Color c = RenderSettings.fogColor * brightness;
            c.a = alpha;
            runtimeMaterial.SetColor(BaseColorId, c);
        }

        var emission = rainSystem.emission;
        emission.rateOverTime = emissionRate * intensity;
        if (intensity > 0.001f)
        {
            if (!rainSystem.isEmitting) rainSystem.Play();
        }
        else if (rainSystem.isEmitting)
        {
            rainSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void CreateRainSystem()
    {
        var rainObj = new GameObject("RainParticles");
        rainObj.transform.SetParent(transform, false);

        rainSystem = rainObj.AddComponent<ParticleSystem>();
        rainSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rainSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 2.5f;
        main.startSpeed = 0f;                                  // скорость — через velocityOverLifetime
        main.startSize = dropWidth;
        main.startColor = Color.white;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;                             // капля уже на установившейся скорости
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // Rain в корне, масштаб 1

        var shape = rainSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(area, 0.1f, area);

        var emission = rainSystem.emission;
        emission.rateOverTime = 0f;

        var vel = rainSystem.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = 0f;
        vel.y = -fallSpeed;
        vel.z = 0f;

        // Рубка: капли внутри объёма уничтожаются
        if (shelters != null && shelters.Length > 0)
        {
            var trigger = rainSystem.trigger;
            trigger.enabled = true;
            foreach (Collider c in shelters)
                if (c != null) trigger.AddCollider(c);
            trigger.enter = ParticleSystemOverlapAction.Kill;
            trigger.inside = ParticleSystemOverlapAction.Kill;
            trigger.exit = ParticleSystemOverlapAction.Ignore;
            trigger.outside = ParticleSystemOverlapAction.Ignore;
        }

        rainRenderer = rainObj.GetComponent<ParticleSystemRenderer>();
        rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        rainRenderer.velocityScale = streakVelocityScale;
        rainRenderer.lengthScale = 1f;
        rainRenderer.cameraVelocityScale = 0f;
        rainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rainRenderer.receiveShadows = false;
        if (rainMaterial != null)
        {
            // Копия — чтобы цвет в рантайме не менял ассет
            runtimeMaterial = new Material(rainMaterial);
            rainRenderer.sharedMaterial = runtimeMaterial;
        }
        else
        {
            Debug.LogWarning("[RainEffect] Нет материала капель — запустите Tools/VR Bridge/Setup Rain.", this);
        }
    }
}
