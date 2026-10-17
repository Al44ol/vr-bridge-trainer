// ============================================================
// TugWorkReaction.cs — Реакция на команды лоцмана упором (запрос 19.4в, Core; критерий G11 и отчёт)
//
// Команды упором — события PilotOrder вида PushStep / StopPush («Толкай малым», «Держи средним», «Стоп толкать»):
// названная ступень k = round(Value / 25 %) (0 — стоп, 1…4 — 25…100 % тяги на гаке). Положение рукоятки — события
// JoystickChanged (установившееся положение 0,5 с; Value — упор хода, %), ступень рукоятки s = max(0, round(Value / 25 %)):
// назад — тоже «не толкает». Лагом и разворот не учитываются (ими буксир держит место у борта).
// Команда действует до следующей команды упором, а последняя — до «концы закреплены» (дальше — «отходите»)
// или до конца упражнения. Реакция τ:
//   • рукоятка уже на названной ступени в момент команды — 0 с;
//   • иначе — время до первого установившегося положения на этой ступени, пока команда действует;
//   • не встала на ступень, пока команда действовала, — «не выполнена» (в G11 — не меньше порога нуля).
// Одна функция и у оценки (ExerciseAssessor — в шлеме и по записи), и у раздела отчёта «Работа буксира».
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Команда лоцмана упором и реакция на неё.</summary>
public struct PilotPowerReaction
{
    public double OrderTime;
    /// <summary>Команда действует до … (NaN — ещё действует).</summary>
    public double Until;
    public int Step;
    public string Text;
    /// <summary>Ступень рукоятки в момент команды.</summary>
    public int StepBefore;
    /// <summary>Рукоятка встала на названную ступень; ReactionS — через сколько, с.</summary>
    public bool Executed;
    public double ReactionS;
    /// <summary>Итог известен: выполнена или команда уже не действует.</summary>
    public bool Resolved => Executed || !double.IsNaN(Until);
}

public static class TugWorkReaction
{
    public static bool IsPowerOrder(in ExerciseEvent e) =>
        e.Type == ExerciseEventType.PilotOrder && (e.Index == (int)PilotOrderKind.PushStep || e.Index == (int)PilotOrderKind.StopPush);

    /// <summary>Ступень по упору, % (0…4; назад — 0).</summary>
    public static int StepOf(double pct) =>
        double.IsNaN(pct) ? 0 : Math.Max(0, Math.Min(TugJoystick.StepCount, (int)Math.Round(pct / (100.0 * TugJoystick.MenuStep))));

    /// <summary>
    /// Реакции на команды упором. orders — события PilotOrder (берутся команды упором), joystick — JoystickChanged, по времени;
    /// lastUntil — до какого времени действует последняя команда («концы закреплены» или конец упражнения; NaN — ещё действует).
    /// </summary>
    public static List<PilotPowerReaction> Compute(IReadOnlyList<ExerciseEvent> orders, IReadOnlyList<ExerciseEvent> joystick, double lastUntil)
    {
        var power = new List<ExerciseEvent>();
        foreach (ExerciseEvent e in orders) if (IsPowerOrder(e)) power.Add(e);
        var r = new List<PilotPowerReaction>(power.Count);
        for (int i = 0; i < power.Count; i++)
        {
            ExerciseEvent o = power[i];
            double until = i + 1 < power.Count ? power[i + 1].Time : lastUntil;
            if (!double.IsNaN(until) && until < o.Time) until = o.Time;
            var x = new PilotPowerReaction
            {
                OrderTime = o.Time, Until = until, Step = StepOf(o.Value), Text = o.Subject ?? "", ReactionS = double.NaN
            };
            int j = 0;
            while (j < joystick.Count && joystick[j].Time <= o.Time + 1e-9) j++;
            x.StepBefore = j > 0 ? StepOf(joystick[j - 1].Value) : 0;
            if (x.StepBefore == x.Step) { x.Executed = true; x.ReactionS = 0.0; }
            else
                for (; j < joystick.Count; j++)
                {
                    if (!double.IsNaN(until) && joystick[j].Time >= until - 1e-9) break;
                    if (StepOf(joystick[j].Value) != x.Step) continue;
                    x.Executed = true;
                    x.ReactionS = joystick[j].Time - o.Time;
                    break;
                }
            r.Add(x);
        }
        return r;
    }

    /// <summary>Время действия команды, которую не выполнили (до следующей / конца), с.</summary>
    public static double InForce(in PilotPowerReaction x) => double.IsNaN(x.Until) ? double.NaN : x.Until - x.OrderTime;
}
