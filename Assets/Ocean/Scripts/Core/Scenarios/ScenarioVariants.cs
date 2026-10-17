// ============================================================
// ScenarioVariants.cs — Вариант сценария на другом судне (запрос 15)
//
// «Тип судна: катер» не дублирует ассеты сценариев: при загрузке сценарий
// копируется в памяти (Object.Instantiate — маршрут, цели, акватория по ссылке),
// в копии меняются судно, начальная скорость, id («B2-01-FB») и название.
//
// Начальная скорость пересчитывается на установившуюся скорость нового судна
// при том же положении телеграфа: v' = v · U_уст'(SF) / U_уст(SF). «Полный вперёд»
// 15 уз модели 45 м → 35 уз катера; цели с «точкой встречи» (B3) размещаются по
// новой скорости своего судна сами (TrafficPlanner берёт её из initial).
// Если на исходном судне при этом SF хода нет (стоп) — скорость не больше
// эксплуатационной нового судна.
//
// Копия запоминает исходный сценарий: перезапуск и смена типа судна во время
// брифинга берут исходник, а не копию копии. Копии кэшируются (одна на пару
// «сценарий — судно»).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public static class ScenarioVariants
{
    private static readonly Dictionary<ScenarioDefinition, ScenarioDefinition> sources =
        new Dictionary<ScenarioDefinition, ScenarioDefinition>();
    private static readonly Dictionary<(ScenarioDefinition, ShipParameters), ScenarioDefinition> cache =
        new Dictionary<(ScenarioDefinition, ShipParameters), ScenarioDefinition>();

    /// <summary>Исходный сценарий копии (для исходного — он сам).</summary>
    public static ScenarioDefinition SourceOf(ScenarioDefinition s)
    {
        if (s != null && sources.TryGetValue(s, out ScenarioDefinition src) && src != null) return src;
        return s;
    }

    /// <summary>Сценарий — копия на другом судне.</summary>
    public static bool IsVariant(ScenarioDefinition s) => s != null && SourceOf(s) != s;

    /// <summary>
    /// Сценарий на судне <paramref name="ship"/>. Если судно сценария (или, при пустом поле,
    /// <paramref name="sceneDefault"/>) уже это — исходный сценарий без копии.
    /// </summary>
    public static ScenarioDefinition WithShip(ScenarioDefinition s, ShipParameters ship, ShipParameters sceneDefault,
                                              string idSuffix, string titleSuffix, string briefingNote = "")
    {
        s = SourceOf(s);
        if (s == null || ship == null) return s;
        ShipParameters original = s.shipParameters != null ? s.shipParameters : sceneDefault;
        if (original == ship) return s;
        if (cache.TryGetValue((s, ship), out ScenarioDefinition cached) && cached != null) return cached;

        ScenarioDefinition v = UnityEngine.Object.Instantiate(s);
        v.name = s.name + idSuffix;
        v.sourceFile = s.sourceFile;   // 21.1: [NonSerialized] — Instantiate не копирует
        v.id = s.id + idSuffix;
        v.title = s.title + titleSuffix;
        if (!string.IsNullOrEmpty(briefingNote))
            v.briefing = string.IsNullOrEmpty(s.briefing) ? briefingNote : s.briefing + "\n\n" + briefingNote;
        v.shipParameters = ship;
        if (v.initial != null && s.initial != null)
            v.initial.speedKnots = (float)ScaledSpeed(s.initial, original, ship);
        sources[v] = s;
        cache[(s, ship)] = v;
        return v;
    }

    /// <summary>Начальная скорость на новом судне при том же телеграфе, уз.</summary>
    public static double ScaledSpeed(OwnShipInitialState init, ShipParameters from, ShipParameters to)
    {
        double sf = ScenarioText.SpeedFactor(init.telegraph);
        double vTo = ShipDynamicsModel.SteadySpeed(to, sf) / ShipParameters.KnotsToMs;
        double vFrom = from != null ? ShipDynamicsModel.SteadySpeed(from, sf) / ShipParameters.KnotsToMs : 0.0;
        if (vFrom > 0.1 && vTo > 0.0) return init.speedKnots * vTo / vFrom;
        return Math.Min(init.speedKnots, to.serviceSpeedKnots);
    }
}
