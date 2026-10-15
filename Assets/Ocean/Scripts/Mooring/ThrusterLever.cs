// ============================================================
// ThrusterLever.cs — Рычаг носового подруливающего устройства на пульте (запрос 19.2б)
//
// 5 положений (ThrusterOrders): ЛБ 100 / 50 / 0 / ПБ 50 / 100 %. Рычаг качается вбок вокруг
// продольной оси (leverRotationAxis, по умолчанию Z модели судна): наклон на правый борт — тяга
// на правый борт (нос идёт вправо). Управление — как у телеграфа (EngineTelegraph):
//   • рука рядом с осью (≤ maxTrackingDistance) — рычаг следует за рукой, ближайшее положение
//     выбирается само, при отпускании рычаг встаёт в него;
//   • хват лучом (дальше) — смещение точки попадания луча вбок переключает положения по одному.
// Положение кодом — SetIndex (меню «Управление» на кнопке B, сброс сценария).
// Модель строит Setup 19.2 Berth (ThrusterHead: корпус, шкала, ось LeverPivot, рукоятка Knob —
// единственный коллайдер взаимодействия). Команду в модель передаёт MooringController.
// ============================================================
using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class ThrusterLever : MonoBehaviour
{
    [Header("Визуал")]
    [Tooltip("Ось рычага (LeverPivot); вращается вокруг leverRotationAxis")]
    public Transform leverVisual;
    [Tooltip("Ось качания в системе родителя leverVisual: Z — продольная ось судна (нос модели — −Z)")]
    public Vector3 leverRotationAxis = Vector3.forward;
    [Tooltip("Наклон рычага на 100 %, градусы")]
    public float fullAngle = 40f;
    [Tooltip("Знак: +1 — положительный поворот вокруг оси наклоняет рычаг на правый борт (−X модели)")]
    public float starboardSign = 1f;

    [Header("Управление рукой")]
    public bool angleTracking = true;
    [Tooltip("Если рука дальше от оси рычага (м) — переключение по одному положению смещением луча")]
    public float maxTrackingDistance = 0.6f;
    [Tooltip("Смещение точки луча вбок для переключения на одно положение, м")]
    public float switchThreshold = 0.06f;

    [Header("Состояние")]
    [SerializeField] private int currentIndex = ThrusterOrders.ZeroIndex;

    /// <summary>Положение сменилось: прежнее, новое, кодом (меню / сброс) или рукой.</summary>
    public event Action<int, int, bool> Changed;

    public int Index => currentIndex;
    /// <summary>Команда BowThruster.Command, −1 … 1 (> 0 — ПБ).</summary>
    public double Command => ThrusterOrders.Command(currentIndex);
    public bool IsGrabbed => isGrabbed;

    private bool isGrabbed, tracking;
    private Transform grabController;
    private Vector3 previousControllerPos;
    private float accumulatedDelta, handAngle;
    private AudioSource audioSource;
    private AudioClip clickClip;

    void Start()
    {
        if (leverVisual == null) leverVisual = transform;
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
        clickClip = MakeClick();

        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
        if (leverVisual != transform) leverVisual.localRotation = Quaternion.AngleAxis(AngleOf(currentIndex), Axis);
    }

    private Vector3 Axis => leverRotationAxis.sqrMagnitude > 1e-6f ? leverRotationAxis.normalized : Vector3.forward;
    private float AngleOf(int index) => starboardSign * fullAngle * (float)ThrusterOrders.Command(index);

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        // Как у телеграфа: у ближнего хвата — рука, у хвата лучом — точка попадания луча
        grabController = args.interactorObject.GetAttachTransform(args.interactableObject);
        if (grabController == null) grabController = args.interactorObject.transform;
        previousControllerPos = grabController.position;
        accumulatedDelta = 0f;
        tracking = angleTracking && leverVisual != transform
                   && Vector3.Distance(grabController.position, leverVisual.position) <= maxTrackingDistance;
        if (tracking) handAngle = AngleOf(currentIndex);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        grabController = null;
        tracking = false;
    }

    void Update()
    {
        if (isGrabbed && grabController != null)
        {
            if (tracking) UpdateAngleTracking();
            else UpdateRaySteps();
        }
        float target = tracking ? handAngle : AngleOf(currentIndex);
        if (leverVisual != transform)
            leverVisual.localRotation = Quaternion.Lerp(leverVisual.localRotation, Quaternion.AngleAxis(target, Axis), Time.deltaTime * 10f);
    }

    // Угол руки вокруг оси рычага (в системе родителя рычага, 0° — рычаг вверх)
    private void UpdateAngleTracking()
    {
        Transform frame = leverVisual.parent;
        Vector3 local = frame != null ? frame.InverseTransformPoint(grabController.position) : grabController.position;
        Vector3 v = local - leverVisual.localPosition;
        v -= Axis * Vector3.Dot(v, Axis);
        if (v.sqrMagnitude < 1e-6f) return;
        handAngle = Mathf.Clamp(Vector3.SignedAngle(Vector3.up, v, Axis), -fullAngle, fullAngle);
        int nearest = ThrusterOrders.IndexOf(starboardSign * handAngle / fullAngle);
        if (nearest != currentIndex) Apply(nearest, false);
    }

    // Луч: смещение точки попадания в сторону правого борта (направление наклона рычага на ПБ)
    private void UpdateRaySteps()
    {
        Transform frame = leverVisual.parent != null ? leverVisual.parent : leverVisual;
        Vector3 stbd = frame.TransformDirection(Quaternion.AngleAxis(starboardSign * 90f, Axis) * Vector3.up).normalized;
        accumulatedDelta += Vector3.Dot(grabController.position - previousControllerPos, stbd);
        previousControllerPos = grabController.position;
        if (accumulatedDelta > switchThreshold && currentIndex < ThrusterOrders.Count - 1) { Apply(currentIndex + 1, false); accumulatedDelta = 0f; }
        else if (accumulatedDelta < -switchThreshold && currentIndex > 0) { Apply(currentIndex - 1, false); accumulatedDelta = 0f; }
    }

    /// <summary>Поставить положение кодом (меню «Управление», сброс). Рычаг доворачивается сам; слежение за рукой прерывается.</summary>
    public void SetIndex(int index)
    {
        index = ThrusterOrders.Clamp(index);
        tracking = false;
        if (index == currentIndex) return;
        Apply(index, true);
    }

    private void Apply(int index, bool fromCode)
    {
        int old = currentIndex;
        currentIndex = index;
        if (audioSource != null && clickClip != null) audioSource.PlayOneShot(clickClip);
        Changed?.Invoke(old, index, fromCode);
    }

    private static AudioClip MakeClick()
    {
        const int rate = 44100;
        int len = (int)(rate * 0.05f);
        var data = new float[len];
        var rnd = new System.Random(19);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            data[i] = (Mathf.Sin(2f * Mathf.PI * 650f * t) * 0.3f + ((float)rnd.NextDouble() * 2f - 1f) * 0.1f) * Mathf.Exp(-t * 80f) * 0.5f;
        }
        AudioClip clip = AudioClip.Create("ThrusterClick", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
