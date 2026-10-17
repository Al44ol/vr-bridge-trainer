// ============================================================
// AidEditing.cs — Знаки МАМС в редакторе акватории (запрос 21.2б)
//
// Правка списка WaterArea.aids без сцены (окно редактора только вызывает):
//   • Palette — порядок типов в палитре; Prefix — начало имени («Л», «П», «N», «E», «S», «W»,
//     «ОД» — отдельной опасности, «Ос» — осевой); NextName — «Л-1», «Л-2»… первый свободный номер.
//   • Add / Move / Remove; SetType — у имени «по умолчанию» (Prefix-N) меняется и имя.
//   • HitTest — ближайший знак к пикселю окна карты (не дальше maxPx), последний поставленный — сверху.
//   • LightWarning — огонь против правил МАМС (регион A): цвет и ритм для типа знака.
//     Пустое поле огня — огонь по умолчанию для типа (IalaMarks), он всегда верный.
// Снимок для отмены пишет вызывающий (AreaDocument.Commit) — здесь только сама правка.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;

public static class AidEditing
{
    /// <summary>Типы в палитре — в порядке МАМС (латеральные, кардинальные, прочие).</summary>
    public static readonly AidType[] Palette =
    {
        AidType.LateralPort, AidType.LateralStarboard,
        AidType.CardinalNorth, AidType.CardinalEast, AidType.CardinalSouth, AidType.CardinalWest,
        AidType.IsolatedDanger, AidType.SafeWater
    };

    /// <summary>Начало имени нового знака.</summary>
    public static string Prefix(AidType t)
    {
        switch (t)
        {
            case AidType.LateralPort: return "Л";
            case AidType.LateralStarboard: return "П";
            case AidType.CardinalNorth: return "N";
            case AidType.CardinalEast: return "E";
            case AidType.CardinalSouth: return "S";
            case AidType.CardinalWest: return "W";
            case AidType.IsolatedDanger: return "ОД";
            default: return "Ос";
        }
    }

    /// <summary>«Л-1», «Л-2»… — первый номер, которого нет среди имён знаков.</summary>
    public static string NextName(WaterArea a, AidType t)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (a?.aids != null) foreach (AidToNavigation x in a.aids) if (x != null && x.name != null) used.Add(x.name.Trim());
        string p = Prefix(t);
        for (int n = 1; ; n++)
            if (!used.Contains($"{p}-{n}")) return $"{p}-{n}";
    }

    /// <summary>Имя дано по умолчанию для этого типа («Л-3»)?</summary>
    public static bool IsAutoName(string name, AidType t)
    {
        string p = Prefix(t) + "-";
        return name != null && name.StartsWith(p, StringComparison.Ordinal) &&
               int.TryParse(name.Substring(p.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0;
    }

    /// <summary>Новый знак в точке p; возвращает его номер в списке.</summary>
    public static int Add(WaterArea a, AidType t, WorldPosition p)
    {
        a.aids.Add(new AidToNavigation(NextName(a, t), t, p.East, p.North));
        return a.aids.Count - 1;
    }

    public static void Move(WaterArea a, int i, WorldPosition p)
    {
        if (Valid(a, i)) a.aids[i].position = p;
    }

    public static bool Remove(WaterArea a, int i)
    {
        if (!Valid(a, i)) return false;
        a.aids.RemoveAt(i);
        return true;
    }

    /// <summary>Сменить тип; имя по умолчанию («Л-1») становится именем нового типа («П-1»…).</summary>
    public static void SetType(WaterArea a, int i, AidType t)
    {
        if (!Valid(a, i) || a.aids[i].type == t) return;
        AidToNavigation x = a.aids[i];
        bool auto = IsAutoName(x.name, x.type);
        x.type = t;
        if (auto)
        {
            x.name = "";
            x.name = NextName(a, t);
        }
    }

    public static bool Valid(WaterArea a, int i) => a?.aids != null && i >= 0 && i < a.aids.Count && a.aids[i] != null;

    /// <summary>Знак под пикселем окна карты (не дальше maxPx); −1 — нет. При наложении — поставленный позже.</summary>
    public static int HitTest(WaterArea a, MapViewport v, double px, double py, double maxPx)
    {
        if (a?.aids == null || v == null) return -1;
        int best = -1;
        double bestD = maxPx;
        for (int i = a.aids.Count - 1; i >= 0; i--)
        {
            if (a.aids[i] == null) continue;
            v.ToPixel(a.aids[i].position, out double x, out double y);
            double d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            if (d < bestD - 1e-9) { bestD = d; best = i; }
        }
        return best;
    }

    // ─────────── Огонь по МАМС ───────────
    /// <summary>Замечание к огню знака (null — верно или огонь по умолчанию).</summary>
    public static string LightWarning(AidType t, string light)
    {
        string s = (light ?? "").Trim();
        if (s.Length == 0) return null;
        NavLightColor color = NavLightColor.White;
        string rhythm = "";
        foreach (string token in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "R") color = NavLightColor.Red;
            else if (token == "G") color = NavLightColor.Green;
            else if (token == "W") color = NavLightColor.White;
            else if (token == "Y") color = NavLightColor.Yellow;
            else if (token.EndsWith("s", StringComparison.Ordinal) &&
                     double.TryParse(token.Substring(0, token.Length - 1).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { }
            else if (rhythm.Length == 0) rhythm = token;
        }
        if (!KnownRhythm(rhythm)) return $"огонь «{s}» не распознан (ритм: Fl, LFl, Q, VQ, Iso, Oc, Mo(A), F; цвет: R, G, W, Y; период, например 4s)";

        string title = IalaMarks.Get(t).TitleRu.ToLowerInvariant();
        switch (t)
        {
            case AidType.LateralPort:
                return color == NavLightColor.Red ? null : $"у знака «{title}» огонь по МАМС красный (R), например «Fl R 4s»";
            case AidType.LateralStarboard:
                return color == NavLightColor.Green ? null : $"у знака «{title}» огонь по МАМС зелёный (G), например «Fl G 4s»";
            case AidType.CardinalNorth: return Cardinal(color, rhythm, "", title, "Q или VQ");
            case AidType.CardinalEast: return Cardinal(color, rhythm, "(3)", title, "Q(3) 10s или VQ(3) 5s");
            case AidType.CardinalSouth: return Cardinal(color, rhythm, "(6)+LFl", title, "Q(6)+LFl 15s или VQ(6)+LFl 10s");
            case AidType.CardinalWest: return Cardinal(color, rhythm, "(9)", title, "Q(9) 15s или VQ(9) 10s");
            case AidType.IsolatedDanger:
                return color == NavLightColor.White && rhythm == "Fl(2)" ? null : $"у знака «{title}» огонь по МАМС белый Fl(2), например «Fl(2) 5s»";
            default:
                return color == NavLightColor.White && (rhythm == "Iso" || rhythm == "Oc" || rhythm == "LFl" || rhythm == "Mo(A)")
                    ? null : $"у знака «{title}» огонь по МАМС белый Iso, Oc, LFl 10s или Mo(A)";
        }
    }

    private static string Cardinal(NavLightColor color, string rhythm, string group, string title, string example) =>
        color == NavLightColor.White && (rhythm == "Q" + group || rhythm == "VQ" + group)
            ? null : $"у знака «{title}» огонь по МАМС белый {example}";

    private static bool KnownRhythm(string rhythm)
    {
        if (rhythm.Length == 0) return false;
        foreach (string part in rhythm.Split('+'))
        {
            string kind = part;
            int br = part.IndexOf('(');
            if (br > 0)
            {
                if (!part.EndsWith(")", StringComparison.Ordinal)) return false;
                kind = part.Substring(0, br);
                string inner = part.Substring(br + 1, part.Length - br - 2);
                if (kind == "Mo") { if (inner.Length != 1 || !char.IsLetter(inner[0])) return false; continue; }
                if (!int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1) return false;
            }
            if (kind != "F" && kind != "Fl" && kind != "LFl" && kind != "Q" && kind != "VQ" && kind != "Iso" && kind != "Oc") return false;
        }
        return true;
    }
}
