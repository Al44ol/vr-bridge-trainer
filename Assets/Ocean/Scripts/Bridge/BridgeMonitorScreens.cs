// ============================================================
// BridgeMonitorScreens.cs — Содержимое мониторов пульта (запрос 16а)
//
// Вид — по образцам пользователя (пульт с круглыми указателями руля и ROT, картушка, табло HDG красными цифрами;
// приборная панель оборотов с цифровой вставкой) и мостику Transas: тёмный фон, светлые деления, цветные сектора
// (ЛБ / назад — красный, ПБ / вперёд — зелёный), цифры табло — красные на «погашенных» 8.
//   • HeadingScreen (M4, Cube.6306) — картушка (вращается, курс под курсовой чертой), табло курса, поворот, скорость;
//   • RudderScreen (M3, Cube.6319) — указатель руля ±δ_max: заданный (голубая стрелка) и фактический (янтарная),
//     подписи «ЗАДАН. / ФАКТ.», «ОТКАЗ РУЛЯ», полоса подруливающего;
//   • EngineScreen (M2, Cube.6314) — обороты ГД (−n_ном … +n_ном, 0 — вверху; по телеграфу с инерцией — Core EngineRpm),
//     цифровая вставка, положение телеграфа, «ОТКАЗ ГД»;
//   • PlaceholderScreen — остальные экраны: номер, имя экрана и что на нём будет (16б, 16в).
// Размеры — доли стороны холста (ширина холста — BridgeMonitors.canvasWidthPx), поэтому экраны разного размера
// выглядят одинаково; портретный экран — прибор сверху, тексты снизу.
// ============================================================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class MonitorScreen
{
    public static readonly Color Background = new Color(0.027f, 0.043f, 0.063f, 1f);
    public static readonly Color Face = new Color(0.055f, 0.08f, 0.106f, 1f);
    public static readonly Color TickColor = new Color(0.85f, 0.89f, 0.92f, 1f);
    public static readonly Color LabelColor = new Color(0.78f, 0.83f, 0.87f, 1f);
    public static readonly Color MutedColor = new Color(0.45f, 0.52f, 0.58f, 1f);
    public static readonly Color DigitColor = new Color(1f, 0.23f, 0.19f, 1f);
    public static readonly Color DigitGhost = new Color(0.23f, 0.05f, 0.045f, 1f);
    public static readonly Color PortColor = new Color(0.85f, 0.21f, 0.17f, 1f);
    public static readonly Color StbdColor = new Color(0.18f, 0.75f, 0.31f, 1f);
    public static readonly Color OrderedColor = new Color(0.31f, 0.76f, 0.97f, 1f);
    public static readonly Color ActualColor = new Color(1f, 0.72f, 0.3f, 1f);
    public static readonly Color AlarmColor = new Color(1f, 0.25f, 0.2f, 1f);

    public string Label = "", ScreenName = "", Title = "";
    protected RectTransform Root;
    protected Vector2 Size;
    protected BridgeMonitors M;
    protected float W => Size.x;
    protected float H => Size.y;
    protected bool Landscape => W >= 1.2f * H;

    public static MonitorScreen For(int index, string label, string screenName, bool big)
    {
        MonitorScreen s = big ? new PlaceholderScreen("Карта (копия планшета: маршрут, курс, цели)", "16б")
            : index == 1 ? new EngineScreen()
            : index == 2 ? new RudderScreen()
            : index == 3 ? new HeadingScreen()
            : index == 0 ? new PlaceholderScreen("Задание и сообщения", "16в")
            : index == 4 ? new PlaceholderScreen("Скорость и поворот", "16б")
            : index == 5 ? new PlaceholderScreen("Маршрут", "16б")
            : index == 6 ? new PlaceholderScreen("Цели (CPA, TCPA)", "16в")
            : index == 7 ? new PlaceholderScreen("Ветер и погода", "16в")
            : (MonitorScreen)new PlaceholderScreen("Сигнализация и швартовка", "16в");
        s.Label = label;
        s.ScreenName = screenName;
        return s;
    }

    public void Setup(RectTransform root, Vector2 size, BridgeMonitors m)
    {
        Root = root;
        Size = size;
        M = m;
        Image bg = root.gameObject.AddComponent<Image>();
        bg.color = Background;
        bg.raycastTarget = false;
        Build();
    }

    protected abstract void Build();
    public virtual void Needles() { }
    public virtual void Texts() { }

    // ─────────── Элементы ───────────
    protected RectTransform Node(string name, Vector2 pos, Vector2 size, RectTransform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent != null ? parent : Root, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    protected DialGraphic Dial(string name, Vector2 center, float r, RectTransform parent = null)
    {
        RectTransform rt = Node(name, center, new Vector2(2f * r, 2f * r), parent);
        DialGraphic g = rt.gameObject.AddComponent<DialGraphic>();
        g.raycastTarget = false;
        return g;
    }

    protected NeedleGraphic Needle(string name, Vector2 center, float len, float width, Color c, RectTransform parent = null)
    {
        RectTransform rt = Node(name, center, new Vector2(width, len), parent);
        NeedleGraphic n = rt.gameObject.AddComponent<NeedleGraphic>();
        n.raycastTarget = false;
        n.Configure(len, width, width * 0.3f, len * 0.15f, c);
        return n;
    }

    protected TextMeshProUGUI Text(string text, Vector2 pos, Vector2 size, float fontSize, Color c,
                                   TextAlignmentOptions align = TextAlignmentOptions.Center, RectTransform parent = null)
    {
        RectTransform rt = Node("Text", pos, size, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = c;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Табло красными цифрами: «погашенные» 8 под живыми цифрами (вид семисегментного индикатора).</summary>
    protected TextMeshProUGUI Digits(string ghost, Vector2 pos, Vector2 size, float fontSize)
    {
        fontSize = Mathf.Min(fontSize, size.x / (ghost.Length * 0.72f));   // цифры — в поле табло
        RectTransform box = Node("Digits", pos, size);
        Image bg = box.gameObject.AddComponent<Image>();
        bg.color = new Color(0.01f, 0.012f, 0.016f, 1f);
        bg.raycastTarget = false;
        TextMeshProUGUI g = Text(ghost, Vector2.zero, size, fontSize, DigitGhost, TextAlignmentOptions.Center, box);
        g.fontStyle = FontStyles.Bold;
        g.characterSpacing = 8f;
        TextMeshProUGUI t = Text("", Vector2.zero, size, fontSize, DigitColor, TextAlignmentOptions.Center, box);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 8f;
        return t;
    }

    protected void Header(string text)
    {
        TextMeshProUGUI t = Text(text, new Vector2(-W * 0.5f + W * 0.03f, H * 0.5f - H * 0.06f), new Vector2(W * 0.3f, H * 0.1f),
                                 Mathf.Min(W, H) * 0.065f, MutedColor, TextAlignmentOptions.MidlineLeft);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.fontStyle = FontStyles.Bold;
    }

    /// <summary>Шкала: деления и подписи по GaugeScale.</summary>
    protected void Scale(DialGraphic d, GaugeScale s, float r, double minor, double major, Func<double, string> label, float labelR, float fontSize,
                         RectTransform labelParent)
    {
        foreach (double v in s.Ticks(minor))
        {
            bool big = Math.Abs(Math.IEEERemainder(v, major)) < minor * 0.01;
            float a = (float)s.AngleOf(v);
            d.TickAt(a, big ? r * 0.82f : r * 0.9f, r, big ? r * 0.028f : r * 0.014f, TickColor);
            if (big && label != null)
            {
                Vector2 p = DialGraphic.Dir(a) * labelR;
                Text(label(v), p + d.rectTransform.anchoredPosition, new Vector2(fontSize * 3f, fontSize * 1.4f), fontSize, LabelColor,
                     TextAlignmentOptions.Center, labelParent);
            }
        }
    }
}

// ─────────── M4: курс ───────────
public sealed class HeadingScreen : MonitorScreen
{
    private RectTransform card;
    private TextMeshProUGUI hdg, rot, speed;

    public HeadingScreen() => Title = "Курс";

    protected override void Build()
    {
        Header("КУРС · ГИРОКОМПАС");
        float r = Landscape ? Mathf.Min(H * 0.4f, W * 0.27f) : Mathf.Min(W * 0.4f, H * 0.28f);
        Vector2 c = Landscape ? new Vector2(-W * 0.5f + r + W * 0.06f, -H * 0.04f) : new Vector2(0f, H * 0.5f - r - H * 0.12f);

        DialGraphic face = Dial("Face", c, r * 1.08f);
        face.Disc(r * 1.06f, Face);
        face.Arc(0f, 360f, r * 1.0f, r * 1.02f, MutedColor);
        face.TickAt(0f, r * 1.03f, r * 1.2f, r * 0.06f, ActualColor);   // курсовая черта

        card = Node("Card", c, new Vector2(2f * r, 2f * r));
        DialGraphic ticks = Dial("CardTicks", Vector2.zero, r, card);
        for (int b = 0; b < 360; b += 5)
        {
            bool major = b % 30 == 0, mid = b % 10 == 0;
            ticks.TickAt(b, major ? r * 0.8f : mid ? r * 0.87f : r * 0.92f, r * 0.98f, major ? r * 0.03f : r * 0.014f, TickColor);
            if (!major) continue;
            string l = b == 0 ? "N" : b == 90 ? "E" : b == 180 ? "S" : b == 270 ? "W" : (b / 10).ToString();
            Vector2 p = DialGraphic.Dir(b) * (r * 0.66f);
            TextMeshProUGUI t = Text(l, p, new Vector2(r * 0.4f, r * 0.22f), r * (b % 90 == 0 ? 0.2f : 0.15f),
                                     b == 0 ? ActualColor : LabelColor, TextAlignmentOptions.Center, card);
            t.fontStyle = b % 90 == 0 ? FontStyles.Bold : FontStyles.Normal;
            t.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -b);   // подпись — по радиусу, как на картушке
        }
        DialGraphic hub = Dial("Hub", c, r * 0.06f);
        hub.Disc(r * 0.05f, TickColor);

        // Тексты справа (или снизу)
        float colX = Landscape ? c.x + r * 1.25f : -W * 0.42f;
        float colW = Landscape ? W * 0.5f - W * 0.04f - colX : W * 0.84f;
        float top = Landscape ? H * 0.28f : c.y - r * 1.3f;
        float digH = Landscape ? H * 0.22f : H * 0.12f;
        Text("КУРС, °", new Vector2(colX + colW * 0.5f, top + digH * 0.85f), new Vector2(colW, digH * 0.4f), digH * 0.3f, MutedColor);
        hdg = Digits("888,8", new Vector2(colX + colW * 0.5f, top), new Vector2(colW, digH), digH * 0.78f);
        rot = Text("", new Vector2(colX + colW * 0.5f, top - digH * 1.05f), new Vector2(colW, digH * 0.5f), digH * 0.36f, LabelColor);
        speed = Text("", new Vector2(colX + colW * 0.5f, top - digH * 1.6f), new Vector2(colW, digH * 0.5f), digH * 0.36f, LabelColor);
    }

    public override void Needles()
    {
        if (M.Nav == null) return;
        card.localRotation = Quaternion.Euler(0f, 0f, -(float)CompassCard.CardRotation(M.Nav.Heading));
    }

    public override void Texts()
    {
        if (M.Nav == null) return;
        hdg.text = ConningText.Heading(M.Nav.Heading);
        float r = M.Nav.RateOfTurn;
        rot.text = $"ПОВОРОТ  {(Mathf.Abs(r) < 0.5f ? "0" : (r > 0f ? "+" : "−") + Mathf.Abs(r).ToString("0"))} °/мин {(Mathf.Abs(r) < 0.5f ? "" : r > 0f ? "ПБ" : "ЛБ")}";
        speed.text = $"СКОРОСТЬ  {ScenarioText.Num(M.Nav.SpeedKnots, 1)} уз";
    }
}

// ─────────── M3: руль ───────────
public sealed class RudderScreen : MonitorScreen
{
    private NeedleGraphic ordered, actual;
    private TextMeshProUGUI orderedText, actualText, alarm, thrusterText;
    private RectTransform thrusterFill;
    private GaugeScale scale;
    private float thrusterHalf;

    public RudderScreen() => Title = "Руль";

    protected override void Build()
    {
        Header("РУЛЬ");
        float max = M.Nav != null && M.Nav.parameters != null ? M.Nav.parameters.maxRudderAngle : 35f;
        scale = new GaugeScale(-max, max, -70, 70);
        float r = Mathf.Min(W * 0.4f, H * 0.62f);
        Vector2 c = new Vector2(0f, -H * 0.5f + H * 0.32f);
        DialGraphic d = Dial("Scale", c, r);
        d.Arc(-70f, 0f, r * 0.93f, r, PortColor);
        d.Arc(0f, 70f, r * 0.93f, r, StbdColor);
        Scale(d, scale, r * 0.92f, 5, 10, v => Math.Abs(v).ToString("0"), r * 0.66f, r * 0.11f, Root);
        Text("ЛБ", c + DialGraphic.Dir(-78f) * (r * 0.96f), new Vector2(r * 0.3f, r * 0.15f), r * 0.11f, PortColor);
        Text("ПБ", c + DialGraphic.Dir(78f) * (r * 0.96f), new Vector2(r * 0.3f, r * 0.15f), r * 0.11f, StbdColor);
        ordered = Needle("Ordered", c, r * 0.9f, r * 0.035f, OrderedColor);
        actual = Needle("Actual", c, r * 0.8f, r * 0.07f, ActualColor);
        DialGraphic hub = Dial("Hub", c, r * 0.09f);
        hub.Disc(r * 0.08f, TickColor);

        float rowY = c.y - r * 0.2f, fs = Mathf.Min(W, H) * 0.075f;
        orderedText = Text("", new Vector2(-W * 0.27f, rowY), new Vector2(W * 0.42f, fs * 1.4f), fs, OrderedColor);
        actualText = Text("", new Vector2(W * 0.27f, rowY), new Vector2(W * 0.42f, fs * 1.4f), fs, ActualColor);
        alarm = Text("", new Vector2(W * 0.3f, H * 0.5f - H * 0.06f), new Vector2(W * 0.4f, H * 0.1f), Mathf.Min(W, H) * 0.065f, AlarmColor,
                     TextAlignmentOptions.MidlineRight);
        alarm.fontStyle = FontStyles.Bold;

        // Подруливающее (если есть рычаг): полоса −100…+100 %, от середины
        if (M.Thruster == null) return;
        float by = -H * 0.5f + H * 0.09f, bw = W * 0.56f, bh = H * 0.045f;
        thrusterHalf = bw * 0.5f;
        Text("ПОДРУЛ.", new Vector2(-W * 0.5f + W * 0.11f, by), new Vector2(W * 0.2f, bh * 2f), fs * 0.7f, MutedColor);
        RectTransform track = Node("ThrusterTrack", new Vector2(0f, by), new Vector2(bw, bh));
        track.gameObject.AddComponent<Image>().color = Face;
        Node("Mid", Vector2.zero, new Vector2(2f, bh * 1.4f), track).gameObject.AddComponent<Image>().color = TickColor;
        thrusterFill = Node("Fill", Vector2.zero, new Vector2(0f, bh * 0.7f), track);
        thrusterFill.gameObject.AddComponent<Image>().color = ActualColor;
        thrusterText = Text("", new Vector2(W * 0.5f - W * 0.11f, by), new Vector2(W * 0.2f, bh * 2f), fs * 0.7f, LabelColor);
    }

    public override void Needles()
    {
        if (M.Nav == null) return;
        ordered.Point((float)scale.AngleOf(M.Nav.RudderOrdered));
        actual.Point((float)scale.AngleOf(M.Nav.RudderActual));
    }

    public override void Texts()
    {
        if (M.Nav == null) return;
        orderedText.text = "ЗАДАН.  " + ConningText.Rudder(M.Nav.RudderOrdered);
        actualText.text = "ФАКТ.  " + ConningText.Rudder(M.Nav.RudderActual);
        string f = BridgeMonitors.FailureLine;
        alarm.text = f.Contains("РУЛ") && BridgeMonitors.BlinkOn ? "ОТКАЗ РУЛЯ" : "";
        if (thrusterFill == null || M.Thruster == null) return;
        float k = Mathf.Clamp((float)M.Thruster.Command, -1f, 1f);
        thrusterFill.sizeDelta = new Vector2(Mathf.Abs(k) * thrusterHalf, thrusterFill.sizeDelta.y);
        thrusterFill.anchoredPosition = new Vector2(k * thrusterHalf * 0.5f, 0f);
        thrusterText.text = Mathf.Abs(k) < 0.01f ? "0" : $"{(k > 0f ? "ПБ" : "ЛБ")} {Mathf.Abs(k) * 100f:0} %";
    }
}

// ─────────── M2: машина ───────────
public sealed class EngineScreen : MonitorScreen
{
    private NeedleGraphic needle;
    private TextMeshProUGUI rpm, order, status;
    private GaugeScale scale;

    public EngineScreen() => Title = "Машина";

    protected override void Build()
    {
        Header("ГЛАВНЫЙ ДВИГАТЕЛЬ");
        double n = Math.Max(100.0, M.ratedRpm);
        scale = new GaugeScale(-n, n, -135, 135);
        float r = Landscape ? Mathf.Min(H * 0.4f, W * 0.27f) : Mathf.Min(W * 0.42f, H * 0.3f);
        Vector2 c = Landscape ? new Vector2(-W * 0.5f + r + W * 0.06f, -H * 0.04f) : new Vector2(0f, H * 0.5f - r - H * 0.12f);
        DialGraphic d = Dial("Dial", c, r * 1.08f);
        d.Disc(r * 1.06f, Face);
        d.Arc(-135f, 0f, r * 0.94f, r, PortColor);
        d.Arc(0f, 135f, r * 0.94f, r, StbdColor);
        double major = n / 3.0, minor = n / 15.0;
        Scale(d, scale, r * 0.93f, minor, major, v => Math.Abs(v).ToString("0"), r * 0.67f, r * 0.12f, Root);
        Text("НАЗАД", c + new Vector2(-r * 0.35f, -r * 0.75f), new Vector2(r * 0.6f, r * 0.15f), r * 0.1f, PortColor);
        Text("ВПЕРЁД", c + new Vector2(r * 0.35f, -r * 0.75f), new Vector2(r * 0.6f, r * 0.15f), r * 0.1f, StbdColor);
        Text("об/мин", c + new Vector2(0f, r * 0.3f), new Vector2(r, r * 0.15f), r * 0.1f, MutedColor);
        // Цифровая вставка (как на образце пользователя)
        rpm = Digits("+888", c + new Vector2(0f, -r * 0.42f), new Vector2(r * 0.8f, r * 0.26f), r * 0.2f);
        needle = Needle("Needle", c, r * 0.86f, r * 0.06f, TickColor);
        DialGraphic hub = Dial("Hub", c, r * 0.09f);
        hub.Disc(r * 0.08f, ActualColor);

        float colX = Landscape ? c.x + r * 1.25f : -W * 0.42f;
        float colW = Landscape ? W * 0.5f - W * 0.04f - colX : W * 0.84f;
        float top = Landscape ? H * 0.2f : c.y - r * 1.3f;
        float fs = Mathf.Min(W, H) * 0.075f;
        Text("ТЕЛЕГРАФ", new Vector2(colX + colW * 0.5f, top + fs * 1.2f), new Vector2(colW, fs * 1.2f), fs * 0.75f, MutedColor);
        order = Text("", new Vector2(colX + colW * 0.5f, top), new Vector2(colW, fs * 1.5f), fs * 1.05f, Color.white);
        order.fontStyle = FontStyles.Bold;
        order.enableAutoSizing = true;
        order.fontSizeMin = fs * 0.5f;
        order.fontSizeMax = fs * 1.05f;
        status = Text("", new Vector2(colX + colW * 0.5f, top - fs * 1.8f), new Vector2(colW, fs * 1.3f), fs * 0.85f, StbdColor);
        status.fontStyle = FontStyles.Bold;
    }

    public override void Needles() => needle.Point((float)scale.AngleOf(M.Engine.Rpm));

    public override void Texts()
    {
        rpm.text = ConningText.Rpm(M.Engine.Rpm);
        order.text = ConningText.Telegraph(M.Nav != null ? M.Nav.TelegraphPosition : "");
        bool failed = M.Nav != null && M.Nav.EngineFailed && BridgeMonitors.FailureLine.Contains("ГД");
        status.text = failed ? (BridgeMonitors.BlinkOn ? "ОТКАЗ ГД" : "") : "ГД В РАБОТЕ";
        status.color = failed ? AlarmColor : StbdColor;
    }
}

// ─────────── Остальные экраны (16б, 16в) ───────────
public sealed class PlaceholderScreen : MonitorScreen
{
    private readonly string part;

    public PlaceholderScreen(string title, string part)
    {
        Title = title;
        this.part = part;
    }

    protected override void Build()
    {
        float fs = Mathf.Min(W, H) * 0.09f;
        Text(Label, new Vector2(0f, fs * 1.6f), new Vector2(W * 0.9f, fs * 2f), fs * 1.6f, LabelColor).fontStyle = FontStyles.Bold;
        Text(Title, new Vector2(0f, 0f), new Vector2(W * 0.9f, fs * 1.3f), fs, LabelColor);
        Text($"{ScreenName} · будет в {part}", new Vector2(0f, -fs * 1.3f), new Vector2(W * 0.9f, fs * 1.2f), fs * 0.7f, MutedColor);
    }
}
