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
//   • 16б: SpeedScreen (M5) — указатель поворота, табло скорости, лагом; RouteScreen (M6) — карта с маршрутом, коридором
//     и целями, следующая точка, XTE; TargetsScreen (M7) — таблица САРП; WindScreen (M8) — анемометр, погода;
//     MessagesScreen (M1) — состояние упражнения и журнал событий (данные — Core MonitorData);
//   • StandbyScreen — прибор в режиме ожидания (название, тёмный экран, лампа «ОЖИДАНИЕ») — для экрана без данных
//     (сейчас только скрытая большая панель); номер экрана и часть модели — только в консоли (по проверке 16а-2).
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
            : index == 0 ? new MessagesScreen()
            : index == 4 ? new SpeedScreen()
            : index == 5 ? new RouteScreen()
            : index == 6 ? new TargetsScreen()
            : index == 7 ? new WindScreen()
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

    // Моноширинные цифры (16б): живые ложатся точно на «погашенные» 8 при любом числе знаков и знаке «−»
    protected const string Mono = "<mspace=0.6em>";
    protected static string MonoText(string s, int cells) => Mono + s.PadLeft(cells);

    /// <summary>Табло на cells знакоместах (последнее — после запятой): окно в рамке, «888,8» и живые цифры справа.</summary>
    protected TextMeshProUGUI MonoDigits(int cells, Vector2 pos, Vector2 size)
    {
        MonitorDraw.Rect(Root, pos, size + new Vector2(S * 0.02f, S * 0.02f), new Color(0.3f, 0.34f, 0.38f, 1f), "DigitsFrame");
        RectTransform box = MonitorDraw.Rect(Root, pos, size, new Color(0.012f, 0.012f, 0.015f, 1f), "Digits").rectTransform;
        float fs = Mathf.Min(size.y * 0.75f, size.x * 0.88f / (cells * 0.62f));
        Vector2 inner = new Vector2(size.x * 0.9f, size.y);
        DigitText(MonoText(new string('8', cells - 2) + ",8", cells), inner, fs, DigitGhost, TextAlignmentOptions.MidlineRight, box);
        return DigitText("", inner, fs, DigitColor, TextAlignmentOptions.MidlineRight, box);
    }

    /// <summary>Круглый прибор слева (на узком экране — сверху): центр и радиус, как у «Машины».</summary>
    protected void LeftGauge(out Vector2 c, out float r)
    {
        r = Landscape ? Mathf.Min(H * 0.34f, W * 0.24f) : Mathf.Min(W * 0.36f, H * 0.26f);
        c = Landscape ? new Vector2(-W * 0.5f + r * 1.12f + W * 0.07f, -H * 0.06f) : new Vector2(0f, H * 0.5f - r * 1.12f - H * 0.14f);
    }

    /// <summary>Столбец справа от прибора (на узком экране — под ним): левый край, ширина, центр.</summary>
    protected void RightColumn(Vector2 c, float r, out float colX, out float colW, out float cx)
    {
        colX = Landscape ? c.x + r * 1.3f : -W * 0.4f;
        colW = Landscape ? W * 0.5f - W * 0.07f - colX : W * 0.8f;
        cx = colX + colW * 0.5f;
    }

    /// <summary>Строка столбца: текст слева, по ширине столбца.</summary>
    protected TextMeshProUGUI ColumnLine(float colX, float colW, float y, float fs, Color c) =>
        Text("", new Vector2(colX, y), new Vector2(colW, fs * 1.3f), fs, c, TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f));

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

// ─────────── M5: скорость и поворот ───────────
// Указатель угловой скорости −120…+120 °/мин (0 — вверху, ЛБ — красный, ПБ — зелёный), табло скорости по лагу
// (продольная u, уз; назад — «−»), поперечная скорость — только в модели 3 СС (в модели Номото её нет — «—»).
public sealed class SpeedScreen : MonitorScreen
{
    private const double MaxRot = 120.0;
    private RectTransform needle;
    private TextMeshProUGUI speed, rotText, sway;
    private GaugeScale scale;

    public SpeedScreen() => Title = "Скорость и поворот";

    protected override void Build()
    {
        Header("СКОРОСТЬ · ПОВОРОТ");
        scale = new GaugeScale(-MaxRot, MaxRot, -135, 135);
        LeftGauge(out Vector2 c, out float r);
        Gauge(c, r);
        MonitorDraw.Arc(Root, c, -135f, 0f, r * 0.92f, r * 0.98f, PortColor);
        MonitorDraw.Arc(Root, c, 0f, 135f, r * 0.92f, r * 0.98f, StbdColor);
        Scale(c, scale, r * 0.91f, 10, 30, v => Math.Abs(v).ToString("0"), r * 0.66f, r * 0.12f);
        Text("°/мин", c + new Vector2(0f, r * 0.3f), new Vector2(r, r * 0.15f), r * 0.1f, MutedColor);
        Text("ЛБ", c + new Vector2(-r * 0.34f, -r * 0.76f), new Vector2(r * 0.6f, r * 0.15f), r * 0.11f, PortColor).fontStyle = FontStyles.Bold;
        Text("ПБ", c + new Vector2(r * 0.34f, -r * 0.76f), new Vector2(r * 0.6f, r * 0.15f), r * 0.11f, StbdColor).fontStyle = FontStyles.Bold;
        rotText = Text("", c + new Vector2(0f, -r * 0.42f), new Vector2(r, r * 0.22f), r * 0.17f, LabelColor);
        rotText.fontStyle = FontStyles.Bold;
        needle = MonitorDraw.Needle(Root, c, r * 0.86f, r * 0.045f, r * 0.14f, TickColor);
        GaugeTop(c, r, r * 0.09f, ActualColor);

        RightColumn(c, r, out float colX, out float colW, out float cx);
        float top = Landscape ? H * 0.12f : c.y - r * 1.45f;
        float digH = Landscape ? H * 0.24f : H * 0.12f;
        Text("СКОРОСТЬ, уз", new Vector2(cx, top + digH * 0.85f), new Vector2(colW, digH * 0.4f), digH * 0.28f, EngraveColor);
        speed = MonoDigits(5, new Vector2(cx, top), new Vector2(colW, digH));
        sway = ColumnLine(colX, colW, top - digH * 1.0f, digH * 0.3f, LabelColor);
    }

    public override void Needles()
    {
        if (M.Nav != null) MonitorDraw.Point(needle, (float)scale.AngleOf(M.Nav.RateOfTurn));
    }

    public override void Texts()
    {
        if (M.Nav == null) return;
        speed.text = MonoText(MonitorText.Speed(M.Nav.SpeedKnots), 5);
        float r = M.Nav.RateOfTurn;
        bool still = Mathf.Abs(r) < 0.5f;
        rotText.text = still ? "0" : (r > 0f ? "+" : "−") + ScenarioText.Num(Mathf.Abs(r));
        rotText.color = still ? LabelColor : r > 0f ? StbdColor : PortColor;
        if (!M.Nav.Uses3Dof) { sway.text = "ЛАГОМ  —"; return; }
        float v = M.Nav.SwayKnots;
        sway.text = $"ЛАГОМ  {ScenarioText.Num(Mathf.Abs(v), 2)} уз{(Mathf.Abs(v) < 0.05f ? "" : v > 0f ? " ПБ" : " ЛБ")}";
    }
}

// ─────────── M6: маршрут и карта ───────────
// Карта «север вверху», своё судно в центре (точка и линия курса), линия маршрута с коридором (ширина 2·XTE_доп),
// точки (следующая — янтарная), другие суда — точки с вектором скорости на 6 мин (опасные — красные, как на M7).
// Масштаб — MapRange по дистанции до следующей точки (без маршрута — до ближайшей цели, иначе 1 миля).
// Справа — следующая точка, дистанция, курс участка, XTE / предел, «ВНЕ КОРИДОРА». Карта обрезается окном (RectMask2D).
public sealed class RouteScreen : MonitorScreen
{
    private static readonly Color RouteColor = new Color(0.3f, 0.76f, 0.98f, 1f);
    private static readonly Color PassedColor = new Color(0.3f, 0.76f, 0.98f, 0.35f);
    private static readonly Color CorridorColor = new Color(0.3f, 0.76f, 0.98f, 0.13f);
    private static readonly Color RingColor = new Color(1f, 1f, 1f, 0.12f);
    private const float VectorMin = 6f;   // вектор скорости цели, мин

    private RectTransform corridorLayer, legLayer, pointLayer, targetLayer, own;
    private float mapR;
    private readonly List<Image> corridor = new List<Image>(), legs = new List<Image>(), points = new List<Image>(),
                                 dots = new List<Image>(), vectors = new List<Image>();
    private TextMeshProUGUI scaleText, point, pointName, dist, course, xte, alarm;

    public RouteScreen() => Title = "Маршрут и карта";

    protected override void Build()
    {
        Header("МАРШРУТ · КАРТА");
        float top = H * 0.5f - Frame - S * 0.13f, bottom = -H * 0.5f + Frame + S * 0.06f;
        float mh = top - bottom, mw = Mathf.Min(mh * 1.15f, (W - 2f * Margin) * 0.52f);
        float left = -W * 0.5f + Margin;
        Vector2 mc = new Vector2(left + mw * 0.5f, (top + bottom) * 0.5f);
        MonitorDraw.Rect(Root, mc, new Vector2(mw + S * 0.02f, mh + S * 0.02f), new Color(0.3f, 0.34f, 0.38f, 1f), "MapFrame");
        RectTransform map = MonitorDraw.Rect(Root, mc, new Vector2(mw, mh), new Color(0.015f, 0.03f, 0.045f, 1f), "Map").rectTransform;
        map.gameObject.AddComponent<RectMask2D>();
        mapR = Mathf.Min(mw, mh) * 0.5f * 0.92f;
        MonitorDraw.Ring(map, Vector2.zero, mapR * 0.5f - 1.5f, mapR * 0.5f, RingColor);
        MonitorDraw.Ring(map, Vector2.zero, mapR - 1.5f, mapR, RingColor);
        // Слои по порядку рисования: коридор, линии, точки, цели, своё судно
        corridorLayer = MonitorDraw.Node(map, "Corridor", Vector2.zero, Vector2.zero);
        legLayer = MonitorDraw.Node(map, "Legs", Vector2.zero, Vector2.zero);
        pointLayer = MonitorDraw.Node(map, "Points", Vector2.zero, Vector2.zero);
        targetLayer = MonitorDraw.Node(map, "Targets", Vector2.zero, Vector2.zero);
        own = MonitorDraw.Needle(map, Vector2.zero, mapR * 0.35f, 2.5f, 0f, ActualColor);
        MonitorDraw.Disc(map, Vector2.zero, S * 0.014f, ActualColor);
        float fsMap = S * 0.045f;
        Text("N", new Vector2(-mw * 0.5f + fsMap, mh * 0.5f - fsMap), new Vector2(fsMap * 1.5f, fsMap * 1.3f), fsMap, EngraveColor,
             TextAlignmentOptions.Center, map).fontStyle = FontStyles.Bold;
        scaleText = Text("", new Vector2(-mw * 0.5f + fsMap * 0.4f, -mh * 0.5f + fsMap * 0.8f), new Vector2(mw * 0.9f, fsMap * 1.3f), fsMap,
                         MutedColor, TextAlignmentOptions.MidlineLeft, map, new Vector2(0f, 0.5f));

        float colX = left + mw + S * 0.06f, colW = W * 0.5f - Margin - colX, fs = S * 0.062f;
        point = ColumnLine(colX, colW, top - fs * 0.6f, fs * 0.9f, EngraveColor);
        pointName = ColumnLine(colX, colW, top - fs * 1.8f, fs, Color.white);
        pointName.fontStyle = FontStyles.Bold;
        dist = ColumnLine(colX, colW, top - fs * 3.2f, fs, LabelColor);
        course = ColumnLine(colX, colW, top - fs * 4.4f, fs, LabelColor);
        xte = ColumnLine(colX, colW, top - fs * 5.6f, fs, LabelColor);
        alarm = ColumnLine(colX, colW, top - fs * 7.0f, fs, AlarmColor);
        alarm.fontStyle = FontStyles.Bold;
    }

    public override void Needles()
    {
        if (M.Nav != null) MonitorDraw.Point(own, M.Nav.Heading);
    }

    public override void Texts()
    {
        if (M.Nav == null) return;
        WorldPosition p = M.Nav.PositionEastNorth;

        // Маршрут: в упражнении — по RouteTracker (активный участок), на брифинге — маршрут сценария с первого участка
        ScenarioRunner rn = ScenarioRunner.Instance;
        IReadOnlyList<RouteWaypoint> route = null;
        int leg = 0;
        bool tracked = false, finished = false;
        if (rn != null && rn.Current != null && rn.CurrentPhase != ScenarioRunner.Phase.Idle)
        {
            tracked = rn.Route.Valid && rn.CurrentPhase != ScenarioRunner.Phase.Briefing;
            route = tracked ? rn.Route.Route : rn.Current.route;
            leg = tracked ? rn.Route.Leg : 0;
            finished = tracked && rn.Route.Finished;
        }
        RouteReadout rr = RouteReadout.For(route, leg, p);
        List<TargetRow> rows = TargetsScreen.Rows(M);

        double rangeNm = rr.Valid && !finished ? MapRange.Choose(rr.DistanceM)
            : finished ? 0.5
            : rows.Count > 0 ? MapRange.Choose(MinRange(rows) * CpaCalculator.MetersPerNm) : 1.0;
        float k = (float)(mapR / (rangeNm * CpaCalculator.MetersPerNm));
        Vector2 P(WorldPosition w) => new Vector2((float)(w.East - p.East) * k, (float)(w.North - p.North) * k);
        scaleText.text = $"РАДИУС {(rangeNm == 0.25 ? "0,25" : ScenarioText.Num(rangeNm, rangeNm < 1 ? 1 : 0))} мили";

        int n = rr.Valid ? route.Count : 0;
        Pool(corridor, Math.Max(0, n - 1), corridorLayer, CorridorColor);
        Pool(legs, Math.Max(0, n - 1), legLayer, RouteColor);
        for (int i = 0; i + 1 < n; i++)
        {
            Vector2 a = P(route[i].position), b = P(route[i + 1].position);
            MonitorDraw.SetLine(corridor[i].rectTransform, a, b, Mathf.Max(2f, 2f * route[i + 1].xteLimit * k));
            MonitorDraw.SetLine(legs[i].rectTransform, a, b, 2.5f);
            legs[i].color = tracked && (i < leg || finished) ? PassedColor : RouteColor;
        }
        PoolPoints(n);
        for (int i = 0; i < n; i++)
        {
            points[i].rectTransform.anchoredPosition = P(route[i].position);
            points[i].color = !finished && i == rr.Next ? ActualColor : RouteColor;
        }

        // Цели — точки и векторы скорости
        int t = 0;
        foreach (ITrackedTarget tg in ExerciseTargets.All)
        {
            if (tg == null || WorkVessels.Is(tg.TargetId)) continue;
            if (t >= dots.Count)
            {
                dots.Add(MonitorDraw.Disc(targetLayer, Vector2.zero, S * 0.011f, LabelColor));
                vectors.Add(MonitorDraw.Line(targetLayer, Vector2.zero, Vector2.zero, 1.5f, LabelColor));
            }
            Vector2 a = P(tg.Position);
            float len = (float)(tg.SpeedKnots * ShipParameters.KnotsToMs * VectorMin * 60.0) * k;
            Vector2 b = a + MonitorDraw.Dir((float)tg.HeadingDeg) * len;
            bool danger = rows.Exists(row => row.Id == tg.TargetId && row.Dangerous);
            Color c = danger ? AlarmColor : LabelColor;
            dots[t].gameObject.SetActive(true);
            vectors[t].gameObject.SetActive(len > 1f);
            dots[t].rectTransform.anchoredPosition = a;
            MonitorDraw.SetLine(vectors[t].rectTransform, a, b, 1.5f);
            dots[t].color = vectors[t].color = c;
            t++;
        }
        for (int i = t; i < dots.Count; i++)
        {
            dots[i].gameObject.SetActive(false);
            vectors[i].gameObject.SetActive(false);
        }

        // Столбец справа
        dist.text = course.text = xte.text = alarm.text = "";
        if (!rr.Valid) { point.text = "МАРШРУТ НЕ ЗАДАН"; pointName.text = ""; return; }
        if (finished) { point.text = "МАРШРУТ ПРОЙДЕН"; pointName.text = route[n - 1].name; return; }
        point.text = $"ТОЧКА {rr.Next} / {rr.Count - 1}";
        pointName.text = rr.Name;
        dist.text = $"ДО ТОЧКИ  {MonitorText.Nm(rr.DistanceM / CpaCalculator.MetersPerNm)} мили";
        course.text = $"КУРС ПУТИ  {Mathf.RoundToInt((float)rr.LegCourseDeg) % 360:000}°";
        xte.text = $"ОТКЛ.  {MonitorText.Xte(rr.Xte)} / {ScenarioText.Num(rr.XteLimit)}";
        bool outside = tracked ? rn.Route.OutsideCorridor : rr.Outside;
        alarm.text = outside && BridgeMonitors.BlinkOn ? "ВНЕ КОРИДОРА" : "";
        xte.color = outside ? AlarmColor : LabelColor;
    }

    private static double MinRange(List<TargetRow> rows)
    {
        double m = double.MaxValue;
        foreach (TargetRow r in rows) m = Math.Min(m, r.RangeNm);
        return m;
    }

    private static void Pool(List<Image> list, int count, RectTransform layer, Color c)
    {
        while (list.Count < count) list.Add(MonitorDraw.Line(layer, Vector2.zero, Vector2.zero, 1f, c));
        for (int i = 0; i < list.Count; i++) list[i].gameObject.SetActive(i < count);
    }

    private void PoolPoints(int count)
    {
        float r = S * 0.018f;
        while (points.Count < count) points.Add(MonitorDraw.Ring(pointLayer, Vector2.zero, r * 0.6f, r, RouteColor));
        for (int i = 0; i < points.Count; i++) points[i].gameObject.SetActive(i < count);
    }
}

// ─────────── M7: цели ───────────
// Таблица САРП (Core TargetTable): до 4 судов — сближающиеся по CPA, затем расходящиеся по дистанции; опасная
// (CPA < 0,5 мили и 0 < TCPA ≤ 12 мин) — белым по красному, вверху справа мигает «ОПАСНАЯ ЦЕЛЬ».
public sealed class TargetsScreen : MonitorScreen
{
    private const int RowCount = 4;
    private static readonly float[] ColX = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };   // центры столбцов — доли ширины
    private readonly TextMeshProUGUI[,] cells = new TextMeshProUGUI[RowCount, 5];
    private readonly Image[] rowBg = new Image[RowCount];
    private TextMeshProUGUI empty, footer, alarm;

    public TargetsScreen() => Title = "Цели (CPA, TCPA)";

    /// <summary>Таблица целей по своему судну (и для карты M6).</summary>
    public static List<TargetRow> Rows(BridgeMonitors m)
    {
        var input = new List<TargetInput>();
        foreach (ITrackedTarget t in ExerciseTargets.All)
            if (t != null) input.Add(new TargetInput(t.TargetId, t.Position, t.HeadingDeg, t.SpeedKnots));
        if (m.Nav == null) return new List<TargetRow>();
        return TargetTable.Build(m.Nav.PositionEastNorth, m.Nav.Heading, m.Nav.SpeedKnots, m.Nav.Uses3Dof ? m.Nav.SwayKnots : 0.0,
                                 input, exclude: WorkVessels.Is);
    }

    protected override void Build()
    {
        Header("ЦЕЛИ · САРП");
        float left = -W * 0.5f + Margin, width = W - 2f * Margin;
        float top = H * 0.5f - Frame - S * 0.15f, bottom = -H * 0.5f + Frame + S * 0.12f;
        float rowH = (top - bottom) / (RowCount + 1), fs = rowH * 0.5f, cw = width * 0.19f;
        string[] heads = { "СУДНО", "ПЕЛ., °", "ДИСТ., мили", "CPA, мили", "TCPA, мин" };
        for (int k = 0; k < 5; k++)
            Text(heads[k], new Vector2(left + width * ColX[k], top - rowH * 0.5f), new Vector2(cw, rowH * 0.8f), fs * 0.75f, EngraveColor);
        MonitorDraw.Rect(Root, new Vector2(0f, top - rowH), new Vector2(width, 1.5f), new Color(0.3f, 0.34f, 0.38f, 1f), "Rule");
        for (int i = 0; i < RowCount; i++)
        {
            float y = top - rowH * (i + 1.5f);
            rowBg[i] = MonitorDraw.Rect(Root, new Vector2(0f, y), new Vector2(width, rowH * 0.9f), new Color(0.6f, 0.08f, 0.06f, 0f), "RowAlarm");
            for (int k = 0; k < 5; k++)
                cells[i, k] = Text("", new Vector2(left + width * ColX[k], y), new Vector2(cw, rowH * 0.8f), fs, LabelColor);
        }
        empty = Text("", new Vector2(0f, top - rowH * 2.5f), new Vector2(width, rowH), fs, MutedColor);
        float fy = -H * 0.5f + Frame + S * 0.06f;
        footer = Text("", new Vector2(left, fy), new Vector2(width * 0.62f, fs), fs * 0.7f, MutedColor,
                      TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f));
        alarm = Text("", new Vector2(left + width, fy), new Vector2(width * 0.36f, fs), fs * 0.8f, AlarmColor,
                     TextAlignmentOptions.MidlineRight, null, new Vector2(1f, 0.5f));
        alarm.fontStyle = FontStyles.Bold;
    }

    public override void Texts()
    {
        List<TargetRow> rows = Rows(M);
        bool anyDanger = false;
        for (int i = 0; i < RowCount; i++)
        {
            bool has = i < rows.Count;
            TargetRow r = has ? rows[i] : default;
            cells[i, 0].text = has ? r.Id : "";
            cells[i, 1].text = has ? $"{(int)Math.Round(r.BearingDeg) % 360:000}" : "";
            cells[i, 2].text = has ? MonitorText.Nm(r.RangeNm) : "";
            cells[i, 3].text = has ? MonitorText.Nm(r.CpaNm) : "";
            cells[i, 4].text = has ? MonitorText.Tcpa(r.TcpaMin) : "";
            Color c = !has ? LabelColor : r.Dangerous ? Color.white : r.Approaching ? LabelColor : MutedColor;
            for (int k = 0; k < 5; k++) cells[i, k].color = c;
            rowBg[i].color = new Color(0.6f, 0.08f, 0.06f, has && r.Dangerous ? 0.85f : 0f);
            anyDanger |= has && r.Dangerous;
        }
        foreach (TargetRow r in rows) anyDanger |= r.Dangerous;
        empty.text = rows.Count == 0 ? "ЦЕЛЕЙ НЕТ" : "";
        string limits = $"CPA < {ScenarioText.Num(TargetTable.DefaultCpaLimitNm, 1)} мили, TCPA ≤ {TargetTable.DefaultTcpaLimitMin:0} мин";
        footer.text = rows.Count > RowCount ? $"ЦЕЛЕЙ {rows.Count} · опасно: {limits}" : $"ОПАСНО: {limits}";
        alarm.text = anyDanger && BridgeMonitors.BlinkOn ? "ОПАСНАЯ ЦЕЛЬ" : "";
    }
}

// ─────────── M8: ветер и погода ───────────
// Анемометр: неподвижная картушка «север вверху», стрелка — откуда дует истинный ветер (TowingController), синяя
// метка на ободе — свой курс (видно, с какого борта ветер). Табло — скорость ветра, уз; ниже — м/с, волнение,
// видимость, время суток (WeatherController). На судно ветер действует только в модели 3 СС (как и раньше).
public sealed class WindScreen : MonitorScreen
{
    private RectTransform windNeedle, heading;
    private TextMeshProUGUI wind, from, sea, vis, tod;

    public WindScreen() => Title = "Ветер и погода";

    protected override void Build()
    {
        Header("ВЕТЕР · ПОГОДА");
        LeftGauge(out Vector2 c, out float r);
        Gauge(c, r);
        for (int b = 0; b < 360; b += 10)
        {
            bool major = b % 30 == 0;
            MonitorDraw.Tick(Root, c, b, major ? r * 0.84f : r * 0.91f, r * 0.97f, major ? r * 0.03f : r * 0.013f, TickColor);
            if (!major) continue;
            string l = b == 0 ? "N" : b == 90 ? "E" : b == 180 ? "S" : b == 270 ? "W" : (b / 10).ToString();
            Text(l, c + MonitorDraw.Dir(b) * (r * 0.66f), new Vector2(r * 0.4f, r * 0.24f), r * (b % 90 == 0 ? 0.19f : 0.13f),
                 b % 90 == 0 ? LabelColor : MutedColor).fontStyle = b % 90 == 0 ? FontStyles.Bold : FontStyles.Normal;
        }
        heading = MonitorDraw.Node(Root, "Heading", c, Vector2.zero);
        MonitorDraw.Tick(heading, Vector2.zero, 0f, r * 0.9f, r * 1.12f, r * 0.05f, OrderedColor);
        windNeedle = MonitorDraw.Needle(Root, c, r * 0.8f, r * 0.06f, r * 0.2f, ActualColor);
        GaugeTop(c, r, r * 0.08f, BezelColor);

        RightColumn(c, r, out float colX, out float colW, out float cx);
        float top = Landscape ? H * 0.2f : c.y - r * 1.45f;
        float digH = Landscape ? H * 0.2f : H * 0.1f;
        Text("ВЕТЕР, уз", new Vector2(cx, top + digH * 0.85f), new Vector2(colW, digH * 0.4f), digH * 0.3f, EngraveColor);
        wind = MonoDigits(4, new Vector2(cx, top), new Vector2(colW, digH));
        from = ColumnLine(colX, colW, top - digH * 1.0f, digH * 0.3f, LabelColor);
        sea = ColumnLine(colX, colW, top - digH * 1.5f, digH * 0.28f, LabelColor);
        vis = ColumnLine(colX, colW, top - digH * 1.95f, digH * 0.28f, LabelColor);
        tod = ColumnLine(colX, colW, top - digH * 2.4f, digH * 0.28f, LabelColor);
    }

    private static Wind CurrentWind => TowingController.Instance != null ? TowingController.Instance.Wind : default;

    public override void Needles()
    {
        Wind w = CurrentWind;
        bool calm = w.SpeedMs / ShipParameters.KnotsToMs < 0.05;
        if (windNeedle.gameObject.activeSelf == calm) windNeedle.gameObject.SetActive(!calm);
        MonitorDraw.Point(windNeedle, (float)w.FromDeg);
        if (M.Nav != null) MonitorDraw.Point(heading, M.Nav.Heading);
    }

    public override void Texts()
    {
        Wind w = CurrentWind;
        double kn = w.SpeedMs / ShipParameters.KnotsToMs;
        wind.text = MonoText(ScenarioText.Num(Math.Min(kn, 99.9), 1), 4);
        double fromDeg = ((w.FromDeg % 360.0) + 360.0) % 360.0;
        from.text = kn < 0.05 ? "ШТИЛЬ"
            : $"ИЗ {(int)Math.Round(fromDeg) % 360:000}° ({MonitorText.Point8(fromDeg)}) · {ScenarioText.Num(w.SpeedMs, 1)} м/с";
        WeatherController wc = WeatherController.Instance;
        if (wc == null) { sea.text = vis.text = tod.text = ""; return; }
        sea.text = $"ВОЛНЕНИЕ  {MonitorText.Sea(wc.CurrentPresetId)}";
        double nm = wc.VisibilityM / CpaCalculator.MetersPerNm;
        vis.text = nm >= 10 ? "ВИДИМОСТЬ  более 10 миль" : $"ВИДИМОСТЬ  {ScenarioText.Num(nm, 1)} мили";
        tod.text = $"ВРЕМЯ СУТОК  {ScenarioText.TimeOfDayTitle(wc.CurrentTimeOfDay)}";
    }
}

// ─────────── M1: сообщения ───────────
// Состояние упражнения («ИДЁТ 12:34 · 01 · Фарватер», «БРИФИНГ», «СВОБОДНОЕ ПЛАВАНИЕ») и журнал последних событий
// шины ExerciseEvents (BridgeMonitors.Messages: русское описание события, как в консоли и отчёте; без действий
// обучающегося и служебных событий оценки — Core MonitorMessages). Новые — сверху, тревожные — красные.
public sealed class MessagesScreen : MonitorScreen
{
    private const int RowCount = 6;
    private readonly TextMeshProUGUI[] rows = new TextMeshProUGUI[RowCount];
    private TextMeshProUGUI status;
    private int shownVersion = -1;

    public MessagesScreen() => Title = "Сообщения";

    protected override void Build()
    {
        Header("СООБЩЕНИЯ");
        float left = -W * 0.5f + Margin, width = W - 2f * Margin;
        float top = H * 0.5f - Frame - S * 0.15f, bottom = -H * 0.5f + Frame + S * 0.05f, fs = S * 0.058f;
        status = Text("", new Vector2(left, top), new Vector2(width, fs * 1.3f), fs, Color.white,
                      TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f));
        status.fontStyle = FontStyles.Bold;
        MonitorDraw.Rect(Root, new Vector2(0f, top - fs * 0.95f), new Vector2(width, 1.5f), new Color(0.3f, 0.34f, 0.38f, 1f), "Rule");
        float y0 = top - fs * 1.7f, rowH = (y0 - bottom) / RowCount;
        for (int i = 0; i < RowCount; i++)
            rows[i] = Text("", new Vector2(left, y0 - rowH * (i + 0.5f)), new Vector2(width, rowH * 0.9f), rowH * 0.55f, LabelColor,
                           TextAlignmentOptions.MidlineLeft, null, new Vector2(0f, 0.5f));
    }

    public override void Texts()
    {
        ScenarioRunner rn = ScenarioRunner.Instance;
        if (rn == null || rn.Current == null || rn.CurrentPhase == ScenarioRunner.Phase.Idle) status.text = "СВОБОДНОЕ ПЛАВАНИЕ";
        else
        {
            string name = $"{rn.Current.id} · {rn.Current.title}", clock = ScenarioText.Clock(rn.ExerciseTime);
            status.text = rn.CurrentPhase == ScenarioRunner.Phase.Briefing ? $"БРИФИНГ · {name}"
                : rn.CurrentPhase == ScenarioRunner.Phase.Finished ? $"ЗАВЕРШЕНО {clock} · {name}"
                : M.Nav != null && M.Nav.Paused ? $"ПАУЗА {clock} · {name}"
                : $"ИДЁТ {clock} · {name}";
        }

        if (shownVersion == M.MessagesVersion) return;
        shownVersion = M.MessagesVersion;
        for (int i = 0; i < RowCount; i++)
        {
            if (i >= M.Messages.Count)
            {
                rows[i].text = i == 0 ? "Сообщений нет" : "";
                rows[i].color = MutedColor;
                continue;
            }
            BridgeMonitors.MonitorMessage m = M.Messages[i];
            rows[i].text = $"{m.Time}  {m.Text}";
            rows[i].color = m.Alarm ? AlarmColor : i == 0 ? Color.white : LabelColor;
        }
    }
}

// ─────────── M9: глубина и сигнализация мели ───────────
// То же, что полоса над NavDisplay (B1): глубина по карте под судном, запас под килем; табло «МЕЛЬ» — тёмное,
// мигает при опасном приближении (горит ровно после квитирования), горит на мели; ниже — опасность и «КВИТ.».
public sealed class AlarmScreen : MonitorScreen
{
    private static readonly Color TileIdle = new Color(0.1f, 0.035f, 0.03f, 1f);
    private static readonly Color TileIdleText = new Color(0.32f, 0.12f, 0.1f, 1f);

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
        depth = MonoDigits(5, new Vector2(lx, -H * 0.01f), new Vector2(colW, digH));
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
        depth.text = float.IsNaN(d) ? MonoText("—  ", 5) : MonoText(ScenarioText.Num(Mathf.Min(d, 999.9f), 1), 5);
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
