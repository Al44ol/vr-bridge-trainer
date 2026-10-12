// ============================================================
// BridgeLighting.cs — Освещение мостика ночью (B4)
//
// Ночью свет в рубке либо выключен, либо красный приглушённый: красный свет
// меньше всего сбивает темновую адаптацию (палочки к нему почти нечувствительны),
// поэтому огни судов за окном остаются различимыми. Подсветка приборов —
// тусклая и регулируемая.
//   • Свет рубки: точечные источники под подволоком (без теней), режимы
//     «выкл / красный / белый». Днём лампы на фоне дневного света почти не видны —
//     яркость × lerp(daylightShare, 1, Darkness).
//   • Подсветка приборов (0…100 %): панели NavDisplay (курс, скорость, указатель руля,
//     телеграф) и панель погоды затемняются чёрной полупрозрачной «шторкой» поверх
//     панели: яркость f = lerp(1, подсветка, Darkness) — днём панели как раньше.
//     Сигнализация (полоса глубины) не тусклее alarmMinimum (60 %): тревога должна быть видна.
//     Шкала барабана телеграфа светится (emission) ∝ подсветка × Darkness.
//   • Отсвет своих ходовых огней на палубе и крыльях (точечные источники у мест огней
//     по Приложению I МППСС для судна 45 м) — горят вместе с огнями (NavLightField).
// Darkness — WeatherController (0 — день, 0,6 — сумерки, 1 — ночь).
// Управление в VR: меню «Параметры» (кнопка Y) — «Свет рубки: < … >»,
// «Подсветка приборов: < … >»; тумблер «СВЕТ» на пульте (BridgeLightSwitch) — вкл/выкл.
// Объекты, источники и тумблер создаёт Tools/VR Bridge/Setup B4 Night.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BridgeLighting : MonoBehaviour
{
    public enum CabinMode { Off, Red, White }

    public static BridgeLighting Instance { get; private set; }

    [Header("Свет рубки")]
    public Light[] cabinLamps;
    [Tooltip("Свет включён (тумблер на пульте)")]
    public bool cabinOn = true;
    [Tooltip("Цвет при включении: красный (ночью) или белый")]
    public bool cabinRed = true;
    public Color redColor = new Color(1f, 0.1f, 0.04f);
    public float redIntensity = 0.6f;
    public Color whiteColor = new Color(1f, 0.93f, 0.82f);
    public float whiteIntensity = 1.6f;
    [Tooltip("Доля яркости ламп днём (на фоне дневного света)")]
    [Range(0f, 1f)] public float daylightShare = 0.15f;

    [Header("Подсветка приборов")]
    [Range(0f, 1f)] public float backlight = 0.3f;
    [Tooltip("Панели приборов (затемняются целиком)")]
    public RectTransform[] instrumentPanels;
    [Tooltip("Сигнализация — не тусклее alarmMinimum")]
    public RectTransform[] alarmPanels;
    [Range(0f, 1f)] public float alarmMinimum = 0.6f;
    [Tooltip("Светящиеся шкалы (emission материала, напр. барабан телеграфа)")]
    public Renderer[] instrumentEmitters;
    public float emitterStrength = 0.6f;

    [Header("Отсвет своих ходовых огней")]
    public Light[] ownLightGlows;

    public CabinMode Mode => !cabinOn ? CabinMode.Off : cabinRed ? CabinMode.Red : CabinMode.White;

    private readonly List<(Image img, bool alarm)> shades = new List<(Image, bool)>();
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private float[] glowIntensity;
    private Color[] emitterBase;
    private MaterialPropertyBlock block;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private const string MenuGroup = "Свет мостика";

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Мостик] В сцене несколько BridgeLighting — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder(MenuGroup, -50);
        menuCommands.Add(BridgeMenu.RegisterStepper(MenuGroup, () => $"Свет рубки: {ModeTitle(Mode)}",
            () => SetMode((CabinMode)Mathf.Max(0, (int)Mode - 1)),
            () => SetMode((CabinMode)Mathf.Min((int)CabinMode.White, (int)Mode + 1))).InMenu(BridgeMenu.SettingsMenuId));
        menuCommands.Add(BridgeMenu.RegisterStepper(MenuGroup, () => $"Подсветка приборов: {Mathf.RoundToInt(backlight * 100f)} %",
            () => SetBacklight(backlight - 0.1f), () => SetBacklight(backlight + 0.1f)).InMenu(BridgeMenu.SettingsMenuId));
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
    }

    void Start()
    {
        block = new MaterialPropertyBlock();
        if (instrumentPanels != null)
            foreach (RectTransform p in instrumentPanels) AddShade(p, false);
        if (alarmPanels != null)
            foreach (RectTransform p in alarmPanels) AddShade(p, true);

        glowIntensity = new float[ownLightGlows != null ? ownLightGlows.Length : 0];
        for (int i = 0; i < glowIntensity.Length; i++)
            glowIntensity[i] = ownLightGlows[i] != null ? ownLightGlows[i].intensity : 0f;

        emitterBase = new Color[instrumentEmitters != null ? instrumentEmitters.Length : 0];
        for (int i = 0; i < emitterBase.Length; i++)
        {
            Renderer r = instrumentEmitters[i];
            Material m = r != null ? r.sharedMaterial : null;
            emitterBase[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
        }
    }

    /// <summary>
    /// Панель, построенная в рантайме (D2: навигационный планшет), — затемнять вместе с приборами
    /// («Подсветка приборов»). Шторка — последним дочерним элементом панели, лучи не ловит.
    /// </summary>
    public void RegisterInstrumentPanel(RectTransform panel) => AddShade(panel, false);

    public static string ModeTitle(CabinMode m) => m == CabinMode.Off ? "выкл" : m == CabinMode.Red ? "красный" : "белый";

    /// <summary>Тумблер на пульте: включить / выключить (цвет сохраняется).</summary>
    public void ToggleCabin() => cabinOn = !cabinOn;

    public void SetMode(CabinMode m)
    {
        cabinOn = m != CabinMode.Off;
        if (m != CabinMode.Off) cabinRed = m == CabinMode.Red;
    }

    public void SetBacklight(float v) => backlight = Mathf.Clamp01(Mathf.Round(v * 10f) / 10f);

    void Update()
    {
        WeatherController w = WeatherController.Instance;
        float dark = w != null ? Mathf.Clamp01(w.Darkness) : 0f;

        // Свет рубки
        CabinMode mode = Mode;
        float lampShare = Mathf.Lerp(daylightShare, 1f, dark);
        if (cabinLamps != null)
            foreach (Light l in cabinLamps)
            {
                if (l == null) continue;
                l.enabled = mode != CabinMode.Off;
                if (mode == CabinMode.Off) continue;
                l.color = mode == CabinMode.Red ? redColor : whiteColor;
                l.intensity = (mode == CabinMode.Red ? redIntensity : whiteIntensity) * lampShare;
            }

        // Подсветка приборов: шторка α = 1 − f
        float f = Mathf.Lerp(1f, backlight, dark);
        float fAlarm = Mathf.Lerp(1f, Mathf.Max(backlight, alarmMinimum), dark);
        foreach ((Image img, bool alarm) in shades)
        {
            if (img == null) continue;
            float a = 1f - (alarm ? fAlarm : f);
            img.enabled = a > 0.002f;
            img.color = new Color(0f, 0f, 0f, a);
        }

        // Светящиеся шкалы
        if (instrumentEmitters != null)
            for (int i = 0; i < instrumentEmitters.Length; i++)
            {
                Renderer r = instrumentEmitters[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, emitterBase[i] * (emitterStrength * backlight * dark));
                r.SetPropertyBlock(block);
            }

        // Отсвет своих огней — когда огни горят
        bool lightsOn = NavLightField.Instance != null ? NavLightField.Instance.LightsOn : dark >= EnvironmentModel.DarknessForLights;
        for (int i = 0; i < glowIntensity.Length; i++)
        {
            Light l = ownLightGlows[i];
            if (l == null) continue;
            l.enabled = lightsOn;
            l.intensity = glowIntensity[i] * Mathf.Max(dark, 0.3f);
        }
    }

    // Шторка поверх панели: растянута на весь прямоугольник, лучи не ловит, раскладку не трогает.
    // У панели с вложенной сигнализацией шторка ставится ПЕРЕД ней — сигнализацию затемняет своя.
    private void AddShade(RectTransform panel, bool alarm)
    {
        if (panel == null) return;
        var go = new GameObject("NightShade", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(panel, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        img.enabled = false;

        int index = panel.childCount - 1;
        if (!alarm && alarmPanels != null)
            foreach (RectTransform a in alarmPanels)
                if (a != null && a.parent == panel) index = Mathf.Min(index, a.GetSiblingIndex());
        rt.SetSiblingIndex(index);
        shades.Add((img, alarm));
    }
}
