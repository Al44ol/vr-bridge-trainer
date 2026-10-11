// ============================================================
// TrafficDefinition.cs — Другое судно в сценарии (B3)
//
// Заполняется в ScenarioDefinition.traffic (B2 — заготовка: name, parameters,
// position, headingDeg, speedKnots; имена и смысл этих полей сохранены,
// поэтому прежние ассеты сценариев открываются без изменений).
//
// Начальная позиция (placement):
//   • Absolute — восток/север, м (position);
//   • RelativeToOwnShip — истинный пеленг (°) и дистанция (мили) от начальной
//     позиции своего судна;
//   • MeetingPoint — «выйти в точку встречи»: CPA (мили) и TCPA (мин) относительно
//     своего судна, которое идёт начальным курсом и скоростью; сторона — где будет
//     цель в момент CPA относительно своего судна. Позиция считается сама
//     (TrafficPlanner, формулы — в CpaCalculator.cs).
// Движение (motion):
//   • ConstantCourse — постоянный курс (headingDeg) и скорость;
//   • Route — по путевым точкам от начальной позиции (курс на первую точку);
//     в режиме MeetingPoint маршрут продолжается ПОСЛЕ точки встречи: цель идёт
//     заданным курсом до момента TCPA + 2 мин (CPA не искажается), затем — по точкам.
// Тип судна (type) — только внешний вид (корпус, надстройка, места огней);
// размеры и манёвренность — из parameters (пусто — параметры по умолчанию
// для типа из TrafficController).
// Огни — по статусу судна; реализовано только «механическое судно на ходу»
// (МППСС, правило 23), остальные — задел.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Тип другого судна — внешний вид.</summary>
public enum TrafficVesselType
{
    [InspectorName("Сухогруз")] Cargo,
    [InspectorName("Танкер")] Tanker,
    [InspectorName("Малое судно")] Small
}

/// <summary>Как задана начальная позиция цели.</summary>
public enum TrafficPlacement
{
    [InspectorName("Абсолютная позиция (E, N)")] Absolute,
    [InspectorName("Пеленг и дистанция от своего судна")] RelativeToOwnShip,
    [InspectorName("Точка встречи (CPA, TCPA)")] MeetingPoint
}

/// <summary>Движение цели.</summary>
public enum TrafficMotion
{
    [InspectorName("Постоянный курс и скорость")] ConstantCourse,
    [InspectorName("По маршруту")] Route
}

/// <summary>Где будет цель относительно своего судна в момент CPA.</summary>
public enum MeetingSide
{
    [InspectorName("По правому борту")] Starboard,
    [InspectorName("По левому борту")] Port,
    [InspectorName("По носу (пересекает курс впереди)")] Ahead,
    [InspectorName("По корме (пересекает курс позади)")] Astern
}

/// <summary>
/// Статус судна для огней по МППСС. Реализовано только PowerDrivenUnderway (правило 23);
/// остальные — задел (сейчас показываются как механическое судно на ходу).
/// </summary>
public enum VesselLightStatus
{
    [InspectorName("Механическое судно на ходу (пр. 23)")] PowerDrivenUnderway,
    [InspectorName("На якоре (пр. 30) — задел")] AtAnchor,
    [InspectorName("Лишённое возможности управляться (пр. 27а) — задел")] NotUnderCommand,
    [InspectorName("Ограниченное в возможности маневрировать (пр. 27б) — задел")] RestrictedManoeuvrability,
    [InspectorName("Стеснённое своей осадкой (пр. 28) — задел")] ConstrainedByDraught,
    [InspectorName("На мели (пр. 30г) — задел")] Aground
}

/// <summary>Другое судно (B3).</summary>
[Serializable]
public class TrafficShipDefinition
{
    [Tooltip("Идентификатор для записи (targets.csv, события); пусто — T1, T2, …")]
    public string id = "";
    public string name = "Судно";
    [Tooltip("Внешний вид; размеры — из параметров")]
    public TrafficVesselType type = TrafficVesselType.Cargo;
    [Tooltip("Пусто — параметры по умолчанию для типа (TrafficController)")]
    public ShipParameters parameters;

    [Header("Начальная позиция")]
    public TrafficPlacement placement = TrafficPlacement.Absolute;
    [Tooltip("Абсолютная позиция: восток, север, м")]
    public WorldPosition position;
    [Tooltip("От своего судна: истинный пеленг на цель, °")]
    public float bearingDeg;
    [Tooltip("От своего судна: дистанция, мили")]
    public float distanceNm = 3f;
    [Tooltip("Точка встречи: дистанция кратчайшего сближения CPA, мили (0 — курс столкновения)")]
    public float cpaNm = 0.5f;
    [Tooltip("Точка встречи: время до кратчайшего сближения TCPA, мин")]
    public float tcpaMinutes = 12f;
    [Tooltip("Точка встречи: где будет цель в момент CPA относительно своего судна")]
    public MeetingSide meetingSide = MeetingSide.Starboard;

    [Header("Движение")]
    [Tooltip("Курс, ° (при движении по маршруту — курс на первую точку считается сам)")]
    public float headingDeg;
    [Tooltip("Скорость, уз (не больше эксплуатационной)")]
    public float speedKnots = 10f;
    public TrafficMotion motion = TrafficMotion.ConstantCourse;
    [Tooltip("Путевые точки (от начальной позиции; в режиме «точка встречи» — после неё)")]
    public List<WorldPosition> route = new List<WorldPosition>();

    [Header("Огни")]
    public VesselLightStatus lights = VesselLightStatus.PowerDrivenUnderway;

    [Header("Оценка (C2)")]
    [Tooltip("Допустимая дистанция расхождения CPA_доп, мили; 0 — по набору критериев (по умолчанию 0,5). Опасность столкновения — CPA < k·CPA_доп")]
    public float requiredCpaNm;

    /// <summary>Идентификатор цели: заданный или T{номер}.</summary>
    public string TargetId(int index) => string.IsNullOrWhiteSpace(id) ? $"T{index + 1}" : id.Trim();

    public TrafficShipDefinition() { }

    public TrafficShipDefinition Clone()
    {
        var c = (TrafficShipDefinition)MemberwiseClone();
        c.route = route != null ? new List<WorldPosition>(route) : new List<WorldPosition>();
        return c;
    }
}
