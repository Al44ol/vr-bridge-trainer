// ============================================================
// DemoTriggerScenario.cs — Сценарий 21-01 «Фарватер с триггерами» (запрос 21.6а)
//
// Пример триггеров «если — то» на маршруте B2-01 (DemoScenario: старт (0; −2500) 000° 15 уз → вход в фарватер →
// поворот вправо на 60° → выход). Цель T1 — сухогруз (Mariner), стоит без хода к востоку от поворота
// (2600; 2600), курс 330°. Триггеры:
//   Тр1 — время 01:00: сообщение диспетчера — за поворотом стоит судно, готовится сняться;
//   Тр2 — своё судно в круге 400 м у (0; 2400) — за 800 м до поворота: T1 начинает движение 330°, 8 уз.
//         Без манёвра своего судна (по маршруту, 15 уз) T1 приходит в точку пересечения второго колена
//         (≈ 1690; 4176) почти одновременно со своим (≈ 442 с против ≈ 408 с) — курс пересечения справа
//         (правило 15: своё судно уступает дорогу; в узкости — правило 9 г для T1);
//   Тр3 — через 60 с после Тр2: видимость 1 миля (ограниченная — туманные сигналы, правило 19, критерии F);
//   Тр4 — дистанция до T1 меньше 1 мили: сообщение «T1 не отвечает на вызов по УКВ».
// Значения — демонстрационные (пример возможностей, без обоснования как учебной нормы).
// Ассет создаётся сам при входе в Play Mode (SetupTriggerScenario); версия — Version (demoVersion).
// ============================================================
using System.Collections.Generic;

public static class DemoTriggerScenario
{
    public const string Id = "21-01";
    public const string Title = "Фарватер с триггерами";
    /// <summary>Версия содержимого (ассет с меньшей demoVersion заполняется заново при входе в Play Mode).</summary>
    public const int Version = 1;

    public static readonly WorldPosition TargetStart = new WorldPosition(2600, 2600);
    public const float TargetHeadingDeg = 330f, TargetSpeedKnots = 8f;
    public static readonly WorldPosition CircleCenter = new WorldPosition(0, 2400);
    public const float CircleRadiusM = 400f;
    public const float FogVisibilityNm = 1f;

    public const string DispatcherMessage = "Диспетчер: за поворотом фарватера, у восточного берега, стоит сухогруз — готовится сняться.";
    public const string VhfMessage = "Сухогруз T1 не отвечает на вызов по УКВ. Действуйте по МППСС.";

    public const string Briefing =
        "Своё судно — в 1,2 мили к югу от входа в учебный фарватер, курс 000°, полный ход 15 уз.\n\n" +
        "Задача: пройти фарватером по путевым точкам, как в упражнении B2-01: вход в фарватер, " +
        "поворот вправо на 60°, выход из фарватера. " + DemoScenario.KeepRightSentence + "\n\n" +
        "Обстановка в районе может меняться по ходу упражнения: следите за сообщениями, другими судами " +
        "и видимостью и действуйте по МППСС-72.\n\n" +
        "Столкновение и посадка на мель — провал упражнения. Лимит времени — 30 минут.";

    /// <summary>Заполнить сценарий (прежнее содержимое заменяется); assessment — набор оценки (как у B2-01).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters own, ShipParameters cargo, AssessmentCriteria assessment)
    {
        DemoScenario.Fill(s, area, own);
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.demoVersion = Version;
        s.assessment = assessment;
        s.traffic = new List<TrafficShipDefinition>
        {
            new TrafficShipDefinition
            {
                id = "T1", name = "Сухогруз у берега", type = TrafficVesselType.Cargo, parameters = cargo,
                placement = TrafficPlacement.Absolute, position = TargetStart,
                headingDeg = TargetHeadingDeg, speedKnots = 0f, motion = TrafficMotion.ConstantCourse
            }
        };
        s.triggers = Triggers();
    }

    public static List<ScenarioTrigger> Triggers()
    {
        return new List<ScenarioTrigger>
        {
            new ScenarioTrigger
            {
                id = "Тр1", title = "Диспетчер", condition = TriggerCondition.Time, timeSeconds = 60f,
                actions = { new TriggerAction { kind = TriggerActionKind.Message, text = DispatcherMessage } }
            },
            new ScenarioTrigger
            {
                id = "Тр2", title = "Сухогруз снимается", condition = TriggerCondition.EnterCircle,
                center = CircleCenter, radiusM = CircleRadiusM,
                actions = { new TriggerAction { kind = TriggerActionKind.TargetOrder, target = "T1", courseDeg = TargetHeadingDeg, speedKnots = TargetSpeedKnots } }
            },
            new ScenarioTrigger
            {
                id = "Тр3", title = "Туман", condition = TriggerCondition.AfterTrigger, afterTrigger = "Тр2", delaySeconds = 60f,
                actions = { new TriggerAction { kind = TriggerActionKind.Visibility, visibilityNm = FogVisibilityNm } }
            },
            new ScenarioTrigger
            {
                id = "Тр4", title = "УКВ", condition = TriggerCondition.TargetRange, target = "T1", rangeNm = 1f,
                actions = { new TriggerAction { kind = TriggerActionKind.Message, text = VhfMessage } }
            }
        };
    }
}
