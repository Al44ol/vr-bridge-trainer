// ============================================================
// MonitorGraphics.cs — Рисование приборов на мониторах пульта (запрос 16а)
//
// Без картинок и RenderTexture — сетка UI (как схема разбора C3):
//   • DialGraphic — неподвижная часть прибора: круг-подложка, дуги-сектора (цветные полосы шкалы), деления.
//     Сетка строится один раз (при настройке); стрелки и картушка двигаются поворотом RectTransform,
//     поэтому сетка не перестраивается каждый кадр.
//   • NeedleGraphic — стрелка от центра вверх (трапеция с хвостом), поворачивается вокруг центра.
// Углы — как в Core GaugeScale: 0° — вверх, по часовой; координаты — пиксели холста от центра элемента.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DialGraphic : MaskableGraphic
{
    private struct Band { public float from, to, inner, outer; public Color color; }
    private struct Tick { public float angle, inner, outer, width; public Color color; }

    private readonly List<Band> bands = new List<Band>();
    private readonly List<Tick> ticks = new List<Tick>();
    private float discRadius;
    private Color discColor;

    public void Clear()
    {
        bands.Clear();
        ticks.Clear();
        discRadius = 0f;
        SetVerticesDirty();
    }

    /// <summary>Круг-подложка радиуса r.</summary>
    public void Disc(float r, Color c)
    {
        discRadius = r;
        discColor = c;
        SetVerticesDirty();
    }

    /// <summary>Дуга от fromDeg до toDeg между радиусами inner и outer.</summary>
    public void Arc(float fromDeg, float toDeg, float inner, float outer, Color c)
    {
        bands.Add(new Band { from = fromDeg, to = toDeg, inner = inner, outer = outer, color = c });
        SetVerticesDirty();
    }

    /// <summary>Деление по углу angleDeg от радиуса inner до outer, толщиной width.</summary>
    public void TickAt(float angleDeg, float inner, float outer, float width, Color c)
    {
        ticks.Add(new Tick { angle = angleDeg, inner = inner, outer = outer, width = width, color = c });
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (discRadius > 0f) Ring(vh, 0f, 360f, 0f, discRadius, discColor);
        foreach (Band b in bands) Ring(vh, b.from, b.to, b.inner, b.outer, b.color);
        foreach (Tick t in ticks)
        {
            Vector2 dir = Dir(t.angle), side = new Vector2(dir.y, -dir.x) * (t.width * 0.5f);
            Quad(vh, dir * t.inner - side, dir * t.outer - side, dir * t.outer + side, dir * t.inner + side, t.color);
        }
    }

    public static Vector2 Dir(float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
    }

    private static void Ring(VertexHelper vh, float from, float to, float inner, float outer, Color c)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 4f));   // сегмент ≈ 4°
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Lerp(from, to, i / (float)n), a1 = Mathf.Lerp(from, to, (i + 1) / (float)n);
            Vector2 d0 = Dir(a0), d1 = Dir(a1);
            Quad(vh, d0 * inner, d0 * outer, d1 * outer, d1 * inner, c);
        }
    }

    public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int i = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = a; vh.AddVert(v);
        v.position = b; vh.AddVert(v);
        v.position = c; vh.AddVert(v);
        v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}

public class NeedleGraphic : MaskableGraphic
{
    public float length = 100f, baseWidth = 10f, tipWidth = 3f, tail = 15f;

    public void Configure(float len, float width, float tipW, float tailLen, Color c)
    {
        length = len;
        baseWidth = width;
        tipWidth = tipW;
        tail = tailLen;
        color = c;
        SetVerticesDirty();
    }

    /// <summary>Повернуть стрелку на угол (0° — вверх, по часовой).</summary>
    public void Point(float angleDeg) => rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float b = baseWidth * 0.5f, t = tipWidth * 0.5f;
        DialGraphic.Quad(vh, new Vector2(-b, -tail), new Vector2(-t, length), new Vector2(t, length), new Vector2(b, -tail), color);
    }
}
