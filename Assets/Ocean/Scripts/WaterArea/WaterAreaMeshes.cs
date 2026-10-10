// ============================================================
// WaterAreaMeshes.cs — Процедурные low-poly модели для акватории (B1)
//
// Буи: столбовой буй (поплавок + конусная/цилиндрическая колонна) с поясами
// окраски по МАМС (IalaMarks), мачта и топовая фигура. Тела вращения
// с 12 гранями, плоское затенение (низкополигональный вид). Подмеш на каждый
// цвет — материалы общие (MarkColor), SRP Batcher.
// Размеры (м, от ватерлинии): поплавок Ø 2,0 (−0,9…0,65), колонна до 3,0,
// топовая фигура ≈ 3,2…4,2 — порядок размеров морских столбовых буёв.
//
// Суша: верх — триангуляция многоугольника на высоте берега, откос —
// смещённый наружу контур на глубине −baseDepth (без зазора с водой при волнении).
// Вершины задаются относительно опорной точки (anchor) — float остаётся точным.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class WaterAreaMeshes
{
    public const int Segments = 12;
    public const float BodyTop = 3.0f;          // верх колонны над ватерлинией, м
    private const float TopmarkBase = 3.25f;    // низ топовой фигуры, м

    /// <summary>Готовая модель: подмеш i окрашивается материалом Colors[i].</summary>
    public struct BuoyMesh
    {
        public Mesh Mesh;
        public MarkColor[] Colors;
    }

    // ─────────── Буй ───────────
    public static BuoyMesh BuildBuoy(AidType type)
    {
        MarkAppearance look = IalaMarks.Get(type);
        var b = new Builder();

        // Корпус: подводная часть, поплавок, скос палубы, колонна
        float colTop = look.ConicalTop ? 0.12f : 0.32f;
        Body(b, look, -0.9f, -0.3f, 0.7f, 1.0f);
        Body(b, look, -0.3f, 0.45f, 1.0f, 1.0f);
        Body(b, look, 0.45f, 0.65f, 1.0f, 0.45f);
        Body(b, look, 0.65f, BodyTop, look.ConicalTop ? 0.5f : 0.45f, colTop);
        b.Disk(-0.9f, 0.7f, false, ColorAt(look, -0.9f, 0));
        b.Disk(BodyTop, colTop, true, ColorAt(look, BodyTop, 0));

        // Топовая фигура и мачта
        float top = Topmark(b, look.Topmark, look.TopmarkColor, TopmarkBase);
        b.Frustum(BodyTop, top, 0.05f, 0.05f, MarkColor.Black, 6);

        return b.ToMesh("Buoy_" + type);
    }

    // Участок корпуса с разбиением по поясам окраски
    private static void Body(Builder b, MarkAppearance look, float y0, float y1, float r0, float r1)
    {
        var cuts = new List<float> { y0 };
        foreach (ColorBand band in look.Bands)
        {
            float y = band.From * BodyTop;
            if (y > y0 + 1e-4f && y < y1 - 1e-4f) cuts.Add(y);
        }
        cuts.Add(y1);
        cuts.Sort();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            float a = cuts[i], c = cuts[i + 1];
            float ra = Mathf.Lerp(r0, r1, (a - y0) / (y1 - y0));
            float rc = Mathf.Lerp(r0, r1, (c - y0) / (y1 - y0));
            float mid = 0.5f * (a + c);
            if (look.VerticalStripes)
                b.Frustum(a, c, ra, rc, Segments, k => (k / 2) % 2 == 0 ? look.StripeColorA : look.StripeColorB);
            else
                b.Frustum(a, c, ra, rc, ColorAt(look, mid, 0), Segments);
        }
    }

    private static MarkColor ColorAt(MarkAppearance look, float y, int segment)
    {
        if (look.VerticalStripes) return (segment / 2) % 2 == 0 ? look.StripeColorA : look.StripeColorB;
        float f = Mathf.Clamp01(y / BodyTop);
        foreach (ColorBand band in look.Bands)
            if (f >= band.From && f <= band.To) return band.Color;
        return look.Bands[0].Color;
    }

    // Возвращает верх топовой фигуры, м
    private static float Topmark(Builder b, TopmarkShape shape, MarkColor color, float y)
    {
        const float r = 0.3f, h = 0.45f, gap = 0.12f;
        switch (shape)
        {
            case TopmarkShape.Can:
                b.Frustum(y, y + 0.55f, 0.27f, 0.27f, color, Segments);
                b.Disk(y, 0.27f, false, color); b.Disk(y + 0.55f, 0.27f, true, color);
                return y + 0.55f;
            case TopmarkShape.ConeUp:
                ConeUp(b, y, r, h + 0.1f, color);
                return y + h + 0.1f;
            case TopmarkShape.TwoConesUp:
                ConeUp(b, y, r, h, color); ConeUp(b, y + h + gap, r, h, color);
                return y + 2 * h + gap;
            case TopmarkShape.TwoConesDown:
                ConeDown(b, y, r, h, color); ConeDown(b, y + h + gap, r, h, color);
                return y + 2 * h + gap;
            case TopmarkShape.TwoConesBaseToBase:   // ◆: нижний вершиной вниз, верхний вверх
                ConeDown(b, y, r, h, color); ConeUp(b, y + h + 0.04f, r, h, color);
                return y + 2 * h + 0.04f;
            case TopmarkShape.TwoConesPointToPoint: // ⧗: нижний вершиной вверх, верхний вниз
                ConeUp(b, y, r, h, color); ConeDown(b, y + h + 0.04f, r, h, color);
                return y + 2 * h + 0.04f;
            case TopmarkShape.TwoSpheres:
                Sphere(b, y + 0.25f, 0.25f, color); Sphere(b, y + 0.25f + 0.6f, 0.25f, color);
                return y + 1.1f;
            default: // Sphere
                Sphere(b, y + 0.3f, 0.3f, color);
                return y + 0.6f;
        }
    }

    private static void ConeUp(Builder b, float y, float r, float h, MarkColor c)
    {
        b.Disk(y, r, false, c);
        b.Frustum(y, y + h, r, 0f, c, Segments);
    }

    private static void ConeDown(Builder b, float y, float r, float h, MarkColor c)
    {
        b.Frustum(y, y + h, 0f, r, c, Segments);
        b.Disk(y + h, r, true, c);
    }

    private static void Sphere(Builder b, float cy, float r, MarkColor c)
    {
        const int rings = 6;
        for (int i = 0; i < rings; i++)
        {
            float a0 = Mathf.PI * i / rings - Mathf.PI * 0.5f, a1 = Mathf.PI * (i + 1) / rings - Mathf.PI * 0.5f;
            b.Frustum(cy + r * Mathf.Sin(a0), cy + r * Mathf.Sin(a1), r * Mathf.Cos(a0), r * Mathf.Cos(a1), c, Segments);
        }
    }

    // ─────────── Суша ───────────
    /// <summary>Модель берега: подмеш 0 — верх, 1 — откос. Вершины относительно anchor (м).</summary>
    public static Mesh BuildLand(LandArea land, WorldPosition anchor, float baseDepth, float slopeFactor)
    {
        var mesh = new Mesh { name = "Land_" + land.name };
        if (land.vertices == null || land.vertices.Count < 3) return mesh;

        // Обход против часовой стрелки (при «север вверх») — нужен для смещения и боковин
        var poly = new List<WorldPosition>(land.vertices);
        if (Polygon2D.SignedArea(poly) < 0) poly.Reverse();
        float h = Mathf.Max(0.5f, land.height);
        List<WorldPosition> foot = Polygon2D.Offset(poly, h * slopeFactor);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var top = new List<int>();
        var slope = new List<int>();

        // Верх: плоский, нормаль вверх
        for (int i = 0; i < poly.Count; i++)
        {
            verts.Add(Local(poly[i], anchor, h));
            normals.Add(Vector3.up);
        }
        List<int> tris = Polygon2D.Triangulate(poly);
        for (int t = 0; t + 2 < tris.Count; t += 3)
            AddTriangle(verts, top, tris[t], tris[t + 1], tris[t + 2], Vector3.up);

        // Откос: четырёхугольник на каждое ребро, плоское затенение
        for (int i = 0; i < poly.Count; i++)
        {
            int j = (i + 1) % poly.Count;
            Vector3 a = Local(poly[i], anchor, h), bb = Local(poly[j], anchor, h);
            Vector3 c = Local(foot[j], anchor, -baseDepth), d = Local(foot[i], anchor, -baseDepth);
            Vector3 outward = Vector3.Cross(Vector3.up, bb - a).normalized;   // для обхода против часовой — наружу (вправо по ходу)
            Vector3 n = Vector3.Cross(d - a, bb - a).normalized;
            if (Vector3.Dot(n, outward) < 0f) n = -n;
            int k = verts.Count;
            verts.Add(a); verts.Add(bb); verts.Add(c); verts.Add(d);
            for (int m = 0; m < 4; m++) normals.Add(n);
            AddTriangle(verts, slope, k, k + 1, k + 2, n);
            AddTriangle(verts, slope, k, k + 2, k + 3, n);
        }

        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(slope, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 Local(WorldPosition p, WorldPosition anchor, float y) =>
        new Vector3((float)(p.East - anchor.East), y, (float)(p.North - anchor.North));

    // Треугольник с лицевой стороной в сторону facing (в Unity лицевая — по часовой)
    private static void AddTriangle(List<Vector3> v, List<int> list, int a, int b, int c, Vector3 facing)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(n, facing) >= 0f) { list.Add(a); list.Add(b); list.Add(c); }
        else { list.Add(a); list.Add(c); list.Add(b); }
    }

    // ─────────── Сборщик тел вращения ───────────
    private sealed class Builder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly Dictionary<MarkColor, List<int>> tris = new Dictionary<MarkColor, List<int>>();

        private List<int> List(MarkColor c)
        {
            if (!tris.TryGetValue(c, out List<int> l)) { l = new List<int>(); tris[c] = l; }
            return l;
        }

        public void Frustum(float y0, float y1, float r0, float r1, MarkColor color, int segments) =>
            Frustum(y0, y1, r0, r1, segments, _ => color);

        /// <summary>Боковая поверхность усечённого конуса, грань k — цвет colorOf(k).</summary>
        public void Frustum(float y0, float y1, float r0, float r1, int segments, System.Func<int, MarkColor> colorOf)
        {
            for (int k = 0; k < segments; k++)
            {
                float a0 = 2f * Mathf.PI * k / segments, a1 = 2f * Mathf.PI * (k + 1) / segments;
                Vector3 p00 = new Vector3(r0 * Mathf.Sin(a0), y0, r0 * Mathf.Cos(a0));
                Vector3 p01 = new Vector3(r0 * Mathf.Sin(a1), y0, r0 * Mathf.Cos(a1));
                Vector3 p10 = new Vector3(r1 * Mathf.Sin(a0), y1, r1 * Mathf.Cos(a0));
                Vector3 p11 = new Vector3(r1 * Mathf.Sin(a1), y1, r1 * Mathf.Cos(a1));
                float am = 0.5f * (a0 + a1);
                Vector3 radial = new Vector3(Mathf.Sin(am), 0f, Mathf.Cos(am));
                // Нормаль грани: наружу и с наклоном по образующей
                float dr = r0 - r1, dy = y1 - y0;
                Vector3 n = (radial * dy + Vector3.up * dr).normalized;
                Quad(p00, p01, p11, p10, n, colorOf(k));
            }
        }

        public void Disk(float y, float r, bool up, MarkColor color)
        {
            if (r <= 1e-4f) return;
            Vector3 n = up ? Vector3.up : Vector3.down;
            List<int> list = List(color);
            int c = verts.Count;
            verts.Add(new Vector3(0f, y, 0f)); normals.Add(n);
            for (int k = 0; k <= Segments; k++)
            {
                float a = 2f * Mathf.PI * k / Segments;
                verts.Add(new Vector3(r * Mathf.Sin(a), y, r * Mathf.Cos(a))); normals.Add(n);
            }
            for (int k = 0; k < Segments; k++)
                Tri(list, c, c + 1 + k, c + 2 + k, n);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, MarkColor color)
        {
            List<int> list = List(color);
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int m = 0; m < 4; m++) normals.Add(n);
            Tri(list, i, i + 1, i + 2, n);
            Tri(list, i, i + 2, i + 3, n);
        }

        private void Tri(List<int> list, int a, int b, int c, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (n.sqrMagnitude < 1e-12f) return;   // вырожденный (вершина конуса)
            if (Vector3.Dot(n, facing) >= 0f) { list.Add(a); list.Add(b); list.Add(c); }
            else { list.Add(a); list.Add(c); list.Add(b); }
        }

        public BuoyMesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            var colors = new List<MarkColor>(tris.Keys);
            colors.Sort();
            mesh.subMeshCount = colors.Count;
            for (int i = 0; i < colors.Count; i++)
                mesh.SetTriangles(tris[colors[i]], i);
            mesh.RecalculateBounds();
            return new BuoyMesh { Mesh = mesh, Colors = colors.ToArray() };
        }
    }
}
