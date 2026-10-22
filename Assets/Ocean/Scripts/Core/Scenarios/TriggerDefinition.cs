// ============================================================
// TriggerDefinition.cs — Триггеры сценария «если — то» (запрос 21.6а)
//
// Триггер — одно условие, задержка и список действий; срабатывает один раз за упражнение.
// Условия (TriggerCondition):
//   Time          — время упражнения от «Начать» ≥ timeSeconds;
//   EnterCircle   — своё судно в круге (center, radiusM) — вошло или уже в нём;
//   Waypoint      — пройдена точка маршрута с именем waypoint (у первой точки «прохождения» нет);
//   TargetRange   — дистанция до цели target меньше rangeNm;
//   TaskStage     — выполнен этап задания stage (швартовка — MooringStage, работа буксира — TugWorkStage);
//   AfterTrigger  — сработал триггер afterTrigger (цепочки; задержка — от его срабатывания).
// Действия (TriggerActionKind):
//   TargetOrder   — цель target: курс courseDeg и/или скорость speedKnots (< 0 — не менять; заданный курс
//                   снимает маршрут цели — дальше постоянным курсом; скорость 0 — «стоп», > 0 у стоящей — «начать движение»);
//   Weather       — пресет погоды; Visibility — видимость visibilityNm (0 — по погоде); Wind — windKnots / windFromDeg;
//   EngineFailure / EngineRestore / SteeringFailure / SteeringRestore — отказ ГД или руля и устранение;
//   Message       — сообщение text в шлеме (табличка и тон) и в «Событиях».
// Действия выполняет сцена (ScenarioTriggers) теми же путями, что вводные инструктора, — запись и оценка
// видят их последствия (видимость, отказы, ветер, движение целей) без изменений. Исполнитель — TriggerRunner.
// Поле ScenarioDefinition.triggers попадает в файл сценария само (FieldJson); пустой список — триггеров нет.
// Идентификатор по умолчанию — «Тр1», «Тр2» … (не путать с целями T1, T2).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>21.6а: условие триггера.</summary>
public enum TriggerCondition
{
    [InspectorName("Время от начала")] Time,
    [InspectorName("Своё судно в круге")] EnterCircle,
    [InspectorName("Пройдена точка маршрута")] Waypoint,
    [InspectorName("Дистанция до цели меньше")] TargetRange,
    [InspectorName("Выполнен этап задания")] TaskStage,
    [InspectorName("Сработал другой триггер")] AfterTrigger
}

/// <summary>21.6а: действие триггера.</summary>
public enum TriggerActionKind
{
    [InspectorName("Цель: курс и скорость")] TargetOrder,
    [InspectorName("Погода")] Weather,
    [InspectorName("Видимость")] Visibility,
    [InspectorName("Ветер")] Wind,
    [InspectorName("Отказ главного двигателя")] EngineFailure,
    [InspectorName("ГД восстановлен")] EngineRestore,
    [InspectorName("Отказ рулевого устройства")] SteeringFailure,
    [InspectorName("Руль восстановлен")] SteeringRestore,
    [InspectorName("Сообщение")] Message
}

/// <summary>21.6а: действие триггера (поля — по виду действия).</summary>
[Serializable]
public class TriggerAction
{
    public TriggerActionKind kind = TriggerActionKind.Message;
    [Tooltip("Цель (id, например T1) — для «Цель: курс и скорость»")]
    public string target = "";
    [Tooltip("Курс цели, °; < 0 — не менять (заданный курс снимает маршрут цели)")]
    public float courseDeg = -1f;
    [Tooltip("Скорость цели, уз; < 0 — не менять; 0 — стоп")]
    public float speedKnots = -1f;
    public WeatherPresetId weather = WeatherPresetId.Moderate;
    [Tooltip("Видимость, мили; 0 — по погоде")]
    public float visibilityNm;
    [Tooltip("Ветер, уз (0 — нет) и «откуда», °")]
    public float windKnots;
    public float windFromDeg;
    [Tooltip("Текст сообщения обучающемуся")]
    [TextArea(2, 5)] public string text = "";

    public bool ChangesCourse => courseDeg >= 0f;
    public bool ChangesSpeed => speedKnots >= 0f;

    /// <summary>21.6б: копия (редактор).</summary>
    public TriggerAction Clone() => (TriggerAction)MemberwiseClone();
}

/// <summary>21.6а: триггер сценария «если — то».</summary>
[Serializable]
public class ScenarioTrigger
{
    [Tooltip("Идентификатор (Тр1, Тр2 …) — для цепочек и журнала")]
    public string id = "Тр1";
    [Tooltip("Короткое название для инструктора (необязательно)")]
    public string title = "";
    public TriggerCondition condition = TriggerCondition.Time;
    [Tooltip("«Время от начала»: с")]
    public float timeSeconds = 60f;
    [Tooltip("«Своё судно в круге»: центр (восток, север), м и радиус, м")]
    public WorldPosition center;
    public float radiusM = 300f;
    [Tooltip("«Пройдена точка маршрута»: имя точки")]
    public string waypoint = "";
    [Tooltip("«Дистанция до цели»: id цели и дистанция, мили")]
    public string target = "";
    public float rangeNm = 1f;
    [Tooltip("«Выполнен этап задания»: номер этапа (швартовка 1–4, работа буксира 1–4)")]
    public int stage = 1;
    [Tooltip("«Сработал другой триггер»: его id")]
    public string afterTrigger = "";
    [Tooltip("Задержка после выполнения условия, с")]
    public float delaySeconds;
    public List<TriggerAction> actions = new List<TriggerAction>();

    /// <summary>Подпись: id и название.</summary>
    public string Label => string.IsNullOrWhiteSpace(title) ? id : $"{id} «{title}»";

    /// <summary>21.6б: копия с копиями действий (редактор).</summary>
    public ScenarioTrigger Clone()
    {
        var c = (ScenarioTrigger)MemberwiseClone();
        c.actions = new List<TriggerAction>();
        if (actions != null) foreach (TriggerAction a in actions) if (a != null) c.actions.Add(a.Clone());
        return c;
    }
}

/// <summary>21.6а: тексты триггеров (окно инструктора, журнал, проверка).</summary>
public static class TriggerText
{
    public const string IdPrefix = "Тр";

    /// <summary>Следующий свободный id: «Тр1», «Тр2» …</summary>
    public static string NextId(IList<ScenarioTrigger> list)
    {
        for (int n = 1; ; n++)
        {
            string id = IdPrefix + n;
            bool used = false;
            if (list != null) foreach (ScenarioTrigger t in list) if (t != null && t.id == id) { used = true; break; }
            if (!used) return id;
        }
    }

    /// <summary>21.6б: название условия (выбор в редакторе).</summary>
    public static string ConditionTitle(TriggerCondition c)
    {
        switch (c)
        {
            case TriggerCondition.Time: return "Время от начала";
            case TriggerCondition.EnterCircle: return "Своё судно в круге";
            case TriggerCondition.Waypoint: return "Пройдена точка маршрута";
            case TriggerCondition.TargetRange: return "Дистанция до цели меньше";
            case TriggerCondition.TaskStage: return "Выполнен этап задания";
            case TriggerCondition.AfterTrigger: return "Сработал другой триггер";
            default: return c.ToString();
        }
    }

    /// <summary>21.6б: название вида действия (выбор в редакторе).</summary>
    public static string ActionTitle(TriggerActionKind k)
    {
        switch (k)
        {
            case TriggerActionKind.TargetOrder: return "Цель: курс и скорость";
            case TriggerActionKind.Weather: return "Погода";
            case TriggerActionKind.Visibility: return "Видимость";
            case TriggerActionKind.Wind: return "Ветер";
            case TriggerActionKind.EngineFailure: return "Отказ главного двигателя";
            case TriggerActionKind.EngineRestore: return "Главный двигатель восстановлен";
            case TriggerActionKind.SteeringFailure: return "Отказ рулевого устройства";
            case TriggerActionKind.SteeringRestore: return "Рулевое устройство восстановлено";
            case TriggerActionKind.Message: return "Сообщение";
            default: return k.ToString();
        }
    }

    /// <summary>Условие словами: «через 05:00 от начала», «в круге 300 м у E 0, N 2900» …</summary>
    public static string Condition(ScenarioTrigger t, ScenarioDefinition s = null)
    {
        string c;
        switch (t.condition)
        {
            case TriggerCondition.Time: c = $"время {ScenarioText.Clock(t.timeSeconds)}"; break;
            case TriggerCondition.EnterCircle: c = $"в круге {ScenarioText.Num(t.radiusM)} м (E {ScenarioText.Num(t.center.East)}, N {ScenarioText.Num(t.center.North)})"; break;
            case TriggerCondition.Waypoint: c = $"пройдена «{t.waypoint}»"; break;
            case TriggerCondition.TargetRange: c = $"до {t.target} < {ScenarioText.Num(t.rangeNm, 2)} мили"; break;
            case TriggerCondition.TaskStage: c = $"выполнено «{StageTitle(s, t.stage)}»"; break;
            case TriggerCondition.AfterTrigger: c = $"после {t.afterTrigger}"; break;
            default: c = t.condition.ToString(); break;
        }
        return t.delaySeconds > 0f ? $"{c} + {ScenarioText.Num(t.delaySeconds)} с" : c;
    }

    /// <summary>Этап задания сценария по номеру (швартовка или работа буксира).</summary>
    public static string StageTitle(ScenarioDefinition s, int stage)
    {
        if (s != null && s.IsTugWork) return TugWorkText.Stage((TugWorkStage)stage);
        if (s != null && s.IsMooring) return MooringTasks.Title((MooringStage)stage);
        return $"этап {stage}";
    }

    /// <summary>Действие словами: «T1: курс 270°, 12 уз», «видимость 1 миля», «сообщение «…»».</summary>
    public static string Action(TriggerAction a)
    {
        if (a == null) return "—";
        switch (a.kind)
        {
            case TriggerActionKind.TargetOrder:
                var parts = new List<string>();
                if (a.ChangesCourse) parts.Add($"курс {ScenarioText.Bearing(a.courseDeg)}");
                if (a.ChangesSpeed) parts.Add(a.speedKnots > 0f ? $"{ScenarioText.Num(a.speedKnots, 1)} уз" : "стоп");
                return $"{a.target}: {(parts.Count > 0 ? string.Join(", ", parts) : "без изменений")}";
            case TriggerActionKind.Weather: return $"погода «{ScenarioText.WeatherTitle(a.weather)}»";
            case TriggerActionKind.Visibility: return a.visibilityNm > 0f ? $"видимость {ScenarioText.Num(a.visibilityNm, 2)} мили" : "видимость по погоде";
            case TriggerActionKind.Wind: return $"ветер {ScenarioText.Wind(a.windKnots, a.windFromDeg)}";
            case TriggerActionKind.EngineFailure: return "отказ ГД";
            case TriggerActionKind.EngineRestore: return "ГД восстановлен";
            case TriggerActionKind.SteeringFailure: return "отказ руля";
            case TriggerActionKind.SteeringRestore: return "руль восстановлен";
            case TriggerActionKind.Message: return $"сообщение «{Short(a.text, 40)}»";
            default: return a.kind.ToString();
        }
    }

    /// <summary>Строка триггера: «Тр2 · в круге 300 м … + 60 с → T1: курс 270°; сообщение «…»».</summary>
    public static string Line(ScenarioTrigger t, ScenarioDefinition s = null)
    {
        var acts = new List<string>();
        if (t.actions != null) foreach (TriggerAction a in t.actions) acts.Add(Action(a));
        return $"{t.Label} · {Condition(t, s)} → {(acts.Count > 0 ? string.Join("; ", acts) : "нет действий")}";
    }

    public static string Short(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string one = text.Replace('\n', ' ').Trim();
        return one.Length <= max ? one : one.Substring(0, max - 1) + "…";
    }

    /// <summary>Проблемы триггеров сценария (для ScenarioDefinition.Validate).</summary>
    public static void Validate(ScenarioDefinition s, List<string> problems)
    {
        if (s.triggers == null || s.triggers.Count == 0) return;
        var ids = new HashSet<string>();
        var targets = new HashSet<string>();
        if (s.traffic != null)
            for (int i = 0; i < s.traffic.Count; i++) if (s.traffic[i] != null) targets.Add(s.traffic[i].TargetId(i));
        var points = new HashSet<string>();
        if (s.route != null)
            for (int i = 1; i < s.route.Count; i++) if (s.route[i] != null) points.Add(s.route[i].name);
        foreach (ScenarioTrigger t in s.triggers) if (t != null && !string.IsNullOrWhiteSpace(t.id)) ids.Add(t.id);

        var seen = new HashSet<string>();
        for (int i = 0; i < s.triggers.Count; i++)
        {
            ScenarioTrigger t = s.triggers[i];
            if (t == null) { problems.Add($"Триггер {i + 1}: пустая запись."); continue; }
            string tag = $"Триггер {(string.IsNullOrWhiteSpace(t.id) ? (i + 1).ToString() : t.id)}";
            if (string.IsNullOrWhiteSpace(t.id)) problems.Add($"{tag}: не задан идентификатор.");
            else if (!seen.Add(t.id)) problems.Add($"{tag}: идентификатор повторяется.");
            if (t.delaySeconds < 0f) problems.Add($"{tag}: задержка отрицательная.");
            switch (t.condition)
            {
                case TriggerCondition.Time:
                    if (t.timeSeconds < 0f) problems.Add($"{tag}: время отрицательное.");
                    else if (s.timeLimitSeconds > 0f && t.timeSeconds + t.delaySeconds >= s.timeLimitSeconds)
                        problems.Add($"{tag}: время {ScenarioText.Clock(t.timeSeconds + t.delaySeconds)} — не раньше лимита {ScenarioText.Clock(s.timeLimitSeconds)}, не сработает.");
                    break;
                case TriggerCondition.EnterCircle:
                    if (t.radiusM <= 0f) problems.Add($"{tag}: радиус круга должен быть больше 0.");
                    if (s.waterArea != null && !s.waterArea.InBounds(t.center)) problems.Add($"{tag}: центр круга вне района акватории.");
                    break;
                case TriggerCondition.Waypoint:
                    if (!points.Contains(t.waypoint ?? "")) problems.Add($"{tag}: в маршруте нет точки «{t.waypoint}» (первая точка — старт, её не «проходят»).");
                    break;
                case TriggerCondition.TargetRange:
                    if (!targets.Contains(t.target ?? "")) problems.Add($"{tag}: нет цели «{t.target}».");
                    if (t.rangeNm <= 0f) problems.Add($"{tag}: дистанция должна быть больше 0.");
                    break;
                case TriggerCondition.TaskStage:
                    if (!s.IsMooring && !s.IsTugWork) problems.Add($"{tag}: «этап задания» — только в сценарии швартовки или работы буксира.");
                    else if (t.stage < 1 || t.stage > 4) problems.Add($"{tag}: этап задания — от 1 до 4.");
                    break;
                case TriggerCondition.AfterTrigger:
                    if (t.afterTrigger == t.id) problems.Add($"{tag}: ссылается сам на себя.");
                    else if (!ids.Contains(t.afterTrigger ?? "")) problems.Add($"{tag}: нет триггера «{t.afterTrigger}».");
                    break;
            }
            if (t.actions == null || t.actions.Count == 0) { problems.Add($"{tag}: нет действий."); continue; }
            foreach (TriggerAction a in t.actions)
            {
                if (a == null) { problems.Add($"{tag}: пустое действие."); continue; }
                switch (a.kind)
                {
                    case TriggerActionKind.TargetOrder:
                        if (!targets.Contains(a.target ?? "")) problems.Add($"{tag}: нет цели «{a.target}».");
                        if (!a.ChangesCourse && !a.ChangesSpeed) problems.Add($"{tag}: цели {a.target} не задан ни курс, ни скорость.");
                        if (a.courseDeg >= 360f) problems.Add($"{tag}: курс цели — от 0 до 360°.");
                        break;
                    case TriggerActionKind.Visibility:
                        if (a.visibilityNm < 0f) problems.Add($"{tag}: видимость отрицательная.");
                        break;
                    case TriggerActionKind.Wind:
                        if (a.windKnots < 0f) problems.Add($"{tag}: ветер отрицательный.");
                        else if (a.windKnots > 0f && s.motionModel != ScenarioMotionModel.Manoeuvring3Dof)
                            problems.Add($"{tag}: ветер действует только в модели 3 СС.");
                        break;
                    case TriggerActionKind.Message:
                        if (string.IsNullOrWhiteSpace(a.text)) problems.Add($"{tag}: пустое сообщение.");
                        break;
                }
            }
        }
        string cycle = Cycle(s.triggers);
        if (cycle != null) problems.Add($"Триггеры: цепочка «сработал другой триггер» замкнута по кругу ({cycle}) — не сработает ни один.");
    }

    // Цикл в цепочках AfterTrigger: «Тр1 → Тр2 → Тр1» или null
    private static string Cycle(List<ScenarioTrigger> list)
    {
        var after = new Dictionary<string, string>();
        foreach (ScenarioTrigger t in list)
            if (t != null && t.condition == TriggerCondition.AfterTrigger && !string.IsNullOrWhiteSpace(t.id) && !after.ContainsKey(t.id))
                after[t.id] = t.afterTrigger ?? "";
        foreach (string start in after.Keys)
        {
            var path = new List<string> { start };
            string cur = start;
            while (after.TryGetValue(cur, out string next) && next != cur)
            {
                if (next == start) { path.Add(start); return string.Join(" → ", path); }
                if (path.Contains(next) || path.Count > list.Count) break;
                path.Add(next);
                cur = next;
            }
        }
        return null;
    }
}
