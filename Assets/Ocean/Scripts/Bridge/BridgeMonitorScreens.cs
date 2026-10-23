// ============================================================
// BridgeMonitorScreens.cs — Содержимое мониторов пульта (запрос 16а; вид приборной панели — по проверке 23.10.2026)
//
// Вид — по образцам пользователя (приборная панель оборотов в светлой рамке с винтами и цифровой вставкой;
// пульт с круглыми указателями руля и ROT, картушка, табло HDG красными цифрами) и мостику Transas:
//   • панель — светлая рамка, тёмная плита, винты по углам, гравированный заголовок вверху слева;
//   • прибор — металлический обод, чёрный циферблат, светлые деления, цветные сектора (ЛБ / назад — красный,
//     ПБ / вперёд — зелёный), стрелка с осью, блик стекла; табло — утопленное окно, красные цифры на «погашенных» 8.
//   • HeadingScreen (M4, Cube.6306) — картушка (вращается, курс под курсовой чертой), табло курса, поворот, скорость;
//   • RudderScreen (M3, Cube.6319) — указатель руля ±δ_max (сектор): заданный (голубая стрелка) и фактический (янтарная),
//     «ЗАДАН. / ФАКТ.», «ОТКАЗ РУЛЯ», полоса подруливающего;
//   • EngineScreen (M2, Cube.6314) — обороты ГД −n_ном … +n_ном (0 — вверху; по телеграфу с инерцией — Core EngineRpm),
//     цифровая вставка, положение телеграфа, «ОТКАЗ ГД»;
//   • PlaceholderScreen — остальные экраны: та же панель, номер, назначение и часть (16б, 16в).
// Рисование — стандартными Image (MonitorDraw). Размеры — доли сторон холста (ширина — BridgeMonitors.canvasWidthPx).
// ============================================================
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class MonitorScreen
{
    public static readonly Color FrameColor = new Color(0.62f, 0.66f, 0.7f, 1f);
    public static readonly Color PlateColor = new Color(0.075f, 0.09f, 0.105f, 1f);
    public static readonly Color ScrewColor = new Color(0.55f, 0.59f, 0.63f, 1f);
    public static readonly Color EngraveColor = new Color(0.66f, 0.71f, 0.76f, 1f);
    public static readonly Color BezelColor = new Color(0.72f, 0.76f, 0.8f, 1f);
    public static readonly Color BezelShadow = new Color(0.02f, 0.025f, 0.03f, 1f);
    public static readonly Color FaceColor = new Color(0.03f, 0.035f, 0.042f, 1f);
    public static readonly Color TickColor = new Color(0.9f, 0.93f, 0.95f, 1f);
    public static readonly Color LabelColor = new Color(0.85f, 0.88f, 0.91f, 1f);
    public static readonly Color MutedColor = new Color(0.48f, 0.54f, 0.6f, 1f);
    public static readonly Color DigitColor = new Color(1f, 0.22f, 0.17f, 1f);
    public static readonly Color DigitGhost = new Color(0.2f, 0.045f, 0.04f, 1f);
    public static readonly Color PortColor = new Color(0.86f, 0.2f, 0.16f, 1f);
    public static readonly Color StbdColor = new Color(0.16f, 0.74f, 0.3f, 1f);
    public static readonly Color OrderedColor = new Color(0.3f, 0.76f, 0.98f, 1f);
    public static readonly Color ActualColor = new Color(1f, 0.7f, 0.26f, 1f);
    public static readonly Color AlarmColor = new Color(1f, 0.25f, 0.2f, 1f);
    public static readonly Color OkColor = new Color(0.3f, 0.85f, 0.4f, 1f);

    public string Label = "", ScreenName = "", Title = "";
    protected RectTransform Root;
    protected Vector2 Size;
    protected BridgeMonitors M;
    protected float W => Size.x;
    protected float H => Size.y;
    protected float S => Mathf.Min(W, H);
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
        Plate();
        Build();
    }

    protected abstract void Build();
    public virtual void Needles() { }
    public virtual void Texts() { }

    // ─────────── Панель ───────────
    // Светлая рамка, тёмная плита, четыре винта по углам
    private void Plate()
    {
        MonitorDraw.Img(Root, FrameColor);
        float f = S * 0.035f;
        MonitorDraw.Rect(Root, Vector2.zero, new Vector2(W - 2f * f, H - 2f * f), PlateColor, "Plate");
        float sr = S * 0.022f, sx = W * 0.5f - f - sr * 1.8f, sy = H * 0.5f - f - sr * 1.8f;
        foreach (Vector2 p in new[] { new Vector2(-sx, sy), new Vector2(sx, sy), new Vector2(-sx, -sy), new Vector2(sx, -sy) })
        {
            MonitorDraw.Disc(Root, p, sr, ScrewColor);
            RectTransform slot = MonitorDraw.Rect(Root, p, new Vector2(sr * 1.5f, sr * 0.3f), BezelShadow, "Slot").rectTransform;
            slot.localRotation = Quaternion.Euler(0f, 0f, 35f);
        }
    }

    /// <summary>Гравированный заголовок панели вверху слева.</summary>
    protected void Header(string text)
    {
        float f = S * 0.035f;
        TextMeshProUGUI t = Text(text, new Vector2(-W * 0.5f + f + S * 0.08f, H * 0.5f - f - S * 0.065f), new Vector2(W * 0.6f, S * 0.08f),
                                 S * 0.055f, EngraveColor, TextAlignmentOptions.MidlineLeft);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 6f;
    }

    // ─────────── Элементы ───────────
    protected TextMeshProUGUI Text(string text, Vector2 pos, Vector2 size, float fontSize, Color c,
                                   TextAlignmentOptions align = TextAlignmentOptions.Center, RectTransform parent = null)
    {
        RectTransform rt = MonitorDraw.Node(parent != null ? parent : Root, "Text", pos, size);
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

    /// <summary>Круглый прибор: тень, металлический обод, чёрный циферблат радиуса r.</summary>
    protected void Gauge(Vector2 c, float r)
    {
        MonitorDraw.Disc(Root, c + new Vector2(r * 0.03f, -r * 0.04f), r * 1.16f, BezelShadow);
        MonitorDraw.Disc(Root, c, r * 1.12f, BezelColor);
        MonitorDraw.Ring(Root, c, r * 1.0f, r * 1.04f, new Color(0.35f, 0.38f, 0.42f, 1f));
        MonitorDraw.Disc(Root, c, r, FaceColor);
    }

    /// <summary>Ось стрелки и блик стекла — поверх стрелок.</summary>
    protected void GaugeTop(Vector2 c, float r, float hub, Color hubColor)
    {
        MonitorDraw.Disc(Root, c, hub, hubColor);
        MonitorDraw.Disc(Root, c, hub * 0.4f, BezelShadow);
        MonitorDraw.GlareOn(Root, c, r, 0.09f);
    }

    /// <summary>Табло: утопленное окно, «погашенные» 8 под живыми красными цифрами.</summary>
    protected TextMeshProUGUI Digits(string ghost, Vector2 pos, Vector2 size, float fontSize)
    {
        fontSize = Mathf.Min(fontSize, size.x / (ghost.Length * 0.72f));   // цифры — в окне табло
        MonitorDraw.Rect(Root, pos, size + new Vector2(S * 0.02f, S * 0.02f), new Color(0.3f, 0.34f, 0.38f, 1f), "DigitsFrame");
        RectTransform box = MonitorDraw.Rect(Root, pos, size, new Color(0.012f, 0.012f, 0.015f, 1f), "Digits").rectTransform;
        TextMeshProUGUI g = Text(ghost, Vector2.zero, size, fontSize, DigitGhost, TextAlignmentOptions.Center, box);
        g.fontStyle = FontStyles.Bold;
        g.characterSpacing = 8f;
        TextMeshProUGUI t = Text("", Vector2.zero, size, fontSize, DigitColor, TextAlignmentOptions.Center, box);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 8f;
        return t;
    }

    /// <summary>Деления и подписи шкалы по GaugeScale.</summary>
    protected void Scale(Vector2 c, GaugeScale s, float r, double minor, double major, Func<double, string> label, float labelR, float fontSize)
    {
        foreach (double v in s.Ticks(minor))
        {
            bool big = Math.Abs(Math.IEEERemainder(v, major)) < minor * 0.01;
            float a = (float)s.AngleOf(v);
            MonitorDraw.Tick(Root, c, a, big ? r * 0.84f : r * 0.91f, r, big ? r * 0.026f : r * 0.013f, TickColor);
            if (big && label != null)
                Text(label(v), c + MonitorDraw.Dir(a) * labelR, new Vector2(fontSize * 3f, fontSize * 1.4f), fontSize, LabelColor);
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
        float r = Landscape ? Mathf.Min(H * 0.34f, W * 0.24f) : Mathf.Min(W * 0.36f, H * 0.26f);
        Vector2 c = Landscape ? new Vector2(-W * 0.5f + r * 1.12f + W * 0.07f, -H * 0.06f) : new Vector2(0f, H * 0.5f - r * 1.12f - H * 0.14f);
        Gauge(c, r);

        card = MonitorDraw.Node(Root, "Card", c, new Vector2(2f * r, 2f * r));
        for (int b = 0; b < 360; b += 5)
        {
            bool major = b % 30 == 0, mid = b % 10 == 0;
            MonitorDraw.Tick(card, Vector2.zero, b, major ? r * 0.78f : mid ? r * 0.86f : r * 0.91f, r * 0.97f, major ? r * 0.03f : r * 0.013f, TickColor);
            if (!major) continue;
            string l = b == 0 ? "N" : b == 90 ? "E" : b == 180 ? "S" : b == 270 ? "W" : (b / 10).ToString();
            TextMeshProUGUI t = Text(l, MonitorDraw.Dir(b) * (r * 0.62f), new Vector2(r * 0.4f, r * 0.24f), r * (b % 90 == 0 ? 0.21f : 0.15f),
                                     b == 0 ? ActualColor : LabelColor, TextAlignmentOptions.Center, card);
            t.fontStyle = b % 90 == 0 ? FontStyles.Bold : FontStyles.Normal;
            t.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -b);   // подпись — по радиусу, как на картушке
        }
        // Курсовая черта — неподвижная, на ободе сверху
        MonitorDraw.Tick(Root, c, 0f, r * 0.9f, r * 1.12f, r * 0.05f, ActualColor);
        GaugeTop(c, r, r * 0.07f, BezelColor);

        float colX = Landscape ? c.x + r * 1.3f : -W * 0.4f;
        float colW = Landscape ? W * 0.5f - W * 0.07f - colX : W * 0.8f;
        float cx = colX + colW * 0.5f;
        float top = Landscape ? H * 0.12f : c.y - r * 1.45f;
        float digH = Landscape ? H * 0.24f : H * 0.12f;
        Text("КУРС, °", new Vector2(cx, top + digH * 0.85f), new Vector2(colW, digH * 0.4f), digH * 0.28f, EngraveColor);
        hdg = Digits("888,8", new Vector2(cx, top), new Vector2(colW, digH), digH * 0.8f);
        rot = Text("", new Vector2(cx, top - digH * 1.0f), new Vector2(colW, digH * 0.45f), digH * 0.3f, LabelColor);
        speed = Text("", new Vector2(cx, top - digH * 1.45f), new Vector2(colW, digH * 0.45f), digH * 0.3f, LabelColor);
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
        bool still = Mathf.Abs(r) < 0.5f;
        rot.text = $"ПОВОРОТ  {(still ? "0" : (r > 0f ? "+" : "−") + Mathf.Abs(r).ToString("0"))} °/мин {(still ? "" : r > 0f ? "ПБ" : "ЛБ")}";
        speed.text = $"СКОРОСТЬ  {ScenarioText.Num(M.Nav.SpeedKnots, 1)} уз";
    }
}

// ─────────── M3: руль ───────────
public sealed class RudderScreen : MonitorScreen
{
    private RectTransform ordered, actual, thrusterFill;
    private TextMeshProUGUI orderedText, actualText, alarm, thrusterText;
    private GaugeScale scale;
    private float thrusterHalf;

    public RudderScreen() => Title = "Руль";

    protected override void Build()
    {
        Header("РУЛЬ");
        float max = M.Nav != null && M.Nav.parameters != null ? M.Nav.parameters.maxRudderAngle : 35f;
        scale = new GaugeScale(-max, max, -70, 70);
        bool thr = M.Thruster != null;
        float r = Mathf.Min(W * 0.42f, H * 0.58f);
        Vector2 c = new Vector2(0f, -H * 0.5f + H * (thr ? 0.36f : 0.3f));
        // Сектор-циферблат в металлическом ободе
        MonitorDraw.Arc(Root, c, -80f, 80f, r * 0.0f, r * 1.1f, BezelColor);
        MonitorDraw.Arc(Root, c, -78f, 78f, r * 0.0f, r * 1.02f, FaceColor);
        MonitorDraw.Arc(Root, c, -70f, 0f, r * 0.92f, r, PortColor);
        MonitorDraw.Arc(Root, c, 0f, 70f, r * 0.92f, r, StbdColor);
        Scale(c, scale, r * 0.91f, 5, 10, v => Math.Abs(v).ToString("0"), r * 0.66f, r * 0.11f);
        Text("ЛБ", c + MonitorDraw.Dir(-58f) * (r * 0.42f), new Vector2(r * 0.3f, r * 0.15f), r * 0.11f, PortColor).fontStyle = FontStyles.Bold;
        Text("ПБ", c + MonitorDraw.Dir(58f) * (r * 0.42f), new Vector2(r * 0.3f, r * 0.15f), r * 0.11f, StbdColor).fontStyle = FontStyles.Bold;
        ordered = MonitorDraw.Needle(Root, c, r * 0.9f, r * 0.022f, r * 0.1f, OrderedColor);
        actual = MonitorDraw.Needle(Root, c, r * 0.8f, r * 0.05f, r * 0.12f, ActualColor);
        GaugeTop(c, r, r * 0.08f, BezelColor);

        float rowY = c.y - r * 0.16f, fs = S * 0.075f;
        orderedText = Text("", new Vector2(-W * 0.28f, rowY), new Vector2(W * 0.4f, fs * 1.4f), fs, OrderedColor);
        actualText = Text("", new Vector2(W * 0.28f, rowY), new Vector2(W * 0.4f, fs * 1.4f), fs, ActualColor);
        orderedText.fontStyle = actualText.fontStyle = FontStyles.Bold;
        alarm = Text("", new Vector2(W * 0.28f, H * 0.5f - S * 0.1f), new Vector2(W * 0.38f, S * 0.09f), S * 0.06f, AlarmColor, TextAlignmentOptions.MidlineRight);
        alarm.fontStyle = FontStyles.Bold;

        if (!thr) return;
        // Подруливающее: полоса −100…+100 % от середины
        float by = -H * 0.5f + S * 0.12f, bw = W * 0.5f, bh = S * 0.05f;
        thrusterHalf = bw * 0.5f;
        Text("ПОДРУЛ.", new Vector2(-W * 0.5f + W * 0.14f, by), new Vector2(W * 0.2f, bh * 2f), fs * 0.7f, EngraveColor);
        MonitorDraw.Rect(Root, new Vector2(0f, by), new Vector2(bw + S * 0.012f, bh + S * 0.012f), new Color(0.3f, 0.34f, 0.38f, 1f), "ThrusterFrame");
        RectTransform track = MonitorDraw.Rect(Root, new Vector2(0f, by), new Vector2(bw, bh), FaceColor, "ThrusterTrack").rectTransform;
        MonitorDraw.Rect(track, Vector2.zero, new Vector2(2f, bh * 1.4f), TickColor, "Mid");
        thrusterFill = MonitorDraw.Rect(track, Vector2.zero, new Vector2(0f, bh * 0.6f), ActualColor, "Fill").rectTransform;
        thrusterText = Text("", new Vector2(W * 0.5f - W * 0.14f, by), new Vector2(W * 0.2f, bh * 2f), fs * 0.7f, LabelColor);
    }

    public override void Needles()
    {
        if (M.Nav == null) return;
        MonitorDraw.Point(ordered, (float)scale.AngleOf(M.Nav.RudderOrdered));
        MonitorDraw.Point(actual, (float)scale.AngleOf(M.Nav.RudderActual));
    }

    public override void Texts()
    {
        if (M.Nav == null) return;
        orderedText.text = "ЗАДАН.  " + ConningText.Rudder(M.Nav.RudderOrdered);
        actualText.text = "ФАКТ.  " + ConningText.Rudder(M.Nav.RudderActual);
        alarm.text = BridgeMonitors.FailureLine.Contains("РУЛ") && BridgeMonitors.BlinkOn ? "ОТКАЗ РУЛЯ" : "";
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
    private RectTransform needle;
    private TextMeshProUGUI rpm, order, status;
    private GaugeScale scale;

    public EngineScreen() => Title = "Машина";

    protected override void Build()
    {
        Header("ГЛАВНЫЙ ДВИГАТЕЛЬ");
        double n = Math.Max(100.0, M.ratedRpm);
        scale = new GaugeScale(-n, n, -135, 135);
        float r = Landscape ? Mathf.Min(H * 0.34f, W * 0.24f) : Mathf.Min(W * 0.36f, H * 0.26f);
        Vector2 c = Landscape ? new Vector2(-W * 0.5f + r * 1.12f + W * 0.07f, -H * 0.06f) : new Vector2(0f, H * 0.5f - r * 1.12f - H * 0.14f);
        Gauge(c, r);
        MonitorDraw.Arc(Root, c, -135f, 0f, r * 0.92f, r * 0.98f, PortColor);
        MonitorDraw.Arc(Root, c, 0f, 135f, r * 0.92f, r * 0.98f, StbdColor);
        Scale(c, scale, r * 0.91f, n / 15.0, n / 3.0, v => Math.Abs(v).ToString("0"), r * 0.66f, r * 0.12f);
        Text("об/мин", c + new Vector2(0f, r * 0.3f), new Vector2(r, r * 0.15f), r * 0.1f, MutedColor);
        Text("НАЗАД", c + new Vector2(-r * 0.34f, -r * 0.76f), new Vector2(r * 0.6f, r * 0.15f), r * 0.1f, PortColor);
        Text("ВПЕРЁД", c + new Vector2(r * 0.34f, -r * 0.76f), new Vector2(r * 0.6f, r * 0.15f), r * 0.1f, StbdColor);
        rpm = Digits("+888", c + new Vector2(0f, -r * 0.42f), new Vector2(r * 0.78f, r * 0.26f), r * 0.2f);   // цифровая вставка
        needle = MonitorDraw.Needle(Root, c, r * 0.86f, r * 0.045f, r * 0.14f, TickColor);
        GaugeTop(c, r, r * 0.09f, ActualColor);

        float colX = Landscape ? c.x + r * 1.3f : -W * 0.4f;
        float colW = Landscape ? W * 0.5f - W * 0.07f - colX : W * 0.8f;
        float cx = colX + colW * 0.5f;
        float top = Landscape ? H * 0.1f : c.y - r * 1.45f;
        float fs = S * 0.072f;
        Text("ТЕЛЕГРАФ", new Vector2(cx, top + fs * 1.3f), new Vector2(colW, fs * 1.2f), fs * 0.72f, EngraveColor);
        order = Text("", new Vector2(cx, top), new Vector2(colW, fs * 1.5f), fs * 1.05f, Color.white);
        order.fontStyle = FontStyles.Bold;
        order.enableAutoSizing = true;
        order.fontSizeMin = fs * 0.5f;
        order.fontSizeMax = fs * 1.05f;
        // Лампа состояния ГД
        MonitorDraw.Disc(Root, new Vector2(colX + fs * 0.5f, top - fs * 1.9f), fs * 0.42f, new Color(0.3f, 0.34f, 0.38f, 1f));
        lamp = MonitorDraw.Disc(Root, new Vector2(colX + fs * 0.5f, top - fs * 1.9f), fs * 0.34f, OkColor);
        status = Text("", new Vector2(cx + fs * 0.6f, top - fs * 1.9f), new Vector2(colW - fs * 1.2f, fs * 1.3f), fs * 0.8f, OkColor);
        status.fontStyle = FontStyles.Bold;
    }

    private Image lamp;

    public override void Needles() => MonitorDraw.Point(needle, (float)scale.AngleOf(M.Engine.Rpm));

    public override void Texts()
    {
        rpm.text = ConningText.Rpm(M.Engine.Rpm);
        order.text = ConningText.Telegraph(M.Nav != null ? M.Nav.TelegraphPosition : "");
        bool failed = M.Nav != null && M.Nav.EngineFailed && BridgeMonitors.FailureLine.Contains("ГД");
        bool on = !failed || BridgeMonitors.BlinkOn;
        status.text = failed ? "ОТКАЗ ГД" : "ГД В РАБОТЕ";
        status.color = failed ? AlarmColor : OkColor;
        lamp.color = failed ? (on ? AlarmColor : new Color(0.25f, 0.05f, 0.04f, 1f)) : OkColor;
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
        Header($"{Label} · {Title.ToUpperInvariant()}");
        float fs = S * 0.085f;
        Text(Label, new Vector2(0f, fs * 0.9f), new Vector2(W * 0.8f, fs * 2f), fs * 1.6f, MutedColor).fontStyle = FontStyles.Bold;
        Text($"{ScreenName} · будет в {part}", new Vector2(0f, -fs * 0.8f), new Vector2(W * 0.8f, fs * 1.2f), fs * 0.6f, MutedColor);
    }
}
