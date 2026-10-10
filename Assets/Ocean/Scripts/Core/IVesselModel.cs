// ============================================================
// IVesselModel.cs — Общий интерфейс модели судна для стенда испытаний (A3.2)
//
// Стенд ManoeuvringTrials прогоняет одни и те же манёвры и считает одни и те же
// показатели для любой модели: тренажёрной ShipDynamicsModel и эталонной
// MarinerModel (калибровка). Так определения выдвига, тактического диаметра,
// углов зарыскивания и т. д. гарантированно совпадают у эталона и модели.
//
// Обозначения как в ShipDynamicsModel: ψ — курс (рад, 0 = N, по часовой),
// r — рад/с (> 0 — вправо), δ — градусы (> 0 — правый борт).
// ============================================================

public interface IVesselModel
{
    /// <summary>Курс, рад.</summary>
    double Psi { get; }
    /// <summary>Угловая скорость, рад/с.</summary>
    double R { get; }
    /// <summary>Скорость судна, м/с (у ShipDynamicsModel — со знаком, у эталона — модуль скорости).</summary>
    double U { get; }
    /// <summary>Фактический угол руля, градусы.</summary>
    double Delta { get; }
    /// <summary>Заданный угол руля после ограничения, градусы.</summary>
    double Ordered { get; }
    /// <summary>Позиция: восток, север, м.</summary>
    double East { get; }
    double North { get; }
    /// <summary>Длина судна L, м.</summary>
    double Length { get; }
    /// <summary>Команда руля, градусы (> 0 — правый борт).</summary>
    double RudderCommandDeg { get; set; }
    /// <summary>Положение телеграфа SF, −1..1 (у эталона с постоянными оборотами не используется).</summary>
    double SpeedFactor { get; set; }
    /// <summary>Шаг интегрирования, с.</summary>
    void Step(double dt);
}
