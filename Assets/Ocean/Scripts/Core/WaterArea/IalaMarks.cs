// ============================================================
// IalaMarks.cs — Внешний вид знаков МАМС, регион A (B1)
//
// Окраска, топовая фигура и огонь по умолчанию для каждого типа знака
// по Системе навигационного ограждения МАМС (IALA Maritime Buoyage System),
// регион A (в т. ч. Россия):
//   • латеральный левой стороны — красный, топовая фигура — красный цилиндр;
//   • латеральный правой стороны — зелёный, топовая фигура — зелёный конус вершиной вверх;
//     «левая/правая сторона» — при движении в принятом направлении ограждения
//     (с моря к порту);
//   • кардинальные — чёрно-жёлтые, топовая фигура из двух чёрных конусов,
//     вершины конусов указывают положение чёрных поясов:
//       N — оба вверх, чёрный над жёлтым;
//       S — оба вниз, жёлтый над чёрным;
//       E — основаниями друг к другу, чёрный с широким жёлтым поясом;
//       W — вершинами друг к другу, жёлтый с широким чёрным поясом;
//   • отдельной опасности — чёрный с широким красным поясом, два чёрных шара;
//   • осевой (безопасной воды) — красные и белые вертикальные полосы, красный шар.
// Огни (для B4): латеральные — Fl R / Fl G; N — Q (непрерывные частые),
// E — Q(3) 10s, S — Q(6)+LFl 15s, W — Q(9) 15s; отдельной опасности — Fl(2);
// осевой — LFl 10s (также Iso, Oc, Mo(A)).
// Сверить с первоисточником перед включением в ВКР: IALA Maritime Buoyage System
// (IALA, ред. 2010 и позднее) и российские правила навигационного ограждения.
// ============================================================

/// <summary>Цвета окраски знаков (порядок = порядок материалов WaterAreaView).</summary>
public enum MarkColor { Red = 0, Green = 1, Yellow = 2, Black = 3, White = 4 }

/// <summary>Топовая фигура.</summary>
public enum TopmarkShape
{
    Can,                    // цилиндр
    ConeUp,                 // конус вершиной вверх
    TwoConesUp,             // N
    TwoConesDown,           // S
    TwoConesBaseToBase,     // E
    TwoConesPointToPoint,   // W
    TwoSpheres,             // отдельная опасность
    Sphere                  // осевой
}

/// <summary>Горизонтальный пояс окраски: доли высоты надводной части (0 — ватерлиния, 1 — верх).</summary>
public struct ColorBand
{
    public float From, To;
    public MarkColor Color;
    public ColorBand(float from, float to, MarkColor color) { From = from; To = to; Color = color; }
}

public sealed class MarkAppearance
{
    public string TitleRu;
    public string ShortLabel;          // подпись на гизмо
    public ColorBand[] Bands;          // снизу вверх
    public bool VerticalStripes;       // осевой знак: вертикальные полосы
    public MarkColor StripeColorA, StripeColorB;
    public bool ConicalTop;            // латеральный правой стороны — сужение кверху
    public TopmarkShape Topmark;
    public MarkColor TopmarkColor;
    public string DefaultLight;

    /// <summary>Основной цвет (для гизмо и планшета).</summary>
    public MarkColor MainColor => VerticalStripes ? StripeColorA : Bands[Bands.Length - 1].Color;
}

public static class IalaMarks
{
    private static readonly MarkAppearance[] table = Build();

    public static MarkAppearance Get(AidType type)
    {
        int i = (int)type;
        return i >= 0 && i < table.Length ? table[i] : table[0];
    }

    private static MarkAppearance[] Build()
    {
        var t = new MarkAppearance[8];
        t[(int)AidType.LateralPort] = new MarkAppearance
        {
            TitleRu = "Латеральный левой стороны", ShortLabel = "Л",
            Bands = new[] { new ColorBand(0f, 1f, MarkColor.Red) },
            Topmark = TopmarkShape.Can, TopmarkColor = MarkColor.Red, DefaultLight = "Fl R 4s"
        };
        t[(int)AidType.LateralStarboard] = new MarkAppearance
        {
            TitleRu = "Латеральный правой стороны", ShortLabel = "П",
            Bands = new[] { new ColorBand(0f, 1f, MarkColor.Green) }, ConicalTop = true,
            Topmark = TopmarkShape.ConeUp, TopmarkColor = MarkColor.Green, DefaultLight = "Fl G 4s"
        };
        t[(int)AidType.CardinalNorth] = new MarkAppearance
        {
            TitleRu = "Северный кардинальный", ShortLabel = "N",
            Bands = new[] { new ColorBand(0f, 0.5f, MarkColor.Yellow), new ColorBand(0.5f, 1f, MarkColor.Black) },
            Topmark = TopmarkShape.TwoConesUp, TopmarkColor = MarkColor.Black, DefaultLight = "Q"
        };
        t[(int)AidType.CardinalSouth] = new MarkAppearance
        {
            TitleRu = "Южный кардинальный", ShortLabel = "S",
            Bands = new[] { new ColorBand(0f, 0.5f, MarkColor.Black), new ColorBand(0.5f, 1f, MarkColor.Yellow) },
            Topmark = TopmarkShape.TwoConesDown, TopmarkColor = MarkColor.Black, DefaultLight = "Q(6)+LFl 15s"
        };
        t[(int)AidType.CardinalEast] = new MarkAppearance
        {
            TitleRu = "Восточный кардинальный", ShortLabel = "E",
            Bands = new[] { new ColorBand(0f, 0.3f, MarkColor.Black), new ColorBand(0.3f, 0.7f, MarkColor.Yellow), new ColorBand(0.7f, 1f, MarkColor.Black) },
            Topmark = TopmarkShape.TwoConesBaseToBase, TopmarkColor = MarkColor.Black, DefaultLight = "Q(3) 10s"
        };
        t[(int)AidType.CardinalWest] = new MarkAppearance
        {
            TitleRu = "Западный кардинальный", ShortLabel = "W",
            Bands = new[] { new ColorBand(0f, 0.3f, MarkColor.Yellow), new ColorBand(0.3f, 0.7f, MarkColor.Black), new ColorBand(0.7f, 1f, MarkColor.Yellow) },
            Topmark = TopmarkShape.TwoConesPointToPoint, TopmarkColor = MarkColor.Black, DefaultLight = "Q(9) 15s"
        };
        t[(int)AidType.IsolatedDanger] = new MarkAppearance
        {
            TitleRu = "Знак отдельной опасности", ShortLabel = "●●",
            Bands = new[] { new ColorBand(0f, 0.3f, MarkColor.Black), new ColorBand(0.3f, 0.7f, MarkColor.Red), new ColorBand(0.7f, 1f, MarkColor.Black) },
            Topmark = TopmarkShape.TwoSpheres, TopmarkColor = MarkColor.Black, DefaultLight = "Fl(2) 5s"
        };
        t[(int)AidType.SafeWater] = new MarkAppearance
        {
            TitleRu = "Осевой знак", ShortLabel = "●",
            Bands = new[] { new ColorBand(0f, 1f, MarkColor.Red) },
            VerticalStripes = true, StripeColorA = MarkColor.Red, StripeColorB = MarkColor.White,
            Topmark = TopmarkShape.Sphere, TopmarkColor = MarkColor.Red, DefaultLight = "LFl 10s"
        };
        return t;
    }
}
