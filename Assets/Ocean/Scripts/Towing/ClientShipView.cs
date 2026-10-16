// ============================================================
// ClientShipView.cs — Судно-клиент упражнения на буксире в сцене (запрос 19.4б-2)
//
// Создаётся TugWorkController при загрузке сценария с заданием буксира (объект «Client» в корне сцены, не дочерний
// к своему судну), удаляется вместе с упражнением. По образцу TugView (19.3б):
//   • модель — TrafficMeshes.Build(профиль сухогруза по L × B × d клиента, огни): Model45m — 45 × 10 м;
//     материалы — как у судов B3 (TrafficController), без него — простой Lit;
//   • позиция — WorldOrigin.WorldToScene, интерполяция между шагами физики (α как у ShipNavigation);
//     качка — SeakeepingModel, как у других судов; огни — NavigationLights.PowerDrivenUnderway;
//   • концы клиента — второй MooringLinesView (источник — швартовка клиента): провис, цвет по натяжению —
//     только «обучение» и «инструктор», как у своего судна.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-70)]
public class ClientShipView : MonoBehaviour
{
    public float farWavesDistance = 6500f;

    private TugWorkController controller;
    private HullProfile profile;
    private List<NavLight> lights;
    private readonly Vector3[] lightLocal = new Vector3[8];
    private SeakeepingModel seakeeping;
    private double heavePrev, rollPrev, pitchPrev;
    private bool seakeepingActive;
    private Mesh mesh;
    private Material ownMaterial;
    private MooringLinesView linesView;

    /// <summary>Курс на время отображения, °.</summary>
    public double DisplayHeadingDeg { get; private set; }

    private ShipDynamicsModel Model => controller != null && controller.Work != null ? controller.Work.Client.Model : null;

    public void Init(TugWorkController c)
    {
        controller = c;
        ShipParameters p = c.Work.Client.Parameters;
        profile = HullProfile.For(TrafficVesselType.Cargo, p.length, p.beam, p.draft);
        lights = NavigationLights.PowerDrivenUnderway(profile);
        for (int i = 0; i < lights.Count && i < lightLocal.Length; i++)
            lightLocal[i] = new Vector3((float)lights[i].Starboard, (float)lights[i].Height, (float)lights[i].Forward);
        mesh = TrafficMeshes.Build(profile, lights);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = Materials();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        seakeeping = new SeakeepingModel(p) { SamplesAlong = 5, SamplesAcross = 3 };

        // Концы клиента — отдельный объект в корне (линии — в мировых координатах)
        var lv = new GameObject("ClientMooringLines");
        lv.transform.SetParent(transform, false);
        linesView = lv.AddComponent<MooringLinesView>();
        linesView.sourceSystem = c.Work.Client.Mooring;
        linesView.sourceModel = c.Work.Client.Model;
        Place();
    }

    // Материалы сухогруза (TrafficController, Setup B3); без него — простой Lit
    private Material[] Materials()
    {
        TrafficController tc = TrafficController.Instance;
        if (tc != null && tc.smallHull != null) return tc.MaterialsFor(TrafficVesselType.Cargo);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        ownMaterial = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "ClientFallback", color = new Color(0.2f, 0.25f, 0.32f) };
        var m = new Material[TrafficMeshes.SubmeshCount];
        for (int i = 0; i < m.Length; i++) m[i] = ownMaterial;
        return m;
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (ownMaterial != null) Destroy(ownMaterial);
    }

    void FixedUpdate()
    {
        ShipDynamicsModel m = Model;
        if (m == null) return;
        OceanWaves water = OceanWaves.Instance;
        WorldOrigin origin = WorldOrigin.Instance;
        heavePrev = seakeeping.Heave; rollPrev = seakeeping.Roll; pitchPrev = seakeeping.Pitch;
        if (water == null || origin == null) return;
        double de = m.East - origin.ShipPosition.East, dn = m.North - origin.ShipPosition.North;
        if (de * de + dn * dn < (double)farWavesDistance * farWavesDistance)
        {
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
            double k = System.Math.Exp(-Time.fixedDeltaTime / 3.0);
            seakeeping.Heave *= k; seakeeping.Roll *= k; seakeeping.Pitch *= k;
            seakeepingActive = false;
        }
    }

    void Update()
    {
        if (Model == null) return;
        Place();
        SubmitLights();
    }

    private void Place()
    {
        WorldOrigin origin = WorldOrigin.Instance;
        ShipDynamicsModel m = Model;
        if (origin == null || m == null) return;
        float dt = Time.fixedDeltaTime;
        double alpha = Application.isPlaying && dt > 0f ? System.Math.Min(1.0, System.Math.Max(0.0, (Time.time - Time.fixedTime) / dt)) : 1.0;
        double e = controller.PrevEast + (m.East - controller.PrevEast) * alpha;
        double n = controller.PrevNorth + (m.North - controller.PrevNorth) * alpha;
        double psi = controller.PrevPsi + System.Math.IEEERemainder(m.Psi - controller.PrevPsi, 2.0 * System.Math.PI) * alpha;
        double heave = heavePrev + (seakeeping.Heave - heavePrev) * alpha;
        double roll = rollPrev + (seakeeping.Roll - rollPrev) * alpha;
        double pitch = pitchPrev + (seakeeping.Pitch - pitchPrev) * alpha;

        Vector3 p = origin.WorldToScene(new WorldPosition(e, n));
        OceanWaves water = OceanWaves.Instance;
        p.y = (water != null ? water.MeanLevel : 0f) + (float)heave;
        Quaternion q = Quaternion.Euler(0f, (float)(psi * Mathf.Rad2Deg), 0f)
                     * Quaternion.AngleAxis(-(float)(pitch * Mathf.Rad2Deg), Vector3.right)
                     * Quaternion.AngleAxis(-(float)(roll * Mathf.Rad2Deg), Vector3.forward);
        transform.SetPositionAndRotation(p, q);
        DisplayHeadingDeg = psi * Mathf.Rad2Deg;
    }

    private void SubmitLights()
    {
        NavLightField field = NavLightField.Instance;
        Camera cam = Camera.main;
        if (field == null || cam == null || !field.LightsOn) return;
        Vector3 eye = cam.transform.position;
        for (int i = 0; i < lights.Count && i < lightLocal.Length; i++)
        {
            NavLight l = lights[i];
            Vector3 lp = transform.TransformPoint(lightLocal[i]);
            Vector3 toEye = eye - lp;
            double q = CpaCalculator.RelativeBearing(CpaCalculator.Bearing(toEye.x, toEye.z), DisplayHeadingDeg);
            float level = field.Level(l.IntensityCd, toEye.magnitude, NavigationLights.SectorFactor(l, q));
            if (level > 0f) field.Submit(lp, l.Color, level);
        }
    }
}
