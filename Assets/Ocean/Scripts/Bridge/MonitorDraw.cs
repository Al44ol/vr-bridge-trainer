// ============================================================
// MonitorDraw.cs — Рисование приборов на мониторах пульта стандартными Image (запрос 16а, по проверке 23.10.2026)
//
// По первой проверке собственный графический элемент (сетка UI) на мониторах не отрисовывался, поэтому
// всё рисуется обычными Image: спрайты круга, кольца и блика строятся кодом один раз (текстура 256 × 256
// со сглаженным краем), без файлов-картинок:
//   • Disc / Ring — круг и кольцо; Arc — дуга шкалы (кольцо, Image.Filled Radial360 от угла from по часовой);
//   • Tick — деление (прямоугольник, повёрнутый по радиусу); Needle — стрелка (прямоугольник с осью у основания);
//   • Glare — блик «стекла» на приборе; Rect — прямоугольник (рамки, табло, винты панели).
// Углы — как в Core GaugeScale: 0° — вверх, по часовой. Координаты — пиксели холста от центра родителя.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class MonitorDraw
{
    private const int Tex = 256;
    private static Sprite circle, glare;
    private static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();

    // ─────────── Спрайты ───────────
    public static Sprite Circle => circle != null ? circle : circle = Make("MonitorCircle", (x, y) => Edge(1f - R(x, y)));

    /// <summary>Кольцо с внутренним радиусом inner (доля внешнего, шаг 0,01).</summary>
    public static Sprite RingSprite(float inner)
    {
        int key = Mathf.Clamp(Mathf.RoundToInt(inner * 100f), 0, 99);
        if (rings.TryGetValue(key, out Sprite s) && s != null) return s;
        float k = key / 100f;
        s = Make("MonitorRing" + key, (x, y) => { float r = R(x, y); return Mathf.Min(Edge(1f - r), Edge(r - k)); });
        rings[key] = s;
        return s;
    }

    /// <summary>Блик стекла: светлое пятно в верхней половине, гаснет к низу и краям.</summary>
    public static Sprite Glare => glare != null ? glare : glare = Make("MonitorGlare", (x, y) =>
    {
        float r = R(x, y);
        float top = Mathf.Clamp01((y - 0.45f) / 0.55f);                   // 0 ниже середины, 1 у верхнего края
        return Edge(0.96f - r) * top * top * 0.9f;
    });

    private static float R(float x, float y) => Mathf.Sqrt((2f * x - 1f) * (2f * x - 1f) + (2f * y - 1f) * (2f * y - 1f));

    // Сглаживание края: 0 → 1 на ширине ≈ 1,5 пикселя текстуры
    private static float Edge(float d) => Mathf.Clamp01(d * Tex * 0.5f / 1.5f + 0.5f);

    private static Sprite Make(string name, System.Func<float, float, float> alpha)
    {
        var t = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[Tex * Tex];
        for (int j = 0; j < Tex; j++)
            for (int i = 0; i < Tex; i++)
                px[j * Tex + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alpha((i + 0.5f) / Tex, (j + 0.5f) / Tex))));
        t.SetPixels32(px);
        t.Apply(false, true);
        Sprite s = Sprite.Create(t, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), Tex);
        s.name = name;
        return s;
    }

    // ─────────── Элементы ───────────
    public static RectTransform Node(RectTransform parent, string name, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Img(RectTransform rt, Color c, Sprite s = null)
    {
        Image i = rt.gameObject.AddComponent<Image>();
        i.sprite = s;
        i.color = c;
        i.raycastTarget = false;
        return i;
    }

    public static Image Rect(RectTransform parent, Vector2 pos, Vector2 size, Color c, string name = "Rect") =>
        Img(Node(parent, name, pos, size), c);

    public static Image Disc(RectTransform parent, Vector2 pos, float r, Color c) =>
        Img(Node(parent, "Disc", pos, new Vector2(2f * r, 2f * r)), c, Circle);

    public static Image Ring(RectTransform parent, Vector2 pos, float inner, float outer, Color c) =>
        Img(Node(parent, "Ring", pos, new Vector2(2f * outer, 2f * outer)), c, RingSprite(inner / Mathf.Max(1e-3f, outer)));

    /// <summary>Дуга от fromDeg до toDeg (по часовой) между радиусами inner и outer.</summary>
    public static Image Arc(RectTransform parent, Vector2 pos, float fromDeg, float toDeg, float inner, float outer, Color c)
    {
        Image i = Ring(parent, pos, inner, outer, c);
        i.type = Image.Type.Filled;
        i.fillMethod = Image.FillMethod.Radial360;
        i.fillOrigin = (int)Image.Origin360.Top;
        i.fillClockwise = true;
        i.fillAmount = Mathf.Clamp01((toDeg - fromDeg) / 360f);
        i.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -fromDeg);   // начало заливки — с угла from
        return i;
    }

    /// <summary>Деление по углу angleDeg от радиуса inner до outer.</summary>
    public static Image Tick(RectTransform parent, Vector2 center, float angleDeg, float inner, float outer, float width, Color c)
    {
        RectTransform rt = Node(parent, "Tick", center + Dir(angleDeg) * inner, new Vector2(width, outer - inner), new Vector2(0.5f, 0f));
        rt.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        return Img(rt, c);
    }

    /// <summary>Стрелка: от оси (center) на length, хвост tail; поворот — Point.</summary>
    public static RectTransform Needle(RectTransform parent, Vector2 center, float length, float width, float tail, Color c)
    {
        RectTransform rt = Node(parent, "Needle", center, new Vector2(width, length + tail), new Vector2(0.5f, tail / (length + tail)));
        Img(rt, c);
        // Острие — узкий конец
        RectTransform tip = Node(rt, "Tip", new Vector2(0f, (length + tail) * (1f - rt.pivot.y) - width * 0.2f), new Vector2(width * 0.45f, width * 0.8f));
        Img(tip, c);
        return rt;
    }

    public static void Point(RectTransform needle, float angleDeg) => needle.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);

    public static Image GlareOn(RectTransform parent, Vector2 pos, float r, float alpha = 0.1f) =>
        Img(Node(parent, "Glare", pos, new Vector2(2f * r, 2f * r)), new Color(1f, 1f, 1f, alpha), Glare);

    public static Vector2 Dir(float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
    }
}
