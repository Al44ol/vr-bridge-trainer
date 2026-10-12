// ============================================================
// BoatMeshes.cs — Процедурная low-poly модель открытого катера (запрос 15)
//
// Корпус с острым носом, V-образным днищем со скулой, транцем, открытой палубой
// (кокпит) и низким фальшбортом по периметру; мачта топового огня в корме.
// Рулевой стойки в модели нет — на катере остаётся стойка мостика (штурвал,
// телеграф, тифон, свет, приборы), остальной мостик скрыт (ShipTypeSwitcher).
// Система координат модели (как TrafficMeshes): +Z — нос, +X — правый борт,
// +Y — вверх, начало — мидель на ватерлинии. Плоское затенение, без внешних ассетов.
// Подмеши (материалы Setup Fast Boat): 0 — борт и фальшборт, 1 — подводная часть,
// 2 — палуба, 3 — тёмные детали (планширь, мачта).
// Модель строит Setup Fast Boat и сохраняет ассетом (видна в Scene View).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class BoatMeshes
{
    public const int SubHull = 0, SubBottom = 1, SubDeck = 2, SubTrim = 3;

    /// <summary>Фальшборт над палубой в корме, м (к носу растёт на BowSheerRise).</summary>
    public const float BulwarkHeight = 0.45f;
    public const float BowSheerRise = 0.5f;
    /// <summary>Толщина фальшборта, м.</summary>
    public const float Wall = 0.06f;
    /// <summary>Мачта топового огня: высота над палубой, м; положение x/L от миделя (> 0 — к носу).</summary>
    public const float MastHeight = 2.9f;
    public const float MastStation = -0.36f;
    /// <summary>Бортовые огни: x/L от миделя.</summary>
    public const float SidelightStation = 0.2f;

    private const int Stations = 24;

    /// <summary>Полуширина по длине: t = 0 — транец, 1 — форштевень.</summary>
    public static float HalfBeam(float t, float beam)
    {
        float b2 = 0.5f * beam;
        if (t <= 0.55f) return b2 * (0.9f + 0.1f * t / 0.55f);
        float u = (t - 0.55f) / 0.45f;
        return b2 * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
    }

    /// <summary>Верх фальшборта (планширь) над ватерлинией, м.</summary>
    public static float Sheer(float t, float deck) => deck + BulwarkHeight + BowSheerRise * t * t;

    // Киль: прямой до 0,7 L, к форштевню поднимается
    private static float Keel(float t, float draft) =>
        t < 0.7f ? -draft : -draft * (1f - 0.75f * Mathf.Pow((t - 0.7f) / 0.3f, 1.5f));

    /// <summary>Место топового огня (над мачтой) в системе модели.</summary>
    public static Vector3 MastheadLight(float length, float deck) =>
        new Vector3(0f, deck + MastHeight + 0.15f, MastStation * length);

    /// <summary>Место бортового огня (side = +1 — ПБ, −1 — ЛБ) в системе модели.</summary>
    public static Vector3 Sidelight(float length, float beam, float deck, int side)
    {
        float t = 0.5f + SidelightStation;
        return new Vector3(side * (HalfBeam(t, beam) + 0.05f), Sheer(t, deck) + 0.1f, SidelightStation * length);
    }

    /// <summary>Модель катера: длина, ширина, осадка, высота палубы над ватерлинией (м).</summary>
    public static Mesh Build(float length, float beam, float draft, float deck)
    {
        var mb = new Builder(4);
        for (int i = 0; i < Stations; i++)
        {
            float t0 = (float)i / Stations, t1 = (float)(i + 1) / Stations;
            for (int side = -1; side <= 1; side += 2)
            {
                Section s0 = Section.At(t0, side, length, beam, draft, deck);
                Section s1 = Section.At(t1, side, length, beam, draft, deck);
                Vector3 outward = new Vector3(side, 0f, 0f);
                mb.Quad(SubBottom, s0.Keel, s1.Keel, s1.Chine, s0.Chine, new Vector3(side * 0.3f, -1f, 0f));
                mb.Quad(SubBottom, s0.Chine, s1.Chine, s1.Boot, s0.Boot, new Vector3(side, -0.3f, 0f));
                mb.Quad(SubHull, s0.Boot, s1.Boot, s1.Gunwale, s0.Gunwale, outward);
                mb.Quad(SubTrim, s0.Gunwale, s1.Gunwale, s1.InnerTop, s0.InnerTop, Vector3.up);
                mb.Quad(SubHull, s0.InnerTop, s1.InnerTop, s1.InnerDeck, s0.InnerDeck, -outward);
            }
            // Палуба между внутренними сторонами фальшборта
            Section p0 = Section.At(t0, -1, length, beam, draft, deck), p1 = Section.At(t1, -1, length, beam, draft, deck);
            Section q0 = Section.At(t0, 1, length, beam, draft, deck), q1 = Section.At(t1, 1, length, beam, draft, deck);
            mb.Quad(SubDeck, p0.InnerDeck, p1.InnerDeck, q1.InnerDeck, q0.InnerDeck, Vector3.up);
        }

        // Транец (веер от центра) и кормовой фальшборт поперёк
        Section ps = Section.At(0f, -1, length, beam, draft, deck), ss = Section.At(0f, 1, length, beam, draft, deck);
        var ring = new List<Vector3>
        {
            ss.Keel, ss.Chine, ss.Boot, ss.Gunwale, ss.InnerTop, ss.InnerDeck,
            ps.InnerDeck, ps.InnerTop, ps.Gunwale, ps.Boot, ps.Chine
        };
        float zs = ss.Keel.z;
        var center = new Vector3(0f, 0.5f * (ss.Keel.y + deck), zs);
        for (int k = 0; k < ring.Count; k++)
        {
            Vector3 a = ring[k], b = ring[(k + 1) % ring.Count];
            bool under = a.y < 0.15f && b.y < 0.15f;
            mb.Tri(under ? SubBottom : SubHull, center, a, b, Vector3.back);
        }
        float sternTop = Sheer(0f, deck);
        var sternWallOut0 = new Vector3(ss.InnerDeck.x, deck, zs);
        var sternWallOut1 = new Vector3(ps.InnerDeck.x, deck, zs);
        var sternWallTop0 = new Vector3(ss.InnerDeck.x, sternTop, zs);
        var sternWallTop1 = new Vector3(ps.InnerDeck.x, sternTop, zs);
        Vector3 inward = new Vector3(0f, 0f, Wall);
        mb.Quad(SubHull, sternWallOut0, sternWallTop0, sternWallTop1, sternWallOut1, Vector3.back);
        mb.Quad(SubHull, sternWallOut0 + inward, sternWallTop0 + inward, sternWallTop1 + inward, sternWallOut1 + inward, Vector3.forward);
        mb.Quad(SubTrim, sternWallTop0, sternWallTop1, sternWallTop1 + inward, sternWallTop0 + inward, Vector3.up);

        // Мачта топового огня
        mb.Box(SubTrim, new Vector3(0f, deck + 0.5f * MastHeight, MastStation * length), new Vector3(0.12f, MastHeight, 0.12f));

        Mesh mesh = mb.ToMesh("FastBoat_Hull");
        return mesh;
    }

    // Поперечное сечение одного борта на станции t
    private struct Section
    {
        public Vector3 Keel, Chine, Boot, Gunwale, InnerTop, InnerDeck;

        public static Section At(float t, int side, float length, float beam, float draft, float deck)
        {
            float z = (t - 0.5f) * length;
            float b = HalfBeam(t, beam);
            float yk = Keel(t, draft);
            float sh = Sheer(t, deck);
            float bi = Mathf.Max(0f, b - Wall);
            return new Section
            {
                Keel = new Vector3(0f, yk, z),
                Chine = new Vector3(side * 0.85f * b, 0.35f * yk, z),
                Boot = new Vector3(side * b, 0.15f, z),
                Gunwale = new Vector3(side * b, sh, z),
                InnerTop = new Vector3(side * bi, sh, z),
                InnerDeck = new Vector3(side * bi, deck, z),
            };
        }
    }

    // ─────────── Сборщик: плоское затенение, нормаль — наружу по подсказке ───────────
    private sealed class Builder
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<Vector3> n = new List<Vector3>();
        private readonly List<int>[] tris;

        public Builder(int subMeshes)
        {
            tris = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) tris[i] = new List<int>();
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a);
            if (nn.sqrMagnitude < 1e-10f) nn = Vector3.Cross(c - a, d - a);
            if (nn.sqrMagnitude < 1e-10f) return;   // вырожденный (точка у форштевня)
            if (Vector3.Dot(nn, hint) < 0f) { Vector3 t = b; b = d; d = t; nn = -nn; }
            nn.Normalize();
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(nn);
            tris[sub].Add(i0); tris[sub].Add(i0 + 1); tris[sub].Add(i0 + 2);
            tris[sub].Add(i0); tris[sub].Add(i0 + 2); tris[sub].Add(i0 + 3);
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 hint)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a);
            if (nn.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(nn, hint) < 0f) { Vector3 t = b; b = c; c = t; nn = -nn; }
            nn.Normalize();
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            for (int k = 0; k < 3; k++) n.Add(nn);
            tris[sub].Add(i0); tris[sub].Add(i0 + 1); tris[sub].Add(i0 + 2);
        }

        public void Box(int sub, Vector3 c, Vector3 size)
        {
            Vector3 h = 0.5f * size;
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right);
            Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left);
            Quad(sub, P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), Vector3.forward);
            Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), Vector3.back);
            Quad(sub, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}
