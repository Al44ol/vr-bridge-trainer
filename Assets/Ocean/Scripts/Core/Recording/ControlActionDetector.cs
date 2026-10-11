// ============================================================
// ControlActionDetector.cs — Действия обучающегося на органах управления (C1)
//
// Чистый класс: на каждом шаге модели получает состояние органов управления
// (ControlState) и возвращает события для шины ExerciseEvents. В сцене его
// вызывает BridgeActionMonitor; время — собственные часы монитора
// (симуляционные, сумма шагов), они нужны только для длительностей.
//
// Перекладка руля (штурвал):
//   начало — штурвал в руке и заданный руль ушёл от последнего устоявшегося
//            значения δ₀ на |δ_зад − δ₀| ≥ ε_δ (0,5°) → HelmStarted (Value = δ₀);
//   конец  — штурвал отпущен или δ_зад не уходит больше чем на ε_ст (0,2°)
//            от последнего положения T_уст (1 с) → HelmEnded
//            (Value = итоговый δ_зад, Value2 = длительность до последнего
//            движения, с); событие опаздывает на T_уст, длительность — нет.
//   Без руки на штурвале перекладка не считается: начальные условия
//   сценария (SetRudderOrder) — не действие обучающегося.
// Телеграф: положение (номер TelegraphOrder) считается поданной командой,
//   когда устоялось — рычаг отпущен или T_тел (0,5 с) без изменений.
//   Промежуточные положения, через которые прошёл рычаг, командами не
//   считаются (в track.csv они есть). TelegraphChanged: Index — новое
//   положение, Value — новый SF, Value2 — прежний SF; ByInstructor —
//   положение поставлено кодом (съём с мели инструктором, B1).
// Гудок: начало / конец звучания → HornStarted / HornEnded (Value — длительность, с).
// Погода: смена пресета → WeatherChanged (Index — новый, Value — прежний пресет).
// Время суток (B4): смена → TimeOfDayChanged (Index — новое, Value — прежнее).
// Пороги — предварительные, задаются в инспекторе монитора.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Состояние органов управления на шаге.</summary>
public struct ControlState
{
    /// <summary>Заданный руль, градусы (> 0 — ПБ).</summary>
    public double RudderOrderedDeg;
    public bool WheelGrabbed;
    /// <summary>Положение телеграфа — номер TelegraphOrder.</summary>
    public int Telegraph;
    public double TelegraphSf;
    public bool TelegraphGrabbed;
    /// <summary>Последнее изменение положения сделано кодом (не рукой).</summary>
    public bool TelegraphByCode;
    public bool HornSounding;
    public int WeatherPreset;
    /// <summary>Время суток — номер TimeOfDay (B4; 0 — день).</summary>
    public int TimeOfDay;
}

public sealed class ControlActionDetector
{
    public double HelmThresholdDeg = 0.5;
    public double HelmStillDeg = 0.2;
    public double HelmSettleSeconds = 1.0;
    public double TelegraphSettleSeconds = 0.5;

    private readonly List<ExerciseEvent> events = new List<ExerciseEvent>();

    // Руль
    private bool helmActive;
    private double helmBase, helmStartTime, helmAnchor, helmLastMove;
    // Телеграф
    private int telBase, telPending;
    private double telBaseSf, telPendingTime;
    // Гудок, погода
    private bool horn;
    private double hornStart;
    private int weather, timeOfDay;

    /// <summary>Начальное состояние (начало упражнения): текущие положения — не действия.</summary>
    public void Reset(double time, in ControlState s)
    {
        helmActive = false;
        helmBase = s.RudderOrderedDeg;
        telBase = telPending = s.Telegraph;
        telBaseSf = s.TelegraphSf;
        telPendingTime = time;
        horn = s.HornSounding;
        hornStart = time;
        weather = s.WeatherPreset;
        timeOfDay = s.TimeOfDay;
    }

    public IReadOnlyList<ExerciseEvent> Update(double time, in ControlState s)
    {
        events.Clear();
        Helm(time, s);
        Telegraph(time, s);
        Horn(time, s);
        Weather(s);
        DayTime(s);
        return events;
    }

    private void Helm(double time, in ControlState s)
    {
        double d = s.RudderOrderedDeg;
        if (!helmActive)
        {
            if (s.WheelGrabbed && Math.Abs(d - helmBase) >= HelmThresholdDeg)
            {
                helmActive = true;
                helmStartTime = time;
                helmAnchor = d;
                helmLastMove = time;
                events.Add(new ExerciseEvent(ExerciseEventType.HelmStarted) { Value = helmBase });
            }
            else if (!s.WheelGrabbed) helmBase = d;   // руль поставлен кодом — новое исходное значение
            return;
        }

        if (Math.Abs(d - helmAnchor) > HelmStillDeg)
        {
            helmAnchor = d;
            helmLastMove = time;
        }
        if (!s.WheelGrabbed || time - helmLastMove >= HelmSettleSeconds)
        {
            helmActive = false;
            helmBase = d;
            events.Add(new ExerciseEvent(ExerciseEventType.HelmEnded) { Value = d, Value2 = Math.Max(0.0, helmLastMove - helmStartTime) });
        }
    }

    private void Telegraph(double time, in ControlState s)
    {
        if (s.Telegraph != telPending)
        {
            telPending = s.Telegraph;
            telPendingTime = time;
        }
        if (telPending == telBase) return;
        if (s.TelegraphGrabbed && time - telPendingTime < TelegraphSettleSeconds) return;

        events.Add(new ExerciseEvent(ExerciseEventType.TelegraphChanged)
        {
            Index = telPending, Value = s.TelegraphSf, Value2 = telBaseSf,
            Subject = ScenarioText.Telegraph((TelegraphOrder)telPending), ByInstructor = s.TelegraphByCode && !s.TelegraphGrabbed
        });
        telBase = telPending;
        telBaseSf = s.TelegraphSf;
    }

    private void Horn(double time, in ControlState s)
    {
        if (s.HornSounding == horn) return;
        horn = s.HornSounding;
        if (horn)
        {
            hornStart = time;
            events.Add(new ExerciseEvent(ExerciseEventType.HornStarted));
        }
        else events.Add(new ExerciseEvent(ExerciseEventType.HornEnded) { Value = time - hornStart });
    }

    private void Weather(in ControlState s)
    {
        if (s.WeatherPreset == weather) return;
        events.Add(new ExerciseEvent(ExerciseEventType.WeatherChanged)
        {
            Index = s.WeatherPreset, Value = weather, Subject = ScenarioText.WeatherTitle((WeatherPresetId)s.WeatherPreset)
        });
        weather = s.WeatherPreset;
    }

    private void DayTime(in ControlState s)
    {
        if (s.TimeOfDay == timeOfDay) return;
        events.Add(new ExerciseEvent(ExerciseEventType.TimeOfDayChanged)
        {
            Index = s.TimeOfDay, Value = timeOfDay, Subject = ScenarioText.TimeOfDayTitle((TimeOfDay)s.TimeOfDay)
        });
        timeOfDay = s.TimeOfDay;
    }
}
