// ============================================================
// TugJoystickIndicator.cs — Табличка у рукоятки джойстика своего буксира (запрос 19.4а-4)
//
// По проверке в шлеме: «не понимаю, с какой скоростью иду и где задержать рукоятку». Подсвеченная табличка
// 14 × 10 см на поверхности пульта рядом с рукояткой (строит OwnTugDrive, Setup не нужен), лицом к оператору:
//   • схема отклонения — крест «вперёд-назад / ПБ-ЛБ» с рисками ступеней (25 / 50 / 75 / 100 % упора) и точка —
//     куда ведёт рука (белая) и ступень упора (жёлтая); под крестом — шкала разворота с рисками и меткой;
//   • строки: «ХОД вперёд малый 50 %», «ЛАГОМ 0», «РАЗВОРОТ нос вправо 25 %» (TugJoystick — доли упора);
//   • движение буксира: «вперёд 5,8 уз · лагом 0,3 уз на ПБ · 1,2 °/с вправо» (TugJoystick.MotionTitle) и автомат курса.
// Обновление 10 раз в секунду. Видна во всех режимах тренажёра (приборы буксира, не подсказка).
// Временно — до мониторов на пульте (запрос 16): тогда строки переедут туда, табличка отключится (enabled).
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TugJoystickIndicator : MonoBehaviour
{
    [Tooltip("Обновление строк, Гц")]
    public float rateHz = 10f;

    private const float W = 700f, H = 500f, Scale = 0.0002f;   // 14 × 10 см
    private const float CrossSize = 300f, YawBarW = 300f;
    private static readonly Color Bg = new Color(0.03f, 0.06f, 0.09f, 0.95f), Line = new Color(0.45f, 0.55f, 0.62f, 1f),
                                  Tick = new Color(0.65f, 0.75f, 0.8f, 1f), HandColor = new Color(1f, 1f, 1f, 0.9f),
                                  StepColor = new Color(1f, 0.8f, 0.2f, 1f), TextColor = new Color(0.85f, 0.95f, 1f, 1f);

    private OwnTugDrive drive;
    private RectTransform crossArea, rawDot, stepDot, yawRaw, yawStep;
    private TextMeshProUGUI axes, motion;
    private float nextText;

    /// <summary>Построить табличку: parent — рукоятка (TugJoystick); место и поворот задаёт вызывающий.</summary>
    public static TugJoystickIndicator Build(Transform parent, OwnTugDrive drive)
    {
        var go = new GameObject("TugJoystickIndicator", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var ind = go.AddComponent<TugJoystickIndicator>();
        ind.drive = drive;
        ind.Construct();
        return ind;
    }

    private void Construct()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(W, H);
        transform.localScale = Vector3.one * Scale;
        Img("Background", transform, Vector2.zero, new Vector2(W, H), Bg);

        // Крест хода и лагом: слева, с рисками ступеней
        crossArea = Img("Cross", transform, new Vector2(-W / 2f + 20f + CrossSize / 2f, 40f), new Vector2(CrossSize, CrossSize), new Color(0f, 0f, 0f, 0.25f));
        Img("AxisV", crossArea, Vector2.zero, new Vector2(3f, CrossSize), Line);
        Img("AxisH", crossArea, Vector2.zero, new Vector2(CrossSize, 3f), Line);
        for (int k = -TugJoystick.StepCount; k <= TugJoystick.StepCount; k++)
        {
            if (k == 0) continue;
            float p = Pos(TugJoystick.StepPosition(k), CrossSize);
            float len = System.Math.Abs(k) == TugJoystick.StepCount ? 30f : 18f;
            Img("TickV" + k, crossArea, new Vector2(0f, p), new Vector2(len, 3f), Tick);
            Img("TickH" + k, crossArea, new Vector2(p, 0f), new Vector2(3f, len), Tick);
        }
        Label(crossArea, "ВПЕРЁД", new Vector2(0f, CrossSize / 2f + 14f), 22f);
        Label(crossArea, "НАЗАД", new Vector2(0f, -CrossSize / 2f - 14f), 22f);
        Label(crossArea, "ЛБ", new Vector2(-CrossSize / 2f - 22f, 0f), 22f);
        Label(crossArea, "ПБ", new Vector2(CrossSize / 2f + 22f, 0f), 22f);
        stepDot = Img("Step", crossArea, Vector2.zero, new Vector2(26f, 26f), StepColor);
        rawDot = Img("Hand", crossArea, Vector2.zero, new Vector2(12f, 12f), HandColor);

        // Шкала разворота под крестом
        var yaw = Img("Yaw", transform, new Vector2(-W / 2f + 20f + CrossSize / 2f, -H / 2f + 40f), new Vector2(YawBarW, 6f), Line);
        for (int k = -TugJoystick.StepCount; k <= TugJoystick.StepCount; k++)
            Img("YawTick" + k, yaw, new Vector2(Pos(TugJoystick.StepPosition(k), YawBarW), 0f), new Vector2(3f, k == 0 ? 30f : 18f), Tick);
        Label(yaw, "нос влево", new Vector2(-YawBarW / 2f + 50f, -24f), 18f);
        Label(yaw, "нос вправо", new Vector2(YawBarW / 2f - 50f, -24f), 18f);
        yawStep = Img("YawStep", yaw, Vector2.zero, new Vector2(10f, 34f), StepColor);
        yawRaw = Img("YawHand", yaw, Vector2.zero, new Vector2(5f, 22f), HandColor);

        // Строки справа
        float textX = -W / 2f + 40f + CrossSize + 30f, textW = W / 2f - textX - 16f;
        axes = Text("Axes", new Vector2(textX + textW / 2f, 80f), new Vector2(textW, 300f), 30f);
        motion = Text("Motion", new Vector2(textX + textW / 2f, -150f), new Vector2(textW, 160f), 25f);
    }

    // Положение рукоятки (−1…1) → координата на схеме размера size
    private static float Pos(double v, float size) => (float)(v * size / 2.0);

    void Update()
    {
        if (drive == null || !drive.Active) return;
        TugJoystickHandle h = drive.Handle;
        JoystickState step = drive.State, raw = h != null ? h.Raw : step;
        stepDot.anchoredPosition = new Vector2(Pos(step.Sway, CrossSize), Pos(step.Surge, CrossSize));
        rawDot.anchoredPosition = new Vector2(Pos(raw.Sway, CrossSize), Pos(raw.Surge, CrossSize));
        yawStep.anchoredPosition = new Vector2(Pos(step.Yaw, YawBarW), 0f);
        yawRaw.anchoredPosition = new Vector2(Pos(raw.Yaw, YawBarW), 0f);

        if (Time.unscaledTime < nextText) return;
        nextText = Time.unscaledTime + 1f / Mathf.Max(1f, rateHz);
        axes.text = $"<b>ХОД</b> {TugJoystick.SurgeTitle(step.Surge)}\n<b>ЛАГОМ</b> {TugJoystick.SwayTitle(step.Sway)}\n<b>РАЗВОРОТ</b> {TugJoystick.YawTitle(step.Yaw)}" +
                    $"\n<size=75%>рукоятка: {(h != null && !h.detents ? "плавно" : "ступени")}</size>";
        ShipNavigation nav = drive.navigation;
        ShipDynamicsModel m = nav != null ? nav.Model : null;
        string course = drive.Drive != null && drive.Drive.HeadingHold
            ? double.IsNaN(drive.Drive.HoldHeadingDeg) ? "автомат курса: разворот" : $"автомат курса {((drive.Drive.HoldHeadingDeg % 360) + 360) % 360:000}°"
            : "автомат курса выкл";
        motion.text = m != null ? $"{TugJoystick.MotionTitle(m.U, m.V, m.R).Replace(" · ", "\n")}\n<size=80%>{course}</size>" : "";
    }

    private RectTransform Img(string name, Transform parent, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        var r = (RectTransform)go.transform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return r;
    }

    private void Label(Transform parent, string text, Vector2 pos, float size)
    {
        TextMeshProUGUI t = Text(text, Vector2.zero, new Vector2(160f, 30f), size, parent);
        ((RectTransform)t.transform).anchoredPosition = pos;
        t.text = text;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Tick;
    }

    private TextMeshProUGUI Text(string name, Vector2 pos, Vector2 size, float fontSize, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent != null ? parent : transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.color = TextColor;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        var r = (RectTransform)go.transform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return t;
    }
}
