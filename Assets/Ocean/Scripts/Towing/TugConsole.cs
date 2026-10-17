// ============================================================
// TugConsole.cs — Пульт своего буксира (запрос 19.4а-5)
//
// По проверке в шлеме (19.4а-4): табличка джойстика мала и зажата на стойке МОСТИКИ, сверху её перекрывает NavDisplay.
// На буксире вместо рулевой стойки мостика — свой пульт (по фото пульта портового буксира): строится кодом при первом
// переключении на буксир (ShipTypeSwitcher.EnsureTugView), Setup не нужен; на модели 45 м и катере его нет.
//   • Корпус — одна сетка (профиль, вытянутый поперёк): тумба 1,4 × 0,7 м, наклонная столешница (передний край 0,93 м
//     над полом рубки, задний 1,03 м — 12°) и приборная панель над ней (до 1,32 м, наклон 27° от вертикали) — ниже
//     линии взгляда на бак. Место — там, где стояла стойка МОСТИКИ: край у оператора — по её кормовой грани, середина —
//     по её оси (границы keptConsole/forceKeep), + сдвиг ShipTypeSwitcher.tugConsoleOffset.
//   • Столешница слева направо: ТИФОН и СВЕТ (те же объекты сцены — на буксире переносятся сюда, обратно — при смене
//     типа), тумблер «УДЕРЖ. КУРС», рукоятка джойстика (OwnTugDrive ставит их по якорям пульта).
//   • Панель — три экрана: колонки (два указателя: направление упора и загрузка), монитор буксира (TugConsoleText:
//     глубина / мель, курс и автомат курса, ход, лагом, поворот, ветер, причал, лоцман, отказ), джойстик
//     (TugJoystickIndicator — схема ступеней и строки). Обновление 10 Гц; во всех режимах (приборы, не подсказка).
//   • На буксире скрыты: стойка МОСТИКИ (рендеры keptConsole / forceKeep) и NavDisplay (холсты и рендеры) —
//     всё, что он показывал, есть на мониторе; при смене типа судна всё возвращается.
// ============================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TugConsole : MonoBehaviour
{
    public static TugConsole Instance { get; private set; }

    // Профиль пульта (z — к носу от края у оператора, y — вверх от пола), м; полуширина
    private static readonly Vector2[] Profile =
    {
        new Vector2(0f, 0f), new Vector2(0.7f, 0f), new Vector2(0.7f, 1.25f), new Vector2(0.61f, 1.32f),
        new Vector2(0.46f, 1.03f), new Vector2(0f, 0.93f)
    };
    private const int PanelEdge = 3, DeskEdge = 4;   // ребро P3→P4 — панель, P4→P5 — столешница
    public const float HalfWidth = 0.7f;

    [Tooltip("Обновление экранов, Гц")]
    public float rateHz = 10f;

    /// <summary>Якоря на столешнице: +Y — нормаль столешницы, +Z — к носу.</summary>
    public Transform JoystickAnchor { get; private set; }
    public Transform SwitchAnchor { get; private set; }
    public Transform HornAnchor { get; private set; }
    public Transform LightAnchor { get; private set; }
    /// <summary>Экран джойстика на панели (холст таблички — дочерний).</summary>
    public Transform JoystickScreen { get; private set; }
    /// <summary>Место пульта в системе Ship без ручного сдвига (Tools/VR Bridge/Tug — запомнить положение пульта).</summary>
    public Vector3 BaseLocalPosition { get; private set; }

    private ShipTypeSwitcher switcher;
    private Mesh mesh;
    private readonly List<Material> materials = new List<Material>();
    private TextMeshProUGUI monitorText, podText1, podText2;
    private RectTransform podArrow1, podArrow2, podFill1, podFill2;
    private float nextText;
    private GroundingMonitor grounding;

    // Что переносится / скрывается на буксире — исходное состояние
    private readonly List<(Transform t, Transform anchor, Transform parent, Vector3 pos, Quaternion rot)> moved =
        new List<(Transform, Transform, Transform, Vector3, Quaternion)>();
    private readonly List<(Renderer r, bool was)> hiddenRenderers = new List<(Renderer, bool)>();
    private readonly List<(Canvas c, bool was)> hiddenCanvases = new List<(Canvas, bool)>();
    private bool onTug;

    private const float PodRadius = 105f, PodBarW = 220f;

    // ─────────── Постройка ───────────

    /// <summary>Построить пульт (один раз, выключенным): место — по стойке МОСТИКИ, материалы — от template.</summary>
    public static TugConsole Build(ShipTypeSwitcher sts, Vector3 bowLocal, Material template)
    {
        Transform ship = sts.transform;
        bowLocal.y = 0f;
        bowLocal = bowLocal.sqrMagnitude > 1e-6f ? bowLocal.normalized : Vector3.back;
        Vector3 stbd = Vector3.Cross(Vector3.up, bowLocal);
        string where = StandFrame(sts, bowLocal, stbd, out Vector3 basePos);

        var go = new GameObject("TugConsole");
        go.SetActive(false);
        go.transform.SetParent(ship, false);
        var c = go.AddComponent<TugConsole>();
        c.switcher = sts;
        c.BaseLocalPosition = basePos;
        go.transform.localPosition = basePos + sts.tugConsoleOffset;
        go.transform.localRotation = Quaternion.LookRotation(bowLocal, Vector3.up);   // +Z — к носу, +X — ПБ
        c.BuildBody(template);
        c.BuildScreens();
        c.CollectHidden();
        Instance = c;
        Debug.Log($"[Буксир] Пульт буксира построен: {where}, сдвиг {sts.tugConsoleOffset}; на буксире скрыты стойка МОСТИКИ " +
                  $"({c.hiddenRenderers.Count} рендеров вместе с NavDisplay) и холсты NavDisplay ({c.hiddenCanvases.Count}).", c);
        return c;
    }

    // Край пульта у оператора и ось — по стойке МОСТИКИ (границы keptConsole / forceKeep в системе Ship)
    private static string StandFrame(ShipTypeSwitcher sts, Vector3 bow, Vector3 stbd, out Vector3 basePos)
    {
        Transform ship = sts.transform;
        float minB = float.MaxValue, maxS = float.MinValue, minS = float.MaxValue, minY = float.MaxValue;
        int n = 0;
        foreach (Renderer r in Stand(sts))
        {
            Bounds b = r.bounds;
            if (b.size.sqrMagnitude < 1e-8f) continue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 w = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 l = ship.InverseTransformPoint(w);
                minB = Mathf.Min(minB, Vector3.Dot(l, bow));
                minS = Mathf.Min(minS, Vector3.Dot(l, stbd));
                maxS = Mathf.Max(maxS, Vector3.Dot(l, stbd));
                minY = Mathf.Min(minY, l.y);
            }
            n++;
        }
        if (n > 0)
        {
            basePos = bow * minB + stbd * (0.5f * (minS + maxS)) + Vector3.up * minY;
            return $"по стойке МОСТИКИ ({n} частей)";
        }
        // Без списка стойки — от телеграфа: край на 0,25 м ближе к корме, пол на 1 м ниже
        ShipNavigation nav = sts.navigation;
        Transform head = nav != null && nav.telegraph != null ? nav.telegraph.transform : ship;
        Vector3 h = ship.InverseTransformPoint(head.position);
        basePos = bow * (Vector3.Dot(h, bow) - 0.25f) + stbd * Vector3.Dot(h, stbd) + Vector3.up * (h.y - 1f);
        return "стойка не найдена — от телеграфа";
    }

    private static IEnumerable<Renderer> Stand(ShipTypeSwitcher sts)
    {
        if (sts.keptConsole != null) foreach (Renderer r in sts.keptConsole) if (r != null) yield return r;
        if (sts.forceKeep != null) foreach (Renderer r in sts.forceKeep) if (r != null) yield return r;
    }

    private void BuildBody(Material template)
    {
        Material body = Mat(template, "TugConsole_Body", new Color(0.8f, 0.82f, 0.84f), 0.35f);
        Material desk = Mat(template, "TugConsole_Desk", new Color(0.36f, 0.4f, 0.44f), 0.25f);
        Material panel = Mat(template, "TugConsole_Panel", new Color(0.12f, 0.13f, 0.15f), 0.3f);
        mesh = ExtrudeProfile(Profile, HalfWidth);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { body, desk, panel };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Transform deskFrame = Frame("Desk", DeskEdge, out _);
        HornAnchor = Anchor(deskFrame, "HornAnchor", new Vector3(-0.5f, 0.002f, -0.02f));
        LightAnchor = Anchor(deskFrame, "LightAnchor", new Vector3(-0.3f, 0.002f, 0f));
        SwitchAnchor = Anchor(deskFrame, "SwitchAnchor", new Vector3(0.04f, 0.002f, 0f));        // 19.4а-5: левее на 8 см (было 0,12)
        JoystickAnchor = Anchor(deskFrame, "JoystickAnchor", new Vector3(0.28f, 0.002f, -0.04f));   // левее на 8 см (было 0,36)
    }

    private Material Mat(Material template, string name, Color color, float smooth)
    {
        Material m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.name = name;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); else m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        materials.Add(m);
        return m;
    }

    // Рамка на грани профиля i→i+1: начало — середина грани, +Z — вдоль грани (к носу или вверх), +Y — наружу
    private Transform Frame(string name, int edge, out float length)
    {
        Vector2 a = Profile[edge], b = Profile[(edge + 1) % Profile.Length];
        Vector3 pa = new Vector3(0f, a.y, a.x), pb = new Vector3(0f, b.y, b.x);
        Vector3 along = (pb - pa).normalized;
        Vector3 outward = Outward(edge);
        if (Vector3.Dot(along, Vector3.forward) < 0f && Mathf.Abs(along.z) > Mathf.Abs(along.y)) along = -along;   // столешница — к носу
        if (Mathf.Abs(along.y) >= Mathf.Abs(along.z) && along.y < 0f) along = -along;                              // панель — вверх
        length = (pb - pa).magnitude;
        Transform f = new GameObject(name).transform;
        f.SetParent(transform, false);
        f.localPosition = 0.5f * (pa + pb);
        f.localRotation = Quaternion.LookRotation(along, outward);
        return f;
    }

    private static Vector3 Outward(int edge)
    {
        Vector2 a = Profile[edge], b = Profile[(edge + 1) % Profile.Length];
        Vector2 c = Vector2.zero;
        foreach (Vector2 p in Profile) c += p;
        c /= Profile.Length;
        Vector2 d = b - a, nrm = new Vector2(-d.y, d.x).normalized;   // (z, y)
        Vector2 mid = 0.5f * (a + b);
        if (Vector2.Dot(nrm, mid - c) < 0f) nrm = -nrm;
        return new Vector3(0f, nrm.y, nrm.x);
    }

    private static Transform Anchor(Transform parent, string name, Vector3 local)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        return t;
    }

    // Профиль (z, y) вытянут по x на ±halfWidth; подмеши: 0 — тумба и бока, 1 — столешница, 2 — приборная панель
    private static Mesh ExtrudeProfile(Vector2[] prof, float halfWidth)
    {
        var v = new List<Vector3>();
        var nrm = new List<Vector3>();
        var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
        void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { Vector3 t = b; b = c; c = t; }
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            nrm.Add(n); nrm.Add(n); nrm.Add(n);
            tris[sub].Add(i0); tris[sub].Add(i0 + 1); tris[sub].Add(i0 + 2);
        }
        Vector3 P(Vector2 p, float x) => new Vector3(x, p.y, p.x);
        for (int i = 0; i < prof.Length; i++)
        {
            int j = (i + 1) % prof.Length;
            Vector3 n = Outward(i);
            int sub = i == DeskEdge ? 1 : i == PanelEdge ? 2 : 0;
            Vector3 a0 = P(prof[i], -halfWidth), a1 = P(prof[i], halfWidth), b0 = P(prof[j], -halfWidth), b1 = P(prof[j], halfWidth);
            Tri(sub, a0, a1, b1, n);
            Tri(sub, a0, b1, b0, n);
        }
        for (int s = -1; s <= 1; s += 2)
            for (int i = 1; i + 1 < prof.Length; i++)
                Tri(0, P(prof[0], s * halfWidth), P(prof[i], s * halfWidth), P(prof[i + 1], s * halfWidth), new Vector3(s, 0f, 0f));
        var m = new Mesh { name = "TugConsole" };
        m.SetVertices(v);
        m.SetNormals(nrm);
        m.subMeshCount = 3;
        for (int s = 0; s < 3; s++) m.SetTriangles(tris[s], s);
        m.RecalculateBounds();
        return m;
    }

    // ─────────── Экраны ───────────

    private void BuildScreens()
    {
        Transform panel = Frame("Panel", PanelEdge, out float h);
        // Холст: +Z — в панель (от оператора), верх — вверх по панели
        Transform Screen(string name, float x)
        {
            Transform s = new GameObject(name).transform;
            s.SetParent(panel, false);
            s.localPosition = new Vector3(x, 0.003f, 0f);
            s.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return s;
        }
        Transform pods = Screen("PodsScreen", -0.47f), mon = Screen("MonitorScreen", 0f);
        JoystickScreen = Screen("JoystickScreen", 0.47f);

        // Колонки
        RectTransform pc = Canvas(pods, "Pods", 600f, 386f, 0.0007f);
        Txt(pc, "КОЛОНКИ", new Vector2(0f, 168f), new Vector2(560f, 40f), 26f, TextAlignmentOptions.Center);
        Dial(pc, -150f, "ЛБ", out podArrow2, out podFill2, out podText2);
        Dial(pc, 150f, "ПБ", out podArrow1, out podFill1, out podText1);

        // Монитор
        RectTransform mc = Canvas(mon, "Monitor", 600f, 386f, 0.0007f);
        monitorText = Txt(mc, "", Vector2.zero, new Vector2(570f, 370f), 30f, TextAlignmentOptions.TopLeft);
        monitorText.lineSpacing = -8f;
    }

    private static RectTransform Canvas(Transform parent, string name, float w, float h, float scale)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(w, h);
        go.transform.localScale = Vector3.one * scale;
        Img(rt, "Background", Vector2.zero, new Vector2(w, h), new Color(0.02f, 0.04f, 0.06f, 1f));
        return rt;
    }

    private void Dial(RectTransform parent, float x, string side, out RectTransform arrow, out RectTransform fill, out TextMeshProUGUI text)
    {
        Color tick = new Color(0.6f, 0.7f, 0.76f), lineC = new Color(0.3f, 0.36f, 0.42f);
        RectTransform dial = Img(parent, "Dial" + side, new Vector2(x, 30f), new Vector2(2f * PodRadius + 8f, 2f * PodRadius + 8f), new Color(0f, 0f, 0f, 0f));
        for (int k = 0; k < 36; k++)
        {
            bool major = k % 3 == 0;
            float a = k * 10f;
            RectTransform t = Img(dial, "Tick", Vector2.zero, new Vector2(major ? 4f : 2f, major ? 18f : 9f), major ? tick : lineC);
            t.pivot = new Vector2(0.5f, 0f);
            t.localRotation = Quaternion.Euler(0f, 0f, -a);
            t.anchoredPosition = Quaternion.Euler(0f, 0f, -a) * new Vector3(0f, PodRadius - (major ? 18f : 9f), 0f);
        }
        Txt(dial, "нос", new Vector2(0f, PodRadius + 14f), new Vector2(80f, 24f), 18f, TextAlignmentOptions.Center).color = tick;
        Txt(dial, side, new Vector2(0f, -28f), new Vector2(80f, 30f), 24f, TextAlignmentOptions.Center).color = tick;
        Img(dial, "Hub", Vector2.zero, new Vector2(14f, 14f), tick);
        arrow = Img(dial, "Arrow", Vector2.zero, new Vector2(8f, PodRadius - 12f), new Color(1f, 0.8f, 0.2f));
        arrow.pivot = new Vector2(0.5f, 0f);
        arrow.anchoredPosition = Vector2.zero;
        RectTransform bar = Img(parent, "Bar" + side, new Vector2(x, -110f), new Vector2(PodBarW, 14f), lineC);
        fill = Img(bar, "Fill", Vector2.zero, new Vector2(0f, 14f), new Color(1f, 0.8f, 0.2f));
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        text = Txt(parent, "", new Vector2(x, -145f), new Vector2(280f, 34f), 26f, TextAlignmentOptions.Center);
    }

    private static RectTransform Img(RectTransform parent, string name, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        var r = (RectTransform)go.transform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return r;
    }

    private static TextMeshProUGUI Txt(RectTransform parent, string text, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = new Color(0.85f, 0.95f, 1f);
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        var r = (RectTransform)go.transform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return t;
    }

    // ─────────── На буксире / не на буксире ───────────

    // Стойка МОСТИКИ и NavDisplay — исходное состояние; ТИФОН и СВЕТ — места
    private void CollectHidden()
    {
        foreach (Renderer r in Stand(switcher)) hiddenRenderers.Add((r, r.enabled));
        BridgeNavDisplay nav = FindAnyObjectByType<BridgeNavDisplay>(FindObjectsInactive.Include);
        if (nav != null)
        {
            Transform root = nav.transform;
            while (root.parent != null && root.parent != switcher.transform && root.parent.parent != null) root = root.parent;
            if (root.parent != switcher.transform) root = nav.transform;   // NavDisplay не под Ship — только сам объект
            foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true)) hiddenCanvases.Add((c, c.enabled));
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) hiddenRenderers.Add((r, r.enabled));
        }
        ShipHorn horn = FindAnyObjectByType<ShipHorn>(FindObjectsInactive.Include);
        if (horn != null) moved.Add((horn.transform, HornAnchor, null, Vector3.zero, Quaternion.identity));
        BridgeLightSwitch light = FindAnyObjectByType<BridgeLightSwitch>(FindObjectsInactive.Include);
        if (light != null) moved.Add((light.transform, LightAnchor, null, Vector3.zero, Quaternion.identity));
    }

    /// <summary>Пульт на буксире: показать пульт, скрыть стойку и NavDisplay, перенести ТИФОН и СВЕТ (и обратно).</summary>
    public void SetOnTug(bool on)
    {
        if (on == onTug) { gameObject.SetActive(on); return; }
        onTug = on;
        gameObject.SetActive(on);
        foreach ((Renderer r, bool was) in hiddenRenderers) if (r != null) r.enabled = !on && was;
        foreach ((Canvas c, bool was) in hiddenCanvases) if (c != null) c.enabled = !on && was;
        for (int i = 0; i < moved.Count; i++)
        {
            var m = moved[i];
            if (m.t == null) continue;
            if (on)
            {
                moved[i] = (m.t, m.anchor, m.t.parent, m.t.localPosition, m.t.localRotation);
                Quaternion q = m.t.rotation;
                // Как на стойке, но «вверх» — нормаль столешницы (как тумблер «УДЕРЖ. КУРС», 19.4а-3)
                m.t.SetPositionAndRotation(m.anchor.position, Quaternion.FromToRotation(q * Vector3.up, m.anchor.up) * q);
                SitOnDesk(m.t, m.anchor);
            }
            else if (m.parent != null)
            {
                m.t.SetParent(m.parent, false);
                m.t.localPosition = m.pos;
                m.t.localRotation = m.rot;
            }
        }
    }

    // 19.4а-5 (по проверке в Unity): модель кнопки ТИФОН лежит ниже своей точки привязки — после переноса объект
    // поднимается так, чтобы нижняя точка его видимых частей легла на столешницу (по границам рендеров)
    private static void SitOnDesk(Transform t, Transform anchor)
    {
        float min = float.MaxValue;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled) continue;
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 w = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                min = Mathf.Min(min, Vector3.Dot(w - anchor.position, anchor.up));
            }
        }
        if (min < float.MaxValue && min < 0f) t.position += anchor.up * (0.002f - min);
    }

    // ─────────── Экраны: обновление ───────────

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (mesh != null) Destroy(mesh);
        foreach (Material m in materials) if (m != null) Destroy(m);
    }

    void Update()
    {
        OwnTugDrive drive = OwnTugDrive.Instance;
        AzimuthDrive d = drive != null ? drive.Drive : null;
        if (d != null)
        {
            Pod(podArrow1, podFill1, d.UnitDirectionDeg(1), d.UnitLoad(1));
            Pod(podArrow2, podFill2, d.UnitDirectionDeg(2), d.UnitLoad(2));
        }
        if (Time.unscaledTime < nextText) return;
        nextText = Time.unscaledTime + 1f / Mathf.Max(1f, rateHz);
        if (d != null)
        {
            podText1.text = TugConsoleText.Pod("ПБ", d.UnitDirectionDeg(1), d.UnitLoad(1));
            podText2.text = TugConsoleText.Pod("ЛБ", d.UnitDirectionDeg(2), d.UnitLoad(2));
        }
        monitorText.text = TugConsoleText.Monitor(MonitorData(drive, d));
    }

    private static void Pod(RectTransform arrow, RectTransform fill, double dirDeg, double load)
    {
        float l = Mathf.Clamp01((float)load);
        arrow.localRotation = Quaternion.Euler(0f, 0f, -(float)dirDeg);   // по часовой от носа
        arrow.gameObject.SetActive(l > 0.005f);
        fill.sizeDelta = new Vector2(PodBarW * l, fill.sizeDelta.y);
    }

    private TugMonitorData MonitorData(OwnTugDrive drive, AzimuthDrive d)
    {
        ShipNavigation nav = drive != null ? drive.navigation : null;
        ShipDynamicsModel m = nav != null ? nav.Model : null;
        var x = new TugMonitorData { DepthM = double.NaN, HoldHeadingDeg = double.NaN, Alarm = "", Berth = "", Pilot = "", Failure = "" };
        if (m == null) return x;
        x.HeadingDeg = nav.Heading;
        x.SpeedKn = m.U / ShipParameters.KnotsToMs;
        x.SwayKn = m.V / ShipParameters.KnotsToMs;
        x.RotDegMin = m.R * 180.0 / System.Math.PI * 60.0;
        x.HeadingHold = d != null && d.HeadingHold;
        x.HoldHeadingDeg = d != null ? d.HoldHeadingDeg : double.NaN;

        if (grounding == null) grounding = FindAnyObjectByType<GroundingMonitor>();
        if (grounding != null)
        {
            x.DepthM = grounding.DepthUnderShip;
            x.DraftM = grounding.Draft;
            if (grounding.State == GroundingState.Grounded) x.Alarm = "НА МЕЛИ";
            else if (grounding.State == GroundingState.Warning && grounding.Nearest.Valid)
                x.Alarm = $"ОПАСНО: МЕЛЬ {grounding.Nearest.Distance / WaterArea.CableMeters:F1} кбт";
        }
        TowingController tc = TowingController.Instance;
        if (tc != null) { x.WindKn = tc.Wind.SpeedMs / ShipParameters.KnotsToMs; x.WindFromDeg = tc.Wind.FromDeg; }

        // Причал — ближе 300 м (как строка NavDisplay)
        MooringController mc = MooringController.Instance;
        WaterArea area = mc != null ? mc.Area : null;
        if (area != null && nav.parameters != null)
        {
            int i = area.NearestBerth(nav.PositionEastNorth, out double dist);
            if (i >= 0 && dist <= BerthReadout.ShowWithinM) x.Berth = BerthReadout.Compute(area.berths[i], nav.parameters, m).Format().Replace("\n", " · ");
        }
        // Упражнение на буксире (19-03): команда лоцмана и задания
        TugWorkController tw = TugWorkController.Instance;
        if (tw != null && tw.Work != null && tw.Work.Order != PilotOrderKind.None)
            x.Pilot = $"Лоцман: «{TugWorkText.Order(tw.Work.Order, tw.Work.WorkSide)}» · заданий {tw.Work.DoneCount}/{TugWorkSystem.StageCount}";
        if (InstructorInputs.Instance != null) x.Failure = InstructorInputs.Instance.FailureLine ?? "";
        return x;
    }
}
