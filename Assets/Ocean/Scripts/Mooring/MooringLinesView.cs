// ============================================================
// MooringLinesView.cs — Швартовные концы в сцене (запрос 19.2б-2)
//
// Поданный конец — линия (LineRenderer, 17 точек) от клюза судна к кнехту причала с провисом
// по параболе: точка u ∈ [0; 1] на хорде опущена на 4·s·u·(1 − u), s — MooringLineShape.Sag
// (слабый конец — по длине без натяжения L₀, обтянутый — почти прямой). На силу провис не влияет.
// Цвет по натяжению — подсказка режимов «обучение» и «инструктор» (TrainerModes.HintsAllowed):
// T < 0,3·MBL — зелёный, < 0,5 — жёлтый, ≥ 0,5 (перегрузка) — красный; в «экзамене» — цвет троса.
// Клюз — по положению модели (MooringSystem.Fairlead) на высоте FairleadHeightM над средним уровнем,
// кнехт — по причалу; качку судна линии не повторяют (швартовка — в штиль, ThesisNotes 19.19).
// Добавляется сам на объект MooringController (Setup не нужен).
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-50)]   // после WaterAreaView (−60): причал уже на месте
public class MooringLinesView : MonoBehaviour
{
    [Tooltip("Толщина конца, м")]
    public float width = 0.05f;
    [Tooltip("Кнехт: точка крепления над кромкой, м")]
    public float bollardTopM = 0.45f;
    public Color ropeColor = new Color(0.86f, 0.78f, 0.58f);
    public Color slackColor = new Color(0.25f, 0.85f, 0.35f);
    public Color loadedColor = new Color(1f, 0.82f, 0.2f);
    public Color overloadColor = new Color(0.95f, 0.2f, 0.15f);

    private const int Points = 17;
    private MooringController mc;
    private LineRenderer[] lines = new LineRenderer[0];
    private Material[] materials = new Material[0];
    private Color[] shown = new Color[0];
    private readonly Vector3[] buffer = new Vector3[Points];
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake() => mc = GetComponent<MooringController>();

    void OnDestroy()
    {
        foreach (Material m in materials) if (m != null) Destroy(m);
    }

    private void Ensure(int count)
    {
        if (lines.Length == count) return;
        foreach (LineRenderer l in lines) if (l != null) Destroy(l.gameObject);
        foreach (Material m in materials) if (m != null) Destroy(m);
        lines = new LineRenderer[count];
        materials = new Material[count];
        shown = new Color[count];
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"MooringLine_{i + 1}");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = Points;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            materials[i] = new Material(unlit != null ? unlit : Shader.Find("Sprites/Default")) { name = go.name };
            lr.sharedMaterial = materials[i];
            lr.enabled = false;
            lines[i] = lr;
            shown[i] = Color.clear;
        }
    }

    void LateUpdate()
    {
        MooringSystem sys = mc != null ? mc.System : null;
        BerthArea berth = mc != null ? mc.ActiveBerth : null;
        WorldOrigin origin = WorldOrigin.Instance;
        if (sys == null || berth == null || origin == null || mc.navigation == null || mc.navigation.Model == null)
        {
            foreach (LineRenderer l in lines) if (l != null) l.enabled = false;
            return;
        }
        Ensure(sys.Lines.Count);
        float mean = OceanWaves.Instance != null ? OceanWaves.Instance.MeanLevel : 0f;
        bool hints = TrainerModes.HintsAllowedNow;
        ShipDynamicsModel m = mc.navigation.Model;

        for (int i = 0; i < lines.Length; i++)
        {
            MooringLine line = sys.Lines[i];
            LineRenderer lr = lines[i];
            if (line.State != MooringLineState.Fast) { lr.enabled = false; continue; }

            Vector3 a = origin.WorldToScene(sys.Fairlead(m, line));
            a.y = mean + (float)sys.FairleadHeightM;
            Vector3 b = origin.WorldToScene(berth.BollardWorld(line.Bollard));
            b.y = mean + berth.quayHeightM + bollardTopM;
            float chord = Vector3.Distance(a, b);
            float sag = (float)MooringLineShape.Sag(chord, line.RestLength, line.Tension, line.Mbl);
            for (int k = 0; k < Points; k++)
            {
                float u = k / (float)(Points - 1);
                buffer[k] = Vector3.Lerp(a, b, u) + Vector3.down * (4f * sag * u * (1f - u));
            }
            lr.SetPositions(buffer);
            lr.widthMultiplier = width;

            double share = line.Mbl > 0 ? line.Tension / line.Mbl : 0.0;
            Color c = !hints ? ropeColor
                : share >= MooringLine.OverloadFraction ? overloadColor
                : share >= 0.3 ? loadedColor : slackColor;
            if (c != shown[i])
            {
                materials[i].SetColor(BaseColorId, c);
                materials[i].color = c;
                shown[i] = c;
            }
            lr.enabled = true;
        }
    }
}
