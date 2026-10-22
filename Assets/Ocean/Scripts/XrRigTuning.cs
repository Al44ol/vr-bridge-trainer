// ============================================================
// XrRigTuning.cs — К2, К3 (22.10.2026): луч контроллера, телепорт и поворот игрока
//
// Ставится сам на XR Origin при загрузке сцены тренажёра (префабы XRI и сцена не меняются).
//
// К2. Луч Near-Far гнулся при выборе в меню, хотя игрок неподвижен (особенно в шторм):
//   цель ищется по сглаженному направлению (CurveInteractionCaster.enableStabilization —
//   0,25 м / 20°), а линия рисуется от контроллера и изгибается к точке попадания
//   (CurveVisualController, lineBendRatio 0,25–0,5). «Луч: прямой» — сглаживание дальнего
//   луча выключено, линия прямая в состояниях «нет цели», «наведение», «меню», «нажатие
//   в меню»; у взятого предмета (штурвал, телеграф, ручки панелей) изгиб остаётся — это
//   исключение для органов управления. «Как в XRI» — прежние значения префаба.
// К3. Телепорт: на сцене не было ни одной зоны телепорта — дуга всегда красная. Зона —
//   коллайдер пола под игроком (луч вниз от XR Origin; заново — при смене типа судна):
//   объект «TeleportFloor» (дочерний к Ship) с TeleportationArea, слой взаимодействия
//   «Teleport», только поверхности «вверх» ±20° (по оси палубы), ориентация игрока
//   не меняется (MatchOrientation.None — иначе игрок выравнивался бы по миру и стоял
//   с креном относительно палубы). Поворот правым стиком — плавный (60 °/с) или
//   шагами 45° (ControllerInputActionManager.smoothTurnEnabled правой руки).
// Меню Y → «Игрок»: «Луч: прямой / как в XRI», «Поворот: плавный / шагами 45°»;
// выбор запоминается на станции (PlayerPrefs).
// ============================================================
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public class XrRigTuning : MonoBehaviour
{
    public const string MenuGroup = "Игрок";
    private const string StraightPref = "VRBridge.StraightRay";
    private const string SmoothTurnPref = "VRBridge.SmoothTurn";

    [Tooltip("К2: прямой луч без сглаживания (false — как в префабе XRI)")]
    public bool straightRay = true;
    [Tooltip("К3: поворот правым стиком плавный (false — шагами 45°)")]
    public bool smoothTurn = true;
    [Tooltip("К3: допуск нормали пола для телепорта, градусы от оси палубы")]
    public float floorNormalToleranceDeg = 20f;
    [Tooltip("К3: искать пол не дальше, м (вниз от XR Origin)")]
    public float floorSearchDistance = 3f;

    public static XrRigTuning Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Ensure()   // и при повторной загрузке сцены — TrainerScenes
    {
        if (FindAnyObjectByType<XrRigTuning>() != null) return;
        if (FindAnyObjectByType<ScenarioRunner>() == null) return;   // не сцена тренажёра
        XROrigin origin = FindAnyObjectByType<XROrigin>();
        if (origin != null) origin.gameObject.AddComponent<XrRigTuning>();
    }

    // Прежние значения префаба — для «как в XRI»
    private sealed class RaySaved
    {
        public InteractionCasterBase caster;
        public bool stabilization;
        public CurveVisualController visual;
        public bool customize;
        public bool[] curve;   // noValidHit, uiHit, uiPress, hover
    }

    private XROrigin origin;
    private readonly List<RaySaved> rays = new List<RaySaved>();
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private GameObject teleportFloor;
    private ShipTypeSwitcher typeSwitcher;

    void Awake()
    {
        Instance = this;
        origin = GetComponent<XROrigin>();
        try
        {
            if (PlayerPrefs.HasKey(StraightPref)) straightRay = PlayerPrefs.GetInt(StraightPref, 1) != 0;
            if (PlayerPrefs.HasKey(SmoothTurnPref)) smoothTurn = PlayerPrefs.GetInt(SmoothTurnPref, 1) != 0;
        }
        catch (System.Exception) { }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (typeSwitcher != null) typeSwitcher.TypeChanged -= OnShipTypeChanged;
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder(MenuGroup, -94);
        menuCommands.Add(BridgeMenu.Register(MenuGroup, () => straightRay ? "Луч: прямой" : "Луч: как в XRI (сглаженный)",
            () => SetStraightRay(!straightRay)).InMenu(BridgeMenu.SettingsMenuId));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, () => smoothTurn ? "Поворот: плавный" : "Поворот: шагами 45°",
            () => SetSmoothTurn(!smoothTurn)).InMenu(BridgeMenu.SettingsMenuId));
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
    }

    IEnumerator Start()
    {
        CollectRays();
        ApplyRay();
        ApplyTurn();
        typeSwitcher = ShipTypeSwitcher.Instance;
        if (typeSwitcher != null) typeSwitcher.TypeChanged += OnShipTypeChanged;
        yield return WaitAndBuildFloor(0.5f);   // игрок встаёт на пол (гравитация XR Rig)
    }

    // ─── К2: луч ───

    private void CollectRays()
    {
        rays.Clear();
        foreach (NearFarInteractor nf in GetComponentsInChildren<NearFarInteractor>(true))
        {
            var saved = new RaySaved { caster = nf.farInteractionCaster as InteractionCasterBase };
            if (saved.caster != null) saved.stabilization = saved.caster.enableStabilization;
            saved.visual = nf.GetComponentInChildren<CurveVisualController>(true);
            if (saved.visual != null)
            {
                saved.customize = saved.visual.customizeLinePropertiesForState;
                saved.curve = new[]
                {
                    saved.visual.noValidHitProperties.smoothlyCurveLine, saved.visual.uiHitProperties.smoothlyCurveLine,
                    saved.visual.uiPressHitProperties.smoothlyCurveLine, saved.visual.hoverHitProperties.smoothlyCurveLine
                };
            }
            rays.Add(saved);
        }
        if (rays.Count == 0) Debug.LogWarning("[Игрок] Near-Far Interactor не найден — луч не настроен.", this);
    }

    public void SetStraightRay(bool on)
    {
        straightRay = on;
        try { PlayerPrefs.SetInt(StraightPref, on ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { }
        ApplyRay();
    }

    private void ApplyRay()
    {
        foreach (RaySaved r in rays)
        {
            if (r.caster != null) r.caster.enableStabilization = straightRay ? false : r.stabilization;
            if (r.visual == null) continue;
            // без настроек по состояниям CurveVisualController гнёт линию всегда (0,5)
            r.visual.customizeLinePropertiesForState = straightRay || r.customize;
            r.visual.noValidHitProperties.smoothlyCurveLine = !straightRay && r.curve[0];
            r.visual.uiHitProperties.smoothlyCurveLine = !straightRay && r.curve[1];
            r.visual.uiPressHitProperties.smoothlyCurveLine = !straightRay && r.curve[2];
            r.visual.hoverHitProperties.smoothlyCurveLine = !straightRay && r.curve[3];
            // selectHitProperties не трогаем: у взятого органа управления луч гнётся к точке хвата
        }
        Debug.Log($"[Игрок] Луч: {(straightRay ? "прямой, без сглаживания" : "как в XRI")} (лучей {rays.Count}).", this);
    }

    // ─── К3: поворот ───

    public void SetSmoothTurn(bool on)
    {
        smoothTurn = on;
        try { PlayerPrefs.SetInt(SmoothTurnPref, on ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { }
        ApplyTurn();
    }

    private void ApplyTurn()
    {
        int n = 0;
        foreach (ControllerInputActionManager m in GetComponentsInChildren<ControllerInputActionManager>(true))
        {
            if (m.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) < 0) continue;   // поворот — правый стик
            m.smoothTurnEnabled = smoothTurn;
            n++;
        }
        if (n == 0) Debug.LogWarning("[Игрок] ControllerInputActionManager правой руки не найден — поворот не настроен.", this);
        else Debug.Log($"[Игрок] Поворот правым стиком: {(smoothTurn ? "плавный" : "шагами 45°")}.", this);
    }

    // ─── К3: телепорт по полу ───

    private void OnShipTypeChanged()
    {
        if (isActiveAndEnabled) StartCoroutine(WaitAndBuildFloor(0.5f));
    }

    private IEnumerator WaitAndBuildFloor(float delay)
    {
        yield return new WaitForSeconds(delay);
        BuildTeleportFloor();
    }

    private void BuildTeleportFloor()
    {
        if (teleportFloor != null) Destroy(teleportFloor);
        teleportFloor = null;

        Transform ship = transform.parent;   // XR Origin — дочерний к Ship
        Vector3 up = ship != null ? ship.up : Vector3.up;
        Collider floor = FindFloor(up);
        if (floor == null)
        {
            Debug.LogWarning($"[Игрок] Телепорт: пол под игроком не найден (луч вниз {floorSearchDistance:F1} м) — зоны телепорта нет.", this);
            return;
        }
        if (floor.GetComponentInParent<IXRInteractable>() != null)
        {
            Debug.LogWarning($"[Игрок] Телепорт: пол «{floor.name}» принадлежит взаимодействию — зону не ставлю.", floor);
            return;
        }

        // Зона — отдельный объект с осью «вверх» по палубе (по ней фильтр нормали); коллайдер — пол сцены
        teleportFloor = new GameObject("TeleportFloor");
        teleportFloor.SetActive(false);
        teleportFloor.transform.SetParent(ship, false);
        var area = teleportFloor.AddComponent<TeleportationArea>();
        area.colliders.Add(floor);
        int teleportLayer = InteractionLayerMask.GetMask("Teleport");
        area.interactionLayers = teleportLayer != 0 ? teleportLayer : (1 << 31);
        area.matchOrientation = MatchOrientation.None;
        area.matchDirectionalInput = false;
        area.filterSelectionByHitNormal = true;
        area.upNormalToleranceDegrees = floorNormalToleranceDeg;
        teleportFloor.SetActive(true);   // Awake с уже заданными коллайдерами

        // Дуга телепорта должна видеть слой пола
        int added = 0;
        foreach (XRRayInteractor ray in GetComponentsInChildren<XRRayInteractor>(true))
        {
            if ((ray.interactionLayers.value & area.interactionLayers.value) == 0) continue;
            if ((ray.raycastMask.value & (1 << floor.gameObject.layer)) != 0) continue;
            ray.raycastMask |= 1 << floor.gameObject.layer;
            added++;
        }

        Bounds b = floor.bounds;
        Debug.Log($"[Игрок] Телепорт: пол «{floor.name}» ({floor.GetType().Name}, слой {LayerMask.LayerToName(floor.gameObject.layer)}), " +
                  $"размер {b.size.x:F1} × {b.size.z:F1} × {b.size.y:F1} м (x × z × высота); нормаль ±{floorNormalToleranceDeg:F0}°" +
                  (added > 0 ? $"; слой пола добавлен лучу телепорта ({added})" : "") + ".", floor);
    }

    // Ближайший не-триггерный коллайдер под игроком, не из XR Origin
    private Collider FindFloor(Vector3 up)
    {
        Vector3 from = transform.position + up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(from, -up, floorSearchDistance + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        Collider best = null;
        float bestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider is CharacterController) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h.collider; }
        }
        return best;
    }
}
