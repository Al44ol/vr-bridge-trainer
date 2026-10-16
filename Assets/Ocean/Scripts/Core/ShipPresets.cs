// ============================================================
// ShipPresets.cs — Наборы параметров судов, заданные кодом (запрос 15)
//
// Быстроходный катер — тестовое судно для отладки сценариев и демонстрации:
// маршрут B2-01 на 35 уз проходится примерно за 9 мин (на модели 45 м, 15 уз, — около 20).
// Прототипа нет: все значения — допущения для открытого катера с центральной
// рулевой стойкой (L ≈ 18 м), источники и оговорки — ShipParameters_Sources.md.
// Модель движения та же (Номото 1-го порядка): глиссирование, дифферент на ходу
// и крен на циркуляции не моделируются (ThesisNotes, раздел 15).
// Единственный источник значений: их берут Setup Fast Boat (ассет) и тесты.
// ============================================================
public static class ShipPresets
{
    public const string FastBoatAssetName = "ShipParameters_FastBoat";

    /// <summary>Заполнить параметры быстроходного катера (периоды качки — по формулам).</summary>
    public static void FillFastBoat(ShipParameters p)
    {
        p.length = 18f;                    // м
        p.serviceSpeedKnots = 35f;         // уз; Fn = V/√(gL) ≈ 1,36 — режим глиссирования
        p.accelerationTimeConstant = 10f;  // с: 95 % хода за ≈ 3τ = 30 с
        p.decelerationTimeConstant = 15f;  // с: катер гасит ход быстро (сопротивление растёт с V)
        p.asternFactor = 0.4f;
        p.nomotoKPrime = 1.0f;             // D(35°) = 2L/(K'·δ) ≈ 59 м ≈ 3,3 L
        p.nomotoTPrime = 1.2f;             // T = T'·L/U ≈ 1,2 с на полном ходу
        p.minCalcSpeed = 0.05f;
        p.yawModel = YawModel.Nomoto;
        p.norrbinAlphaPrime = 0f;
        p.turnSpeedLossCoeff = 0f;
        p.maxRudderAngle = 35f;
        p.rudderRate = 10f;                // °/с: гидравлика, борт на борт ≈ 7 с
        p.beam = 4.8f;                     // м
        p.draft = 1.1f;                    // м
        p.metacentricHeight = 1.2f;        // м
        p.blockCoefficient = 0.42f;
        p.waterplaneCoefficient = 0.72f;
        p.rollDamping = 0.15f;             // скулы и брызгоотбойники гасят крен сильнее
        p.heaveDamping = 0.3f;
        p.pitchDamping = 0.3f;
        p.EstimateMotionPeriods();
    }

    /// <summary>
    /// 19.3а: малый портовый буксир с двумя азимутальными колонками в корме (ASD) — единственный источник значений.
    /// Размерения и тяга — допущение для работы с судном 45 м (Model45m, Δ ≈ 720 т): буксир 22 × 8 м, осадка 3,5 м
    /// (с колонками), тяга на гаке 20 т (TugSpec.DefaultBollardPullTonnes; у буксиров этого размера по каталогам верфей
    /// 15–30 т). Руля нет: курс и ход задают колонки (TugVessel), телеграф стоит на «Стоп», закон телеграфа даёт
    /// сопротивление m₁₁·u/τ: τ подобрано так, чтобы при полной тяге установился ход ≈ 11 уз (T·τ/m₁₁).
    /// </summary>
    public static void FillTug(ShipParameters p)
    {
        p.length = 22f;                    // м
        p.serviceSpeedKnots = 11f;         // уз — свободный ход (справочно; закон телеграфа не используется)
        p.accelerationTimeConstant = 13f;  // с
        p.decelerationTimeConstant = 13f;  // с: m₁₁·u/τ — линейное сопротивление корпуса (допущение)
        p.asternFactor = 0.9f;
        p.nomotoKPrime = 1.5f;
        p.nomotoTPrime = 1.0f;
        p.minCalcSpeed = 0.05f;
        p.yawModel = YawModel.Nomoto;
        p.maxRudderAngle = 35f;
        p.rudderRate = 10f;
        p.beam = 8f;                       // м
        p.draft = 3.5f;                    // м (с колонками)
        p.metacentricHeight = 1.5f;
        p.blockCoefficient = 0.5f;
        p.waterplaneCoefficient = 0.8f;
        p.motionModel = MotionModel.Manoeuvring3Dof;
        p.matchTurningCircle = false;      // руля нет — циркуляция рулём не подбирается
        p.propellerSideForceFactor = 0f;   // поперечную силу дают колонки
        p.diagonalAddedMass = true;        // B/L = 0,36…0,4 — перекрёстные члены Clarke вне области применимости (матрица масс неположительна)
        p.fitYawToNomoto = false;          // руля нет: k_N = 1, Y'_δ = 0
        p.EstimateMotionPeriods();
    }
}
