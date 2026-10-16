// ============================================================
// DemoTugBerthScenario.cs — Сценарий 19-02 «Швартовка с буксиром при отжимном ветре» (запрос 19.3в)
//
// Как 19-01 (DemoBerthScenario: причал №1, Model45m, 3 СС, штиль по волнам, старт в 200 м до середины
// причала и в 30 м от кромки, «Самый малый вперёд», те же четыре задания и финиш «ошвартован»), но:
//   • ветер 20 уз отжимной — с причала на воду, поперёк кромки (для причала №1 — «откуда» 330°);
//     направление случайное в секторе ± WindSectorDeg/2 (19.3б-2: решение пользователя — ветер случайный
//     или выбранный в меню); у прогонов «за обучающегося» — середина сектора;
//   • буксир по вызову (меню B → «Буксир»): вызывает обучающийся сам — где поставить буксир и как им
//     работать, часть задания. Против 14,5 кН ветра подруливающее (17,7 кН в носу) судно лагом не удерживает (19.24.4);
//   • лимит 25 мин, контрольное время 12 мин.
// Набор критериев — как у 19-01 (маршрутные выключены) + группа «Буксир» (G1–G4). Значения — предварительные.
// ============================================================
using System;
using System.Collections.Generic;

public static class DemoTugBerthScenario
{
    public const string Id = "19-02";
    public const string Title = "Швартовка с буксиром при отжимном ветре";
    public const float WindKnots = 20f;
    public const float WindSectorDeg = 60f;
    public const float TimeLimitSeconds = 1500f;    // 25 мин
    public const float TargetTimeSeconds = 720f;    // контрольное время, 12 мин

    public const string Briefing =
        "День, волнения нет. Ветер 20 уз, отжимной — дует с причала, сносит судно от кромки. " +
        "Своё судно идёт самым малым ходом вдоль причала №1, в 200 м до его середины и в 30 м от кромки. Причал — по левому борту.\n\n" +
        "Задача: ошвартоваться левым бортом к причалу №1:\n" +
        "  1. подойти лагом;\n" +
        "  2. погасить ход и остановиться у кранцев;\n" +
        "  3. подать концы с носа и кормы;\n" +
        "  4. обтянуть концы и стоять у кранцев без хода.\n\n" +
        "Против такого ветра одного подруливающего мало: в вашем распоряжении буксир (меню «Управление» → «Буксир»). " +
        "Решите сами, где его поставить (у кормы, у носа или у миделя), тянуть или толкать и с какой тягой. " +
        "Не давайте большую тягу на слабом тросе (рывок) и не прижимайте судно к причалу быстрее, чем при обычном навале.\n\n" +
        "Команды — меню «Управление» (кнопка B): руль, машина, подруливающее, концы, буксир, переход на крыло. " +
        "Расстояния до кромки и скорость к причалу — строка «ПРИЧАЛ» на навигационном дисплее.\n\n" +
        "Лимит времени — 25 минут.";

    /// <summary>Отжимной ветер для причала: «откуда» — с берега поперёк кромки (против нормали к воде), °.</summary>
    public static double OffBerthWindFromDeg(BerthArea b)
    {
        WorldPosition n = b.NAxis;   // к воде
        double d = Math.Atan2(-n.East, -n.North) * 180.0 / Math.PI;
        return Math.Round(d < 0 ? d + 360.0 : d, 1);
    }

    /// <summary>Заполнить сценарий (по 19-01; прежнее содержимое заменяется).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters own, AssessmentCriteria assessment)
    {
        DemoBerthScenario.Fill(s, area, own, assessment);
        BerthArea b = area != null && area.berths != null && area.berths.Count > DemoBerthScenario.BerthIndex
            ? area.berths[DemoBerthScenario.BerthIndex] : DemoWaterArea.Berth();
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.windKnots = WindKnots;
        s.windFromDeg = (float)OffBerthWindFromDeg(b);
        s.windRandomSectorDeg = WindSectorDeg;
        s.tugAvailable = true;
        s.timeLimitSeconds = TimeLimitSeconds;
        s.mooring.targetTimeS = TargetTimeSeconds;
    }

    /// <summary>Набор критериев 19-02: как у 19-01 (маршрутные выключены), группа «Буксир» — по умолчанию.</summary>
    public static AssessmentSettings Assessment() => DemoBerthScenario.Assessment();
}
