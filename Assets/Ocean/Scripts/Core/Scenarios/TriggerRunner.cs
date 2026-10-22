// ============================================================
// TriggerRunner.cs — Исполнитель триггеров сценария (запрос 21.6а, Core)
//
// Чистый класс: сцена (ScenarioTriggers) вызывает Step в FixedUpdate упражнения, тесты — без сцены.
// Состояния триггера: Waiting (условие не выполнено) → Delay (выполнено, идёт задержка; срок — DueAt) →
// Fired (сработал, FiredAt; один раз за упражнение). Ручной запуск (окно инструктора) — сразу, без задержки.
// Факты, которые нельзя вычислить по состоянию (пройдена точка, выполнен этап), сцена сообщает из шины:
// WaypointPassed / StageReached. Цепочки AfterTrigger срабатывают в том же шаге (задержка — от срабатывания
// предыдущего). Step возвращает номера сработавших в этом шаге по порядку — их действия выполняет сцена.
// ============================================================
using System;
using System.Collections.Generic;

public enum TriggerStatus { Waiting, Delay, Fired }

public sealed class TriggerRunner
{
    private readonly List<ScenarioTrigger> triggers = new List<ScenarioTrigger>();
    private readonly List<TriggerStatus> status = new List<TriggerStatus>();
    private readonly List<double> dueAt = new List<double>();
    private readonly List<double> firedAt = new List<double>();
    private readonly List<bool> manual = new List<bool>();
    private readonly HashSet<string> passed = new HashSet<string>();
    private readonly HashSet<int> stages = new HashSet<int>();
    private readonly List<int> fired = new List<int>();

    public IReadOnlyList<ScenarioTrigger> Triggers => triggers;
    public int Count => triggers.Count;
    public TriggerStatus Status(int i) => status[i];
    /// <summary>Срок срабатывания после задержки, с (NaN — условие не выполнено).</summary>
    public double DueAt(int i) => dueAt[i];
    /// <summary>Время срабатывания, с (NaN — не сработал).</summary>
    public double FiredAt(int i) => firedAt[i];
    /// <summary>Сработал вручную (инструктор).</summary>
    public bool Manual(int i) => manual[i];
    public int FiredCount { get { int n = 0; foreach (TriggerStatus s in status) if (s == TriggerStatus.Fired) n++; return n; } }

    /// <summary>Новый набор триггеров (загрузка сценария, «Начать»): всё — в ожидание.</summary>
    public void Reset(IList<ScenarioTrigger> list)
    {
        triggers.Clear();
        if (list != null) foreach (ScenarioTrigger t in list) if (t != null) triggers.Add(t);
        status.Clear(); dueAt.Clear(); firedAt.Clear(); manual.Clear();
        for (int i = 0; i < triggers.Count; i++)
        {
            status.Add(TriggerStatus.Waiting);
            dueAt.Add(double.NaN);
            firedAt.Add(double.NaN);
            manual.Add(false);
        }
        passed.Clear();
        stages.Clear();
    }

    public int IndexOf(string id) => triggers.FindIndex(t => t.id == id);

    /// <summary>Пройдена точка маршрута (WaypointPassed, Subject — имя).</summary>
    public void WaypointPassed(string name)
    {
        if (!string.IsNullOrEmpty(name)) passed.Add(name);
    }

    /// <summary>Выполнен этап задания (MooringStage / TugWorkStage, Index).</summary>
    public void StageReached(int stage) => stages.Add(stage);

    /// <summary>
    /// Шаг: t — время упражнения, с; own — позиция своего судна; rangeM — дистанция до цели по id, м (NaN — нет цели).
    /// Возвращает номера сработавших в этом шаге (список переиспользуется).
    /// </summary>
    public List<int> Step(double t, WorldPosition own, Func<string, double> rangeM)
    {
        fired.Clear();
        bool changed = true;
        for (int pass = 0; changed && pass <= triggers.Count; pass++)   // цепочки — в том же шаге
        {
            changed = false;
            for (int i = 0; i < triggers.Count; i++)
            {
                if (status[i] == TriggerStatus.Fired) continue;
                if (status[i] == TriggerStatus.Waiting)
                {
                    if (!Met(triggers[i], t, own, rangeM, out double since)) continue;
                    status[i] = TriggerStatus.Delay;
                    dueAt[i] = since + Math.Max(0.0, triggers[i].delaySeconds);
                }
                if (t + 1e-9 < dueAt[i]) continue;
                Mark(i, t, false);
                changed = true;
            }
        }
        return fired;
    }

    /// <summary>Сработать вручную (инструктор): сразу, без задержки; false — уже сработал.</summary>
    public bool Fire(int i, double t)
    {
        if (i < 0 || i >= triggers.Count || status[i] == TriggerStatus.Fired) return false;
        Mark(i, t, true);
        return true;
    }

    private void Mark(int i, double t, bool byHand)
    {
        status[i] = TriggerStatus.Fired;
        firedAt[i] = t;
        manual[i] = byHand;
        fired.Add(i);
    }

    // Условие выполнено; since — момент, от которого считается задержка
    private bool Met(ScenarioTrigger tr, double t, WorldPosition own, Func<string, double> rangeM, out double since)
    {
        since = t;
        switch (tr.condition)
        {
            case TriggerCondition.Time:
                since = Math.Max(0.0, tr.timeSeconds);
                return t + 1e-9 >= tr.timeSeconds;
            case TriggerCondition.EnterCircle:
                return tr.radiusM > 0f && (own - tr.center).Magnitude <= tr.radiusM;
            case TriggerCondition.Waypoint:
                return passed.Contains(tr.waypoint ?? "");
            case TriggerCondition.TargetRange:
                double d = rangeM != null ? rangeM(tr.target ?? "") : double.NaN;
                return !double.IsNaN(d) && d < tr.rangeNm * 1852.0;
            case TriggerCondition.TaskStage:
                return stages.Contains(tr.stage);
            case TriggerCondition.AfterTrigger:
                int j = IndexOf(tr.afterTrigger);
                if (j < 0 || status[j] != TriggerStatus.Fired) return false;
                since = firedAt[j];
                return true;
            default:
                return false;
        }
    }

    /// <summary>Состояние словами (окно инструктора): «ждёт», «через 00:42», «сработал 05:12 (вручную)».</summary>
    public string StateText(int i, double now)
    {
        switch (status[i])
        {
            case TriggerStatus.Fired: return $"сработал {ScenarioText.Clock(firedAt[i])}{(manual[i] ? " (вручную)" : "")}";
            case TriggerStatus.Delay: return $"через {ScenarioText.Clock(Math.Max(0.0, dueAt[i] - now))}";
            default: return "ждёт";
        }
    }
}
