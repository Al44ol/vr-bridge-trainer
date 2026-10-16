// ============================================================
// TugView.cs — Буксир в сцене: модель, качка, огни, буксирный трос (запрос 19.3б)
//
// Создаётся TowingController при вызове буксира (объект «Tug» в корне сцены, не дочерний к судну).
//   • Модель — TrafficMeshes.Build(профиль малого судна 22 × 8 м, огни, tug: true): широкий корпус с тупым
//     носом, кранец по носу и бортам, рубка, буксирная лебёдка; материалы — как у малого судна (TrafficController).
//   • Позиция — WorldOrigin.WorldToScene, интерполяция между шагами физики с той же α, что у ShipNavigation;
//     качка — SeakeepingModel, как у других судов (TrafficShipView).
//   • Огни — NavigationLights.PowerDrivenUnderway (как у судна на ходу; огни буксировки по правилу 24 — не моделируются).
//   • Трос — LineRenderer от клюза судна к гаку на носу буксира с провисом по параболе (MooringLineShape.Sag);
//     цвет по натяжению — подсказка «обучения» и «инструктора» (как концы, MooringLinesView), в «экзамене» — цвет троса.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-70)]
public class TugView : MonoBehaviour
{
    public float farWavesDistance = 6500f;
    public float lineWidth = 0.08f;
    public Color ropeColor = new Color(0.86f, 0.78f, 0.58f);
    public Color slackColor = new Color(0.25f, 0.85f, 0.35f);
    public Color loadedColor = new Color(1f, 0.82f, 0.2f);
    public Color overloadColor = new Color(0.95f, 0.2f, 0.15f);

    private TowingController controller;
    private HullProfile profile;
    private List<NavLight> lights;
    private readonly Vector3[] lightLocal = new Vector3[8];
    private SeakeepingModel seakeeping;
    private double heavePrev, rollPrev, pitchPrev;
    private bool seakeepingActive;
    private Mesh mesh;
    private LineRenderer line;
    private Material lineMaterial, ownMaterial;
    private Color lineShown = Color.clear;
    private const int LinePoints = 17;
    private readonly Vector3[] buffer = new Vector3[LinePoints];
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Курс на время отображения, °.</summary>
    public double DisplayHeadingDeg { get; private set; }

    public void Init(TowingController c)
    {
        controller = c;
        TugVessel tug = c.Towing.Tug;
        ShipParameters p = tug.Model.Parameters;
        profile = HullProfile.For(TrafficVesselType.Small, p.length, p.beam, p.draft);
        lights = NavigationLights.PowerDrivenUnderway(profile);
        for (int i = 0; i < lights.Count && i < lightLocal.Length; i++)
            lightLocal[i] = new Vector3((float)lights[i].Starboard, (float)lights[i].Height, (float)lights[i].Forward);
        mesh = TrafficMeshes.Build(profile, lights, tug: true);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = Materials();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        seakeeping = new SeakeepingModel(p) { SamplesAlong = 5, SamplesAcross = 3 };

        var lg = new GameObject("TowLine");
        lg.transform.SetParent(transform, false);
        line = lg.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = LinePoints;
        line.widthMultiplier = lineWidth;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        lineMaterial = new Material(unlit != null ? unlit : Shader.Find("Sprites/Default")) { name = "TowLine" };
        line.sharedMaterial = lineMaterial;
        line.enabled = false;
        Place();
    }

    // Материалы малого судна (TrafficController, Setup B3); без него — простой Lit
    private Material[] Materials()
    {
        TrafficController tc = TrafficController.Instance;
        if (tc != null && tc.smallHull != null) return tc.MaterialsFor(TrafficVesselType.Small);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        ownMaterial = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "TugFallback", color = new Color(0.55f, 0.12f, 0.1f) };
        var m = new Material[TrafficMeshes.SubmeshCount];
        for (int i = 0; i < m.Length; i++) m[i] = ownMaterial;
        return m;
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (lineMaterial != null) Destroy(lineMaterial);
        if (ownMaterial != null) Destroy(ownMaterial);
    }

    void FixedUpdate()
    {
        TowingSystem ts = controller != null ? controller.Towing : null;
        if (ts == null) return;
        OceanWaves water = OceanWaves.Instance;
        WorldOrigin origin = WorldOrigin.Instance;
        heavePrev = seakeeping.Heave; rollPrev = seakeeping.Roll; pitchPrev = seakeeping.Pitch;
        if (water == null || origin == null) return;
        ShipDynamicsModel m = ts.Tug.Model;
        double de = m.East - origin.ShipPosition.East, dn = m.North - origin.ShipPosition.North;
        if (de * de + dn * dn < (double)farWavesDistance * farWavesDistance)
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
            double k = System.Math.Exp(-Time.fixedDeltaTime / 3.0);
            seakeeping.Heave *= k; seakeeping.Roll *= k; seakeeping.Pitch *= k;
            seakeepingActive = false;
        }
    }

    void Update()
    {
        if (controller == null || controller.Towing == null) return;
        Place();
        SubmitLights();
    }

    void LateUpdate()
    {
        if (controller == null || controller.Towing == null) return;
        DrawLine();
    }

    private void Place()
    {
        WorldOrigin origin = WorldOrigin.Instance;
        if (origin == null || controller.Towing == null) return;
        ShipDynamicsModel m = controller.Towing.Tug.Model;
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
        // Крен и дифферент — как у других судов (TrafficShipView)
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

    // Трос: клюз судна → гак на носу буксира, провис по параболе
    private void DrawLine()
    {
        TowingSystem ts = controller.Towing;
        WorldOrigin origin = WorldOrigin.Instance;
        ShipNavigation nav = controller.navigation;
        if (ts == null || origin == null || nav == null || nav.Model == null || !ts.Line.Fast) { line.enabled = false; return; }
        float mean = OceanWaves.Instance != null ? OceanWaves.Instance.MeanLevel : 0f;
        Vector3 a = origin.WorldToScene(ts.TowPoint(nav.Model));
        a.y = mean + controller.fairleadHeightM;
        Vector3 b = transform.TransformPoint(new Vector3(0f, (float)profile.Freeboard + 1f, (float)ts.Tug.Spec.BowX - 0.5f));
        float chord = Vector3.Distance(a, b);
        float sag = (float)MooringLineShape.Sag(chord, ts.Line.RestLength, ts.Line.Tension, ts.Line.Mbl);
        for (int k = 0; k < LinePoints; k++)
        {
            float u = k / (float)(LinePoints - 1);
            buffer[k] = Vector3.Lerp(a, b, u) + Vector3.down * (4f * sag * u * (1f - u));
        }
        line.SetPositions(buffer);
        line.widthMultiplier = lineWidth;
        double share = ts.Line.Mbl > 0 ? ts.Line.Tension / ts.Line.Mbl : 0.0;
        Color c = !TrainerModes.HintsAllowedNow ? ropeColor
            : share >= MooringLine.OverloadFraction ? overloadColor
            : share >= 0.3 ? loadedColor : slackColor;
        if (c != lineShown)
        {
            lineMaterial.SetColor(BaseColorId, c);
            lineMaterial.color = c;
            lineShown = c;
        }
        line.enabled = true;
    }
}
