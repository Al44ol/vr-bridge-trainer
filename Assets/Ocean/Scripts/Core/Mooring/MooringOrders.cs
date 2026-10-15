// ============================================================
// MooringOrders.cs — Положения подруливающего, команды меню «Управление», события швартовки в шину
// (запрос 19.2б)
//
// Подруливающее: 5 положений рычага (решение пользователя) — ЛБ 100 / 50 / 0 / ПБ 50 / 100 %;
// команда BowThruster.Command = −1 … 1 (> 0 — сила на правый борт).
// Меню «Управление» (кнопка B правого контроллера): команды экипажу «как рулевому» — руль
// ступенями (лево/право 5, 10, 20, 35°, прямо), телеграф по положениям, подруливающее.
// MooringEvent (Core, MooringSystem) → ExerciseEvent: единицы — уз, кДж, кН (шапка ExerciseEvents.cs).
// ============================================================
using System;

public enum ThrusterOrder
{
    Port100,
    Port50,
    Zero,
    Starboard50,
    Starboard100
}

public static class ThrusterOrders
{
    public const int Count = 5;
    public const int ZeroIndex = (int)ThrusterOrder.Zero;

    /// <summary>Команда BowThruster.Command, −1 … 1 (> 0 — ПБ).</summary>
    public static double Command(int index) => 0.5 * (Clamp(index) - ZeroIndex);

    /// <summary>Ближайшее положение к команде (−1 … 1).</summary>
    public static int IndexOf(double command) => Clamp((int)Math.Round(command * 2.0) + ZeroIndex);

    public static int Clamp(int index) => Math.Max(0, Math.Min(Count - 1, index));

    public static string Title(int index) => TitleOfPercent(Command(index) * 100.0);

    /// <summary>«ЛБ 50 %», «0», «ПБ 100 %» по проценту (> 0 — ПБ).</summary>
    public static string TitleOfPercent(double percent)
    {
        if (double.IsNaN(percent)) return "—";
        if (Math.Abs(percent) < 0.5) return "0";
        return $"{(percent > 0 ? "ПБ" : "ЛБ")} {ScenarioText.Num(Math.Abs(percent))} %";
    }

    /// <summary>Подпись на шкале рычага.</summary>
    public static string ScaleLabel(int index)
    {
        double p = Command(index) * 100.0;
        return Math.Abs(p) < 0.5 ? "0" : ScenarioText.Num(Math.Abs(p));
    }
}

public static class ControlOrders
{
    public const string Rudder = "руль", Telegraph = "машина", Thruster = "подруливающее";

    /// <summary>Ступени руля по меню, ° (> 0 — ПБ).</summary>
    public static readonly double[] RudderSteps = { -35, -20, -10, -5, 0, 5, 10, 20, 35 };

    /// <summary>Ближайшая ступень руля к углу, номер в RudderSteps.</summary>
    public static int NearestRudderStep(double deg)
    {
        int best = 0;
        for (int i = 1; i < RudderSteps.Length; i++)
            if (Math.Abs(RudderSteps[i] - deg) < Math.Abs(RudderSteps[best] - deg)) best = i;
        return best;
    }

    /// <summary>Руль словами: «прямо», «лево 10°», «право 35°».</summary>
    public static string RudderTitle(double deg)
    {
        if (double.IsNaN(deg)) return "—";
        if (Math.Abs(deg) < 0.05) return "прямо";
        return $"{(deg > 0 ? "право" : "лево")} {ScenarioText.Num(Math.Abs(deg))}°";
    }

    /// <summary>Значение команды для журнала: руль — °, машина — положение телеграфа, подруливающее — %.</summary>
    public static string ValueText(string subject, double value)
    {
        if (double.IsNaN(value)) return "—";
        switch (subject)
        {
            case Rudder: return RudderTitle(value);
            case Telegraph: return ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(value));
            case Thruster: return ThrusterOrders.TitleOfPercent(value);
        }
        return ScenarioText.Num(value, 1);
    }
}

public static class MooringEventBus
{
    /// <summary>Событие MooringSystem → событие шины упражнения (без времени и состояния судна — их ставит источник).</summary>
    public static ExerciseEvent ToExerciseEvent(in MooringEvent e)
    {
        const double kn = ShipParameters.KnotsToMs;
        switch (e.Type)
        {
            case MooringEventType.BerthingImpact:
                return new ExerciseEvent(ExerciseEventType.BerthingImpact)
                {
                    Value = e.Value / kn, Value2 = e.Value2 / 1000.0,
                    Index = (int)Math.Round(e.Value3 * 100.0), Subject = e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            case MooringEventType.FenderOverload:
                return new ExerciseEvent(ExerciseEventType.FenderOverload) { Index = e.Index, Value = e.Value };
            case MooringEventType.QuayImpact:
                return new ExerciseEvent(ExerciseEventType.QuayImpact) { Value = e.Value / kn };
            case MooringEventType.LineMadeFast:
                return new ExerciseEvent(ExerciseEventType.LineMadeFast) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value, Value2 = e.Value2 };
            case MooringEventType.LineReleased:
                return new ExerciseEvent(ExerciseEventType.LineReleased) { Index = e.Index, Subject = e.Subject ?? "" };
            case MooringEventType.LineOverload:
                return new ExerciseEvent(ExerciseEventType.LineOverload) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value / 1000.0, Value2 = e.Value2 };
            case MooringEventType.LineOverloadCleared:
                return new ExerciseEvent(ExerciseEventType.LineOverloadCleared) { Index = e.Index, Subject = e.Subject ?? "" };
            case MooringEventType.LineBroken:
                return new ExerciseEvent(ExerciseEventType.LineBroken) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value / 1000.0 };
        }
        throw new ArgumentOutOfRangeException(nameof(e), e.Type, "нет соответствия в шине");
    }
}
