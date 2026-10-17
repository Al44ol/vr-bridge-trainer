// ============================================================
// TugConsoleText.cs — Строки приборов пульта своего буксира (запрос 19.4а-5, Core)
//
// Пульт буксира (TugConsole, сцена) — три экрана на приборной панели: колонки, монитор буксира, джойстик.
// Здесь — текст монитора и подписи указателей колонок (без сцены, проверяются тестами):
//   • монитор: глубина (или сигнализация мели), курс и автомат курса, скорость вперёд и лагом, поворот,
//     ветер, строка причала, команда лоцмана и задания (19-03), отказ (вводная инструктора);
//   • колонка: «ПБ 045° · 50 %» — направление упора от ДП (0 — вперёд, 90 — на ПБ) и загрузка.
// На буксире монитор заменяет NavDisplay (скрыт): руля и телеграфа у буксира нет — их строк нет.
// ============================================================
using System;
using System.Text;

/// <summary>Данные монитора пульта буксира (единицы — в именах).</summary>
public struct TugMonitorData
{
    public double HeadingDeg, SpeedKn, SwayKn, RotDegMin;
    /// <summary>Автомат курса включён; заданный курс, ° (NaN — идёт разворот).</summary>
    public bool HeadingHold;
    public double HoldHeadingDeg;
    /// <summary>Глубина по карте и осадка, м (NaN — нет карты).</summary>
    public double DepthM, DraftM;
    public double WindKn, WindFromDeg;
    /// <summary>Готовые строки (пусто — нет): сигнализация мели, причал, лоцман и задания, отказ.</summary>
    public string Alarm, Berth, Pilot, Failure;
}

public static class TugConsoleText
{
    /// <summary>Текст монитора буксира (TextMeshPro, разметка &lt;b&gt;, &lt;size&gt;, &lt;color&gt;).</summary>
    public static string Monitor(in TugMonitorData d)
    {
        var b = new StringBuilder();
        if (!string.IsNullOrEmpty(d.Alarm)) b.AppendLine($"<color=#FF5A4A><b>{d.Alarm}</b></color>");
        else if (double.IsNaN(d.DepthM)) b.AppendLine("ГЛУБИНА —");
        else b.AppendLine($"ГЛУБИНА {ScenarioText.Num(d.DepthM)} м <size=75%>под килем {ScenarioText.Num(d.DepthM - d.DraftM, 1)} м</size>");

        double h = ((d.HeadingDeg % 360.0) + 360.0) % 360.0;
        string hold = !d.HeadingHold ? "автомат выкл"
            : double.IsNaN(d.HoldHeadingDeg) ? "автомат: разворот"
            : $"автомат {((d.HoldHeadingDeg % 360.0) + 360.0) % 360.0:000}°";
        b.AppendLine($"<b>КУРС</b> {h:000.0}°  <size=75%>{hold}</size>");
        b.AppendLine($"<b>ХОД</b> {Signed(d.SpeedKn, "вперёд", "назад")}");
        b.AppendLine($"<b>ЛАГОМ</b> {Signed(d.SwayKn, "на ПБ", "на ЛБ", 2)}");
        b.AppendLine($"<b>ПОВОРОТ</b> {(Math.Abs(d.RotDegMin) < 0.5 ? "0" : $"{ScenarioText.Num(Math.Abs(d.RotDegMin))} °/мин {(d.RotDegMin > 0 ? "вправо" : "влево")}")}");
        b.Append(d.WindKn < 0.05 ? "<size=80%>ветра нет</size>" : $"<size=80%>ветер {ScenarioText.Wind(d.WindKn, d.WindFromDeg)}</size>");
        if (!string.IsNullOrEmpty(d.Berth)) b.Append($"\n<size=75%>{d.Berth}</size>");
        if (!string.IsNullOrEmpty(d.Pilot)) b.Append($"\n<size=75%><color=#7FC4FF>{d.Pilot}</color></size>");
        if (!string.IsNullOrEmpty(d.Failure)) b.Append($"\n<color=#FF5A4A><b>{d.Failure}</b></color>");
        return b.ToString();
    }

    // «вперёд 5,8 уз» / «0»
    private static string Signed(double kn, string plus, string minus, int digits = 1)
    {
        double lim = digits >= 2 ? 0.005 : 0.05;
        return Math.Abs(kn) < lim ? "0" : $"{ScenarioText.Num(Math.Abs(kn), digits)} уз {(kn > 0 ? plus : minus)}";
    }

    /// <summary>Указатель колонки: «ПБ 045° · 50 %» (направление упора от ДП, загрузка 0…1).</summary>
    public static string Pod(string side, double dirDeg, double load)
    {
        double d = ((dirDeg % 360.0) + 360.0) % 360.0;
        int pct = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, load)) * 100.0);
        return pct == 0 ? $"{side} — · 0 %" : $"{side} {d:000}° · {pct} %";
    }
}
