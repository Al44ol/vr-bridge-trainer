// ============================================================
// AreaChecks.cs — Проверка акватории в редакторе (запрос 21.2а)
//
// Ошибки — сохранить можно, но сценарий на такой акватории работать не будет как задумано:
// пустое имя, имя встроенной, границы наоборот, глубина ≤ 0. Предупреждения — знаки, мели и
// причалы вне района, многоугольник меньше 3 вершин, фарватер меньше 2 точек, повтор имён знаков;
// 21.2б — огонь знака не по МАМС (AidEditing.LightWarning); 21.3а — у мелей и суши стороны
// пересекаются, фигура целиком вне района, мель не мельче глубины по умолчанию; 21.3б — фарватер
// вне района, у причала с водной стороны суша.
// ============================================================
using System.Collections.Generic;

public sealed class AreaCheckResult
{
    public readonly List<string> Errors = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public bool Ok => Errors.Count == 0;
}

public static class AreaChecks
{
    /// <summary>Наибольший размер района по каждой оси, м (больше — опечатка: лишний ноль).</summary>
    public const double MaxSizeM = 200000.0;

    public static AreaCheckResult Check(WaterArea a, ScenarioCatalog catalog = null)
    {
        var r = new AreaCheckResult();
        if (a == null) { r.Errors.Add("Акватории нет."); return r; }
        if (string.IsNullOrWhiteSpace(a.name)) r.Errors.Add("Не задано имя акватории.");
        else if (catalog != null && catalog.Find(typeof(WaterArea), a.name) != null)
            r.Errors.Add($"Имя «{a.name}» занято встроенной акваторией — выберите другое.");
        double w = a.boundsMax.East - a.boundsMin.East, h = a.boundsMax.North - a.boundsMin.North;
        if (w <= 0.0 || h <= 0.0) r.Errors.Add("Границы района: восток/север «до» должны быть больше, чем «от».");
        else if (w > MaxSizeM || h > MaxSizeM) r.Warnings.Add($"Район больше {MaxSizeM / 1000.0:0} км по стороне — проверьте границы (лишний ноль?).");
        if (a.defaultDepth <= 0f) r.Errors.Add("Глубина по умолчанию должна быть больше 0 м.");

        var names = new HashSet<string>();
        if (a.aids != null)
            for (int i = 0; i < a.aids.Count; i++)
            {
                AidToNavigation x = a.aids[i];
                if (x == null) continue;
                string tag = $"Знак «{x.name}»";
                if (!a.InBounds(x.position)) r.Warnings.Add($"{tag} вне района.");
                if (!string.IsNullOrWhiteSpace(x.name) && !names.Add(x.name)) r.Warnings.Add($"{tag}: имя повторяется.");
                string light = AidEditing.LightWarning(x.type, x.lightCharacter);   // 21.2б
                if (light != null) r.Warnings.Add($"{tag}: {light}.");
            }
        if (a.shoals != null)
            foreach (ShoalArea s in a.shoals)
            {
                if (s == null) continue;
                if (s.vertices == null || s.vertices.Count < 3) r.Warnings.Add($"Мель «{s.name}»: меньше 3 вершин — не учитывается.");
                if (s.depth < 0f) r.Warnings.Add($"Мель «{s.name}»: глубина отрицательная.");
                else if (s.depth >= a.defaultDepth) r.Warnings.Add($"Мель «{s.name}»: {s.depth:0.#} м — не мельче глубины по умолчанию, на глубину не влияет.");
                ShapeWarnings(r, a, $"Мель «{s.name}»", s.vertices);   // 21.3а
            }
        if (a.land != null)
            foreach (LandArea l in a.land)
            {
                if (l == null) continue;
                if (l.vertices == null || l.vertices.Count < 3) r.Warnings.Add($"Суша «{l.name}»: меньше 3 вершин — не учитывается.");
                ShapeWarnings(r, a, $"Суша «{l.name}»", l.vertices);
            }
        if (a.fairways != null)
            foreach (FairwayLine f in a.fairways)
            {
                if (f == null) continue;
                if (f.axis == null || f.axis.Count < 2) r.Warnings.Add($"Фарватер «{f.name}»: меньше 2 точек оси.");
                if (f.width <= 0f) r.Warnings.Add($"Фарватер «{f.name}»: ширина должна быть больше 0.");
                if (PolygonEditing.OutsideBounds(a, f.axis)) r.Warnings.Add($"Фарватер «{f.name}» вне района.");   // 21.3б
            }
        if (a.berths != null)
            for (int i = 0; i < a.berths.Count; i++)
            {
                BerthArea b = a.berths[i];
                if (b == null) continue;
                if (!a.InBounds(b.a) || !a.InBounds(b.b)) r.Warnings.Add($"Причал № {i} «{b.name}» вне района.");
                if (b.Length < 20.0) r.Warnings.Add($"Причал № {i} «{b.name}»: короче 20 м.");
                else if (PolygonEditing.WaterSideOnLand(a, b))   // 21.3б
                    r.Warnings.Add($"Причал № {i} «{b.name}»: с водной стороны суша — кромка перевёрнута? («Вода с другой стороны»).");
            }
        return r;
    }

    // 21.3а: стороны пересекаются; фигура целиком вне района
    private static void ShapeWarnings(AreaCheckResult r, WaterArea a, string tag, List<WorldPosition> v)
    {
        if (v == null || v.Count < 3) return;
        if (PolygonEditing.SelfIntersects(v)) r.Warnings.Add($"{tag}: стороны пересекаются — закрашивается не вся фигура.");
        if (PolygonEditing.OutsideBounds(a, v)) r.Warnings.Add($"{tag} вне района.");
    }
}
