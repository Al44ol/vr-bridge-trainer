// ============================================================
// TugMeshes.cs — Процедурная low-poly модель своего буксира (запрос 19.4а)
//
// ASD-буксир 22 × 8 м (ShipPresets.FillTug): широкий корпус с тупым носом, высокий фальшборт к носу,
// носовой кранец и кранцевый пояс по бортам, лебёдка на баке, открытая рулевая рубка (нижние стенки по пояс,
// стойки, крыша — без стёкол, чтобы видеть бак и борт клиента), мачта с огнями на крыше.
// Рулевой стойки в модели нет — остаётся стойка мостика МОСТИКИ (как на катере, ShipTypeSwitcher.hideOnBoat);
// рубка строится вокруг неё: стойка — в ConsoleStation·L впереди миделя.
// Система координат модели (как BoatMeshes, TrafficMeshes): +Z — нос, +X — правый борт, +Y — вверх,
// начало — мидель на ватерлинии. Подмеши — материалы катера (Setup Fast Boat): 0 — борт и рубка,
// 1 — подводная часть, 2 — палуба, 3 — тёмные детали (кранцы, планширь, стойки, мачта, лебёдка).
// Строится в Play Mode при первом переключении на буксир (ShipTypeSwitcher), Setup не нужен.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public static class TugMeshes
{
    /// <summary>Палуба над ватерлинией, м.</summary>
    public const float DeckHeight = 1.3f;
    /// <summary>Рулевая стойка — от миделя к носу, доля L.</summary>
    public const float ConsoleStation = 0.1f;
    /// <summary>Фальшборт над палубой в корме, м (к носу растёт на BowSheerRise).</summary>
    public const float BulwarkHeight = 0.9f;
    public const float BowSheerRise = 0.5f;
    /// <summary>Рубка: от стойки вперёд и назад, м; полуширина, м; нижние стенки и крыша над палубой, м.</summary>
    public const float HouseFore = 0.8f, HouseAft = 2.6f, HouseHalfWidth = 2.2f, HouseWall = 1.0f, HouseRoof = 2.4f;
    /// <summary>Мачта над крышей, м.</summary>
    public const float MastHeight = 3.0f;

    private const float Wall = 0.08f;
    private const int Stations = 24;
    private const int SubHull = BoatMeshes.SubHull, SubBottom = BoatMeshes.SubBottom, SubDeck = BoatMeshes.SubDeck, SubTrim = BoatMeshes.SubTrim;

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
        new Vector3(0f, DeckHeight + HouseRoof + MastHeight + 0.15f, ConsoleZ(length) - 0.6f);

    public static Vector3 Sidelight(int side) =>
        new Vector3(side * (HouseHalfWidth + 0.25f), DeckHeight + HouseRoof + 0.15f, 0f);

    public static Vector3 SidelightFor(float length, int side) => Sidelight(side) + new Vector3(0f, 0f, ConsoleZ(length) + HouseFore - 0.2f);

    /// <summary>Модель буксира: длина, ширина, осадка (м).</summary>
    public static Mesh Build(float length, float beam, float draft)
    {
        var mb = new BoatMeshes.Builder(4);
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
                // Кранцевый пояс по борту (на уровне палубы)
                Vector3 f0a = s0.Fender(-0.35f), f0b = s0.Fender(0.1f), f1a = s1.Fender(-0.35f), f1b = s1.Fender(0.1f);
                mb.Quad(SubTrim, f0a, f1a, f1b, f0b, outward);
                mb.Quad(SubTrim, f0b, f1b, new Vector3(s1.Boot.x, deck + 0.1f, s1.Boot.z), new Vector3(s0.Boot.x, deck + 0.1f, s0.Boot.z), Vector3.up);
                mb.Quad(SubTrim, f0a, f1a, new Vector3(s1.Boot.x, deck - 0.35f, s1.Boot.z), new Vector3(s0.Boot.x, deck - 0.35f, s0.Boot.z), Vector3.down);
            }
            Section p0 = Section.At(t0, -1, length, beam, draft), p1 = Section.At(t1, -1, length, beam, draft);
            Section q0 = Section.At(t0, 1, length, beam, draft), q1 = Section.At(t1, 1, length, beam, draft);
            mb.Quad(SubDeck, p0.InnerDeck, p1.InnerDeck, q1.InnerDeck, q0.InnerDeck, Vector3.up);
        }
        EndFace(mb, 0f, Vector3.back, length, beam, draft);
        EndFace(mb, 1f, Vector3.forward, length, beam, draft);

        // Носовой кранец — толстый брус поперёк форштевня, от палубы почти до воды
        float zb = 0.5f * length, hb = HalfBeam(1f, beam);
        mb.Box(SubTrim, new Vector3(0f, deck - 0.25f, zb + 0.25f), new Vector3(2f * hb + 0.5f, 1.6f, 0.5f));

        // Лебёдка и кнехты на баке
        mb.Box(SubTrim, new Vector3(0f, deck + 0.45f, 0.3f * length), new Vector3(1.6f, 0.9f, 1.0f));
        for (int side = -1; side <= 1; side += 2)
        {
            mb.Box(SubTrim, new Vector3(side * 0.3f * beam, deck + 0.25f, 0.36f * length), new Vector3(0.3f, 0.5f, 0.3f));
            mb.Box(SubTrim, new Vector3(side * 0.3f * beam, deck + 0.25f, -0.38f * length), new Vector3(0.3f, 0.5f, 0.3f));
        }

        // Рубка: нижние стенки, стойки, крыша; проход сзади по середине 1,2 м
        float zc = ConsoleZ(length), zf = zc + HouseFore, za = zc - HouseAft, w = HouseHalfWidth, len = zf - za;
        float yWall = deck + 0.5f * HouseWall;
        mb.Box(SubHull, new Vector3(0f, yWall, zf), new Vector3(2f * w, HouseWall, Wall));
        mb.Box(SubHull, new Vector3(-w, yWall, 0.5f * (zf + za)), new Vector3(Wall, HouseWall, len));
        mb.Box(SubHull, new Vector3(w, yWall, 0.5f * (zf + za)), new Vector3(Wall, HouseWall, len));
        float doorHalf = 0.6f, piece = w - doorHalf;
        mb.Box(SubHull, new Vector3(-(doorHalf + 0.5f * piece), yWall, za), new Vector3(piece, HouseWall, Wall));
        mb.Box(SubHull, new Vector3(doorHalf + 0.5f * piece, yWall, za), new Vector3(piece, HouseWall, Wall));
        float postH = HouseRoof - HouseWall, yPost = deck + HouseWall + 0.5f * postH;
        foreach (float x in new[] { -w, -0.5f * w, 0.5f * w, w })
            mb.Box(SubTrim, new Vector3(x, yPost, zf), new Vector3(0.1f, postH, 0.1f));
        foreach (float x in new[] { -w, w })
        {
            mb.Box(SubTrim, new Vector3(x, yPost, za), new Vector3(0.1f, postH, 0.1f));
            mb.Box(SubTrim, new Vector3(x, yPost, 0.5f * (zf + za)), new Vector3(0.1f, postH, 0.1f));
        }
        mb.Box(SubHull, new Vector3(0f, deck + HouseRoof + 0.05f, 0.5f * (zf + za)), new Vector3(2f * w + 0.6f, 0.1f, len + 0.6f));
        // Мачта на крыше
        mb.Box(SubTrim, new Vector3(0f, deck + HouseRoof + 0.1f + 0.5f * MastHeight, zc - 0.6f), new Vector3(0.12f, MastHeight, 0.12f));

        return mb.ToMesh("OwnTug_Hull");
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
        private float half, z;

        public Vector3 Fender(float dy) => new Vector3(Mathf.Sign(Boot.x) * (half + 0.18f), DeckHeight + dy, z);

        public static Section At(float t, int side, float length, float beam, float draft)
        {
            float z = (t - 0.5f) * length;
            float b = HalfBeam(t, beam);
            float yk = Keel(t, draft);
            float sh = Sheer(t);
            float bi = Mathf.Max(0f, b - Wall);
            return new Section
            {
                half = b, z = z,
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
