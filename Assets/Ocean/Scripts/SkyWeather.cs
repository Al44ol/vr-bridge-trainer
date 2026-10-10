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
//   _SkyCloudOffset — дрейф туч по ветру (по модулю периода шума).
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

    void Update()
    {
        float brightness = 1f;
        if (sunLight != null)
            brightness = Mathf.Clamp(Mathf.Pow(Mathf.Max(sunLight.intensity, 0f) / Mathf.Max(daylightIntensity, 0.01f), 0.6f),
                                     minBrightness, 1f);

        float overcast = 0f;
        if (shipMotion != null)
            overcast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(overcastFrom, overcastTo, shipMotion.seaState));

        Vector2 wind = oceanWaves != null ? oceanWaves.WindDirection : new Vector2(0f, 1f);
        cloudOffset -= wind * (cloudDriftSpeed * (0.3f + overcast) * Time.deltaTime);
        cloudOffset.x = Mathf.Repeat(cloudOffset.x, CloudCells);
        cloudOffset.y = Mathf.Repeat(cloudOffset.y, CloudCells);

        Shader.SetGlobalFloat(DarkeningId, 1f - brightness);
        Shader.SetGlobalFloat(OvercastId, overcast);
        Shader.SetGlobalVector(OffsetId, new Vector4(cloudOffset.x, cloudOffset.y, 0f, 0f));
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(DarkeningId, 0f);
        Shader.SetGlobalFloat(OvercastId, 0f);
    }
}
