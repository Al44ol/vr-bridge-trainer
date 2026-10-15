// ============================================================
// ShipControlOrders.cs — Меню «Управление» на кнопке B правого контроллера (запрос 19.2б)
//
// Команды экипажу «как рулевому» (решение пользователя): с крыла пульта нет, обучающийся
// командует голосом — в тренажёре пунктами меню; штурвал, телеграф и рычаг подруливающего
// на пульте поворачиваются сами. Переключатели «< значение >» (меню не закрывается):
//   • руль — ступени ControlOrders.RudderSteps (лево 35 / 20 / 10 / 5, прямо, право 5 … 35);
//   • машина — положения телеграфа («>» — больше вперёд);
//   • подруливающее — ЛБ 100 / 50 / 0 / ПБ 50 / 100 % («<» — на левый борт).
// Каждая команда — событие ControlOrder в шину; кроме того, руль и телеграф пишет
// BridgeActionMonitor (HelmStarted/Ended, TelegraphChanged — как действие обучающегося,
// EngineTelegraph.SetSpeedFactor(sf, fromCode: false)), подруливающее — ThrusterChanged.
// Экземпляр BridgeMenu (menuId = MenuId, кнопка B) и этот компонент ставит Setup 19.2 Berth
// на Ship/ControlMenu. Часть 2 добавит группы «Концы» и «Переход».
// ============================================================
using UnityEngine;

public class ShipControlOrders : MonoBehaviour
{
    public const string MenuId = "Управление";
    public const string HelmGroup = "Руль, машина, подруливающее";
    /// <summary>Кнопка B правого контроллера (до D1 — панель погоды PanelToggle при controllerButton).</summary>
    public const string ToggleBinding = "<XRController>{RightHand}/secondaryButton";

    [Header("Сцена (пусто — найти)")]
    public ShipNavigation navigation;
    public MooringController mooring;

    private readonly System.Collections.Generic.List<BridgeMenuCommand> commands = new System.Collections.Generic.List<BridgeMenuCommand>();

    private EngineTelegraph Telegraph => navigation != null ? navigation.telegraph : null;

    void Awake()
    {
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (mooring == null) mooring = FindAnyObjectByType<MooringController>();
    }

    void OnEnable()
    {
        commands.Add(BridgeMenu.RegisterStepper(HelmGroup, RudderTitle, () => StepRudder(-1), () => StepRudder(+1),
            () => navigation != null && navigation.Model != null && navigation.wheel != null).InMenu(MenuId));
        commands.Add(BridgeMenu.RegisterStepper(HelmGroup, TelegraphTitle, () => StepTelegraph(-1), () => StepTelegraph(+1),
            () => Telegraph != null).InMenu(MenuId));
        commands.Add(BridgeMenu.RegisterStepper(HelmGroup, ThrusterTitle, () => StepThruster(-1), () => StepThruster(+1),
            () => mooring != null).InMenu(MenuId));
        BridgeMenu.SetGroupExpanded(HelmGroup, true);
        BridgeMenu.SetGroupOrder(HelmGroup, 10);
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in commands) BridgeMenu.Unregister(c);
        commands.Clear();
    }

    // ─────────── Руль ───────────

    private string RudderTitle()
    {
        if (navigation == null || navigation.Model == null) return "Руль: —";
        double ordered = navigation.RudderOrdered, actual = navigation.RudderActual;
        string fact = Mathf.Abs((float)(actual - ordered)) >= 1f ? $" · факт {ControlOrders.RudderTitle(System.Math.Round(actual))}" : "";
        return $"Руль: {ControlOrders.RudderTitle(System.Math.Round(ordered))}{fact}";
    }

    // dir < 0 — влево (на левый борт), > 0 — вправо
    private void StepRudder(int dir)
    {
        if (navigation == null || navigation.Model == null) return;
        double old = navigation.RudderOrdered;
        int i = ControlOrders.NearestRudderStep(old);
        double cur = ControlOrders.RudderSteps[i];
        // Руль между ступенями (поставлен штурвалом) — первая команда доводит до ближайшей в нужную сторону
        if (dir > 0 && cur <= old + 0.25 || dir < 0 && cur >= old - 0.25) i += dir;
        i = Mathf.Clamp(i, 0, ControlOrders.RudderSteps.Length - 1);
        double target = ControlOrders.RudderSteps[i];
        if (System.Math.Abs(target - old) < 0.05) return;
        navigation.SetRudderOrder((float)target);
        PublishOrder(ControlOrders.Rudder, target, old);
    }

    // ─────────── Машина ───────────

    private string TelegraphTitle()
    {
        EngineTelegraph t = Telegraph;
        return t == null ? "Машина: —" : $"Машина: {ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(t.SpeedFactor))}";
    }

    // dir > 0 — больше вперёд (в positions «Полный вперёд» — первый)
    private void StepTelegraph(int dir)
    {
        EngineTelegraph t = Telegraph;
        if (t == null || t.positions == null || t.positions.Length == 0) return;
        int i = Mathf.Clamp(t.PositionIndex - dir, 0, t.positions.Length - 1);
        if (i == t.PositionIndex) return;
        float old = t.SpeedFactor;
        t.SetSpeedFactor(t.positions[i].speedFactor, false);
        PublishOrder(ControlOrders.Telegraph, t.SpeedFactor, old);
    }

    // ─────────── Подруливающее ───────────

    private string ThrusterTitle()
    {
        if (mooring == null) return "Подруливающее: —";
        string state = mooring.Operating
            ? (System.Math.Abs(mooring.ThrusterActual - ThrusterOrders.Command(mooring.ThrusterIndex)) >= 0.05
                ? $" · факт {ThrusterOrders.TitleOfPercent(System.Math.Round(mooring.ThrusterActual * 20.0) * 5.0)}" : "")
            : " <size=70%>(только 3 СС)</size>";
        return $"Подруливающее: {ThrusterOrders.Title(mooring.ThrusterIndex)}{state}";
    }

    // dir < 0 — на левый борт, > 0 — на правый
    private void StepThruster(int dir)
    {
        if (mooring == null) return;
        int old = mooring.ThrusterIndex, i = ThrusterOrders.Clamp(old + dir);
        if (i == old) return;
        mooring.SetThrusterIndex(i);
        PublishOrder(ControlOrders.Thruster, ThrusterOrders.Command(i) * 100.0, ThrusterOrders.Command(old) * 100.0);
    }

    private void PublishOrder(string subject, double value, double old)
    {
        var e = new ExerciseEvent(ExerciseEventType.ControlOrder) { Subject = subject, Value = value, Value2 = old };
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        Debug.Log("[Управление] " + e.Describe(), this);
        ExerciseEvents.Publish(e);
    }
}
