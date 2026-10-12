// ============================================================
// GroundingMonitor.cs — Контроль глубины и посадка на мель в сцене (B1)
//
// Висит на ShipRoot рядом с ShipNavigation. В FixedUpdate (после шага модели,
// порядок −85) строит контур корпуса L × B по курсу и проверяет его по
// акватории (GroundingDetector, Core):
//   • «опасное приближение» — ближе D_пред к мели с глубиной < d + Δ или к суше:
//     сигнализация на мостике (надпись на NavDisplay + двойной тон раз в 3 с);
//   • «посадка» — контур пересёк опасный объект: ShipDynamicsModel.SetGrounded —
//     судно наползает на грунт и останавливается (τ_ост), удар, скрежет,
//     тряска камеры до остановки; сигнал «на мели»;
//   • «снятие» — контур вышел из мели (задним ходом, если сели на переднем;
//     в модели — k_снят от скорости телеграфа) или команда инструктора в меню
//     мостика «Снять с мели»: возврат на чистую позицию ≈30 с назад, стоп.
// События — C# (оценка C2, тактильный отклик D3), UnityEvent (для настройки
// в инспекторе) и с B2 — шина упражнения ExerciseEvents (время — время упражнения).
// Время GroundingEventInfo — симуляционное, от запуска сцены.
// Квитирование сигнализации — команда меню; новое опасное событие снимает квитирование.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Данные события посадки/приближения.</summary>
public struct GroundingEventInfo
{
    public GroundingEvents Kind;
    /// <summary>Время упражнения (симуляционное), с.</summary>
    public double Time;
    public WorldPosition Position;
    public float HeadingDeg;
    public float SpeedKnots;
    public string HazardName;
    public float HazardDepth;
    /// <summary>Расстояние от контура корпуса до опасности, м.</summary>
    public double Distance;
    /// <summary>Снятие с мели командой инструктора (а не своим ходом).</summary>
    public bool ByInstructor;

    public override string ToString() =>
        $"{Kind} t = {Time:F1} с, {Position}, курс {HeadingDeg:F1}°, {SpeedKnots:F1} уз, «{HazardName}» {HazardDepth:F1} м, D = {Distance:F0} м";
}

[DefaultExecutionOrder(-85)]
public class GroundingMonitor : MonoBehaviour
{
    public ShipNavigation navigation;
    public WaterAreaView waterArea;
    [Tooltip("Для центра корпуса (ShipMotion.pivotLocal — мидель, ДП)")]
    public ShipMotion shipMotion;
    public GroundingCameraShake cameraShake;

    [Header("Корпус (L, B, d — из ShipParameters)")]
    [Tooltip("Центр корпуса — по ShipMotion.pivotLocal; иначе — значения ниже")]
    public bool autoHullCenter = true;
    [Tooltip("Смещение середины корпуса от ShipRoot к носу, м")]
    public float hullCenterForward;
    [Tooltip("Смещение середины корпуса от ShipRoot к правому борту, м")]
    public float hullCenterStarboard;

    [Header("Пороги")]
    [Tooltip("Запас под килем Δ, м: мель опасна, если глубина < d + Δ")]
    public float underKeelClearance = 0.5f;
    [Tooltip("Дистанция опасного приближения D_пред, м (1 кбт = 185,2 м)")]
    public float warningDistance = 185.2f;
    [Range(0f, 0.5f)] public float warningHysteresis = 0.1f;

    [Header("Посадка")]
    [Tooltip("Постоянная остановки на грунте τ_ост, с (путь наползания ≈ u₀·τ)")]
    public float stopTimeConstant = 1.0f;
    [Tooltip("Доля скорости при съёме с мели k_снят")]
    [Range(0.05f, 1f)] public float refloatSpeedFactor = 0.3f;
    [Tooltip("Снятие инструктором: на сколько секунд назад вернуть судно")]
    public float refloatLookback = 30f;

    [Header("Сигнализация на мостике")]
    public bool bridgeAlarm = true;
    [Range(0f, 1f)] public float alarmVolume = 0.5f;
    public float warningBeepInterval = 3f;
    public float groundedBeepInterval = 2f;

    [Header("Звук посадки (пусто — процедурные заглушки)")]
    public AudioClip impactClip;
    public AudioClip scrapeClip;
    [Range(0f, 1f)] public float impactVolume = 1f;
    [Range(0f, 1f)] public float scrapeVolume = 0.8f;

    [Header("События")]
    public UnityEvent onWarningStarted;
    public UnityEvent onWarningCleared;
    public UnityEvent onGrounded;
    public UnityEvent onRefloated;

    public event Action<GroundingEventInfo> WarningStarted, WarningCleared, Grounded, Refloated;

    // ── Состояние для индикации ──
    public GroundingState State => detector.State;
    public HazardInfo Nearest => detector.Nearest;
    public HazardInfo GroundedOn => detector.GroundedOn;
    /// <summary>Глубина по карте под серединой корпуса, м.</summary>
    public float DepthUnderShip { get; private set; } = float.NaN;
    public float Draft => navigation != null && navigation.parameters != null ? navigation.parameters.draft : 3f;
    public float DangerDepth => Draft + underKeelClearance;
    /// <summary>Сигнализация включена (есть опасность) и не квитирована.</summary>
    public bool AlarmSounding => bridgeAlarm && State != GroundingState.Clear && !AlarmAcknowledged;
    public bool AlarmAcknowledged { get; private set; }
    /// <summary>Уровень скрежета 0…1 (для звука и тряски).</summary>
    public float ScrapeLevel { get; private set; }
    public double SimTime => simTime;

    private readonly GroundingDetector detector = new GroundingDetector();
    private struct Snapshot { public double t, east, north, psi; }
    private readonly List<Snapshot> history = new List<Snapshot>();
    private double simTime, nextSnapshot, groundedTime;
    private AudioSource alarmSource, impactSource, scrapeSource;
    private AudioClip warningClip, groundedClip;
    private float nextBeep;
    private BridgeMenuCommand refloatCommand, ackCommand;

    void OnEnable()
    {
        refloatCommand = BridgeMenu.Register("Судно", "Снять с мели", RefloatByInstructor,
            () => State == GroundingState.Grounded, instructorOnly: true);
        // D1: «Сигнализация» — на главной странице меню, команда видна, только пока звучит сигнал
        BridgeMenu.SetGroupOrder("Сигнализация", -90);
        BridgeMenu.SetGroupExpanded("Сигнализация", true);
        ackCommand = BridgeMenu.Register("Сигнализация", "Квитировать сигнализацию", Acknowledge,
            () => AlarmSounding, hideWhenUnavailable: true);
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(refloatCommand);
        BridgeMenu.Unregister(ackCommand);
        refloatCommand = ackCommand = null;
    }

    void Start()
    {
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (shipMotion == null) shipMotion = GetComponentInChildren<ShipMotion>();
        if (autoHullCenter) ComputeHullCenter();

        alarmSource = MakeSource("ShoalAlarm", 0f);
        impactSource = MakeSource("GroundingImpact", 0f);
        scrapeSource = MakeSource("GroundingScrape", 0f);
        warningClip = GroundingSounds.WarningBeep();
        groundedClip = GroundingSounds.GroundedAlarm();
        if (impactClip == null) impactClip = GroundingSounds.Impact();
        if (scrapeClip == null) scrapeClip = GroundingSounds.Scrape();
        scrapeSource.clip = scrapeClip;
        scrapeSource.loop = true;
    }

    private AudioSource MakeSource(string name, float spatial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = spatial;
        return s;
    }

    /// <summary>Центр корпуса в системе ShipRoot по ShipMotion.pivotLocal (мидель, ДП).</summary>
    [ContextMenu("Пересчитать центр корпуса")]
    public void ComputeHullCenter()
    {
        if (shipMotion == null) return;
        Transform ship = shipMotion.transform;
        Vector3 bowAxis = navigation != null && navigation.worldOrigin != null ? navigation.worldOrigin.bowLocalAxis : Vector3.back;
        Quaternion rot = ship.parent == transform ? ship.localRotation : Quaternion.Inverse(transform.rotation) * ship.rotation;
        Vector3 pos = ship.parent == transform ? ship.localPosition : transform.InverseTransformPoint(ship.position);
        Vector3 bow = rot * bowAxis; bow.y = 0f; bow.Normalize();
        Vector3 starboard = Vector3.Cross(Vector3.up, bow);
        Vector3 c = pos + rot * shipMotion.pivotLocal;
        hullCenterForward = Vector3.Dot(c, bow);
        hullCenterStarboard = Vector3.Dot(c, starboard);
    }

    /// <summary>Контур корпуса на текущем шаге модели.</summary>
    public bool TryGetFootprint(out HullFootprint hull)
    {
        hull = default;
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        ShipParameters p = navigation != null ? navigation.parameters : null;
        if (m == null || p == null) return false;
        hull = HullFootprint.FromShip(m.East, m.North, m.Psi, hullCenterForward, hullCenterStarboard,
                                      Mathf.Max(1f, p.length), Mathf.Max(0.5f, p.beam));
        return true;
    }

    void FixedUpdate()
    {
        simTime += Time.fixedDeltaTime;
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        WaterArea area = waterArea != null ? waterArea.area : null;
        if (m == null || area == null || !TryGetFootprint(out HullFootprint hull)) return;

        detector.Area = area;
        detector.Draft = Draft;
        detector.UnderKeelClearance = underKeelClearance;
        detector.WarningDistance = warningDistance;
        detector.Hysteresis = warningHysteresis;
        m.GroundingStopTime = stopTimeConstant;
        m.RefloatSpeedFactor = refloatSpeedFactor;

        double speedBefore = m.U;
        GroundingEvents ev = detector.Update(hull);
        DepthUnderShip = area.DepthAt(hull.Center);

        if ((ev & GroundingEvents.WarningStarted) != 0)
        {
            AlarmAcknowledged = false;
            nextBeep = 0f;
            Raise(GroundingEvents.WarningStarted, m, detector.Nearest, false);
        }
        if ((ev & GroundingEvents.Grounded) != 0)
        {
            m.SetGrounded(true);
            groundedTime = simTime;
            AlarmAcknowledged = false;
            nextBeep = 0f;
            float strength = Mathf.Clamp((float)(Math.Abs(speedBefore) / Math.Max(0.5, navigation.parameters.ServiceSpeedMs)), 0.25f, 1f);
            if (impactSource != null && impactClip != null) impactSource.PlayOneShot(impactClip, impactVolume * Mathf.Lerp(0.5f, 1f, strength));
            if (cameraShake != null) cameraShake.Impact(strength);
            Raise(GroundingEvents.Grounded, m, detector.GroundedOn, false);
        }
        if ((ev & GroundingEvents.Refloated) != 0)
        {
            m.SetGrounded(false);
            Raise(GroundingEvents.Refloated, m, detector.Nearest, false);
        }
        if ((ev & GroundingEvents.WarningCleared) != 0)
            Raise(GroundingEvents.WarningCleared, m, detector.Nearest, false);

        // Чистые позиции — для снятия инструктором
        if (State != GroundingState.Grounded && simTime >= nextSnapshot)
        {
            nextSnapshot = simTime + 1.0;
            history.Add(new Snapshot { t = simTime, east = m.East, north = m.North, psi = m.Psi });
            int cap = Mathf.CeilToInt(refloatLookback) + 30;
            if (history.Count > cap) history.RemoveRange(0, history.Count - cap);
        }
    }

    void Update()
    {
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;

        // Скрежет: судно ползёт по грунту или машина работает «в мель»
        float level = 0f;
        if (m != null && State == GroundingState.Grounded)
        {
            float v = (float)(Math.Abs(m.U) / Math.Max(0.5, navigation.parameters.ServiceSpeedMs));
            float engine = m.SpeedFactor * m.GroundedDirection > 0.0 ? (float)Math.Abs(m.SpeedFactor) * 0.35f : 0f;
            level = Mathf.Clamp01(Mathf.Max(Mathf.Sqrt(v), engine));
        }
        ScrapeLevel = level;
        if (cameraShake != null) cameraShake.ScrapeLevel = level;
        if (scrapeSource != null)
        {
            if (level > 0.01f)
            {
                scrapeSource.volume = scrapeVolume * level;
                scrapeSource.pitch = 0.8f + 0.4f * level;
                if (!scrapeSource.isPlaying) scrapeSource.Play();
            }
            else if (scrapeSource.isPlaying) scrapeSource.Stop();
        }

        // Сигнализация
        if (alarmSource != null && AlarmSounding && Time.time >= nextBeep)
        {
            bool grounded = State == GroundingState.Grounded;
            alarmSource.PlayOneShot(grounded ? groundedClip : warningClip, alarmVolume);
            nextBeep = Time.time + (grounded ? groundedBeepInterval : warningBeepInterval);
        }
    }

    [ContextMenu("Квитировать сигнализацию")]
    public void Acknowledge()
    {
        AlarmAcknowledged = true;
        if (alarmSource != null) alarmSource.Stop();
    }

    /// <summary>Снять с мели (инструктор): на чистую позицию ≈ refloatLookback с назад, стоп.</summary>
    [ContextMenu("Снять с мели (инструктор)")]
    public void RefloatByInstructor()
    {
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (m == null || State != GroundingState.Grounded) return;

        Snapshot target = history.Count > 0 ? history[0] : new Snapshot { east = m.East, north = m.North, psi = m.Psi };
        double limit = groundedTime - refloatLookback;
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i].t <= limit) { target = history[i]; break; }

        HazardInfo on = detector.GroundedOn;
        navigation.TeleportTo(target.east, target.north, target.psi * Mathf.Rad2Deg);
        if (navigation.telegraph != null) navigation.telegraph.SetStop();
        detector.Reset();
        if (scrapeSource != null) scrapeSource.Stop();
        Raise(GroundingEvents.Refloated, m, on, true);
    }

    /// <summary>Сброс состояния после перестановки судна (отладка, начальные условия сценария B2).</summary>
    public void ResetState()
    {
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (m != null && m.Grounded) m.SetGrounded(false);
        detector.Reset();
        history.Clear();
        AlarmAcknowledged = false;
        if (scrapeSource != null) scrapeSource.Stop();
        if (alarmSource != null) alarmSource.Stop();
    }

    private void Raise(GroundingEvents kind, ShipDynamicsModel m, HazardInfo hazard, bool byInstructor)
    {
        var info = new GroundingEventInfo
        {
            Kind = kind, Time = simTime, Position = new WorldPosition(m.East, m.North),
            HeadingDeg = (float)(m.Psi * Mathf.Rad2Deg), SpeedKnots = (float)(m.U / ShipParameters.KnotsToMs),
            HazardName = hazard.Valid ? hazard.Name : "", HazardDepth = hazard.Valid ? hazard.Depth : float.NaN,
            Distance = hazard.Distance, ByInstructor = byInstructor
        };
        // B2: то же событие — в шину упражнения (запись C1, оценка C2, тактильный отклик D3)
        ExerciseEventType busType;
        switch (kind)
        {
            case GroundingEvents.WarningStarted: busType = ExerciseEventType.ShoalWarningStarted; break;
            case GroundingEvents.WarningCleared: busType = ExerciseEventType.ShoalWarningCleared; break;
            case GroundingEvents.Grounded: busType = ExerciseEventType.Grounded; break;
            default: busType = ExerciseEventType.Refloated; break;
        }
        ExerciseEvents.Publish(new ExerciseEvent(busType)
        {
            Position = info.Position, HeadingDeg = info.HeadingDeg, SpeedKnots = info.SpeedKnots,
            Index = hazard.Valid ? hazard.Index : -1, Subject = info.HazardName,
            Value = info.Distance, Value2 = info.HazardDepth, ByInstructor = byInstructor
        });

        string tag = "[Мель] ";
        switch (kind)
        {
            case GroundingEvents.WarningStarted:
                Debug.Log(tag + "Опасное приближение: " + info);
                WarningStarted?.Invoke(info); onWarningStarted?.Invoke(); break;
            case GroundingEvents.WarningCleared:
                Debug.Log(tag + "Опасность миновала: " + info);
                WarningCleared?.Invoke(info); onWarningCleared?.Invoke(); break;
            case GroundingEvents.Grounded:
                Debug.LogWarning(tag + "ПОСАДКА НА МЕЛЬ: " + info);
                Grounded?.Invoke(info); onGrounded?.Invoke(); break;
            case GroundingEvents.Refloated:
                Debug.Log(tag + (byInstructor ? "Снято с мели инструктором: " : "Снялось с мели своим ходом: ") + info);
                Refloated?.Invoke(info); onRefloated?.Invoke(); break;
        }
    }
}
