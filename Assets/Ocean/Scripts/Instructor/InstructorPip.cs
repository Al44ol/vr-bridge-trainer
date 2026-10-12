// ============================================================
// InstructorPip.cs — Вид из шлема в окне инструктора (картинка в картинке, D1)
//
// Отдельная камера в позиции головы обучающегося рисует сцену в маленькую
// текстуру (по умолчанию 480 × 270) не каждый кадр, а с частотой rateHz:
// камера включается на один кадр и снова выключается. Без теней, постобработки,
// сглаживания и HDR, дальность — как у камеры шлема (туман и огни видны).
// По умолчанию выключено: решение по F8 (ThesisNotes, раздел 13) — если с PiP
// частота кадров в шлеме падает ниже 90 Гц, PiP не использовать, вид из шлема
// показывает зеркало (F2).
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
