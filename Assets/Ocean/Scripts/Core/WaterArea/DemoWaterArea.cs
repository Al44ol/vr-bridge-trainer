// ============================================================
// DemoWaterArea.cs — Демонстрационная учебная акватория (B1)
//
// Для первого сценария (B2, «Проход фарватером по путевым точкам»).
// Направление ограждения — с моря (юг) к порту (север/северо-восток):
// латеральные знаки левой стороны (красные) — слева, правой (зелёные) — справа.
//
//   • фарватер шириной 2 кбт (370,4 м): колено 1 — курс 000°, 3,5 км;
//     поворот вправо на 60° у (0, 3200); колено 2 — курс 060°, ≈3,5 км;
//   • латеральные буи — на кромках фарватера через ≤ 800 м, поворотные —
//     в точках пересечения кромок колен (расчёт, а не ручная расстановка);
//   • осевой знак — на подходе (≈500 м до входа);
//   • мели: «Северная» 2,0 м за поворотом (S-кардинальный: не довернул —
//     сел на мель), «у поворота» 2,5 м с внутренней стороны (N-кардинальный:
//     «срезал угол»), «Западная» 2,5 м (E-кардинальный), «Банка Восточная» 6,0 м
//     (W-кардинальный; для судна сцены с осадкой 3 м безопасна, для глубокосидящего — нет);
//   • камень 1,5 м со знаком отдельной опасности на подходе;
//   • суша: западный и северный берег (12 м), остров на востоке (25 м) — ориентиры.
// Корректность расстановки проверяют тесты WaterAreaTests (стороны буёв,
// мели вне коридора, кардинальные знаки с нужной стороны мели).
// ============================================================
using System;
using System.Collections.Generic;

public static class DemoWaterArea
{
    public const double FairwayWidth = 2.0 * WaterArea.CableMeters;   // 370,4 м
    public const double MaxBuoySpacing = 800.0;

    /// <summary>Начальная позиция судна для сценария, м.</summary>
    public static readonly WorldPosition StartPosition = new WorldPosition(0, -2500);
    /// <summary>Начальный курс, градусы.</summary>
    public const double StartHeadingDeg = 0.0;

    /// <summary>Ось фарватера (с моря к порту).</summary>
    public static WorldPosition[] Axis()
    {
        double turn = 60.0 * Math.PI / 180.0;
        var p1 = new WorldPosition(0, 3200);
        return new[]
        {
            new WorldPosition(0, -300),
            p1,
            new WorldPosition(p1.East + 3500.0 * Math.Sin(turn), p1.North + 3500.0 * Math.Cos(turn))
        };
    }

    /// <summary>Заполнить акваторию демонстрационными объектами (прежнее содержимое удаляется).</summary>
    public static void Fill(WaterArea area)
    {
        area.areaName = "Демонстрационная акватория";
        area.description = "Фарватер 2 кбт с поворотом вправо на 60°, отмели с кардинальными знаками, " +
                           "камень со знаком отдельной опасности. Система МАМС, регион A. Старт сценария: " +
                           "E 0, N −2500, курс 000°.";
        area.boundsMin = new WorldPosition(-4000, -4000);
        area.boundsMax = new WorldPosition(7000, 8000);
        area.defaultDepth = 30f;
        area.aids = new List<AidToNavigation>();
        area.shoals = new List<ShoalArea>();
        area.land = new List<LandArea>();
        area.fairways = new List<FairwayLine>();

        WorldPosition[] axis = Axis();
        area.fairways.Add(new FairwayLine { name = "Фарватер №1", width = (float)FairwayWidth, axis = new List<WorldPosition>(axis) });

        // ── Латеральные знаки на кромках ──
        List<WorldPosition> left = OffsetPolyline(axis, FairwayWidth * 0.5);
        List<WorldPosition> right = OffsetPolyline(axis, -FairwayWidth * 0.5);
        int n = 2;
        foreach (WorldPosition p in SampleEdge(left, MaxBuoySpacing))
        {
            area.aids.Add(new AidToNavigation($"Красный №{n}", AidType.LateralPort, p.East, p.North));
            n += 2;
        }
        n = 1;
        foreach (WorldPosition p in SampleEdge(right, MaxBuoySpacing))
        {
            area.aids.Add(new AidToNavigation($"Зелёный №{n}", AidType.LateralStarboard, p.East, p.North));
            n += 2;
        }

        // ── Осевой знак на подходе (чуть левее оси, чтобы судно не шло «сквозь» буй) ──
        area.aids.Add(new AidToNavigation("Осевой «Вход»", AidType.SafeWater, -80, -800));

        // ── Мели и кардинальные знаки ──
        area.shoals.Add(Shoal("Отмель Северная", 2.0f,
            -950, 3820, -600, 3740, -200, 3730, 150, 3780, 300, 3900, 250, 4150, -100, 4330, -600, 4350, -950, 4150));
        area.aids.Add(new AidToNavigation("Южный «Северная»", AidType.CardinalSouth, -50, 3650));

        area.shoals.Add(Shoal("Отмель у поворота", 2.5f,
            480, 2250, 900, 2180, 1350, 2350, 1450, 2700, 1200, 2950, 800, 2980, 500, 2800, 420, 2500));
        area.aids.Add(new AidToNavigation("Северный «Поворот»", AidType.CardinalNorth, 850, 3080));

        area.shoals.Add(Shoal("Отмель Западная", 2.5f,
            -1250, -150, -800, -250, -560, 100, -600, 600, -900, 850, -1250, 500));
        area.aids.Add(new AidToNavigation("Восточный «Западная»", AidType.CardinalEast, -470, 300));

        area.shoals.Add(Shoal("Банка Восточная", 6.0f,
            520, 650, 900, 600, 1150, 900, 1100, 1350, 750, 1500, 500, 1200));
        area.aids.Add(new AidToNavigation("Западный «Банка»", AidType.CardinalWest, 420, 1050));

        // Камень: восьмиугольник R = 35 м
        var rock = new ShoalArea { name = "Камень", depth = 1.5f };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4.0;
            rock.vertices.Add(new WorldPosition(450 + 35 * Math.Sin(a), -1500 + 35 * Math.Cos(a)));
        }
        area.shoals.Add(rock);
        area.aids.Add(new AidToNavigation("Отдельной опасности «Камень»", AidType.IsolatedDanger, 450, -1500));

        // ── Суша ──
        area.land.Add(Land("Западный и северный берег", 12f,
            -6000, -6000, -2600, -6000, -2400, -3500, -2100, -1200, -1900, 800, -2050, 2600, -1800, 4300,
            -1300, 5600, -600, 6600, 400, 7100, 1800, 7000, 3000, 6700, 4200, 6900, 5200, 7400, 6000, 8000,
            6000, 10000, -6000, 10000));
        area.land.Add(Land("Остров Восточный", 25f,
            4600, 2300, 4850, 2050, 5250, 2080, 5480, 2350, 5420, 2750, 5150, 2950, 4800, 2900, 4580, 2650));

        area.MarkChanged();
    }

    /// <summary>
    /// Смещение ломаной на distance (> 0 — влево по ходу, < 0 — вправо);
    /// во внутренних вершинах — пересечение смещённых прямых соседних колен.
    /// </summary>
    public static List<WorldPosition> OffsetPolyline(IList<WorldPosition> axis, double distance)
    {
        int n = axis.Count;
        var result = new List<WorldPosition>(n);
        for (int i = 0; i < n; i++)
        {
            if (i == 0 || i == n - 1)
            {
                int a = i == 0 ? 0 : n - 2;
                LeftNormal(axis[a], axis[a + 1], out double le, out double ln);
                result.Add(new WorldPosition(axis[i].East + le * distance, axis[i].North + ln * distance));
                continue;
            }
            LeftNormal(axis[i - 1], axis[i], out double l1e, out double l1n);
            LeftNormal(axis[i], axis[i + 1], out double l2e, out double l2n);
            double be = l1e + l2e, bn = l1n + l2n;
            double cosHalf = 0.5 * Math.Sqrt(be * be + bn * bn);   // cos половины угла поворота
            double bl = Math.Max(1e-9, Math.Sqrt(be * be + bn * bn));
            double shift = distance / Math.Max(cosHalf, 1e-3);
            result.Add(new WorldPosition(axis[i].East + be / bl * shift, axis[i].North + bn / bl * shift));
        }
        return result;
    }

    /// <summary>Точки на кромке: вершины и равномерно между ними с шагом ≤ maxSpacing.</summary>
    public static List<WorldPosition> SampleEdge(IList<WorldPosition> edge, double maxSpacing)
    {
        var pts = new List<WorldPosition> { edge[0] };
        for (int i = 0; i + 1 < edge.Count; i++)
        {
            WorldPosition a = edge[i], b = edge[i + 1];
            double len = (b - a).Magnitude;
            int k = Math.Max(1, (int)Math.Ceiling(len / maxSpacing - 1e-9));
            for (int j = 1; j <= k; j++)
                pts.Add(a + (b - a) * (j / (double)k));
        }
        return pts;
    }

    /// <summary>Коридор фарватера — многоугольник: левая кромка + правая в обратном порядке.</summary>
    public static List<WorldPosition> Corridor(IList<WorldPosition> axis, double width)
    {
        List<WorldPosition> left = OffsetPolyline(axis, width * 0.5);
        List<WorldPosition> right = OffsetPolyline(axis, -width * 0.5);
        right.Reverse();
        left.AddRange(right);
        return left;
    }

    // Левая нормаль направления ab: (−dN, dE)
    private static void LeftNormal(WorldPosition a, WorldPosition b, out double le, out double ln)
    {
        double de = b.East - a.East, dn = b.North - a.North;
        double l = Math.Max(1e-9, Math.Sqrt(de * de + dn * dn));
        le = -dn / l;
        ln = de / l;
    }

    private static ShoalArea Shoal(string name, float depth, params double[] en)
    {
        var s = new ShoalArea { name = name, depth = depth };
        for (int i = 0; i + 1 < en.Length; i += 2) s.vertices.Add(new WorldPosition(en[i], en[i + 1]));
        return s;
    }

    private static LandArea Land(string name, float height, params double[] en)
    {
        var l = new LandArea { name = name, height = height };
        for (int i = 0; i + 1 < en.Length; i += 2) l.vertices.Add(new WorldPosition(en[i], en[i + 1]));
        return l;
    }
}
