// ============================================================
// TrafficMeshes.cs — Процедурные low-poly модели других судов (B3)
//
// Три типа (TrafficVesselType): сухогруз (надстройка в корме, фок-мачта,
// крышки трюмов), танкер (надстройка в корме, низкий борт, трубопровод
// по палубе), малое судно (рубка в носовой части). Размеры — по L, B, d
// из ShipParameters, пропорции надстройки — HullProfile (Core): те же числа
// используют места навигационных огней, поэтому мачты стоят под огнями.
// Система координат модели: +Z — нос, +X — правый борт, +Y — вверх,
// начало — мидель на ватерлинии. Плоское затенение, без внешних ассетов.
// Подмеши (материалы TrafficController): 0 — борт, 1 — подводная часть
// («сапог»), 2 — палуба, 3 — надстройка, 4 — тёмные детали (окна, мачты,
// верх трубы), 5 — акцент (крышки трюмов, трубопровод, полоса трубы).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class TrafficMeshes
{
    public const int SubmeshCount = 6;
    private const int Hull = 0, Boot = 1, Deck = 2, House = 3, Dark = 4, Accent = 5;

    // Полуширина корпуса по длине: (x/L от миделя, доля B/2) — от кормы к носу
    private static readonly Vector2[] CargoPlan =
    {
        new Vector2(-0.50f, 0.84f), new Vector2(-0.46f, 0.95f), new Vector2(-0.36f, 1f), new Vector2(0.24f, 1f),
        new Vector2(0.34f, 0.9f), new Vector2(0.42f, 0.66f), new Vector2(0.47f, 0.34f), new Vector2(0.50f, 0.04f)
    };
    private static readonly Vector2[] TankerPlan =
    {
        new Vector2(-0.50f, 0.88f), new Vector2(-0.45f, 0.98f), new Vector2(-0.38f, 1f), new Vector2(0.32f, 1f),
        new Vector2(0.40f, 0.9f), new Vector2(0.46f, 0.66f), new Vector2(0.49f, 0.35f), new Vector2(0.50f, 0.08f)
    };
    private static readonly Vector2[] SmallPlan =
    {
        new Vector2(-0.50f, 0.86f), new Vector2(-0.40f, 1f), new Vector2(0.10f, 1f), new Vector2(0.28f, 0.88f),
        new Vector2(0.40f, 0.62f), new Vector2(0.47f, 0.3f), new Vector2(0.50f, 0.05f)
    };
    // 19.3б: буксир — широкий корпус, тупой нос (носом упирается в борт судна)
    private static readonly Vector2[] TugPlan =
    {
        new Vector2(-0.50f, 0.88f), new Vector2(-0.44f, 1f), new Vector2(0.18f, 1f), new Vector2(0.32f, 0.93f),
        new Vector2(0.42f, 0.76f), new Vector2(0.48f, 0.56f), new Vector2(0.50f, 0.40f)
    };

    /// <summary>
    /// Модель судна по профилю и огням (мачты — до высоты огней). tug (19.3б) — буксир по профилю малого судна:
    /// тупой нос, кранец по носу и бортам, буксирная лебёдка на баке.
    /// </summary>
    public static Mesh Build(in HullProfile h, List<NavLight> lights, bool tug = false)
    {
        var b = new Builder();
        float L = (float)h.Length, B = (float)h.Beam, d = (float)h.Draft, fb = (float)h.Freeboard;
        Vector2[] plan = tug ? TugPlan : h.Type == TrafficVesselType.Tanker ? TankerPlan : h.Type == TrafficVesselType.Small ? SmallPlan : CargoPlan;

        // ── Корпус: борта (подводная часть и надводный борт), палуба ──
        var outline = new List<Vector2>();   // замкнутый контур палубы (x — к ПБ, y — к носу)
        foreach (Vector2 p in plan) outline.Add(new Vector2(p.y * 0.5f * B, p.x * L));
        for (int i = plan.Length - 1; i >= 0; i--)
            if (plan[i].y * 0.5f * B > 0.05f) outline.Add(new Vector2(-plan[i].y * 0.5f * B, plan[i].x * L));
        const float bootTop = 0.3f;
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i], c = outline[(i + 1) % outline.Count];
            Side(b, Boot, a, c, -d, bootTop, 0.92f);
            Side(b, Hull, a, c, bootTop, fb, 1f);
        }
        Vector2 center = Vector2.zero;
        foreach (Vector2 p in outline) center += p;
        center /= outline.Count;
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i], c = outline[(i + 1) % outline.Count];
            b.Tri(Deck, new Vector3(center.x, fb, center.y), new Vector3(a.x, fb, a.y), new Vector3(c.x, fb, c.y), Vector3.up);
        }

        // ── Надстройка: ярусы, рубка с окнами, крылья мостика ──
        float zf = (float)h.HouseFore, za = (float)h.HouseAft, th = (float)h.TierHeight;
        float houseLen = zf - za;
        float y = fb;
        for (int t = 0; t < h.HouseTiers; t++)
        {
            bool top = t == h.HouseTiers - 1;
            float w = B * (top ? 0.62f : 0.8f - 0.04f * t);
            float len = top ? houseLen * 0.55f : houseLen * (1f - 0.08f * t);
            float zc = zf - len * 0.5f;
            b.Box(House, new Vector3(0f, y + th * 0.5f, zc), new Vector3(w, th, len));
            if (top)
            {
                // Окна рубки (полоса на передней стенке и бортах) и крылья мостика
                b.Box(Dark, new Vector3(0f, y + th * 0.62f, zf + 0.02f), new Vector3(w * 0.92f, th * 0.35f, 0.08f));
                b.Box(House, new Vector3(0f, y + 0.15f, zf - 1.2f), new Vector3(B, 0.3f, 2.4f));
            }
            y += th;
        }
        float roof = y;

        switch (h.Type)
        {
            case TrafficVesselType.Cargo:
            {
                // Крышки трюмов между фок-мачтой и надстройкой
                float z0 = zf + 0.03f * L, z1 = (float)h.ForemastX - 0.03f * L;
                int n = 5;
                float gap = 0.012f * L, hl = (z1 - z0 - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                    b.Box(Accent, new Vector3(0f, fb + 0.75f, z0 + hl * 0.5f + i * (hl + gap)), new Vector3(B * 0.72f, 1.5f, hl));
                // Бак (полубак)
                b.Box(Hull, new Vector3(0f, fb + 1.2f, 0.45f * L), new Vector3(B * 0.5f, 2.4f, 0.06f * L));
                Funnel(b, h, roof, L, B);
                break;
            }
            case TrafficVesselType.Tanker:
            {
                // Трубопровод и мостик-переход по диаметральной плоскости, манифольд
                float z0 = zf + 0.02f * L, z1 = 0.44f * L;
                b.Box(Accent, new Vector3(0f, fb + 0.8f, 0.5f * (z0 + z1)), new Vector3(1.6f, 1.0f, z1 - z0));
                b.Box(Dark, new Vector3(0f, fb + 2.2f, 0.5f * (z0 + z1)), new Vector3(1.2f, 0.25f, z1 - z0));
                b.Box(Accent, new Vector3(0f, fb + 0.9f, 0.02f * L), new Vector3(B * 0.85f, 1.2f, 2.5f));
                Funnel(b, h, roof, L, B);
                break;
            }
            default:
            {
                // Малое судно: фальшборт открытой кормовой палубы
                b.Box(Hull, new Vector3(0.5f * B - 0.15f, fb + 0.5f, -0.25f * L), new Vector3(0.3f, 1.0f, 0.45f * L));
                b.Box(Hull, new Vector3(-0.5f * B + 0.15f, fb + 0.5f, -0.25f * L), new Vector3(0.3f, 1.0f, 0.45f * L));
                if (tug)
                {
                    // Кранец: толстый брус по носу и привальные брусья по бортам; лебёдка на баке
                    float zb = 0.5f * L;
                    b.Box(Dark, new Vector3(0f, 0.5f * fb, zb - 0.1f), new Vector3(0.8f * B, fb + 1.2f, 1.2f));
                    b.Box(Dark, new Vector3(0.5f * B + 0.1f, fb - 0.3f, -0.05f * L), new Vector3(0.4f, 0.6f, 0.8f * L));
                    b.Box(Dark, new Vector3(-0.5f * B - 0.1f, fb - 0.3f, -0.05f * L), new Vector3(0.4f, 0.6f, 0.8f * L));
                    b.Box(Accent, new Vector3(0f, fb + 0.6f, 0.5f * ((float)h.HouseFore + zb)), new Vector3(0.35f * B, 1.2f, 1.6f));
                }
                break;
            }
        }

        // ── Мачты под топовыми огнями, кормовой флагшток ──
        float mast = Mathf.Clamp(0.004f * L, 0.25f, 0.8f);
        foreach (NavLight l in lights)
        {
            float lz = (float)l.Forward, ly = (float)l.Height;
            switch (l.Kind)
            {
                case NavLightKind.MastheadForward:
                case NavLightKind.MastheadAft:
                {
                    float baseY = (lz <= zf + 0.5f && lz >= za - 0.5f) ? roof : fb;
                    b.Box(Dark, new Vector3(0f, 0.5f * (baseY + ly + 0.4f), lz), new Vector3(mast, ly + 0.4f - baseY, mast));
                    break;
                }
                case NavLightKind.Sternlight:
                    b.Box(Dark, new Vector3(0f, 0.5f * (fb + ly), lz), new Vector3(0.2f, ly - fb, 0.2f));
                    break;
            }
        }

        return b.ToMesh(tug ? "TrafficHull_Tug" : "TrafficHull_" + h.Type);
    }

    private static void Funnel(Builder b, in HullProfile h, float roof, float L, float B)
    {
        float zf = (float)h.HouseFore, za = (float)h.HouseAft;
        float z = za + 0.22f * (zf - za);
        float fh = Mathf.Max(3f, 0.6f * (float)h.TierHeight * h.HouseTiers);
        float fy = (float)h.Freeboard + (float)h.TierHeight * (h.HouseTiers - 1);
        b.Box(House, new Vector3(0f, fy + fh * 0.5f, z), new Vector3(B * 0.22f, fh, 0.07f * L));
        b.Box(Accent, new Vector3(0f, fy + fh * 0.72f, z), new Vector3(B * 0.225f, fh * 0.12f, 0.071f * L));
        b.Box(Dark, new Vector3(0f, fy + fh + 0.15f, z), new Vector3(B * 0.2f, 0.3f, 0.065f * L));
    }

    // Борт между точками контура a → c (плоскость XZ), от y0 до y1; scaleBottom — сужение книзу
    private static void Side(Builder b, int sub, Vector2 a, Vector2 c, float y0, float y1, float scaleBottom)
    {
        var p00 = new Vector3(a.x * scaleBottom, y0, a.y);
        var p01 = new Vector3(c.x * scaleBottom, y0, c.y);
        var p10 = new Vector3(a.x, y1, a.y);
        var p11 = new Vector3(c.x, y1, c.y);
        Vector3 edge = new Vector3(c.x - a.x, 0f, c.y - a.y);
        Vector3 outward = Vector3.Cross(Vector3.up, edge).normalized;   // вправо по обходу
        Vector3 mid = new Vector3(0.5f * (a.x + c.x), 0f, 0.5f * (a.y + c.y));
        if (Vector3.Dot(outward, mid) < 0f) outward = -outward;          // наружу от диаметральной плоскости
        b.Quad(sub, p00, p01, p11, p10, outward);
    }

    // ─────────── Сборщик ───────────
    private sealed class Builder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] tris = new List<int>[SubmeshCount];

        public Builder() { for (int i = 0; i < SubmeshCount; i++) tris[i] = new List<int>(); }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            if (Vector3.Dot(n, facing) < 0f) { Vector3 t = b; b = c; c = t; n = -n; }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Tri(sub, a, b, c, facing);
            Tri(sub, a, c, d, facing);
        }

        /// <summary>Коробка без дна (центр, размеры по X, Y, Z).</summary>
        public void Box(int sub, Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            Vector3 p(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(sub, p(-1, 1, -1), p(1, 1, -1), p(1, 1, 1), p(-1, 1, 1), Vector3.up);
            Quad(sub, p(-1, -1, 1), p(1, -1, 1), p(1, 1, 1), p(-1, 1, 1), Vector3.forward);
            Quad(sub, p(-1, -1, -1), p(1, -1, -1), p(1, 1, -1), p(-1, 1, -1), Vector3.back);
            Quad(sub, p(1, -1, -1), p(1, -1, 1), p(1, 1, 1), p(1, 1, -1), Vector3.right);
            Quad(sub, p(-1, -1, -1), p(-1, -1, 1), p(-1, 1, 1), p(-1, 1, -1), Vector3.left);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
