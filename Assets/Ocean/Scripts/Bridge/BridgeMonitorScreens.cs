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
//   • AlarmScreen (M9, Cube.6286) — эхолот и сигнализация мели (как полоса NavDisplay): глубина, запас под килем,
//     табло «МЕЛЬ» (мигает при опасном приближении, горит на мели), опасность, «КВИТ.» (по проверке 16а-2);
//   • StandbyScreen — остальные экраны: прибор в режиме ожидания (название, тёмный экран, лампа «ОЖИДАНИЕ»);
//     номер экрана, часть модели и запрос, в котором экран заполнится, — только в консоли (по проверке 16а-2).
// Тексты не выходят за плиту: рамка текста ужимается в пределы плиты, шрифт уменьшается до половины, дальше — «…»;
// не поместившиеся тексты BridgeMonitors пишет в консоль (Overflowing).
// Рисование — стандартными Image (MonitorDraw). Размеры — доли сторон холста (ширина — BridgeMonitors.canvasWidthPx).
// ============================================================
using System;
using System.Collections.Generic;
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

    /// <summary>Label (M1…M9, P), ScreenName (Cube.…), Title, Note — только для консоли, на экран не выводятся.</summary>
    public string Label = "", ScreenName = "", Title = "", Note = "";
    protected RectTransform Root;
    protected Vector2 Size;
    protected BridgeMonitors M;
    protected float W => Size.x;
    protected float H => Size.y;
    protected float S => Mathf.Min(W, H);
    protected float Frame => S * 0.035f;            // светлая рамка панели
    protected float Margin => Frame + S * 0.08f;    // поле от края холста до содержимого (винты — в углах поля)
    protected bool Landscape => W >= 1.2f * H;
    private readonly List<TextMeshProUGUI> texts = new List<TextMeshProUGUI>();

    public static MonitorScreen For(int index, string label, string screenName, bool big)
    {
        MonitorScreen s = big ? new StandbyScreen("Карта (копия планшета: маршрут, курс, цели)", "КАРТА", "16б")
            : index == 1 ? new EngineScreen()
            : index == 2 ? new RudderScreen()
            : index == 3 ? new HeadingScreen()
            : index == 0 ? new StandbyScreen("Задание и сообщения", "СООБЩЕНИЯ", "16в")
            : index == 4 ? new StandbyScreen("Скорость и поворот", "СКОРОСТЬ И ПОВОРОТ", "16б")
            : index == 5 ? new StandbyScreen("Маршрут", "МАРШРУТ", "16б")
            : index == 6 ? new StandbyScreen("Цели (CPA, TCPA)", "ЦЕЛИ", "16в")
            : index == 7 ? new StandbyScreen("Ветер и погода", "ВЕТЕР И ПОГОДА", "16в")
            : (MonitorScreen)new AlarmScreen();
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
        float f = Frame;
        MonitorDraw.Rect(Root, Vector2.zero, new Vector2(W - 2f * f, H - 2f * f), PlateColor, "Plate");
        float sr = S * 0.022f, sx = W * 0.5f - f - sr * 1.8f, sy = H * 0.5f - f - sr * 1.8f;
        foreach (Vector2 p in new[] { new Vector2(-sx, sy), new Vector2(sx, sy), new Vector2(-sx, -sy), new Vector2(sx, -sy) })
        {
            MonitorDraw.Disc(Root, p, sr, ScrewColor);
            RectTransform slot = MonitorDraw.Rect(Root, p, new Vector2(sr * 1.5f, sr * 0.3f), BezelShadow, "Slot").rectTransform;
            slot.localRotation = Quaternion.Euler(0f, 0f, 35f);
        }
    }

    /// <summary>Гравированный заголовок панели вверху слева — по ширине плиты между винтами.</summary>
    protected void Header(string text)
    {
        TextMeshProUGUI t = Text(text, new Vector2(-W * 0.5f + Margin, H * 0.5f - Frame - S * 0.065f), new Vector2(W - 2f * Margin, S * 0.08f),
                                 S * 0.055f, EngraveColor, TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f));
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 6f;
    }

    // ─────────── Элементы ───────────
    /// <summary>
    /// Текст в рамке size: не шире плиты (рамка ужимается и сдвигается внутрь), шрифт — от fontSize до половины,
    /// не поместилось и так — «…». pivot — точка рамки в pos (по умолчанию центр).
    /// </summary>
    protected TextMeshProUGUI Text(string text, Vector2 pos, Vector2 size, float fontSize, Color c,
                                   TextAlignmentOptions align = TextAlignmentOptions.Center, RectTransform parent = null,
                                   Vector2? pivot = null)
    {
        Vector2 pv = pivot ?? new Vector2(0.5f, 0.5f);
        size.y = Mathf.Max(size.y, fontSize * 1.3f);   // по высоте шрифт не ужимается
        if (parent == null) FitInPlate(ref pos, ref size, pv);
        RectTransform rt = MonitorDraw.Node(parent != null ? parent : Root, "Text", pos, size, pv);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = fontSize;
        t.fontSizeMin = fontSize * 0.5f;
        t.color = c;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        texts.Add(t);
        return t;
    }

    // Рамка текста (в координатах холста) — внутри плиты: сначала не больше плиты, потом сдвиг внутрь
    private void FitInPlate(ref Vector2 pos, ref Vector2 size, Vector2 pivot)
    {
        float m = Frame + S * 0.01f;
        for (int k = 0; k < 2; k++)
        {
            float half = (k == 0 ? W : H) * 0.5f - m;
            float len = Mathf.Min(size[k], 2f * half);
            float lo = pos[k] - pivot[k] * len;
            size[k] = len;
            pos[k] += Mathf.Max(0f, -half - lo) - Mathf.Max(0f, lo + len - half);
        }
    }

    /// <summary>Тексты, которые не поместились даже мелким шрифтом (обрезаны «…») — для консоли.</summary>
    public void Overflowing(List<string> into)
    {
        foreach (TextMeshProUGUI t in texts)
        {
            if (t == null || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
            t.ForceMeshUpdate();
            if (t.isTextOverflowing || t.isTextTruncated) into.Add($"{Label} «{t.text}»");
        }
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
        TextMeshProUGUI g = DigitText(ghost, size, fontSize, DigitGhost, TextAlignmentOptions.Center, box);
        g.characterSpacing = 8f;
        TextMeshProUGUI t = DigitText("", size, fontSize, DigitColor, TextAlignmentOptions.Center, box);
        t.characterSpacing = 8f;
        return t;
    }

    /// <summary>Цифры табло: размер шрифта постоянный (живые — точно поверх «погашенных»), окно уже внутри плиты.</summary>
    protected TextMeshProUGUI DigitText(string text, Vector2 size, float fontSize, Color c, TextAlignmentOptions align, RectTransform box, Vector2 pos = default)
    {
        TextMeshProUGUI t = Text(text, pos, size, fontSize, c, align, box);
        t.enableAutoSizing = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.fontStyle = FontStyles.Bold;
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

// ─────────── M9: глубина и сигнализация мели ───────────
// То же, что полоса над NavDisplay (B1): глубина по карте под судном, запас под килем; табло «МЕЛЬ» — тёмное,
// мигает при опасном приближении (горит ровно после квитирования), горит на мели; ниже — опасность и «КВИТ.».
public sealed class AlarmScreen : MonitorScreen
{
    private static readonly Color TileIdle = new Color(0.1f, 0.035f, 0.03f, 1f);
    private static readonly Color TileIdleText = new Color(0.32f, 0.12f, 0.1f, 1f);
    private const string Mono = "<mspace=0.6em>";   // цифры одной ширины — живые ложатся на «погашенные» 8

    private TextMeshProUGUI depth, keel, hazard, ack, tileText;
    private Image tile;

    public AlarmScreen() => Title = "Глубина и сигнализация мели";

    protected override void Build()
    {
        Header("ЭХОЛОТ · СИГНАЛИЗАЦИЯ");
        float colW = (W - 2f * Margin) * 0.46f;
        float lx = -W * 0.5f + Margin + colW * 0.5f, rx = W * 0.5f - Margin - colW * 0.5f;
        float digH = H * 0.26f, fs = S * 0.07f;

        // Слева — эхолот
        Text("ГЛУБИНА, м", new Vector2(lx, H * 0.2f), new Vector2(colW, fs * 1.2f), fs * 0.8f, EngraveColor);
        MonitorDraw.Rect(Root, new Vector2(lx, -H * 0.01f), new Vector2(colW + S * 0.02f, digH + S * 0.02f), new Color(0.3f, 0.34f, 0.38f, 1f), "DigitsFrame");
        RectTransform box = MonitorDraw.Rect(Root, new Vector2(lx, -H * 0.01f), new Vector2(colW, digH), new Color(0.012f, 0.012f, 0.015f, 1f), "Digits").rectTransform;
        float dfs = Mathf.Min(digH * 0.75f, colW / (5 * 0.72f));
        DigitText(Mono + "888,8", new Vector2(colW * 0.88f, digH), dfs, DigitGhost, TextAlignmentOptions.MidlineRight, box);
        depth = DigitText("", new Vector2(colW * 0.88f, digH), dfs, DigitColor, TextAlignmentOptions.MidlineRight, box);
        keel = Text("", new Vector2(lx, -H * 0.24f), new Vector2(colW, fs * 1.3f), fs * 0.85f, LabelColor);

        // Справа — табло «МЕЛЬ»
        Vector2 tp = new Vector2(rx, H * 0.06f), ts = new Vector2(colW, H * 0.3f);
        MonitorDraw.Rect(Root, tp, ts + new Vector2(S * 0.024f, S * 0.024f), new Color(0.3f, 0.34f, 0.38f, 1f), "TileFrame");
        tile = MonitorDraw.Rect(Root, tp, ts, TileIdle, "Tile");
        tileText = Text("МЕЛЬ", Vector2.zero, ts * 0.9f, ts.y * 0.42f, TileIdleText, TextAlignmentOptions.Center, tile.rectTransform);
        tileText.fontStyle = FontStyles.Bold;
        hazard = Text("", new Vector2(rx, -H * 0.17f), new Vector2(colW, fs * 1.2f), fs * 0.7f, LabelColor);
        ack = Text("", new Vector2(rx, -H * 0.27f), new Vector2(colW, fs * 1.2f), fs * 0.7f, MutedColor);
    }

    public override void Texts()
    {
        GroundingMonitor g = M.Grounding;
        float d = g != null ? g.DepthUnderShip : float.NaN;
        depth.text = float.IsNaN(d) ? Mono + "  —  " : Mono + ScenarioText.Num(Mathf.Min(d, 999.9f), 1).PadLeft(5);
        keel.text = float.IsNaN(d) ? "ПОД КИЛЕМ  —" : $"ПОД КИЛЕМ  {ScenarioText.Num(d - g.Draft, 1)} м";
        GroundingState st = g != null ? g.State : GroundingState.Clear;
        bool lit = st == GroundingState.Grounded || (st == GroundingState.Warning && (g.AlarmAcknowledged || BridgeMonitors.BlinkOn));
        tile.color = lit ? AlarmColor : TileIdle;
        tileText.color = lit ? Color.white : st == GroundingState.Warning ? new Color(0.55f, 0.15f, 0.12f, 1f) : TileIdleText;
        HazardInfo h = st == GroundingState.Grounded ? g.GroundedOn : g != null ? g.Nearest : default;
        tileText.text = st == GroundingState.Grounded ? "НА МЕЛИ"
            : st == GroundingState.Warning && h.Valid ? $"МЕЛЬ {ScenarioText.Num(h.Distance / WaterArea.CableMeters, 1)} кбт" : "МЕЛЬ";
        hazard.text = st != GroundingState.Clear ? BridgeNavDisplay.Hazard(h) : "";
        ack.text = st != GroundingState.Clear && g.AlarmAcknowledged ? "КВИТ." : "";
    }
}

// ─────────── Остальные экраны (16б, 16в): прибор в режиме ожидания ───────────
// Название прибора, тёмный экран с едва заметной сеткой, янтарная лампа «ОЖИДАНИЕ» — как включённый прибор без данных.
public sealed class StandbyScreen : MonitorScreen
{
    private readonly string header;

    public StandbyScreen(string title, string header, string part)
    {
        Title = title;
        this.header = header;
        Note = $"ожидание, заполнится в {part}";
    }

    protected override void Build()
    {
        Header(header);
        float top = H * 0.5f - Frame - S * 0.13f, bottom = -H * 0.5f + Frame + S * 0.1f;
        Vector2 c = new Vector2(0f, (top + bottom) * 0.5f), size = new Vector2(W - 2f * Margin, top - bottom);
        MonitorDraw.Rect(Root, c, size + new Vector2(S * 0.02f, S * 0.02f), new Color(0.3f, 0.34f, 0.38f, 1f), "ScreenFrame");
        RectTransform screen = MonitorDraw.Rect(Root, c, size, new Color(0.012f, 0.014f, 0.017f, 1f), "Screen").rectTransform;
        Color grid = new Color(1f, 1f, 1f, 0.035f);
        for (int i = 1; i < 4; i++) MonitorDraw.Rect(screen, new Vector2(size.x * (i / 4f - 0.5f), 0f), new Vector2(1.5f, size.y), grid, "Grid");
        for (int j = 1; j < 3; j++) MonitorDraw.Rect(screen, new Vector2(0f, size.y * (j / 3f - 0.5f)), new Vector2(size.x, 1.5f), grid, "Grid");

        float fs = S * 0.05f, ly = -H * 0.5f + Frame + S * 0.05f, lx = -W * 0.5f + Margin + fs * 0.5f;
        MonitorDraw.Disc(Root, new Vector2(lx, ly), fs * 0.42f, new Color(0.3f, 0.34f, 0.38f, 1f));
        MonitorDraw.Disc(Root, new Vector2(lx, ly), fs * 0.32f, new Color(0.85f, 0.6f, 0.15f, 1f));
        Text("ОЖИДАНИЕ", new Vector2(lx + fs * 0.8f, ly), new Vector2(W * 0.4f, fs * 1.2f), fs, MutedColor,
             TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f)).characterSpacing = 4f;
    }
}
