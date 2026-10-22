// ============================================================
// TriggerEditing.cs — Правка триггеров «если — то» в редакторе сценариев (запрос 21.6б, Core)
//
// Лист «Триггеры» (ScenarioEditorTriggers.cs) работает через эти функции:
//   • Add / Duplicate / Remove — триггер «Тр N» (по умолчанию: время 01:00 → сообщение), копия с новым id;
//   • SetCondition — смена условия с разумными значениями (круг — в середине маршрута, R 300 м; точка — первая
//     после старта; цель — первая; другой триггер — первый другой); AddAction / RemoveAction / SetActionKind;
//   • выбор по кругу из списка (Choices, Cycle): точки маршрута (без старта), цели, этапы задания, другие триггеры;
//   • карта: попадание в центр круга (≤ 9 пикс.), в ручку на краю (≤ 8 пикс., к востоку от центра), в окружность
//     (≤ 6 пикс.); радиус от ручки — шагом 10 м;
//   • ссылки: переименовали точку маршрута / сменили id цели — триггеры следуют (RenameWaypoint, RemapTargets;
//     удалённая цель — «T2 (удалено)», ошибка «Проверки»);
//   • Check — предупреждения: круг не пересекает маршрут, старт уже в круге, цели «по маршруту» задан курс;
//   • ParseClock — время «мм:сс» (или число минут).
// Ошибки и прочие предупреждения — TriggerText.Validate (из ScenarioDefinition.Validate); какие из них блокируют —
// ScenarioChecks.IsError.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;

public static class TriggerEditing
{
    public const double CenterHitPx = 9.0, EdgeHitPx = 8.0, RingHitPx = 6.0;
    public const double RadiusStep = 10.0, DefaultRadiusM = 300.0, MinRadiusM = 10.0;
    public const string DefaultMessage = "Сообщение обучающемуся";

    public static bool Valid(List<ScenarioTrigger> list, int i) => list != null && i >= 0 && i < list.Count && list[i] != null;

    /// <summary>Новый триггер в конец: «время 01:00 → сообщение»; номер.</summary>
    public static int Add(ScenarioDefinition s)
    {
        if (s.triggers == null) s.triggers = new List<ScenarioTrigger>();
        var t = new ScenarioTrigger { id = TriggerText.NextId(s.triggers), condition = TriggerCondition.Time, timeSeconds = 60f };
        t.actions.Add(new TriggerAction { kind = TriggerActionKind.Message, text = DefaultMessage });
        s.triggers.Add(t);
        return s.triggers.Count - 1;
    }

    /// <summary>Копия триггера с новым id — сразу после исходного; номер копии (−1 — нет исходного).</summary>
    public static int Duplicate(ScenarioDefinition s, int i)
    {
        if (!Valid(s.triggers, i)) return -1;
        ScenarioTrigger c = s.triggers[i].Clone();
        c.id = TriggerText.NextId(s.triggers);
        c.title = string.IsNullOrWhiteSpace(c.title) ? "" : c.title + " (копия)";
        s.triggers.Insert(i + 1, c);
        return i + 1;
    }

    public static bool Remove(ScenarioDefinition s, int i)
    {
        if (!Valid(s.triggers, i)) return false;
        s.triggers.RemoveAt(i);
        return true;
    }

    // ─────────── Условие ───────────
    /// <summary>Сменить условие; параметры нового — по умолчанию, если не заданы.</summary>
    public static void SetCondition(ScenarioDefinition s, ScenarioTrigger t, TriggerCondition c)
    {
        t.condition = c;
        switch (c)
        {
            case TriggerCondition.EnterCircle:
                if (t.radiusM <= 0f) t.radiusM = (float)DefaultRadiusM;
                if (t.center.East == 0.0 && t.center.North == 0.0) t.center = DefaultCenter(s);
                break;
            case TriggerCondition.Waypoint:
                List<string> wps = WaypointChoices(s);
                if (!wps.Contains(t.waypoint ?? "")) t.waypoint = wps.Count > 0 ? wps[0] : "";
                break;
            case TriggerCondition.TargetRange:
                List<string> ts = TargetChoices(s);
                if (!ts.Contains(t.target ?? "")) t.target = ts.Count > 0 ? ts[0] : "";
                if (t.rangeNm <= 0f) t.rangeNm = 1f;
                break;
            case TriggerCondition.TaskStage:
                if (t.stage < 1 || t.stage > 4) t.stage = 1;
                break;
            case TriggerCondition.AfterTrigger:
                List<string> trs = TriggerChoices(s, t);
                if (!trs.Contains(t.afterTrigger ?? "")) t.afterTrigger = trs.Count > 0 ? trs[0] : "";
                break;
        }
    }

    /// <summary>Центр нового круга: середина маршрута по длине (без маршрута — старт).</summary>
    public static WorldPosition DefaultCenter(ScenarioDefinition s)
    {
        List<RouteWaypoint> r = s.route;
        if (r == null || r.Count < 2) return s.initial != null ? s.initial.position : default;
        double half = 0.5 * s.RouteLength, acc = 0.0;
        for (int i = 0; i + 1 < r.Count; i++)
        {
            WorldPosition a = r[i].position, b = r[i + 1].position;
            double len = (b - a).Magnitude;
            if (acc + len >= half && len > 0.0)
            {
                double k = (half - acc) / len;
                return new WorldPosition(Math.Round(a.East + k * (b.East - a.East)), Math.Round(a.North + k * (b.North - a.North)));
            }
            acc += len;
        }
        return r[r.Count - 1].position;
    }

    // ─────────── Действия ───────────
    /// <summary>Новое действие в конец; номер.</summary>
    public static int AddAction(ScenarioDefinition s, ScenarioTrigger t, TriggerActionKind kind = TriggerActionKind.Message)
    {
        if (t.actions == null) t.actions = new List<TriggerAction>();
        var a = new TriggerAction();
        SetActionKind(s, a, kind);
        t.actions.Add(a);
        return t.actions.Count - 1;
    }

    public static bool RemoveAction(ScenarioTrigger t, int i)
    {
        if (t.actions == null || i < 0 || i >= t.actions.Count) return false;
        t.actions.RemoveAt(i);
        return true;
    }

    /// <summary>Сменить вид действия; поля нового — по умолчанию, если не заданы.</summary>
    public static void SetActionKind(ScenarioDefinition s, TriggerAction a, TriggerActionKind k)
    {
        a.kind = k;
        switch (k)
        {
            case TriggerActionKind.TargetOrder:
                List<string> ts = TargetChoices(s);
                if (!ts.Contains(a.target ?? "")) a.target = ts.Count > 0 ? ts[0] : "";
                if (!a.ChangesCourse && !a.ChangesSpeed) a.speedKnots = 8f;
                break;
            case TriggerActionKind.Visibility:
                if (a.visibilityNm <= 0f) a.visibilityNm = 1f;
                break;
            case TriggerActionKind.Wind:
                if (a.windKnots <= 0f) a.windKnots = 15f;
                break;
            case TriggerActionKind.Message:
                if (string.IsNullOrWhiteSpace(a.text)) a.text = DefaultMessage;
                break;
        }
    }

    // ─────────── Выбор из списка ───────────
    /// <summary>Точки маршрута, которые можно «пройти» (без первой — старта).</summary>
    public static List<string> WaypointChoices(ScenarioDefinition s)
    {
        var list = new List<string>();
        if (s.route != null) for (int i = 1; i < s.route.Count; i++) if (s.route[i] != null) list.Add(s.route[i].name);
        return list;
    }

    public static List<string> TargetChoices(ScenarioDefinition s)
    {
        var list = new List<string>();
        if (s.traffic != null) for (int i = 0; i < s.traffic.Count; i++) if (s.traffic[i] != null) list.Add(s.traffic[i].TargetId(i));
        return list;
    }

    /// <summary>Другие триггеры (для «сработал другой триггер»).</summary>
    public static List<string> TriggerChoices(ScenarioDefinition s, ScenarioTrigger self)
    {
        var list = new List<string>();
        if (s.triggers != null) foreach (ScenarioTrigger t in s.triggers) if (t != null && t != self && !string.IsNullOrWhiteSpace(t.id)) list.Add(t.id);
        return list;
    }

    /// <summary>Следующее значение по кругу (нет текущего — первое); пустой список — текущее.</summary>
    public static string Cycle(List<string> choices, string current, int dir)
    {
        if (choices == null || choices.Count == 0) return current ?? "";
        int k = choices.IndexOf(current ?? "");
        if (k < 0) return choices[0];
        return choices[((k + dir) % choices.Count + choices.Count) % choices.Count];
    }

    /// <summary>Этап задания сценария по номеру: (номер, название) — швартовка или работа буксира; без задания — пусто.</summary>
    public static List<(int stage, string title)> StageChoices(ScenarioDefinition s)
    {
        var list = new List<(int, string)>();
        if (!s.IsMooring && !s.IsTugWork) return list;
        for (int i = 1; i <= 4; i++) list.Add((i, TriggerText.StageTitle(s, i)));
        return list;
    }

    // ─────────── Карта ───────────
    public static WorldPosition EdgeHandle(ScenarioTrigger t) => new WorldPosition(t.center.East + t.radiusM, t.center.North);

    private static bool IsCircle(ScenarioTrigger t) => t != null && t.condition == TriggerCondition.EnterCircle && t.radiusM > 0f;

    private static double PixDist(MapViewport v, WorldPosition p, double px, double py)
    {
        v.ToPixel(p, out double x, out double y);
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    /// <summary>Триггер-круг, в центр которого попали (позже в списке — сверху); −1 — нет.</summary>
    public static int HitCenter(ScenarioDefinition s, MapViewport v, double px, double py)
    {
        if (s.triggers == null) return -1;
        for (int i = s.triggers.Count - 1; i >= 0; i--)
            if (IsCircle(s.triggers[i]) && PixDist(v, s.triggers[i].center, px, py) <= CenterHitPx) return i;
        return -1;
    }

    /// <summary>Попали в ручку радиуса (к востоку от центра) триггера i.</summary>
    public static bool HitEdge(ScenarioDefinition s, int i, MapViewport v, double px, double py) =>
        Valid(s.triggers, i) && IsCircle(s.triggers[i]) && PixDist(v, EdgeHandle(s.triggers[i]), px, py) <= EdgeHitPx;

    /// <summary>Триггер-круг, по окружности которого щёлкнули (≤ 6 пикс.); −1 — нет.</summary>
    public static int HitRing(ScenarioDefinition s, MapViewport v, double px, double py)
    {
        if (s.triggers == null) return -1;
        for (int i = s.triggers.Count - 1; i >= 0; i--)
        {
            ScenarioTrigger t = s.triggers[i];
            if (!IsCircle(t)) continue;
            double r = t.radiusM / Math.Max(1e-6, v.MetersPerPixel);
            if (Math.Abs(PixDist(v, t.center, px, py) - r) <= RingHitPx) return i;
        }
        return -1;
    }

    /// <summary>Радиус от ручки: расстояние от центра, шагом step (не меньше 10 м).</summary>
    public static float RadiusFrom(ScenarioTrigger t, WorldPosition p, double step)
    {
        double d = (p - t.center).Magnitude;
        if (step > 0.0) d = Math.Round(d / step) * step;
        return (float)Math.Max(MinRadiusM, d);
    }

    // ─────────── Ссылки ───────────
    /// <summary>Точку маршрута переименовали: условия триггеров следуют за ней; сколько поправлено.</summary>
    public static int RenameWaypoint(ScenarioDefinition s, string oldName, string newName)
    {
        if (s.triggers == null || string.IsNullOrEmpty(oldName) || oldName == newName) return 0;
        int n = 0;
        foreach (ScenarioTrigger t in s.triggers)
            if (t != null && t.condition == TriggerCondition.Waypoint && t.waypoint == oldName) { t.waypoint = newName; n++; }
        return n;
    }

    /// <summary>
    /// Id целей до правки (oldIds) и после (newIds; null — цель удалена, по номеру старого списка): ссылки триггеров
    /// следуют за целью; на удалённую — «T2 (удалено)» (ошибка «Проверки»). Сколько поправлено.
    /// </summary>
    public static int RemapTargets(ScenarioDefinition s, List<string> oldIds, List<string> newIds)
    {
        if (s.triggers == null || oldIds == null || newIds == null) return 0;
        int n = 0;
        string Map(string r)
        {
            int k = oldIds.IndexOf(r ?? "");
            if (k < 0 || k >= newIds.Count) return r;
            string to = newIds[k] ?? r + " (удалено)";
            if (to != r) n++;
            return to;
        }
        foreach (ScenarioTrigger t in s.triggers)
        {
            if (t == null) continue;
            if (t.condition == TriggerCondition.TargetRange) t.target = Map(t.target);
            if (t.actions != null) foreach (TriggerAction a in t.actions) if (a != null && a.kind == TriggerActionKind.TargetOrder) a.target = Map(a.target);
        }
        return n;
    }

    /// <summary>Id целей по номерам (для RemapTargets).</summary>
    public static List<string> TargetIds(ScenarioDefinition s)
    {
        var list = new List<string>();
        if (s.traffic != null) for (int i = 0; i < s.traffic.Count; i++) list.Add(s.traffic[i] != null ? s.traffic[i].TargetId(i) : null);
        return list;
    }

    // ─────────── Предупреждения редактора ───────────
    public static List<string> Check(ScenarioDefinition s)
    {
        var w = new List<string>();
        if (s.triggers == null) return w;
        foreach (ScenarioTrigger t in s.triggers)
        {
            if (t == null) continue;
            string tag = $"Триггер {t.id}";
            if (IsCircle(t))
            {
                if (s.initial != null && (s.initial.position - t.center).Magnitude <= t.radiusM)
                    w.Add($"{tag}: старт уже в круге — сработает сразу после «Начать».");
                else if (s.route != null && s.route.Count >= 2 && RouteDistance(s.route, t.center) > t.radiusM)
                    w.Add($"{tag}: круг не пересекает маршрут — сработает, только если судно уйдёт с маршрута.");
            }
            if (t.actions == null) continue;
            foreach (TriggerAction a in t.actions)
            {
                if (a == null || a.kind != TriggerActionKind.TargetOrder || !a.ChangesCourse) continue;
                TrafficShipDefinition d = FindTarget(s, a.target);
                if (d != null && d.motion == TrafficMotion.Route && d.route != null && d.route.Count > 0)
                    w.Add($"{tag}: цель {a.target} идёт по маршруту — заданный курс снимет маршрут (дальше постоянным курсом).");
            }
        }
        return w;
    }

    private static TrafficShipDefinition FindTarget(ScenarioDefinition s, string id)
    {
        if (s.traffic == null) return null;
        for (int i = 0; i < s.traffic.Count; i++) if (s.traffic[i] != null && s.traffic[i].TargetId(i) == id) return s.traffic[i];
        return null;
    }

    /// <summary>Расстояние от точки до ломаной маршрута, м.</summary>
    public static double RouteDistance(List<RouteWaypoint> r, WorldPosition p)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i + 1 < r.Count; i++)
        {
            if (r[i] == null || r[i + 1] == null) continue;
            WorldPosition a = r[i].position, b = r[i + 1].position, ab = b - a;
            double len2 = ab.East * ab.East + ab.North * ab.North;
            double k = len2 > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((p.East - a.East) * ab.East + (p.North - a.North) * ab.North) / len2)) : 0.0;
            var q = new WorldPosition(a.East + k * ab.East, a.North + k * ab.North);
            best = Math.Min(best, (p - q).Magnitude);
        }
        return best;
    }

    // ─────────── Время ───────────
    /// <summary>«мм:сс» или «ч:мм:сс»; просто число — минуты («5» → 05:00, «1,5» → 01:30).</summary>
    public static bool ParseClock(string text, out double seconds)
    {
        seconds = 0.0;
        string t = (text ?? "").Trim().Replace(',', '.');
        if (t.Length == 0) return false;
        if (t.IndexOf(':') < 0)
        {
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double min) || min < 0.0) return false;
            seconds = Math.Round(min * 60.0);
            return true;
        }
        string[] parts = t.Split(':');
        if (parts.Length > 3) return false;
        double total = 0.0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0.0) return false;
            if (i > 0 && v >= 60.0) return false;
            total = total * 60.0 + v;
        }
        seconds = Math.Round(total);
        return true;
    }
}
