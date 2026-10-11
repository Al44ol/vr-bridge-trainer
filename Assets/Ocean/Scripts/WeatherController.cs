// ============================================================
// WeatherController.cs — Управление погодой (улучшенные пресеты)
//
// Туман — ExponentialSquared: доля видимого f = exp(−(ρ·d)²). Дальность
// видимости (метеорологическая, порог контраста 5 %): d = √(−ln 0,05)/ρ ≈ 1,73/ρ.
// B1: плотности пересчитаны под морскую видимость (буи фарватера видны
// на несколько кабельтовых и дальше, край океана 10 км скрыт): штиль ≈ 5,8 км,
// умеренное ≈ 4,3 км, шторм ≈ 1,2 км, ночь ≈ 4,3 км. Видимость как условие сценария — C4.
// B3: ночь — 0,00015 ≈ 11,5 км (6,2 мили), чтобы огни судов были видны на несколько миль
// (дальность огня в тумане — закон Алларда, NavigationLights).
//
// B4: погода и время суток — две независимые оси (EnvironmentModel в Core):
//   • погода — пресет (волнение, ветер, дождь, видимость, дневной вид), переход 3 с;
//   • время суток — фаза 0 день / 1 сумерки / 2 ночь, переход timeOfDayTransitionSeconds
//     (день → ночь через сумерки).
// Итоговое освещение (Lighting) применяет только этот компонент: солнце и луна
// (отдельный направленный источник), рассеянный свет — градиент (Trilight),
// туман, блик воды (_OceanNightSpec); небо — SkyWeather, отражения — EnvironmentReflection.
// Пресет «Ночь» остаётся погодой (слабое волнение, видимость 6,2 мили); ночью
// видимость задаёт погода (C4 — видимость сценария).
// Совместимость: клавиши 1–3 и кнопки панели погоды — погода + день, 4 / «Ночь» —
// погода «Ночь» + ночь (как до B4). Раздельно — меню A «Обстановка» и сценарий.
// Исправлено: угол солнца интерполировался через eulerAngles (−15° читалось как 345°,
// переход ночь → день шёл «длинным путём» — кувырок света); теперь состояние не читается из сцены.
// C2: плотности тумана пресетов берутся из WeatherVisibility (Core) — по ним же оценка считает
// видимость и дистанцию обнаружения целей (в шлеме и офлайн по записи).
// ============================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

public class WeatherController : MonoBehaviour
{
    public static WeatherController Instance { get; private set; }

    [Header("Ссылки")]
    public ShipMotion shipMotion;
    public Light sunLight;
    [Tooltip("Луна — отдельный слабый направленный источник (Setup B4 Night)")]
    public Light moonLight;
    public Material oceanMaterial;
    [Tooltip("Дождь (Setup Rain). Интенсивность меняется вместе с погодой.")]
    public RainEffect rainEffect;

    [Header("Текущее состояние")]
    [SerializeField] private string currentPreset = "moderate";
    [SerializeField] private TimeOfDay currentTimeOfDay = TimeOfDay.Day;

    [Header("Время суток (B4)")]
    [Tooltip("Длительность перехода между соседними фазами (день ↔ сумерки ↔ ночь), с")]
    public float timeOfDayTransitionSeconds = 8f;
    [Tooltip("Поворот солнца по Y, градусы (как до B4)")]
    public float sunYawDeg = -30f;
    [Tooltip("Ночь и сумерки: луна, рассеянный свет, небо, экспозиция")]
    public NightProfile nightProfile = new NightProfile();

    [System.Serializable]
    public struct WeatherPreset
    {
        public string name;
        public float seaState;
        public float sunIntensity;
        public Color sunColor;
        public Color ambientSky;
        public Color ambientGround;
        public Color fogColor;
        public float fogDensity;
        public float sunAngle;
        public float windSpeed;
        public float rain;          // интенсивность дождя 0..1 (только storm = 1)
    }

    private WeatherPreset calm = new WeatherPreset
    {
        name = "calm",
        seaState = 0.08f,
        sunIntensity = 1.2f,
        sunColor = new Color(1f, 0.96f, 0.84f),
        ambientSky = new Color(0.53f, 0.71f, 0.85f),
        ambientGround = new Color(0.25f, 0.22f, 0.18f),
        fogColor = new Color(0.7f, 0.8f, 0.9f),
        fogDensity = WeatherVisibility.FogDensity(WeatherPresetId.Calm),       // 0,0003: видимость ≈ 5,8 км (B1; было 0,001 ≈ 1,7 км)
        sunAngle = 50f,
        windSpeed = 0.5f
    };

    private WeatherPreset moderate = new WeatherPreset
    {
        name = "moderate",
        seaState = 0.3f,
        sunIntensity = 1.0f,
        sunColor = new Color(1f, 0.95f, 0.85f),
        ambientSky = new Color(0.45f, 0.58f, 0.7f),
        ambientGround = new Color(0.2f, 0.2f, 0.18f),
        fogColor = new Color(0.6f, 0.68f, 0.76f),
        fogDensity = WeatherVisibility.FogDensity(WeatherPresetId.Moderate),   // 0,0004: ≈ 4,3 км, 2,3 мили (было 0,003 ≈ 0,6 км)
        sunAngle = 45f,
        windSpeed = 1.0f
    };

    private WeatherPreset storm = new WeatherPreset
    {
        name = "storm",
        seaState = 0.85f,
        sunIntensity = 0.12f,
        sunColor = new Color(0.45f, 0.45f, 0.5f),
        ambientSky = new Color(0.08f, 0.09f, 0.11f),
        ambientGround = new Color(0.05f, 0.05f, 0.05f),
        fogColor = new Color(0.25f, 0.27f, 0.3f),
        fogDensity = WeatherVisibility.FogDensity(WeatherPresetId.Storm),      // 0,0015: ≈ 1,2 км (было 0,025 ≈ 70 м)
        sunAngle = 25f,
        windSpeed = 2.2f,
        rain = 1f
    };

    // B4: погода «Ночь» — слабое волнение и хорошая видимость; темноту даёт время суток.
    // Дневной вид пресета нужен только при «Ночь» + «день» (из меню): как у умеренного.
    private WeatherPreset night = new WeatherPreset
    {
        name = "night",
        seaState = 0.15f,
        sunIntensity = 1.0f,
        sunColor = new Color(1f, 0.95f, 0.85f),
        ambientSky = new Color(0.45f, 0.58f, 0.7f),
        ambientGround = new Color(0.2f, 0.2f, 0.18f),
        fogColor = new Color(0.6f, 0.68f, 0.76f),
        // ≈ 11,5 км = 6,2 мили (B3: топовые огни видны ≈ 4,2 мили, бортовые ≈ 2,4 мили;
        // при 0,0004 (4,3 км) — лишь 2,4 и 1,5 мили). Было 0,008 ≈ 0,2 км (до B1).
        fogDensity = WeatherVisibility.FogDensity(WeatherPresetId.Night),   // 0,00015
        sunAngle = 45f,
        windSpeed = 0.6f
    };

    /// <summary>Текущий (целевой) пресет — запись смены погоды (C1).</summary>
    public WeatherPresetId CurrentPresetId => IdOf(currentPreset);

    /// <summary>Текущее (целевое) время суток (B4).</summary>
    public TimeOfDay CurrentTimeOfDay => currentTimeOfDay;

    /// <summary>Итоговое освещение последнего кадра.</summary>
    public EnvironmentLighting Lighting { get; private set; }
    /// <summary>«Темно»: 0 — день, 0,6 — сумерки, 1 — ночь (огни, свет мостика).</summary>
    public float Darkness => Lighting.Darkness;
    /// <summary>Растёт при каждом изменении освещения (обновление отражений).</summary>
    public int LightingVersion { get; private set; }
    public bool IsTransitioning => weatherT < 1f || phaseT < 1f;

    private WeatherPreset weatherFrom, weatherTo, weatherNow;
    private float weatherT = 1f;
    private const float WeatherTransitionSeconds = 3f;
    private float phaseFrom, phaseTo, phaseNow, phaseT = 1f;
    private bool dirty = true;
    private readonly System.Collections.Generic.List<BridgeMenuCommand> menuCommands = new System.Collections.Generic.List<BridgeMenuCommand>();

    private static readonly int OceanNightSpecId = Shader.PropertyToID("_OceanNightSpec");
    private static readonly WeatherPresetId[] WeatherOrder = { WeatherPresetId.Calm, WeatherPresetId.Moderate, WeatherPresetId.Night, WeatherPresetId.Storm };

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Погода] В сцене несколько WeatherController — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Shader.SetGlobalVector(OceanNightSpecId, Vector4.zero);
    }

    void OnEnable()
    {
        menuCommands.Add(BridgeMenu.RegisterStepper("Обстановка",
            () => $"Время суток: {ScenarioText.TimeOfDayTitle(currentTimeOfDay)}",
            () => SetTimeOfDay((TimeOfDay)Mathf.Max(0, (int)currentTimeOfDay - 1)),
            () => SetTimeOfDay((TimeOfDay)Mathf.Min((int)TimeOfDay.Night, (int)currentTimeOfDay + 1))));
        menuCommands.Add(BridgeMenu.RegisterStepper("Обстановка",
            () => $"Погода: {ScenarioText.WeatherTitle(CurrentPresetId)}",
            () => StepWeather(-1), () => StepWeather(+1)));
        menuCommands.Add(BridgeMenu.RegisterStepper("Ночь",
            () => $"Экспозиция ночи: {ScenarioText.Num(nightProfile.Exposure, 1)}",
            () => SetNightExposure(nightProfile.Exposure - 0.1f),
            () => SetNightExposure(nightProfile.Exposure + 0.1f)).InMenu(BridgeMenu.SettingsMenuId));
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
    }

    void Start()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        weatherFrom = weatherTo = weatherNow = moderate;
        phaseFrom = phaseTo = phaseNow = EnvironmentModel.PhaseOf(currentTimeOfDay);
        Apply();
    }

    void Update()
    {
        bool changed = dirty;
        if (weatherT < 1f)
        {
            weatherT = Mathf.Clamp01(weatherT + Time.deltaTime / WeatherTransitionSeconds);
            weatherNow = LerpPreset(weatherFrom, weatherTo, Mathf.SmoothStep(0f, 1f, weatherT));
            changed = true;
        }
        if (phaseT < 1f)
        {
            float span = Mathf.Max(Mathf.Abs(phaseTo - phaseFrom), 1e-3f);
            phaseT = Mathf.Clamp01(phaseT + Time.deltaTime / (Mathf.Max(timeOfDayTransitionSeconds, 0.01f) * span));
            phaseNow = Mathf.Lerp(phaseFrom, phaseTo, Mathf.SmoothStep(0f, 1f, phaseT));
            changed = true;
        }
        if (changed) Apply();

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetCalm();
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SetModerate();
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SetStorm();
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SetNight();
        }
    }

    /// <summary>Пресет по имени (calm / moderate / storm / night) — для стенда мореходности.</summary>
    public bool TryGetPreset(string presetName, out WeatherPreset preset)
    {
        foreach (WeatherPreset p in new[] { calm, moderate, storm, night })
            if (p.name == presetName) { preset = p; return true; }
        preset = default;
        return false;
    }

    /// <summary>
    /// Пресет как до B4 (кнопки панели, клавиши): погода и время суток —
    /// «Ночь» → ночь, остальные → день. instant — сразу, без перехода.
    /// </summary>
    public void SetPreset(WeatherPresetId id, bool instant = false)
    {
        SetWeather(id, instant);
        SetTimeOfDay(id == WeatherPresetId.Night ? TimeOfDay.Night : TimeOfDay.Day, instant);
    }

    /// <summary>Только погода (B4: сценарий, меню «Обстановка»); время суток не меняется.</summary>
    public void SetWeather(WeatherPresetId id, bool instant = false)
    {
        WeatherPreset p = PresetOf(id);
        currentPreset = p.name;
        if (instant)
        {
            weatherFrom = weatherTo = weatherNow = p;
            weatherT = 1f;
            dirty = true;
            return;
        }
        weatherFrom = weatherNow;
        weatherTo = p;
        weatherT = 0f;
    }

    /// <summary>Только время суток (B4). instant — сразу (сценарий при загрузке).</summary>
    public void SetTimeOfDay(TimeOfDay t, bool instant = false)
    {
        currentTimeOfDay = t;
        float target = EnvironmentModel.PhaseOf(t);
        if (instant)
        {
            phaseFrom = phaseTo = phaseNow = target;
            phaseT = 1f;
            dirty = true;
            return;
        }
        if (Mathf.Approximately(target, phaseTo) && phaseT < 1f) return;
        phaseFrom = phaseNow;
        phaseTo = target;
        phaseT = Mathf.Approximately(phaseFrom, phaseTo) ? 1f : 0f;
    }

    /// <summary>Экспозиция ночи (меню «Параметры»): 0,3…3.</summary>
    public void SetNightExposure(float e)
    {
        nightProfile.Exposure = Mathf.Clamp(Mathf.Round(e * 10f) / 10f, 0.3f, 3f);
        dirty = true;
    }

    public void SetCalm() { SetPreset(WeatherPresetId.Calm); }
    public void SetModerate() { SetPreset(WeatherPresetId.Moderate); }
    public void SetStorm() { SetPreset(WeatherPresetId.Storm); }
    public void SetNight() { SetPreset(WeatherPresetId.Night); }

    private void StepWeather(int dir)
    {
        int i = System.Array.IndexOf(WeatherOrder, CurrentPresetId);
        i = Mathf.Clamp(i + dir, 0, WeatherOrder.Length - 1);
        SetWeather(WeatherOrder[i]);
    }

    private WeatherPreset PresetOf(WeatherPresetId id)
    {
        switch (id)
        {
            case WeatherPresetId.Calm: return calm;
            case WeatherPresetId.Storm: return storm;
            case WeatherPresetId.Night: return night;
            default: return moderate;
        }
    }

    private static WeatherPresetId IdOf(string presetName)
    {
        switch (presetName)
        {
            case "calm": return WeatherPresetId.Calm;
            case "storm": return WeatherPresetId.Storm;
            case "night": return WeatherPresetId.Night;
            default: return WeatherPresetId.Moderate;
        }
    }

    private static WeatherPreset LerpPreset(in WeatherPreset a, in WeatherPreset b, float t) => new WeatherPreset
    {
        name = b.name,
        seaState = Mathf.Lerp(a.seaState, b.seaState, t),
        sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
        sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
        ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t),
        ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t),
        fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
        fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
        sunAngle = Mathf.Lerp(a.sunAngle, b.sunAngle, t),
        windSpeed = Mathf.Lerp(a.windSpeed, b.windSpeed, t),
        rain = Mathf.Lerp(a.rain, b.rain, t)
    };

    /// <summary>Пресет → погода модели (Core).</summary>
    public static WeatherLook ToLook(in WeatherPreset p) => new WeatherLook
    {
        SeaState = p.seaState, WindSpeed = p.windSpeed, Rain = p.rain, FogDensity = p.fogDensity,
        SunIntensity = p.sunIntensity, SunColor = ToRgb(p.sunColor), SunElevationDeg = p.sunAngle,
        AmbientSky = ToRgb(p.ambientSky), AmbientGround = ToRgb(p.ambientGround), FogColor = ToRgb(p.fogColor)
    };

    public static Rgb ToRgb(Color c) => new Rgb(c.r, c.g, c.b);
    public static Color ToColor(Rgb c) => new Color(c.R, c.G, c.B, 1f);

    private void Apply()
    {
        dirty = false;
        EnvironmentLighting L = EnvironmentModel.Combine(ToLook(weatherNow), phaseNow, nightProfile);
        Lighting = L;
        LightingVersion++;

        if (shipMotion != null)
            shipMotion.seaState = weatherNow.seaState;

        // Солнце и луна: выключенный источник не рисуется и не даёт блика на воде.
        // Главный источник (блик воды, тени) — более яркий; переключение — в тёмной точке
        // сумерек, когда оба тусклые, поэтому без вспышки.
        if (sunLight != null)
        {
            sunLight.intensity = L.SunIntensity;
            sunLight.color = ToColor(L.SunColor);
            sunLight.transform.rotation = Quaternion.Euler(L.SunElevationDeg, sunYawDeg, 0f);
            sunLight.enabled = L.SunIntensity > 1e-4f;
        }
        if (moonLight != null)
        {
            moonLight.intensity = L.MoonIntensity;
            moonLight.color = ToColor(L.MoonColor);
            // Источник светит ОТ луны: высота E, направление на азимут A + 180°
            moonLight.transform.rotation = Quaternion.Euler(L.MoonElevationDeg, L.MoonAzimuthDeg + 180f, 0f);
            moonLight.enabled = L.MoonIntensity > 1e-4f;
        }
        Light main = sunLight;
        if (moonLight != null && (sunLight == null || L.MoonIntensity > L.SunIntensity)) main = moonLight;
        if (main != null && RenderSettings.sun != main) RenderSettings.sun = main;

        // Рассеянный свет — градиент (B4; раньше — Skybox с запечёнными пробами шаблона)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ToColor(L.AmbientSky);
        RenderSettings.ambientEquatorColor = ToColor(L.AmbientEquator);
        RenderSettings.ambientGroundColor = ToColor(L.AmbientGround);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;

        RenderSettings.fogColor = ToColor(L.FogColor);
        RenderSettings.fogDensity = L.FogDensity;

        if (oceanMaterial != null)
        {
            oceanMaterial.SetFloat("_WaveIntensity", weatherNow.seaState);
            oceanMaterial.SetFloat("_WaveSpeed", weatherNow.windSpeed);
        }
        Shader.SetGlobalVector(OceanNightSpecId, new Vector4(L.WaterSpecGain, L.WaterSpecWidening, 0f, 0f));

        // Дождь — только в шторм, появляется/исчезает вместе с переходом погоды
        if (rainEffect != null)
            rainEffect.Intensity = weatherNow.rain;
    }
}
