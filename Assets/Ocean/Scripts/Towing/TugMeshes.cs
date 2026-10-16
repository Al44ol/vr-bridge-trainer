// ============================================================
// TugMeshes.cs — Процедурная low-poly модель своего буксира (запрос 19.4а; облик — по фото, 17.10.2026)
//
// ASD-буксир 22 × 8 м (ShipPresets.FillTug), как современный портовый буксир: синий корпус с тупым носом и красной
// подводной частью, высокий фальшборт к носу; чёрные кранцы-«колбасы» (труба Ø 0,9 м) по носу в два ряда и вокруг
// кормы — верх кранца выступает над фальшбортом, его видно изнутри; кранцевый брус по бортам; лебёдки на баке и
// на корме, кнехты; белая надстройка, на ней — приподнятая рулевая рубка с окнами (стойки, без стёкол) и крышей,
// трубы за рубкой, мачта с огнями, леерное ограждение на крыше надстройки.
// Игрок стоит на полу рубки (FloorHeight над водой): там — рулевая стойка мостика МОСТИКИ (как на катере,
// ShipTypeSwitcher.hideOnBoat), рубка строится вокруг неё (стойка — в ConsoleStation·L впереди миделя).
// Система координат модели (как BoatMeshes): +Z — нос, +X — правый борт, +Y — вверх, начало — мидель на ватерлинии.
// Подмеши (материалы задаёт ShipTypeSwitcher): 0 — борт (синий), 1 — подводная часть (красная), 2 — палуба (серая),
// 3 — тёмные детали (кранцы, лебёдки, кнехты, планширь), 4 — надстройка и рубка (белые).
// Все коробки — с шестью гранями (крыша видна и снизу, из рубки).
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class TugMeshes
{
    public const int SubHull = 0, SubBottom = 1, SubDeck = 2, SubTrim = 3, SubSuper = 4, SubCount = 5;

    /// <summary>Главная палуба над ватерлинией, м.</summary>
    public const float DeckHeight = 1.3f;
    /// <summary>Пол рулевой рубки над ватерлинией, м (на нём стоит игрок).</summary>
    public const float FloorHeight = 4.2f;
    /// <summary>Рулевая стойка — от миделя к носу, доля L.</summary>
    public const float ConsoleStation = 0.1f;
    /// <summary>Фальшборт над палубой в корме, м (к носу растёт на BowSheerRise).</summary>
    public const float BulwarkHeight = 0.9f;
    public const float BowSheerRise = 0.5f;
    /// <summary>Рубка: от стойки вперёд и назад, м; полуширина, м; стенки под окнами и крыша над полом рубки, м.</summary>
    public const float HouseFore = 0.9f, HouseAft = 2.6f, HouseHalfWidth = 2.3f, HouseWall = 1.0f, HouseRoof = 2.3f;
    /// <summary>Надстройка под рубкой: полуширина, м; вперёд и назад от стойки, м.</summary>
    public const float DeckhouseHalfWidth = 3.0f, DeckhouseFore = 1.6f, DeckhouseAft = 5.2f;
    /// <summary>Мачта над крышей рубки, м.</summary>
    public const float MastHeight = 3.0f;
    /// <summary>Кранец-«колбаса»: радиус, м.</summary>
    public const float FenderRadius = 0.45f;

    private const float Wall = 0.08f;
    private const int Stations = 24;

    /// <summary>Полуширина по длине: t = 0 — транец, 1 — нос (тупой: у форштевня 0,42 полуширины).</summary>
    public static float HalfBeam(float t, float beam)
    {
        float b2 = 0.5f * beam;
        if (t < 0.15f) return b2 * (0.84f + 0.16f * t / 0.15f);
        if (t <= 0.55f) return b2;
        float u = (t - 0.55f) / 0.45f;
        return b2 * (0.42f + 0.58f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)));
    }

    public static float Sheer(float t) => DeckHeight + BulwarkHeight + BowSheerRise * t * t;

    private static float Keel(float t, float draft)
    {
        float d = 0.8f * draft;   // корпус; колонки ниже корпуса не рисуются
        return t < 0.7f ? -d : -d * (1f - 0.55f * Mathf.Pow((t - 0.7f) / 0.3f, 1.3f));
    }

    /// <summary>Стойка (ось рубки) от миделя, м.</summary>
    public static float ConsoleZ(float length) => ConsoleStation * length;

    public static Vector3 MastheadLight(float length) =>
        new Vector3(0f, FloorHeight + HouseRoof + MastHeight + 0.15f, ConsoleZ(length) - 0.8f);

    public static Vector3 SidelightFor(float length, int side) =>
        new Vector3(side * (HouseHalfWidth + 0.3f), FloorHeight + HouseRoof + 0.15f, ConsoleZ(length) + HouseFore - 0.2f);

    /// <summary>Модель буксира: длина, ширина, осадка (м).</summary>
    public static Mesh Build(float length, float beam, float draft)
    {
        var mb = new BoatMeshes.Builder(SubCount);
        float deck = DeckHeight;
        for (int i = 0; i < Stations; i++)
        {
            float t0 = (float)i / Stations, t1 = (float)(i + 1) / Stations;
            for (int side = -1; side <= 1; side += 2)
            {
                Section s0 = Section.At(t0, side, length, beam, draft), s1 = Section.At(t1, side, length, beam, draft);
                Vector3 outward = new Vector3(side, 0f, 0f);
                mb.Quad(SubBottom, s0.Keel, s1.Keel, s1.Bilge, s0.Bilge, new Vector3(side * 0.3f, -1f, 0f));
                mb.Quad(SubBottom, s0.Bilge, s1.Bilge, s1.Boot, s0.Boot, new Vector3(side, -0.3f, 0f));
                mb.Quad(SubHull, s0.Boot, s1.Boot, s1.Gunwale, s0.Gunwale, outward);
                mb.Quad(SubTrim, s0.Gunwale, s1.Gunwale, s1.InnerTop, s0.InnerTop, Vector3.up);
                mb.Quad(SubHull, s0.InnerTop, s1.InnerTop, s1.InnerDeck, s0.InnerDeck, -outward);
            }
            Section p0 = Section.At(t0, -1, length, beam, draft), p1 = Section.At(t1, -1, length, beam, draft);
            Section q0 = Section.At(t0, 1, length, beam, draft), q1 = Section.At(t1, 1, length, beam, draft);
            mb.Quad(SubDeck, p0.InnerDeck, p1.InnerDeck, q1.InnerDeck, q0.InnerDeck, Vector3.up);
        }
        EndFace(mb, 0f, Vector3.back, length, beam, draft);
        EndFace(mb, 1f, Vector3.forward, length, beam, draft);

        Fenders(mb, length, beam);
        DeckGear(mb, length, beam);
        Superstructure(mb, length);
        return mb.ToMesh("OwnTug_Hull");
    }

    // ─────────── Кранцы: «колбасы» по носу (два ряда) и вокруг кормы, брус по бортам ───────────
    private static void Fenders(BoatMeshes.Builder mb, float length, float beam)
    {
        float r = FenderRadius, off = 0.7f * r;
        // Верхний ряд по носу: от 0,62 L до форштевня, поперёк носа, обратно — центр на 0,2 м ниже планширя
        var bow = new List<Vector3>();
        for (int i = 0; i <= 10; i++) { float t = 0.62f + 0.38f * i / 10f; bow.Add(Edge(t, +1, length, beam, off, Sheer(t) - 0.2f)); }
        float hb = HalfBeam(1f, beam), zb = 0.5f * length + off;
        bow.Add(new Vector3(0.5f * hb, Sheer(1f) - 0.2f, zb));
        bow.Add(new Vector3(-0.5f * hb, Sheer(1f) - 0.2f, zb));
        for (int i = 10; i >= 0; i--) { float t = 0.62f + 0.38f * i / 10f; bow.Add(Edge(t, -1, length, beam, off, Sheer(t) - 0.2f)); }
        Tube(mb, SubTrim, bow, r);
        // Нижний ряд по носу
        var low = new List<Vector3>();
        for (int i = 0; i <= 6; i++) { float t = 0.8f + 0.2f * i / 6f; low.Add(Edge(t, +1, length, beam, off, Sheer(t) - 0.2f - 2f * r)); }
        low.Add(new Vector3(0.5f * hb, Sheer(1f) - 0.2f - 2f * r, zb));
        low.Add(new Vector3(-0.5f * hb, Sheer(1f) - 0.2f - 2f * r, zb));
        for (int i = 6; i >= 0; i--) { float t = 0.8f + 0.2f * i / 6f; low.Add(Edge(t, -1, length, beam, off, Sheer(t) - 0.2f - 2f * r)); }
        Tube(mb, SubTrim, low, r);
        // Корма: вокруг транца и раковин
        var stern = new List<Vector3>();
        float hs = HalfBeam(0f, beam), zs = -0.5f * length - off, ys = Sheer(0f) - 0.2f;
        for (int i = 0; i <= 4; i++) { float t = 0.16f - 0.16f * i / 4f; stern.Add(Edge(t, -1, length, beam, off, ys)); }
        stern.Add(new Vector3(-0.6f * hs, ys, zs));
        stern.Add(new Vector3(0.6f * hs, ys, zs));
        for (int i = 4; i >= 0; i--) { float t = 0.16f - 0.16f * i / 4f; stern.Add(Edge(t, +1, length, beam, off, ys)); }
        Tube(mb, SubTrim, stern, r);
        // Брус по бортам между кранцами
        for (int side = -1; side <= 1; side += 2)
        {
            var belt = new List<Vector3>();
            for (int i = 0; i <= 8; i++) { float t = 0.16f + (0.62f - 0.16f) * i / 8f; belt.Add(Edge(t, side, length, beam, 0.1f, DeckHeight - 0.1f)); }
            Tube(mb, SubTrim, belt, 0.18f);
        }
    }

    // Точка у борта на станции t (side +1 — ПБ), вынесенная наружу на off, на высоте y
    private static Vector3 Edge(float t, int side, float length, float beam, float off, float y) =>
        new Vector3(side * (HalfBeam(t, beam) + off), y, (t - 0.5f) * length);

    // ─────────── Палуба: лебёдки, кнехты ───────────
    private static void DeckGear(BoatMeshes.Builder mb, float length, float beam)
    {
        float deck = DeckHeight;
        Winch(mb, new Vector3(0f, deck, 0.31f * length), 1.0f);    // носовая (буксирная) лебёдка
        Winch(mb, new Vector3(0f, deck, -0.33f * length), 0.85f);  // кормовая
        foreach (float z in new[] { 0.41f * length, -0.43f * length })
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 c = new Vector3(side * 0.28f * beam, deck, z);
                Cyl(mb, SubTrim, c, c + Vector3.up * 0.6f, 0.15f, 8);
                Cyl(mb, SubTrim, c + new Vector3(0f, 0f, 0.5f), c + new Vector3(0f, 0.6f, 0.5f), 0.15f, 8);
            }
        // Буксирная скоба (арка) над кормовой палубой
        float za = -0.22f * length, w = 0.32f * beam, h = 2.0f;
        Box6(mb, SubTrim, new Vector3(-w, deck + 0.5f * h, za), new Vector3(0.18f, h, 0.18f));
        Box6(mb, SubTrim, new Vector3(w, deck + 0.5f * h, za), new Vector3(0.18f, h, 0.18f));
        Box6(mb, SubTrim, new Vector3(0f, deck + h, za), new Vector3(2f * w + 0.18f, 0.18f, 0.18f));
    }

    // Лебёдка: основание, барабан поперёк судна, щёки, привод
    private static void Winch(BoatMeshes.Builder mb, Vector3 at, float scale)
    {
        float s = scale;
        Box6(mb, SubTrim, at + new Vector3(0f, 0.15f * s, 0f), new Vector3(2.2f * s, 0.3f * s, 1.1f * s));
        Vector3 c = at + new Vector3(0f, 0.85f * s, 0f);
        Cyl(mb, SubTrim, c + new Vector3(-0.8f * s, 0f, 0f), c + new Vector3(0.8f * s, 0f, 0f), 0.42f * s, 12);
        foreach (float x in new[] { -0.85f * s, 0.85f * s })
            Cyl(mb, SubSuper, c + new Vector3(x - 0.05f * s, 0f, 0f), c + new Vector3(x + 0.05f * s, 0f, 0f), 0.62f * s, 12);
        Box6(mb, SubSuper, at + new Vector3(1.25f * s, 0.6f * s, 0f), new Vector3(0.4f * s, 0.9f * s, 0.8f * s));
    }

    // ─────────── Надстройка, рубка, трубы, мачта ───────────
    private static void Superstructure(BoatMeshes.Builder mb, float length)
    {
        float deck = DeckHeight, floor = FloorHeight, zc = ConsoleZ(length);
        // Надстройка от палубы до пола рубки
        float dzf = zc + DeckhouseFore, dza = zc - DeckhouseAft, dw = DeckhouseHalfWidth;
        Box6(mb, SubSuper, new Vector3(0f, 0.5f * (deck + floor), 0.5f * (dzf + dza)), new Vector3(2f * dw, floor - deck, dzf - dza));
        // Иллюминаторы надстройки — тёмные квадраты по бортам
        for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < 4; k++)
            {
                float z = dza + 1.0f + k * 1.6f;
                mb.Quad(SubTrim, new Vector3(side * (dw + 0.01f), deck + 1.3f, z), new Vector3(side * (dw + 0.01f), deck + 1.3f, z + 0.6f),
                        new Vector3(side * (dw + 0.01f), deck + 1.9f, z + 0.6f), new Vector3(side * (dw + 0.01f), deck + 1.9f, z), new Vector3(side, 0f, 0f));
            }
        // Леер по краю крыши надстройки
        Railing(mb, new[] { new Vector3(-dw, floor, dza), new Vector3(-dw, floor, dzf), new Vector3(dw, floor, dzf), new Vector3(dw, floor, dza), new Vector3(-dw, floor, dza) });

        // Рубка: стенки под окнами, стойки окон, крыша (с козырьком)
        float zf = zc + HouseFore, za = zc - HouseAft, w = HouseHalfWidth, len = zf - za;
        float yWall = floor + 0.5f * HouseWall;
        Box6(mb, SubSuper, new Vector3(0f, yWall, zf), new Vector3(2f * w, HouseWall, Wall));
        Box6(mb, SubSuper, new Vector3(-w, yWall, 0.5f * (zf + za)), new Vector3(Wall, HouseWall, len));
        Box6(mb, SubSuper, new Vector3(w, yWall, 0.5f * (zf + za)), new Vector3(Wall, HouseWall, len));
        float doorHalf = 0.6f, piece = w - doorHalf;
        Box6(mb, SubSuper, new Vector3(-(doorHalf + 0.5f * piece), yWall, za), new Vector3(piece, HouseWall, Wall));
        Box6(mb, SubSuper, new Vector3(doorHalf + 0.5f * piece, yWall, za), new Vector3(piece, HouseWall, Wall));
        float postH = HouseRoof - HouseWall, yPost = floor + HouseWall + 0.5f * postH;
        for (int k = 0; k <= 4; k++) Box6(mb, SubSuper, new Vector3(-w + 2f * w * k / 4f, yPost, zf), new Vector3(0.09f, postH, 0.09f));
        for (int side = -1; side <= 1; side += 2)
            for (int k = 1; k <= 3; k++) Box6(mb, SubSuper, new Vector3(side * w, yPost, zf - len * k / 3f), new Vector3(0.09f, postH, 0.09f));
        foreach (float x in new[] { -doorHalf, doorHalf }) Box6(mb, SubSuper, new Vector3(x, yPost, za), new Vector3(0.09f, postH, 0.09f));
        // Подоконник (тёмный) и перемычка над окнами
        Box6(mb, SubTrim, new Vector3(0f, floor + HouseWall + 0.02f, zf), new Vector3(2f * w + 0.1f, 0.05f, 0.2f));
        Box6(mb, SubSuper, new Vector3(0f, floor + HouseRoof - 0.12f, zf), new Vector3(2f * w, 0.24f, Wall));
        Box6(mb, SubSuper, new Vector3(0f, floor + HouseRoof + 0.06f, 0.5f * (zf + za) + 0.2f), new Vector3(2f * w + 0.6f, 0.12f, len + 0.8f));
        // Трубы за рубкой
        foreach (float x in new[] { -1.7f, 1.7f })
            Box6(mb, SubSuper, new Vector3(x, floor + 1.3f, za - 0.9f), new Vector3(0.55f, 2.6f, 0.7f));
        // Мачта и рея на крыше
        Vector3 mb0 = new Vector3(0f, floor + HouseRoof + 0.12f, zc - 0.8f);
        Cyl(mb, SubSuper, mb0, mb0 + Vector3.up * MastHeight, 0.07f, 8);
        Box6(mb, SubSuper, mb0 + new Vector3(0f, 0.7f * MastHeight, 0f), new Vector3(1.6f, 0.06f, 0.06f));
    }

    private static void Railing(BoatMeshes.Builder mb, Vector3[] path)
    {
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector3 a = path[i], b = path[i + 1];
            Cyl(mb, SubSuper, a + Vector3.up * 1.0f, b + Vector3.up * 1.0f, 0.03f, 6);
            Cyl(mb, SubSuper, a + Vector3.up * 0.5f, b + Vector3.up * 0.5f, 0.02f, 6);
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 1.5f));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)k / n);
                Cyl(mb, SubSuper, p, p + Vector3.up * 1.0f, 0.025f, 6);
            }
        }
    }

    // ─────────── Примитивы ───────────

    /// <summary>Коробка с шестью гранями (у BoatMeshes.Builder.Box нет нижней грани — крыша снизу была бы не видна).</summary>
    private static void Box6(BoatMeshes.Builder mb, int sub, Vector3 c, Vector3 size)
    {
        mb.Box(sub, c, size);
        Vector3 h = 0.5f * size;
        mb.Quad(sub, c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, h.z), Vector3.down);
    }

    /// <summary>Цилиндр (n-гранная призма) от a до b радиуса r, с торцами.</summary>
    private static void Cyl(BoatMeshes.Builder mb, int sub, Vector3 a, Vector3 b, float r, int n)
    {
        Vector3 ax = (b - a).normalized;
        Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, v = Vector3.Cross(ax, u);
        for (int k = 0; k < n; k++)
        {
            float f0 = 2f * Mathf.PI * k / n, f1 = 2f * Mathf.PI * (k + 1) / n;
            Vector3 d0 = u * Mathf.Cos(f0) + v * Mathf.Sin(f0), d1 = u * Mathf.Cos(f1) + v * Mathf.Sin(f1);
            mb.Quad(sub, a + r * d0, b + r * d0, b + r * d1, a + r * d1, d0 + d1);
            mb.Tri(sub, a, a + r * d1, a + r * d0, -ax);
            mb.Tri(sub, b, b + r * d0, b + r * d1, ax);
        }
    }

    /// <summary>Труба радиуса r по ломаной (8 граней, кольца — поперёк средней касательной), с торцами.</summary>
    private static void Tube(BoatMeshes.Builder mb, int sub, List<Vector3> pts, float r)
    {
        const int n = 8;
        var rings = new List<Vector3[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 tan = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized, up = Vector3.Cross(tan, side);
            var ring = new Vector3[n];
            for (int k = 0; k < n; k++) { float f = 2f * Mathf.PI * k / n; ring[k] = pts[i] + r * (side * Mathf.Cos(f) + up * Mathf.Sin(f)); }
            rings.Add(ring);
        }
        for (int i = 0; i + 1 < rings.Count; i++)
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                Vector3 mid = 0.5f * (pts[i] + pts[i + 1]);
                Vector3 c = 0.25f * (rings[i][k] + rings[i][k1] + rings[i + 1][k] + rings[i + 1][k1]);
                mb.Quad(sub, rings[i][k], rings[i + 1][k], rings[i + 1][k1], rings[i][k1], c - mid);
            }
        for (int e = 0; e < 2; e++)
        {
            int i = e == 0 ? 0 : rings.Count - 1;
            Vector3 dir = e == 0 ? pts[0] - pts[1] : pts[i] - pts[i - 1];
            for (int k = 0; k < n; k++) mb.Tri(sub, pts[i], rings[i][k], rings[i][(k + 1) % n], dir);
        }
    }

    // Торец: веер от центра по контуру сечения (корма t = 0, нос t = 1) и поперечный фальшборт
    private static void EndFace(BoatMeshes.Builder mb, float t, Vector3 normal, float length, float beam, float draft)
    {
        Section ps = Section.At(t, -1, length, beam, draft), ss = Section.At(t, 1, length, beam, draft);
        var ring = new List<Vector3> { ss.Keel, ss.Bilge, ss.Boot, ss.Gunwale, ss.InnerTop, ss.InnerDeck, ps.InnerDeck, ps.InnerTop, ps.Gunwale, ps.Boot, ps.Bilge };
        float z = ss.Keel.z;
        var center = new Vector3(0f, 0.5f * (ss.Keel.y + DeckHeight), z);
        for (int k = 0; k < ring.Count; k++)
        {
            Vector3 a = ring[k], b = ring[(k + 1) % ring.Count];
            bool under = a.y < 0.15f && b.y < 0.15f;
            mb.Tri(under ? SubBottom : SubHull, center, a, b, normal);
        }
        float top = Sheer(t);
        Vector3 inward = -normal * Wall;
        var o0 = new Vector3(ss.InnerDeck.x, DeckHeight, z);
        var o1 = new Vector3(ps.InnerDeck.x, DeckHeight, z);
        var t0 = new Vector3(ss.InnerDeck.x, top, z);
        var t1 = new Vector3(ps.InnerDeck.x, top, z);
        mb.Quad(SubHull, o0, t0, t1, o1, normal);
        mb.Quad(SubHull, o0 + inward, t0 + inward, t1 + inward, o1 + inward, -normal);
        mb.Quad(SubTrim, t0, t1, t1 + inward, t0 + inward, Vector3.up);
    }

    private struct Section
    {
        public Vector3 Keel, Bilge, Boot, Gunwale, InnerTop, InnerDeck;

        public static Section At(float t, int side, float length, float beam, float draft)
        {
            float z = (t - 0.5f) * length;
            float b = HalfBeam(t, beam);
            float yk = Keel(t, draft);
            float sh = Sheer(t);
            float bi = Mathf.Max(0f, b - Wall);
            return new Section
            {
                Keel = new Vector3(0f, yk, z),
                Bilge = new Vector3(side * 0.9f * b, 0.6f * yk, z),
                Boot = new Vector3(side * b, 0.15f, z),
                Gunwale = new Vector3(side * b, sh, z),
                InnerTop = new Vector3(side * bi, sh, z),
                InnerDeck = new Vector3(side * bi, DeckHeight, z),
            };
        }
    }
}
