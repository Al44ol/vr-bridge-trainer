// ============================================================
// EditorMapOverlay.cs — Слой редактора поверх карты: сетка, границы района, ручки, курсор (запрос 21.2а)
//
// Карта акватории под ним — DebriefMapGraphic (то же, что в окне инструктора и разборе). Здесь —
// только то, что нужно для правки: сетка шага привязки (прорежена до читаемой), рамка границ района
// с ручками в углах и на серединах сторон (выделенная — ярче), крестик узла привязки под курсором.
// Координаты: пиксель окна MapViewport = локальная точка от левого нижнего угла этой графики.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class EditorMapOverlay : MaskableGraphic
{
    public static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color BoundsColor = new Color(0.62f, 0.55f, 1f, 0.95f);
    public static readonly Color HandleColor = new Color(0.62f, 0.55f, 1f, 1f);
    public static readonly Color HandleHot = new Color(1f, 0.85f, 0.3f, 1f);
    public static readonly Color CursorColor = new Color(0.55f, 0.9f, 1f, 0.9f);

    public MapViewport Viewport;
    public double GridStep = 50.0;
    public bool ShowBounds = true, ShowHandles;
    public WorldPosition BoundsMin, BoundsMax;
    /// <summary>Ручка под курсором или перетаскиваемая (BoundsHandles), −1 — нет.</summary>
    public int HotHandle = -1;
    public bool ShowCursor;
    public WorldPosition Cursor;

    /// <summary>Ручки рамки: 0–3 — углы (ЮЗ, ЮВ, СВ, СЗ), 4–7 — середины сторон (Ю, В, С, З).</summary>
    public static List<WorldPosition> BoundsHandles(WorldPosition min, WorldPosition max)
    {
        double cx = 0.5 * (min.East + max.East), cy = 0.5 * (min.North + max.North);
        return new List<WorldPosition>
        {
            new WorldPosition(min.East, min.North), new WorldPosition(max.East, min.North),
            new WorldPosition(max.East, max.North), new WorldPosition(min.East, max.North),
            new WorldPosition(cx, min.North), new WorldPosition(max.East, cy),
            new WorldPosition(cx, max.North), new WorldPosition(min.East, cy)
        };
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (Viewport == null) return;
        Rect r = GetPixelAdjustedRect();
        MapRect b = Viewport.Bounds;

        // Сетка: прореженный шаг; каждая 5-я — ярче
        double s = MapGrid.DisplayStep(GridStep, Viewport.MetersPerPixel);
        foreach (double e in MapGrid.Lines(b.MinE, b.MaxE, s))
        {
            float x = Px(new WorldPosition(e, 0), r).x;
            Quad(vh, new Vector2(x - 0.5f, r.yMin), new Vector2(x + 0.5f, r.yMax), Major(e, s) ? GridMajor : GridMinor);
        }
        foreach (double n in MapGrid.Lines(b.MinN, b.MaxN, s))
        {
            float y = Px(new WorldPosition(0, n), r).y;
            Quad(vh, new Vector2(r.xMin, y - 0.5f), new Vector2(r.xMax, y + 0.5f), Major(n, s) ? GridMajor : GridMinor);
        }

        if (ShowBounds)
        {
            Vector2 p0 = Px(BoundsMin, r), p1 = Px(BoundsMax, r);
            float t = 2f;
            Quad(vh, new Vector2(p0.x, p0.y - t * 0.5f), new Vector2(p1.x, p0.y + t * 0.5f), BoundsColor);
            Quad(vh, new Vector2(p0.x, p1.y - t * 0.5f), new Vector2(p1.x, p1.y + t * 0.5f), BoundsColor);
            Quad(vh, new Vector2(p0.x - t * 0.5f, p0.y), new Vector2(p0.x + t * 0.5f, p1.y), BoundsColor);
            Quad(vh, new Vector2(p1.x - t * 0.5f, p0.y), new Vector2(p1.x + t * 0.5f, p1.y), BoundsColor);
            if (ShowHandles)
            {
                List<WorldPosition> hs = BoundsHandles(BoundsMin, BoundsMax);
                for (int i = 0; i < hs.Count; i++)
                {
                    Vector2 c = Px(hs[i], r);
                    float h = i == HotHandle ? 8f : 6f;
                    Quad(vh, c - new Vector2(h, h), c + new Vector2(h, h), i == HotHandle ? HandleHot : HandleColor);
                }
            }
        }

        if (ShowCursor)
        {
            Vector2 c = Px(Cursor, r);
            Quad(vh, new Vector2(c.x - 9f, c.y - 0.75f), new Vector2(c.x + 9f, c.y + 0.75f), CursorColor);
            Quad(vh, new Vector2(c.x - 0.75f, c.y - 9f), new Vector2(c.x + 0.75f, c.y + 9f), CursorColor);
        }
    }

    private static bool Major(double v, double s) => System.Math.Abs(System.Math.IEEERemainder(v, 5.0 * s)) < 0.5 * s;

    /// <summary>Точка мира → локальная точка графики.</summary>
    public Vector2 Px(WorldPosition p, Rect r)
    {
        Viewport.ToPixel(p, out double px, out double py);
        return new Vector2(r.xMin + (float)px, r.yMin + (float)py);
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color c)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(a.x, a.y), c, Vector2.zero);
        vh.AddVert(new Vector3(a.x, b.y), c, Vector2.zero);
        vh.AddVert(new Vector3(b.x, b.y), c, Vector2.zero);
        vh.AddVert(new Vector3(b.x, a.y), c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
