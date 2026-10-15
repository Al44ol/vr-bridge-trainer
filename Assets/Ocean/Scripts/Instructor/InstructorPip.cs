// ============================================================
// InstructorPip.cs — Вид из шлема в окне инструктора (картинка в картинке, D1)
//
// Отдельная камера в позиции головы обучающегося рисует сцену в маленькую
// текстуру (по умолчанию 480 × 270) не каждый кадр, а с частотой rateHz:
// камера включается на один кадр и снова выключается. Без теней, постобработки,
// сглаживания и HDR, дальность — как у камеры шлема (туман и огни видны).
// По умолчанию выключено (кнопка в окне / меню Y «Монитор инструктора»).
// Замер 12.10.2026 (ThesisNotes, табл. 13.2): с PiP 90,0 кадра/с, +0,85 мс GPU — оставлен.
// С 19.2 (п. A): размер меняется из окна инструктора ([−] [+], SetResolution — текстура
// пересоздаётся), частота — кнопкой «10 / 20 Гц»; на первом шаге — прежние 480 × 270, 10 Гц.
// ============================================================
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class InstructorPip : MonoBehaviour
{
    [Tooltip("Ширина и высота текстуры, пикселей")]
    public int width = 480, height = 270;
    [Tooltip("Обновлений в секунду (реальное время)")]
    [Range(1f, 30f)] public float rateHz = 10f;
    [Tooltip("Поле зрения по вертикали, °")]
    public float fieldOfView = 60f;

    public RenderTexture Texture { get; private set; }
    public bool Active { get; private set; }
    /// <summary>Сколько раз отрисован (для замера).</summary>
    public int Renders { get; private set; }

    private Camera cam;
    private float nextRender;
    private bool renderedThisFrame;

    public void SetActive(bool on)
    {
        if (on == Active) return;
        Active = on;
        if (on) Build();
        if (cam != null) cam.enabled = false;
        nextRender = 0f;
    }

    /// <summary>Новый размер текстуры, пикселей (16…2560 × 16…1440). Уже созданная текстура пересоздаётся.</summary>
    public void SetResolution(int w, int h)
    {
        w = Mathf.Clamp(w, 16, 2560);
        h = Mathf.Clamp(h, 16, 1440);
        if (w == width && h == height) return;
        width = w;
        height = h;
        if (cam == null) return;   // ещё не включали — создастся в Build с новым размером
        RenderTexture old = Texture;
        Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "InstructorPip" };
        Texture.Create();
        cam.targetTexture = Texture;
        cam.ResetAspect();
        nextRender = 0f;
        if (old != null)
        {
            old.Release();
            Destroy(old);
        }
    }

    void LateUpdate()
    {
        if (!Active || cam == null) return;
        // Камера, включённая в прошлом кадре, уже отрисовала — выключить
        if (renderedThisFrame)
        {
            cam.enabled = false;
            renderedThisFrame = false;
        }
        Camera head = Camera.main;
        if (head == null || Time.unscaledTime < nextRender) return;
        nextRender = Time.unscaledTime + 1f / Mathf.Max(1f, rateHz);
        cam.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
        cam.nearClipPlane = head.nearClipPlane;
        cam.farClipPlane = head.farClipPlane;
        cam.cullingMask = head.cullingMask;
        cam.enabled = true;
        renderedThisFrame = true;
        Renders++;
    }

    private void Build()
    {
        if (cam != null) return;
        Texture = new RenderTexture(Mathf.Max(16, width), Mathf.Max(16, height), 24, RenderTextureFormat.ARGB32) { name = "InstructorPip" };
        Texture.Create();
        var go = new GameObject("InstructorPipCamera");
        go.transform.SetParent(transform, false);
        cam = go.AddComponent<Camera>();
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.targetTexture = Texture;
        cam.fieldOfView = fieldOfView;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.depth = -10;
        cam.enabled = false;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;
        data.allowXRRendering = false;
    }

    void OnDestroy()
    {
        if (Texture != null)
        {
            Texture.Release();
            Destroy(Texture);
        }
    }
}
