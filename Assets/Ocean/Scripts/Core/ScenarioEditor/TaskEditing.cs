// ============================================================
// TaskEditing.cs — Блоки задания сценария в редакторе: швартовка, буксир по вызову, работа буксира (запрос 21.5б)
//
// Модель и тренажёр — запрос 19 (MooringTaskDefinition, tugAvailable, TugWorkDefinition); здесь — только правка в редакторе.
// Правятся: причал (номер в WaterArea.berths), контрольное время; у работы буксира — клиент (tugWorkClient), зазор клиента
// и сдвиг вдоль причала, дистанция буксира, «случайное событие». Пороги этапов (зазоры, скорости, удержание, диапазоны
// события) — методика, не правятся (решение пользователя 22.10.2026).
//   • SetMooring / SetTugWork — включить / выключить блок: модель 3 СС, условия завершения («ошвартован» / «задание буксира
//     выполнено» — да, «прибытие» — нет; выключили — «прибытие» вернулось), швартовка и работа буксира — не вместе;
//     работа буксира — своё судно буксир, клиент по умолчанию, причал по умолчанию.
//   • PlaceTugWork — начальные условия своего буксира и линия «Старт → Борт судна» (только для карты) по месту клиента:
//     тот же расчёт, что DemoTugWorkScenario.Fill и тренажёр (TugWorkSystem.Place → TugStart).
//   • SuggestedSet — набор критериев по заданию: работа буксира — TugWork, швартовка с буксиром — TugBerthing, швартовка — Berthing.
//   • Check — замечания: причал задания мельче осадки + 1 м или короче судна (своего / клиента).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Расстановка работы буксира на старте (21.5б): клиент у причала, свой буксир, точка касания.</summary>
public struct TugWorkLayout
{
    public bool Ok;
    public WorldPosition ClientPos, TugStart, Contact;
    public double ClientHeadingDeg, TugHeadingDeg;
    public string Problem;
}

public static class TaskEditing
{
    public const string DefaultSet = "Assessment_Default", BerthingSet = "Assessment_Berthing",
                        TugBerthingSet = "Assessment_TugBerthing", TugWorkSet = "Assessment_TugWork";
    public const string ClientDefault = "ShipParameters_Mariner";
    /// <summary>Запас под килем у причала для замечания, м.</summary>
    public const double UnderKeelM = 1.0;
    public const string StartPointName = "Старт", ContactPointName = "Борт судна";

    public static BerthArea Berth(WaterArea a, int i) => a != null && a.berths != null && i >= 0 && i < a.berths.Count ? a.berths[i] : null;
    public static int BerthCount(WaterArea a) => a?.berths != null ? a.berths.Count : 0;

    /// <summary>Следующий причал по кругу (dir = ±1); причалов нет — −1.</summary>
    public static int NextBerth(WaterArea a, int current, int dir)
    {
        int n = BerthCount(a);
        if (n == 0) return -1;
        if (current < 0 || current >= n) return dir >= 0 ? 0 : n - 1;
        return ((current + dir) % n + n) % n;
    }

    public static string BerthTitle(WaterArea a, int i)
    {
        BerthArea b = Berth(a, i);
        return b == null ? $"№ {i} — нет в акватории" : $"«{b.name}», {ScenarioText.Num(b.Length, 0)} м, глубина {ScenarioText.Num(b.depthM, 1)} м";
    }

    // Первый причал, подходящий судну (глубина ≥ осадка + запас, не короче судна), иначе первый; нет — 0
    private static int DefaultBerth(WaterArea a, ShipParameters p)
    {
        for (int i = 0; i < BerthCount(a); i++)
            if (p == null || Fits(a.berths[i], p) == null) return i;
        return 0;
    }

    /// <summary>Чем причал не подходит судну (пусто — подходит).</summary>
    public static string Fits(BerthArea b, ShipParameters p)
    {
        if (b == null || p == null) return null;
        if (b.depthM < p.draft + UnderKeelM)
            return $"глубина {ScenarioText.Num(b.depthM, 1)} м меньше осадки {ScenarioText.Num(p.draft, 1)} м + {ScenarioText.Num(UnderKeelM, 0)} м";
        if (b.Length < p.length) return $"причал {ScenarioText.Num(b.Length, 0)} м короче судна {ScenarioText.Num(p.length, 0)} м";
        return null;
    }

    private static void EndsOn(ScenarioDefinition s, ScenarioEndConditions c) => s.endConditions |= c;
    private static void EndsOff(ScenarioDefinition s, ScenarioEndConditions c) => s.endConditions &= ~c;

    // ─────────── Швартовка ───────────
    public static void SetMooring(ScenarioDefinition s, bool on)
    {
        if (s.mooring == null) s.mooring = new MooringTaskDefinition();
        s.mooring.enabled = on;
        if (on)
        {
            if (s.IsTugWork) SetTugWork(s, false, null, null);
            if (Berth(s.waterArea, s.mooring.berthIndex) == null) s.mooring.berthIndex = DefaultBerth(s.waterArea, s.shipParameters);
            s.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
            EndsOn(s, ScenarioEndConditions.Moored);
            EndsOff(s, ScenarioEndConditions.Arrival);
        }
        else
        {
            EndsOff(s, ScenarioEndConditions.Moored);
            if (!s.IsTugWork) EndsOn(s, ScenarioEndConditions.Arrival);
        }
    }

    // ─────────── Работа буксира ───────────
    /// <summary>Включить / выключить работу буксира; tug — параметры своего буксира, client — клиент по умолчанию (если не задан).</summary>
    public static void SetTugWork(ScenarioDefinition s, bool on, ShipParameters tug, ShipParameters client)
    {
        if (s.tugWork == null) s.tugWork = new TugWorkDefinition();
        s.tugWork.enabled = on;
        if (on)
        {
            if (s.IsMooring) SetMooring(s, false);
            s.ownShipType = ScenarioShipType.Tug;
            if (tug != null) s.shipParameters = tug;
            if (s.tugWorkClient == null) s.tugWorkClient = client;
            if (Berth(s.waterArea, s.tugWork.berthIndex) == null) s.tugWork.berthIndex = DefaultBerth(s.waterArea, s.tugWorkClient);
            s.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
            s.tugAvailable = false;   // своё судно — буксир, вызывать другой незачем
            EndsOn(s, ScenarioEndConditions.TugWorkDone);
            EndsOff(s, ScenarioEndConditions.Arrival);
            PlaceTugWork(s);
        }
        else
        {
            EndsOff(s, ScenarioEndConditions.TugWorkDone);
            if (!s.IsMooring) EndsOn(s, ScenarioEndConditions.Arrival);
        }
    }

    /// <summary>Клиент у причала, свой буксир и точка касания на старте — как TugWorkSystem.Place в тренажёре.</summary>
    public static TugWorkLayout Layout(ScenarioDefinition s)
    {
        var r = new TugWorkLayout { Problem = "" };
        BerthArea b = s.tugWork != null ? Berth(s.waterArea, s.tugWork.berthIndex) : null;
        if (b == null) { r.Problem = "нет причала"; return r; }
        if (s.tugWorkClient == null) { r.Problem = "не задан клиент"; return r; }
        if (s.shipParameters == null) { r.Problem = "не заданы параметры своего буксира"; return r; }
        var sys = new TugWorkSystem(s.tugWork, s.tugWorkClient, b, TugSpec.ForOwnTug(s.shipParameters));
        var own = new ShipDynamicsModel(s.shipParameters) { MotionOverride = MotionModel.Manoeuvring3Dof };
        sys.Place(own);
        r.Ok = true;
        r.ClientPos = new WorldPosition(sys.Client.Model.East, sys.Client.Model.North);
        r.ClientHeadingDeg = Deg(sys.Client.Model.Psi);
        r.TugStart = new WorldPosition(own.East, own.North);
        r.TugHeadingDeg = Deg(own.Psi);
        r.Contact = sys.ContactPoint();
        return r;
    }

    private static double Deg(double psi)
    {
        double d = psi * 180.0 / Math.PI % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    /// <summary>Начальные условия своего буксира и линия «Старт → Борт судна» по расстановке; false — не построить.</summary>
    public static bool PlaceTugWork(ScenarioDefinition s)
    {
        TugWorkLayout l = Layout(s);
        if (!l.Ok) return false;
        s.initial = new OwnShipInitialState
        {
            position = l.TugStart, headingDeg = (float)Math.Round(l.TugHeadingDeg, 2), speedKnots = 0f, telegraph = TelegraphOrder.Stop, rudderDeg = 0f
        };
        s.route = new List<RouteWaypoint>
        {
            new RouteWaypoint(StartPointName, l.TugStart, DemoBerthScenario.ApproachXteLimit),
            new RouteWaypoint(ContactPointName, l.Contact, DemoBerthScenario.ApproachXteLimit)
        };
        return true;
    }

    // ─────────── Набор критериев ───────────
    /// <summary>Набор по заданию; null — задание его не определяет (оставить как есть).</summary>
    public static string SuggestedSet(ScenarioDefinition s)
    {
        if (s.IsTugWork) return TugWorkSet;
        if (s.IsMooring) return s.tugAvailable ? TugBerthingSet : BerthingSet;
        return null;
    }

    public static AssessmentCriteria FindSet(ScenarioCatalog c, string name) =>
        c == null || name == null ? null : c.Criteria.Find(x => x != null && x.name == name);

    public static ShipParameters FindShip(ScenarioCatalog c, string name) =>
        c == null || name == null ? null : c.Ships.Find(x => x != null && x.name == name);

    // ─────────── Замечания ───────────
    public static List<string> Check(ScenarioDefinition s)
    {
        var w = new List<string>();
        if (s == null) return w;
        if (s.IsMooring)
        {
            string why = Fits(Berth(s.waterArea, s.mooring.berthIndex), s.shipParameters);
            if (why != null) w.Add($"Швартовка: причал не подходит своему судну — {why}.");
        }
        if (s.IsTugWork)
        {
            string why = Fits(Berth(s.waterArea, s.tugWork.berthIndex), s.tugWorkClient);
            if (why != null) w.Add($"Работа буксира: причал не подходит клиенту — {why}.");
            TugWorkLayout l = Layout(s);
            if (l.Ok && (s.initial == null || (s.initial.position - l.TugStart).Magnitude > 1.0))
                w.Add("Работа буксира: старт своего буксира не совпадает с расстановкой — тренажёр поставит его по начальным условиям (лист «Задание…» → «Поставить буксир по расстановке»).");
        }
        return w;
    }

    // ─────────── Брифинг ───────────
    /// <summary>Строки заготовки брифинга о задании (пусто — задания нет).</summary>
    public static string BriefingLines(ScenarioDefinition s)
    {
        var b = new System.Text.StringBuilder();
        if (s.IsMooring)
        {
            BerthArea berth = Berth(s.waterArea, s.mooring.berthIndex);
            b.Append($"Швартовка к причалу «{berth?.name ?? "?"}»: подойти лагом, остановиться у кранцев, подать концы с носа и кормы, ошвартоваться");
            if (s.mooring.targetTimeS > 0f) b.Append($"; контрольное время — {ScenarioText.Num(s.mooring.targetTimeS / 60.0, 0)} мин");
            b.Append(".\n");
        }
        if (s.IsTugWork)
        {
            BerthArea berth = Berth(s.waterArea, s.tugWork.berthIndex);
            string client = s.tugWorkClient != null ? $"судно {ScenarioText.Num(s.tugWorkClient.length, 0)} м" : "судно";
            b.Append($"Вы — на буксире. {client} стоит лагом в {ScenarioText.Num(s.tugWork.clientGapM, 0)} м от причала «{berth?.name ?? "?"}»; " +
                     "по командам лоцмана подойти к борту у миделя, прижать судно к причалу, держать, пока заводят концы, и отойти");
            if (s.tugWork.targetTimeS > 0f) b.Append($"; контрольное время — {ScenarioText.Num(s.tugWork.targetTimeS / 60.0, 0)} мин");
            b.Append(".\n");
            if (s.tugWork.angleEvent) b.Append("У причала может случиться событие: нос или корма отходит — дожимайте отошедший конец по командам лоцмана.\n");
        }
        return b.ToString();
    }
}
