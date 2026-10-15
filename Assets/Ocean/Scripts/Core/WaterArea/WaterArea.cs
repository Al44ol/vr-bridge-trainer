// ============================================================
// WaterArea.cs — Учебная акватория (B1)
//
// Синтетический район плавания в локальной системе координат тренажёра:
// восток/север в метрах от условного начала (как WorldOrigin.ShipPosition).
// Реальные карты (S-57, OpenSeaMap) не используются.
//
// Состав:
//   • знаки навигационного ограждения МАМС, регион A (латеральные, кардинальные,
//     отдельной опасности, осевой) — точки;
//   • мели — многоугольники с наименьшей глубиной (на воде не видны, видны
//     на навигационном планшете D2 и в отладочном режиме);
//   • суша — многоугольники с высотой берега (для посадки — глубина 0);
//   • фарватеры — ось (ломаная) и ширина: рекомендованный путь, по нему
//     строится маршрут сценария (B2);
//   • границы района и глубина по умолчанию (вне мелей).
//   • причалы (запрос 19.2б) — кромка с кранцами и кнехтами (BerthArea); для
//     детектора посадки это не суша: у кромки нет «НА МЕЛИ» и предупреждения 1 кбт,
//     контакт с причалом считает MooringSystem (только модель 3 СС).
//
// Редактирование — в Scene View мышью (WaterAreaViewEditor), в сцене
// акваторию показывает WaterAreaView. Сборка VRBridge.Core — без MonoBehaviour,
// используется детектором посадки (GroundingDetector) и тестами.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Тип знака навигационного ограждения МАМС (регион A).</summary>
public enum AidType
{
    /// <summary>Латеральный знак левой стороны (красный).</summary>
    LateralPort,
    /// <summary>Латеральный знак правой стороны (зелёный).</summary>
    LateralStarboard,
    /// <summary>Северный кардинальный: безопасная вода к северу от знака.</summary>
    CardinalNorth,
    /// <summary>Восточный кардинальный.</summary>
    CardinalEast,
    /// <summary>Южный кардинальный.</summary>
    CardinalSouth,
    /// <summary>Западный кардинальный.</summary>
    CardinalWest,
    /// <summary>Знак отдельной опасности.</summary>
    IsolatedDanger,
    /// <summary>Осевой знак (знак безопасной воды).</summary>
    SafeWater
}

/// <summary>Знак навигационного ограждения.</summary>
[Serializable]
public class AidToNavigation
{
    public string name = "Знак";
    public AidType type = AidType.LateralPort;
    public WorldPosition position;
    [Tooltip("Характеристика огня (задел на B4), например «Fl R 4s». Пусто — по умолчанию для типа")]
    public string lightCharacter = "";

    public AidToNavigation() { }

    public AidToNavigation(string name, AidType type, double east, double north)
    {
        this.name = name;
        this.type = type;
        position = new WorldPosition(east, north);
    }

    /// <summary>Огонь: заданный или по умолчанию для типа знака.</summary>
    public string Light => string.IsNullOrEmpty(lightCharacter) ? IalaMarks.Get(type).DefaultLight : lightCharacter;
}

/// <summary>Мель: многоугольник с наименьшей глубиной.</summary>
[Serializable]
public class ShoalArea
{
    public string name = "Мель";
    [Tooltip("Наименьшая глубина над мелью, м")]
    public float depth = 2f;
    public List<WorldPosition> vertices = new List<WorldPosition>();
}

/// <summary>Суша (берег, остров): многоугольник с высотой.</summary>
[Serializable]
public class LandArea
{
    public string name = "Берег";
    [Tooltip("Высота берега над водой, м")]
    public float height = 10f;
    public List<WorldPosition> vertices = new List<WorldPosition>();
}

/// <summary>Фарватер: ось (ломаная) и ширина.</summary>
[Serializable]
public class FairwayLine
{
    public string name = "Фарватер";
    [Tooltip("Ширина, м (2 кбт = 370,4 м)")]
    public float width = 370.4f;
    public List<WorldPosition> axis = new List<WorldPosition>();
}

[CreateAssetMenu(menuName = "VR Bridge/Water Area", fileName = "WaterArea")]
public class WaterArea : ScriptableObject
{
    public const double CableMeters = 185.2;   // 1 кабельтов

    public string areaName = "Учебная акватория";
    [TextArea(2, 6)] public string description = "";

    [Header("Район")]
    public WorldPosition boundsMin = new WorldPosition(-5000, -5000);
    public WorldPosition boundsMax = new WorldPosition(5000, 5000);
    [Tooltip("Глубина вне мелей, м")]
    public float defaultDepth = 30f;

    [Header("Объекты")]
    public List<AidToNavigation> aids = new List<AidToNavigation>();
    public List<ShoalArea> shoals = new List<ShoalArea>();
    public List<LandArea> land = new List<LandArea>();
    public List<FairwayLine> fairways = new List<FairwayLine>();
    [Tooltip("Причалы (19.2б): кромка, кранцы, кнехты — BerthArea. Для детектора посадки причал НЕ суша")]
    public List<BerthArea> berths = new List<BerthArea>();

    [Tooltip("Счётчик изменений (растёт при правке в редакторе; WaterAreaView пересобирает модели)")]
    public int revision;

    /// <summary>Центр района.</summary>
    public WorldPosition Center => new WorldPosition(0.5 * (boundsMin.East + boundsMax.East), 0.5 * (boundsMin.North + boundsMax.North));

    /// <summary>Отметить изменение (вызывает редактор после правки).</summary>
    public void MarkChanged() => revision++;

    /// <summary>
    /// Глубина по карте в точке, м: наименьшая из мелей, содержащих точку;
    /// на суше — 0; иначе — глубина по умолчанию.
    /// </summary>
    public float DepthAt(WorldPosition p)
    {
        foreach (LandArea l in land)
            if (l != null && Polygon2D.Contains(l.vertices, p))
                return 0f;
        float depth = defaultDepth;
        foreach (ShoalArea s in shoals)
            if (s != null && s.depth < depth && Polygon2D.Contains(s.vertices, p))
                depth = s.depth;
        return depth;
    }

    /// <summary>Ближайший к точке причал (19.2б): номер в berths, −1 — причалов нет; distance — до кромки, м.</summary>
    public int NearestBerth(WorldPosition p, out double distance)
    {
        int best = -1;
        distance = double.PositiveInfinity;
        if (berths == null) return best;
        for (int i = 0; i < berths.Count; i++)
        {
            if (berths[i] == null || berths[i].Length < 1.0) continue;
            double d = berths[i].DistanceTo(p);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }

    /// <summary>Внутри границ района.</summary>
    public bool InBounds(WorldPosition p) =>
        p.East >= boundsMin.East && p.East <= boundsMax.East && p.North >= boundsMin.North && p.North <= boundsMax.North;
}
