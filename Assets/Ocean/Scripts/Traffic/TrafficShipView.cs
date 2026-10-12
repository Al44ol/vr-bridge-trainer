// ============================================================
// TrafficShipView.cs — Другое судно в сцене: модель, качка, огни (B3)
//
// Создаётся TrafficController для каждой цели (TrafficVessel, Core).
//   • Позиция: WorldOrigin.WorldToScene(p), p — позиция модели, интерполированная
//     между шагами физики с той же α, что у ShipNavigation (иначе цель дёргалась бы
//     относительно своего судна); курс — по кратчайшей дуге.
//   • Качка: SeakeepingModel (Core) с параметрами цели — тот же сэмплер волн,
//     что OceanWaves.SampleHeight(WorldPosition), и та же модель, что у своего
//     судна (ShipMotion): сетка точек по корпусу, плоскость МНК, звенья 2-го порядка.
//     Шаг — в FixedUpdate (порядок −70, после OceanWaves −80, как ShipMotion),
//     отображение интерполируется. Дальше farWavesDistance волны погашены
//     (затухание 1,5…6 км) — качка не считается и плавно гаснет.
//   • Огни: NavigationLights.PowerDrivenUnderway (правила 21–23, Приложение I).
//     Для каждого огня — курсовой угол наблюдателя (камеры) от носа цели →
//     доля силы света по сектору → освещённость по закону Алларда → уровень
//     яркости (NavLightField). Огонь виден, только если над ним нет корпуса
//     своего судна (ZTest шейдера).
//   • C4: туманный сигнал — AudioSource на мачте (у топового огня), 3D-звук без затухания Unity
//     (громкость по дистанции задаёт FogSignals.Loudness), без эффекта Доплера, фильтр нижних частот
//     по дистанции; клип — HornSynth.Signal (частота по длине судна, Приложение III).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-70)]
public class TrafficShipView : MonoBehaviour
{
    public TrafficVessel Vessel { get; private set; }
    public HullProfile Profile { get; private set; }
    public List<NavLight> Lights { get; private set; }
    public float FarWavesDistance = 6500f;
    public int SamplesAlong = 5, SamplesAcross = 3;

    private SeakeepingModel seakeeping;
    private double heavePrev, rollPrev, pitchPrev;
    private bool seakeepingActive;
    private Mesh mesh;
    private readonly Vector3[] lightLocal = new Vector3[8];

    /// <summary>Создать модель судна (вызывает TrafficController).</summary>
    public void Init(TrafficVessel vessel, Material[] materials)
    {
        Vessel = vessel;
        ShipParameters p = vessel.Model.Parameters;
        Profile = HullProfile.For(vessel.Type, p.length, p.beam, p.draft);
        Lights = NavigationLights.PowerDrivenUnderway(Profile);
        for (int i = 0; i < Lights.Count && i < lightLocal.Length; i++)
            lightLocal[i] = new Vector3((float)Lights[i].Starboard, (float)Lights[i].Height, (float)Lights[i].Forward);

        mesh = TrafficMeshes.Build(Profile, Lights);
        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = materials;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        seakeeping = new SeakeepingModel(p) { SamplesAlong = SamplesAlong, SamplesAcross = SamplesAcross };
        seakeepingActive = false;
        Place();
    }

    private AudioSource whistle;
    private AudioLowPassFilter whistleFilter;

    /// <summary>Туманный сигнал с мачты (C4): громкость 0…1 и частота среза фильтра — по дистанции.</summary>
    public void PlaySignal(SoundSignalKind kind, float frequencyHz, float loudness, float lowPassHz)
    {
        if (loudness <= 0f) return;
        if (whistle == null)
        {
            var go = new GameObject("FogWhistle");
            go.transform.SetParent(transform, false);
            float h = 10f, fwd = 0f;
            foreach (NavLight l in Lights)
                if (l.Kind == NavLightKind.MastheadForward) { h = (float)l.Height; fwd = (float)l.Forward; break; }
            go.transform.localPosition = new Vector3(0f, h, fwd);
            whistle = go.AddComponent<AudioSource>();
            whistle.playOnAwake = false;
            whistle.spatialBlend = 1f;
            whistle.dopplerLevel = 0f;
            whistle.spread = 0f;
            whistle.rolloffMode = AudioRolloffMode.Custom;
            whistle.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            whistle.maxDistance = 20000f;
            whistleFilter = go.AddComponent<AudioLowPassFilter>();
        }
        whistleFilter.cutoffFrequency = lowPassHz;
        whistle.clip = HornSynth.Signal(frequencyHz, kind);
        whistle.volume = Mathf.Clamp01(loudness);
        whistle.Play();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    void FixedUpdate()
    {
        if (Vessel == null) return;
        OceanWaves water = OceanWaves.Instance;
        WorldOrigin origin = WorldOrigin.Instance;
        heavePrev = seakeeping.Heave; rollPrev = seakeeping.Roll; pitchPrev = seakeeping.Pitch;
        if (water == null || origin == null) return;

        ShipDynamicsModel m = Vessel.Model;
        double de = m.East - origin.ShipPosition.East, dn = m.North - origin.ShipPosition.North;
        bool near = de * de + dn * dn < (double)FarWavesDistance * FarWavesDistance;
        if (near)
        {
            seakeeping.Parameters = m.Parameters;
            if (!seakeepingActive)
            {
                seakeeping.Reset(water.Field, m.East, m.North, m.Psi);
                heavePrev = seakeeping.Heave; rollPrev = seakeeping.Roll; pitchPrev = seakeeping.Pitch;
                seakeepingActive = true;
            }
            else seakeeping.Step(water.Field, m.East, m.North, m.Psi, Time.fixedDeltaTime);
        }
        else
        {
            // Волны вдали погашены: качка плавно уходит к нулю, сэмплер не вызывается
            double k = System.Math.Exp(-Time.fixedDeltaTime / 3.0);
            seakeeping.Heave *= k; seakeeping.Roll *= k; seakeeping.Pitch *= k;
            seakeepingActive = false;
        }
    }

    void Update()
    {
        if (Vessel == null) return;
        Place();
        SubmitLights();
    }

    // Позиция и ориентация в сцене на время отображения
    private void Place()
    {
        WorldOrigin origin = WorldOrigin.Instance;
        if (origin == null) return;
        ShipDynamicsModel m = Vessel.Model;
        float dt = Time.fixedDeltaTime;
        double alpha = Application.isPlaying && dt > 0f ? System.Math.Min(1.0, System.Math.Max(0.0, (Time.time - Time.fixedTime) / dt)) : 1.0;
        double e = Vessel.PrevEast + (m.East - Vessel.PrevEast) * alpha;
        double n = Vessel.PrevNorth + (m.North - Vessel.PrevNorth) * alpha;
        double dPsi = System.Math.IEEERemainder(m.Psi - Vessel.PrevPsi, 2.0 * System.Math.PI);
        double psi = Vessel.PrevPsi + dPsi * alpha;

        double heave = heavePrev + (seakeeping.Heave - heavePrev) * alpha;
        double roll = rollPrev + (seakeeping.Roll - rollPrev) * alpha;
        double pitch = pitchPrev + (seakeeping.Pitch - pitchPrev) * alpha;

        Vector3 p = origin.WorldToScene(new WorldPosition(e, n));
        OceanWaves water = OceanWaves.Instance;
        p.y = (water != null ? water.MeanLevel : 0f) + (float)heave;
        // Крен на ПБ и дифферент «нос вверх» — как в ShipMotion (знаки — там же)
        Quaternion q = Quaternion.Euler(0f, (float)(psi * Mathf.Rad2Deg), 0f)
                     * Quaternion.AngleAxis(-(float)(pitch * Mathf.Rad2Deg), Vector3.right)
                     * Quaternion.AngleAxis(-(float)(roll * Mathf.Rad2Deg), Vector3.forward);
        transform.SetPositionAndRotation(p, q);
        DisplayHeadingDeg = psi * Mathf.Rad2Deg;
    }

    /// <summary>Курс на время отображения, °.</summary>
    public double DisplayHeadingDeg { get; private set; }

    private void SubmitLights()
    {
        NavLightField field = NavLightField.Instance;
        Camera cam = Camera.main;
        if (field == null || cam == null || !field.LightsOn) return;
        Vector3 eye = cam.transform.position;
        for (int i = 0; i < Lights.Count && i < lightLocal.Length; i++)
        {
            NavLight l = Lights[i];
            Vector3 lp = transform.TransformPoint(lightLocal[i]);
            Vector3 toEye = eye - lp;
            double bearing = CpaCalculator.Bearing(toEye.x, toEye.z);
            double q = CpaCalculator.RelativeBearing(bearing, DisplayHeadingDeg);
            double sector = NavigationLights.SectorFactor(l, q);
            float level = field.Level(l.IntensityCd, toEye.magnitude, sector);
            if (level > 0f) field.Submit(lp, l.Color, level);
        }
    }
}
