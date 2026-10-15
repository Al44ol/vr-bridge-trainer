// ============================================================
// BridgeNavDisplay.cs — Временная индикация навигации на мостике (A2)
//
// World-space панель: курс, скорость, угловая скорость, руль заданный /
// фактический, положение телеграфа и шкала руля ±35° с двумя метками —
// заданный угол (белая) и фактический (зелёная ПБ / красная ЛБ):
// видно отставание рулевой машины.
// Панель строит Editor-скрипт Tools/VR Bridge/Setup A2 Navigation.
// B1: полоса над панелью (Setup B1 Water Area) — глубина по карте и запас
// под килем; при опасном приближении мигает «ОПАСНО: МЕЛЬ … кбт», на мели —
// «НА МЕЛИ» (сигнализация глубины, как у эхолота / планшета).
// D1: при вводной инструктора и включённом оповещении — строка «ОТКАЗ ГД / ОТКАЗ РУЛЯ».
// 19.2б-2: у причала (ближе 300 м) — две строки: расстояние носа и кормы до кромки, их скорость к причалу, угол.
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BridgeNavDisplay : MonoBehaviour
{
    public ShipNavigation navigation;

    [Header("Элементы панели")]
    public TextMeshProUGUI valuesText;
    public RectTransform rudderScale;      // полоса шкалы, ширина = ±maxRudder
    public RectTransform orderedMarker;
    public RectTransform actualMarker;
    public Image actualMarkerImage;

    [Tooltip("Обновление текста, раз в секунду")]
    public float textRate = 10f;

    [Header("Глубина и сигнализация мели (B1)")]
    public GroundingMonitor grounding;
    public TextMeshProUGUI alarmText;
    public Image alarmBackground;

    private static readonly Color AlarmIdle = new Color(0.05f, 0.07f, 0.09f, 0.85f);
    private static readonly Color AlarmRed = new Color(0.75f, 0.08f, 0.06f, 0.95f);

    private static readonly Color StarboardColor = new Color(0.2f, 0.85f, 0.35f);
    private static readonly Color PortColor = new Color(0.9f, 0.25f, 0.25f);
    private float nextTextTime, nextAlarmTime;

    void Update()
    {
        if (navigation == null)
            return;

        float maxRudder = navigation.parameters != null ? navigation.parameters.maxRudderAngle : 35f;
        if (rudderScale != null)
        {
            float half = rudderScale.rect.width * 0.5f;
            SetMarker(orderedMarker, navigation.RudderOrdered / maxRudder * half);
            SetMarker(actualMarker, navigation.RudderActual / maxRudder * half);
        }
        if (actualMarkerImage != null)
            actualMarkerImage.color = navigation.RudderActual >= 0f ? StarboardColor : PortColor;

        if (valuesText != null && Time.unscaledTime >= nextTextTime)
        {
            nextTextTime = Time.unscaledTime + 1f / Mathf.Max(1f, textRate);
            float hdg = Mathf.Repeat(navigation.Heading, 360f);
            valuesText.text =
                $"КУРС        {hdg:000.0}°\n" +
                $"СКОРОСТЬ    {navigation.SpeedKnots,5:F1} уз\n" +
                LagLine() +
                $"ПОВОРОТ     {navigation.RateOfTurn,6:+0;-0;0} °/мин\n" +
                $"РУЛЬ  зад.  {Rudder(navigation.RudderOrdered)}\n" +
                $"      факт. {Rudder(navigation.RudderActual)}\n" +
                $"ТЕЛЕГРАФ    {TelegraphName(navigation.TelegraphPosition)}" +
                (navigation.CirculationTestActive ? "\n<color=#FFD54F>ТЕСТ ЦИРКУЛЯЦИИ</color>" : "") +
                BerthLine() +
                FailureLine();
        }

        UpdateAlarm();
    }

    // 19.2б-2: строка причала — ближе BerthReadout.ShowWithinM (300 м) к кромке, во всех режимах (прибор, не подсказка):
    // расстояние от борта до кромки у носа (плечо, 0,25·L) и у кормы, их скорость к причалу, угол к кромке
    private string BerthLine()
    {
        MooringController mc = MooringController.Instance;
        WaterArea area = mc != null ? mc.Area : null;
        if (area == null || navigation.Model == null || navigation.parameters == null) return "";
        int i = area.NearestBerth(navigation.PositionEastNorth, out double d);
        if (i < 0 || d > BerthReadout.ShowWithinM) return "";
        BerthReadout r = BerthReadout.Compute(area.berths[i], navigation.parameters, navigation.Model);
        string note = navigation.Uses3Dof ? "" : "\n  <color=#FFD54F>силы причала — только в модели 3 СС</color>";
        return $"\n<size=85%>{r.Format()}{note}</size>";
    }

    // Запрос 19.1: поперечная скорость (как у доплеровского лага) — только в модели 3 СС
    private string LagLine()
    {
        if (!navigation.Uses3Dof) return "";
        float v = navigation.SwayKnots;
        string side = Mathf.Abs(v) < 0.05f ? "" : v > 0f ? " ПБ" : " ЛБ";
        return $"  лагом     {Mathf.Abs(v),5:F2} уз{side}\n";
    }

    private void UpdateAlarm()
    {
        if (alarmText == null || grounding == null || Time.unscaledTime < nextAlarmTime) return;
        nextAlarmTime = Time.unscaledTime + 1f / Mathf.Max(1f, textRate);
        bool blinkOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
        string ack = grounding.AlarmAcknowledged ? "  <size=70%>КВИТ.</size>" : "";
        switch (grounding.State)
        {
            case GroundingState.Grounded:
            {
                HazardInfo h = grounding.GroundedOn;
                alarmText.text = $"<b>НА МЕЛИ</b>{ack}\n<size=70%>{Hazard(h)}</size>";
                alarmText.color = Color.white;
                if (alarmBackground != null) alarmBackground.color = AlarmRed;
                break;
            }
            case GroundingState.Warning:
            {
                HazardInfo h = grounding.Nearest;
                double cables = h.Valid ? h.Distance / WaterArea.CableMeters : 0.0;
                alarmText.text = $"<b>ОПАСНО: МЕЛЬ {cables:F1} кбт</b>{ack}\n<size=70%>{Hazard(h)}</size>";
                alarmText.color = Color.white;
                if (alarmBackground != null) alarmBackground.color = blinkOn || grounding.AlarmAcknowledged ? AlarmRed : AlarmIdle;
                break;
            }
            default:
            {
                float depth = grounding.DepthUnderShip;
                alarmText.text = float.IsNaN(depth)
                    ? "ГЛУБИНА —"
                    : $"ГЛУБИНА {depth:F0} м\n<size=70%>под килем {depth - grounding.Draft:F1} м</size>";
                alarmText.color = new Color(0.75f, 0.85f, 0.95f);
                if (alarmBackground != null) alarmBackground.color = AlarmIdle;
                break;
            }
        }
    }

    // D1: вводная инструктора (если оповещение включено) — мигающая строка «ОТКАЗ …»
    private static string FailureLine()
    {
        string line = InstructorInputs.Instance != null ? InstructorInputs.Instance.FailureLine : "";
        if (line.Length == 0) return "";
        bool blinkOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
        return $"\n<color={(blinkOn ? "#FF5A4A" : "#8A2A22")}><b>{line}</b></color>";
    }

    private static string Hazard(HazardInfo h)
    {
        if (!h.Valid) return "";
        return h.IsLand ? $"«{h.Name}» — берег" : $"«{h.Name}», глубина {h.Depth:F1} м";
    }

    private static void SetMarker(RectTransform marker, float x)
    {
        if (marker == null) return;
        Vector2 p = marker.anchoredPosition;
        p.x = x;
        marker.anchoredPosition = p;
    }

    private static string Rudder(float deg)
    {
        if (Mathf.Abs(deg) < 0.5f) return " 0°";
        return $"{Mathf.Abs(deg),2:F0}° {(deg > 0f ? "ПБ" : "ЛБ")}";
    }

    private static string TelegraphName(string name)
    {
        string n = name.ToLowerInvariant();
        string dir = n.Contains("astern") ? " НАЗАД" : n.Contains("ahead") ? " ВПЕРЁД" : "";
        if (n.Contains("stop"))      return "СТОП";
        if (n.Contains("full"))      return "ПОЛНЫЙ" + dir;
        if (n.Contains("half"))      return "СРЕДНИЙ" + dir;
        if (n.Contains("dead slow")) return "САМЫЙ МАЛЫЙ" + dir;
        if (n.Contains("slow"))      return "МАЛЫЙ" + dir;
        return name;
    }
}
