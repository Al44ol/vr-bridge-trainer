// ============================================================
// BridgeNavDisplay.cs — Временная индикация навигации на мостике (A2)
//
// World-space панель: курс, скорость, угловая скорость, руль заданный /
// фактический, положение телеграфа и шкала руля ±35° с двумя метками —
// заданный угол (белая) и фактический (зелёная ПБ / красная ЛБ):
// видно отставание рулевой машины.
// Панель строит Editor-скрипт Tools/VR Bridge/Setup A2 Navigation.
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

    private static readonly Color StarboardColor = new Color(0.2f, 0.85f, 0.35f);
    private static readonly Color PortColor = new Color(0.9f, 0.25f, 0.25f);
    private float nextTextTime;

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
                $"ПОВОРОТ     {navigation.RateOfTurn,6:+0;-0;0} °/мин\n" +
                $"РУЛЬ  зад.  {Rudder(navigation.RudderOrdered)}\n" +
                $"      факт. {Rudder(navigation.RudderActual)}\n" +
                $"ТЕЛЕГРАФ    {TelegraphName(navigation.TelegraphPosition)}" +
                (navigation.CirculationTestActive ? "\n<color=#FFD54F>ТЕСТ ЦИРКУЛЯЦИИ</color>" : "");
        }
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
