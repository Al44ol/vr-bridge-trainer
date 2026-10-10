// ============================================================
// WeatherController.cs — Управление погодой (улучшенные пресеты)
// ============================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

public class WeatherController : MonoBehaviour
{
    [Header("Ссылки")]
    public ShipMotion shipMotion;
    public Light sunLight;
    public Material oceanMaterial;
    [Tooltip("Дождь (Setup Rain). Интенсивность меняется вместе с погодой.")]
    public RainEffect rainEffect;

    [Header("Текущее состояние")]
    [SerializeField] private string currentPreset = "moderate";

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
        fogDensity = 0.001f,
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
        fogDensity = 0.003f,
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
        fogDensity = 0.025f,
        sunAngle = 25f,
        windSpeed = 2.2f,
        rain = 1f
    };

    private WeatherPreset night = new WeatherPreset
    {
        name = "night",
        seaState = 0.15f,
        sunIntensity = 0.01f,
        sunColor = new Color(0.15f, 0.18f, 0.3f),
        ambientSky = new Color(0.01f, 0.01f, 0.03f),
        ambientGround = new Color(0.01f, 0.01f, 0.02f),
        fogColor = new Color(0.02f, 0.03f, 0.06f),
        fogDensity = 0.008f,
        sunAngle = -15f,
        windSpeed = 0.6f
    };

    private WeatherPreset currentState;
    private WeatherPreset targetState;
    private float transitionProgress = 1f;
    private float transitionDuration = 3f;

    void Start()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        currentState = moderate;
        targetState = moderate;
        ApplyPreset(moderate);
    }

    void Update()
    {
        if (transitionProgress < 1f)
        {
            transitionProgress += Time.deltaTime / transitionDuration;
            transitionProgress = Mathf.Clamp01(transitionProgress);
            LerpWeather(currentState, targetState, transitionProgress);
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetCalm();
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SetModerate();
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SetStorm();
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SetNight();
        }
    }

    public void SetCalm() { TransitionTo(calm); }
    public void SetModerate() { TransitionTo(moderate); }
    public void SetStorm() { TransitionTo(storm); }
    public void SetNight() { TransitionTo(night); }

    private void TransitionTo(WeatherPreset target)
    {
        currentState = GetCurrentLerpedState();
        targetState = target;
        transitionProgress = 0f;
        currentPreset = target.name;
    }

    private WeatherPreset GetCurrentLerpedState()
    {
        WeatherPreset s = new WeatherPreset();
        s.seaState = shipMotion != null ? shipMotion.seaState : moderate.seaState;
        s.sunIntensity = sunLight != null ? sunLight.intensity : 1f;
        s.sunColor = sunLight != null ? sunLight.color : Color.white;
        s.ambientSky = RenderSettings.ambientSkyColor;
        s.ambientGround = RenderSettings.ambientGroundColor;
        s.fogColor = RenderSettings.fogColor;
        s.fogDensity = RenderSettings.fogDensity;
        s.sunAngle = sunLight != null ? sunLight.transform.eulerAngles.x : 45f;
        s.windSpeed = oceanMaterial != null ? oceanMaterial.GetFloat("_WaveSpeed") : 1f;
        s.rain = rainEffect != null ? rainEffect.Intensity : 0f;
        return s;
    }

    private void LerpWeather(WeatherPreset from, WeatherPreset to, float t)
    {
        float smooth = Mathf.SmoothStep(0f, 1f, t);

        if (shipMotion != null)
            shipMotion.seaState = Mathf.Lerp(from.seaState, to.seaState, smooth);

        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, smooth);
            sunLight.color = Color.Lerp(from.sunColor, to.sunColor, smooth);
            float angle = Mathf.Lerp(from.sunAngle, to.sunAngle, smooth);
            sunLight.transform.rotation = Quaternion.Euler(angle, -30f, 0f);
        }

        RenderSettings.ambientSkyColor = Color.Lerp(from.ambientSky, to.ambientSky, smooth);
        RenderSettings.ambientGroundColor = Color.Lerp(from.ambientGround, to.ambientGround, smooth);
        RenderSettings.fogColor = Color.Lerp(from.fogColor, to.fogColor, smooth);
        RenderSettings.fogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, smooth);
        RenderSettings.ambientIntensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, smooth);
        RenderSettings.reflectionIntensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, smooth);


        if (oceanMaterial != null)
        {
            oceanMaterial.SetFloat("_WaveIntensity", Mathf.Lerp(from.seaState, to.seaState, smooth));
            oceanMaterial.SetFloat("_WaveSpeed", Mathf.Lerp(from.windSpeed, to.windSpeed, smooth));
        }

        // Дождь — только в шторм, появляется/исчезает вместе с переходом погоды
        if (rainEffect != null)
            rainEffect.Intensity = Mathf.Lerp(from.rain, to.rain, smooth);
        RenderSettings.ambientIntensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, smooth);
    }


    private void ApplyPreset(WeatherPreset preset)
    {
        LerpWeather(preset, preset, 1f);
    }

}
