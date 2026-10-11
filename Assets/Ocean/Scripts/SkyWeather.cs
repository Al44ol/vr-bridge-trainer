// ============================================================
// SkyWeather.cs — Небо по погоде (затемнение, тучи)
//
// Не меняет WeatherController: читает то, что он уже выставил,
// и передаёт в скайбокс SkyboxHorizonFog глобальные переменные:
//   _SkyDarkening = 1 − clamp((I_солнца / I_дня)^0.6, minBrightness, 1)
//       ночь (I = 0.01) → ~0.94, шторм (0.12) → ~0.72, день (≥ 1) → 0;
//   _SkyOvercast  = smoothstep(overcastFrom, overcastTo, seaState)
//       шторм (0.85) → 1, остальные пресеты → 0; переход плавный,
//       потому что WeatherController плавно меняет seaState;
//   _SkyCloudOffset — дрейф туч (по модулю периода шума).
// B4: если есть WeatherController — затемнение и облачность берутся из его итогового
// освещения (EnvironmentModel, те же формулы), плюс ночное небо:
//   _SkyNight — доля ночного неба 0…1; _SkyNightZenith — цвет зенита;
//   _SkyStars — яркость звёзд; _SkyMoonDir (xyz — на луну, w — яркость диска), _SkyMoonColor;
//   _SkyTwilight, _SkyTwilightColor — свечение у горизонта в сумерки; _SkySunDir — на солнце.
// Ассет материала скайбокса не изменяется.
// ============================================================
using UnityEngine;

public class SkyWeather : MonoBehaviour
{
    [Header("Источники (выставляет WeatherController)")]
    public Light sunLight;
    public ShipMotion shipMotion;
    [Tooltip("Для направления дрейфа туч (необязательно)")]
    public OceanWaves oceanWaves;
    [Tooltip("B4: итоговое освещение (пусто — найти; нет — небо как до B4)")]
    public WeatherController weather;

    [Header("Затемнение неба")]
    [Tooltip("Интенсивность солнца, при которой небо обычной яркости")]
    public float daylightIntensity = 1f;
    [Tooltip("Минимальная яркость неба (ночью), 0..1")]
    [Range(0f, 1f)] public float minBrightness = 0.03f;

    [Header("Тучи")]
    [Tooltip("seaState, с которого появляются тучи")]
    [Range(0f, 1f)] public float overcastFrom = 0.45f;
    [Tooltip("seaState, при котором небо полностью затянуто")]
    [Range(0f, 1f)] public float overcastTo = 0.8f;
    [Tooltip("Скорость дрейфа туч, ячеек шума в секунду")]
    public float cloudDriftSpeed = 0.02f;

    private const float CloudCells = 64f;   // = CLOUD_CELLS в SkyboxHorizonFog.shader
    private Vector2 cloudOffset;

    private static readonly int DarkeningId = Shader.PropertyToID("_SkyDarkening");
    private static readonly int OvercastId  = Shader.PropertyToID("_SkyOvercast");
    private static readonly int OffsetId    = Shader.PropertyToID("_SkyCloudOffset");
    private static readonly int NightId     = Shader.PropertyToID("_SkyNight");
    private static readonly int ZenithId    = Shader.PropertyToID("_SkyNightZenith");
    private static readonly int StarsId     = Shader.PropertyToID("_SkyStars");
    private static readonly int MoonDirId   = Shader.PropertyToID("_SkyMoonDir");
    private static readonly int MoonColorId = Shader.PropertyToID("_SkyMoonColor");
    private static readonly int TwilightId  = Shader.PropertyToID("_SkyTwilight");
    private static readonly int TwilightColorId = Shader.PropertyToID("_SkyTwilightColor");
    private static readonly int SunDirId    = Shader.PropertyToID("_SkySunDir");

    void Start()
    {
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
    }

    void Update()
    {
        float darkening, overcast;
        if (weather != null)
        {
            EnvironmentLighting L = weather.Lighting;
            darkening = L.SkyDarkening;
            overcast = L.SkyOvercast;
            SetNight(L);
        }
        else
        {
            float brightness = 1f;
            if (sunLight != null)
                brightness = Mathf.Clamp(Mathf.Pow(Mathf.Max(sunLight.intensity, 0f) / Mathf.Max(daylightIntensity, 0.01f), 0.6f),
                                         minBrightness, 1f);
            darkening = 1f - brightness;
            overcast = 0f;
            if (shipMotion != null)
                overcast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(overcastFrom, overcastTo, shipMotion.seaState));
            Shader.SetGlobalFloat(NightId, 0f);
        }

        Vector2 wind = oceanWaves != null ? oceanWaves.WindDirection : new Vector2(0f, 1f);
        cloudOffset -= wind * (cloudDriftSpeed * (0.3f + overcast) * Time.deltaTime);
        cloudOffset.x = Mathf.Repeat(cloudOffset.x, CloudCells);
        cloudOffset.y = Mathf.Repeat(cloudOffset.y, CloudCells);

        Shader.SetGlobalFloat(DarkeningId, darkening);
        Shader.SetGlobalFloat(OvercastId, overcast);
        Shader.SetGlobalVector(OffsetId, new Vector4(cloudOffset.x, cloudOffset.y, 0f, 0f));
    }

    private void SetNight(in EnvironmentLighting L)
    {
        Shader.SetGlobalFloat(NightId, L.SkyNight);
        Shader.SetGlobalColor(ZenithId, WeatherController.ToColor(L.SkyNightZenith));
        Shader.SetGlobalFloat(StarsId, L.StarBrightness);
        Vector3 moon = DirectionTo(L.MoonAzimuthDeg, L.MoonElevationDeg);
        Shader.SetGlobalVector(MoonDirId, new Vector4(moon.x, moon.y, moon.z, L.MoonDiscBrightness));
        Shader.SetGlobalColor(MoonColorId, WeatherController.ToColor(L.MoonColor));
        Shader.SetGlobalFloat(TwilightId, L.TwilightGlow);
        Shader.SetGlobalColor(TwilightColorId, WeatherController.ToColor(L.TwilightGlowColor));
        Vector3 sun = sunLight != null ? -sunLight.transform.forward : Vector3.up;
        Shader.SetGlobalVector(SunDirId, new Vector4(sun.x, sun.y, sun.z, 0f));
    }

    /// <summary>Единичный вектор на светило: азимут (0 — N, по часовой), высота; +X — восток, +Z — север.</summary>
    public static Vector3 DirectionTo(float azimuthDeg, float elevationDeg)
    {
        float a = azimuthDeg * Mathf.Deg2Rad, e = elevationDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(DarkeningId, 0f);
        Shader.SetGlobalFloat(OvercastId, 0f);
        Shader.SetGlobalFloat(NightId, 0f);
        Shader.SetGlobalFloat(TwilightId, 0f);
    }
}
