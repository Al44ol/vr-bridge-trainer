// ============================================================
// StartMenu.cs — Начальное меню на мониторе: «Обучение» / «Редактор сценариев» / «Выход» (запрос 21.2а)
//
// Поверх окна инструктора при первом запуске сборки на ПК (TrainerScenes.ShowStartMenu): тренажёр уже
// загружен под меню — «Обучение» просто закрывает меню. В шлеме в это время виден мостик.
// В Unity — только если включён пункт Tools/VR Bridge/Start Menu in Play.
// ============================================================
using TMPro;
using UnityEngine;

public class StartMenu : MonoBehaviour
{
    private Canvas canvas;
    private readonly MonitorUi ui = new MonitorUi();

    internal static void Ensure()
    {
        if (!TrainerScenes.ShowStartMenu || FindAnyObjectByType<StartMenu>() != null || FindAnyObjectByType<ScenarioRunner>() == null) return;
        new GameObject("StartMenu").AddComponent<StartMenu>();
    }

    void Start()
    {
        canvas = MonitorUi.Canvas(transform, "StartMenuCanvas", 500);
        RectTransform shade = MonitorUi.Panel(canvas.transform, "Shade", new Color(0.03f, 0.04f, 0.06f, 0.96f));
        MonitorUi.Stretch(shade, 0f, 0f, 0f, 0f);

        RectTransform box = MonitorUi.Panel(shade, "Box", MonitorUi.PanelColor);
        MonitorUi.Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 470f), new Vector2(0.5f, 0.5f));
        TextMeshProUGUI title = MonitorUi.Text(box, "VR-тренажёр судоводительского мостика", 34f, MonitorUi.TitleColor, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(title.rectTransform, 30f, -30f, 840f, 50f);
        TextMeshProUGUI hint = MonitorUi.Text(box, "Выберите режим. В шлеме сейчас — мостик; обучающий может надеть шлем после выбора «Обучение».",
                                              18f, MonitorUi.MutedColor, TextAlignmentOptions.Center);
        MonitorUi.TopLeft(hint.rectTransform, 30f, -90f, 840f, 50f);

        float x = 60f;
        ui.Button(box, ref x, -170f, 380f, "Обучение", MonitorUi.StartColor, Close, height: 120f, fontSize: 30f);
        x += 20f;
        ui.Button(box, ref x, -170f, 380f, "Редактор сценариев", MonitorUi.ButtonColor, TrainerScenes.OpenEditor, height: 120f, fontSize: 30f);
        x = 330f;
        ui.Button(box, ref x, -330f, 240f, "Выход", MonitorUi.NeutralColor, TrainerScenes.Quit, height: 56f, fontSize: 22f);
        TextMeshProUGUI keys = MonitorUi.Text(box, "Сразу в тренажёр без меню — ярлык с ключом -training", 15f, MonitorUi.MutedColor, TextAlignmentOptions.Center);
        MonitorUi.TopLeft(keys.rectTransform, 30f, -410f, 840f, 30f);
        Debug.Log("[Меню] Начальное меню: «Обучение» / «Редактор сценариев» / «Выход».");
    }

    private void Close()
    {
        Debug.Log("[Меню] Обучение.");
        Destroy(gameObject);
    }
}
