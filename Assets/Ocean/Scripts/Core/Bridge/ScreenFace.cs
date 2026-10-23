// ============================================================
// ScreenFace.cs — Лицевая плоскость экрана по граням сетки (запрос 16а, по проверке 23.10.2026)
//
// У модели МОСТИКИ наклон мониторов «запечён» в вершины (экспорт из Blender с применённым поворотом), поэтому
// ограничивающий параллелепипед сетки идёт по осям судна и плоскость экрана по нему не находится (первая проверка:
// холсты легли горизонтально и боком). Здесь плоскость ищется по самим граням:
//   1) треугольники группируются по плоскостям: нормали совпадают (cos > 0,995) и плоскости ближе 5 мм;
//   2) из групп выбирается та, у которой площадь × cos(нормаль, направление на игрока) наибольшая —
//      большая плоская грань, обращённая к игроку (у корпуса монитора — лицевая, а не задняя или боковая);
//   3) «верх» — вертикаль судна, спроецированная на плоскость (у лежащей грани — направление от игрока);
//      ширина и высота — размах вершин грани по осям «вправо» и «вверх», центр — середина размаха.
// Координаты — мировые, double; вход — вершины уже в мире.
// ============================================================
using System;
using System.Collections.Generic;

public readonly struct V3
{
    public readonly double X, Y, Z;
    public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
    public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public double Length => Math.Sqrt(Dot(this, this));
    public V3 Normalized { get { double l = Length; return l > 1e-12 ? this * (1.0 / l) : new V3(0, 0, 0); } }
}

public sealed class ScreenFaceResult
{
    public V3 Center, Normal, Up;
    public double Width, Height, Area;
}

public static class ScreenFace
{
    public const double SameNormalCos = 0.995;
    public const double SamePlaneM = 0.005;

    private sealed class Group
    {
        public V3 NormalSum, N;
        public double D, Area;
        public readonly HashSet<int> Verts = new HashSet<int>();
    }

    /// <summary>Лицевая грань: вершины (мир), треугольники, игрок, вертикаль судна; null — граней нет.</summary>
    public static ScreenFaceResult Find(IList<V3> verts, IList<int> tris, V3 viewer, V3 shipUp)
    {
        var groups = new List<Group>();
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            V3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
            V3 cr = V3.Cross(b - a, c - a);
            double area = cr.Length * 0.5;
            if (area < 1e-10) continue;
            V3 n = cr.Normalized;
            double d = V3.Dot(n, a);
            Group g = groups.Find(x => V3.Dot(x.N, n) > SameNormalCos && Math.Abs(V3.Dot(x.N, a) - x.D) < SamePlaneM);
            if (g == null)
            {
                g = new Group { N = n, D = d };
                groups.Add(g);
            }
            g.NormalSum = g.NormalSum + n * area;
            g.Area += area;
            g.Verts.Add(tris[t]); g.Verts.Add(tris[t + 1]); g.Verts.Add(tris[t + 2]);
        }

        Group best = null;
        double bestScore = 0.0;
        foreach (Group g in groups)
        {
            V3 n = g.NormalSum.Normalized;
            V3 centroid = Centroid(verts, g.Verts);
            double facing = V3.Dot(n, (viewer - centroid).Normalized);
            double score = g.Area * facing;
            if (facing > 0.05 && score > bestScore) { bestScore = score; best = g; }
        }
        if (best == null) return null;

        V3 normal = best.NormalSum.Normalized;
        V3 c0 = Centroid(verts, best.Verts);
        V3 up = shipUp - normal * V3.Dot(shipUp, normal);
        if (up.Length < 0.3)                                   // лежащая грань: «верх» — от игрока
        {
            V3 away = c0 - viewer;
            up = away - normal * V3.Dot(away, normal);
        }
        up = up.Normalized;
        V3 right = V3.Cross(up, normal).Normalized;
        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (int i in best.Verts)
        {
            V3 p = verts[i] - c0;
            double u = V3.Dot(p, up), v = V3.Dot(p, right);
            uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
            vMin = Math.Min(vMin, v); vMax = Math.Max(vMax, v);
        }
        return new ScreenFaceResult
        {
            Center = c0 + up * (0.5 * (uMin + uMax)) + right * (0.5 * (vMin + vMax)),
            Normal = normal,
            Up = up,
            Width = vMax - vMin,
            Height = uMax - uMin,
            Area = best.Area
        };
    }

    private static V3 Centroid(IList<V3> verts, HashSet<int> idx)
    {
        V3 s = new V3(0, 0, 0);
        foreach (int i in idx) s = s + verts[i];
        return idx.Count > 0 ? s * (1.0 / idx.Count) : s;
    }
}
