// ============================================================
// MonitorUi.cs — Элементы интерфейса на мониторе для редактора и начального меню (запрос 21.2а)
//
// Те же приёмы, что в окне инструктора (InstructorWindow): холст Screen Space Overlay 1920 × 1080
// (CanvasScaler, Expand), панели — Image, текст — TextMeshPro, кнопки с подписью / цветом /
// доступностью, которые обновляются Refresh(); поле ввода TMP_InputField. Всё строится кодом.
// ============================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class MonitorUi
{
    public static readonly Color BarColor = new Color(0.08f, 0.1f, 0.13f, 1f);
    public static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.97f);
    /// <summary>21.5а: листы сценария поверх карты — непрозрачные.</summary>
    public static readonly Color SheetColor = new Color(0.07f, 0.09f, 0.12f, 1f);
    public static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    public static readonly Color NeutralColor = new Color(0.2f, 0.23f, 0.28f);
    public static readonly Color ActiveColor = new Color(0.25f, 0.55f, 0.85f);
    public static readonly Color StartColor = new Color(0.16f, 0.55f, 0.3f);
    public static readonly Color StopColor = new Color(0.6f, 0.18f, 0.15f);
    public static readonly Color WarnColor = new Color(0.75f, 0.5f, 0.1f);
    public static readonly Color DisabledColor = new Color(0.16f, 0.17f, 0.19f);
    public static readonly Color TitleColor = new Color(0.8f, 0.9f, 1f);
    public static readonly Color MutedColor = new Color(0.6f, 0.65f, 0.7f);
    public static readonly Color FieldColor = new Color(0.14f, 0.16f, 0.2f);
    public const float Gap = 6f, RowHeight = 38f;

    private readonly List<(TextMeshProUGUI text, Image img, Func<string> label, Func<Color> color, Func<bool> enabled, Color baseColor)> dynamic =
        new List<(TextMeshProUGUI, Image, Func<string>, Func<Color>, Func<bool>, Color)>();

    /// <summary>Вызывается после каждого нажатия кнопки (перерисовать окно).</summary>
    public Action AfterClick;

    /// <summary>Холст поверх всего на мониторе.</summary>
    public static Canvas Canvas(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = color.a > 0f;
        return go.GetComponent<RectTransform>();
    }

    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    /// <summary>Растянуть на весь родитель с отступами (слева, снизу, справа, сверху).</summary>
    public static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>Привязка к левому верхнему углу родителя: x вправо, y вниз (отрицательный).</summary>
    public static void TopLeft(RectTransform rt, float x, float y, float w, float h) =>
        Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(w, h), new Vector2(0f, 1f));

    public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Кнопка в строке: x — от левого края родителя (растёт), y — от верха.</summary>
    public RectTransform Button(RectTransform parent, ref float x, float y, float width, string label, Color color, UnityAction action,
                                Func<string> text = null, Func<Color> dynColor = null, Func<bool> enabled = null, float height = RowHeight, float fontSize = 16f)
    {
        var go = new GameObject("Button " + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (action != null)
            btn.onClick.AddListener(() =>
            {
                if (enabled != null && !enabled()) return;
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
                AfterClick?.Invoke();
            });
        else btn.interactable = false;
        TopLeft(go.GetComponent<RectTransform>(), x, y, width, height);
        TextMeshProUGUI t = Text(go.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(t.rectTransform, 6f, 0f, 6f, 0f);
        if (text != null || dynColor != null || enabled != null)
            dynamic.Add((t, img, text ?? (() => label), dynColor ?? (() => color), enabled, color));
        x += width + Gap;
        return go.GetComponent<RectTransform>();
    }

    /// <summary>Поле ввода; onEnd — по Enter или уходу из поля.</summary>
    public static TMP_InputField Field(RectTransform parent, float x, float y, float w, float h, string placeholder, bool multiline,
                                       TMP_InputField.ContentType type, UnityAction<string> onEnd, float fontSize = 17f)
    {
        var go = new GameObject("Field", typeof(RectTransform));
        go.SetActive(false);   // включается, когда назначены текст и область (иначе TMP_InputField ругается в OnEnable)
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = FieldColor;
        TopLeft(go.GetComponent<RectTransform>(), x, y, w, h);

        var area = new GameObject("TextArea", typeof(RectTransform));
        area.transform.SetParent(go.transform, false);
        area.AddComponent<RectMask2D>();
        RectTransform art = area.GetComponent<RectTransform>();
        Stretch(art, 8f, 4f, 8f, 4f);

        TextMeshProUGUI ph = Text(area.transform, placeholder, fontSize, new Color(1f, 1f, 1f, 0.35f), multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left);
        TextMeshProUGUI txt = Text(area.transform, "", fontSize, Color.white, multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left);
        foreach (TextMeshProUGUI t in new[] { ph, txt })
        {
            t.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
        }
        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = art;
        field.textComponent = txt;
        field.placeholder = ph;
        field.targetGraphic = img;
        field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        field.contentType = type;
        if (onEnd != null) field.onEndEdit.AddListener(onEnd);
        go.SetActive(true);
        return field;
    }

    /// <summary>Подписи, цвета и доступность кнопок (раз в кадр или по событию).</summary>
    public void Refresh()
    {
        foreach (var d in dynamic)
        {
            if (d.text == null) continue;
            bool on = d.enabled == null || d.enabled();
            d.text.text = d.label();
            d.img.color = on ? d.color() : DisabledColor;
            d.text.color = on ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    /// <summary>Число из поля: точка или запятая; false — не число.</summary>
    public static bool TryNumber(string s, out double v) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.').Replace('−', '-'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>Число для поля: запятая, без лишних нулей.</summary>
    public static string Num(double v, int decimals = 1) => ScenarioText.Num(v, decimals);
}
