// ============================================================
// SteeringWheel.cs — Штурвал (автоподписка на XR события)
// Просто добавь на объект — всё работает.
// Требует: XR Simple Interactable + любой Collider (Is Trigger)
//
// Режимы управления рукой:
//   • rimTracking (по умолчанию): штурвал поворачивается вслед за рукой —
//     приращение угла руки вокруг оси штурвала Δθ (в плоскости штурвала)
//     добавляется к углу штурвала. Взяться можно в любой точке обода,
//     перехват (отпустил — взялся в другом месте) работает естественно.
//     Угол штурвала θ и руль: δ_зад = θ / maxWheelAngle · maxRudderAngle.
//   • прежний режим: смещение руки вдоль оси wheelVisual.right × sensitivity.
// Автовозврат в ноль (autoReturn) по умолчанию выключен — на реальном судне
// руль остаётся там, где его поставили.
// Знак: RudderAngle > 0 — поворот штурвала в положительную сторону вокруг
// rotationAxis. Какой это борт, определяет ShipNavigation (по положению оператора).
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class SteeringWheel : MonoBehaviour
{
    [Header("Параметры")]
    public float maxRudderAngle = 35f;
    public float sensitivity = 200f;
    [Tooltip("Автовозврат руля в ноль после отпускания")]
    public bool autoReturn = false;
    public float returnSpeed = 15f;

    [Header("Управление рукой")]
    [Tooltip("Штурвал поворачивается вслед за рукой (берётся за обод)")]
    public bool rimTracking = true;
    [Tooltip("Угол поворота штурвала, соответствующий максимальной перекладке руля, градусы")]
    public float maxWheelAngle = 180f;
    [Tooltip("Ближе к оси (м) рука не учитывается — у центра угол неустойчив")]
    public float minGripRadius = 0.05f;

    [Header("Визуал")]
    [Tooltip("Если пусто — вращается сам этот объект")]
    public Transform wheelVisual;
    public Vector3 rotationAxis = Vector3.forward;

    [Header("Состояние")]
    [SerializeField] private float currentRudderAngle = 0f;
    [SerializeField] private bool isGrabbed = false;

    private Transform grabController;
    private Vector3 previousControllerPos;
    private Vector3 previousGripDir;
    private bool hasGripDir;
    private Quaternion initialRotation;
    private bool started;

    public float RudderAngle => currentRudderAngle;
    public bool IsGrabbed => isGrabbed;

    /// <summary>
    /// Поставить штурвал на заданный угол руля (начальные условия сценария, B2).
    /// Знак — как у RudderAngle (какой это борт, решает ShipNavigation.SetRudderOrder).
    /// Если штурвал держат — хват продолжается от нового положения без рывка.
    /// </summary>
    public void SetRudderAngle(float angle)
    {
        currentRudderAngle = Mathf.Clamp(angle, -maxRudderAngle, maxRudderAngle);
        hasGripDir = false;
    }

    /// <summary>Ось штурвала в мировых координатах.</summary>
    public Vector3 WorldAxis
    {
        get
        {
            Transform v = wheelVisual != null ? wheelVisual : transform;
            Transform frame = v.parent;
            // Ось не меняется при вращении штурвала (до Start — по текущему повороту)
            Quaternion rest = started ? initialRotation : v.localRotation;
            Vector3 axis = rest * rotationAxis;
            return frame != null ? frame.TransformDirection(axis).normalized : axis.normalized;
        }
    }

    void Start()
    {
        if (wheelVisual == null)
            wheelVisual = transform;

        initialRotation = wheelVisual.localRotation;
        started = true;

        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        // Точка хвата: у ближнего хвата — рука, у хвата лучом — точка попадания луча,
        // которая затем движется вместе с лучом (attach transform интерактора)
        grabController = args.interactorObject.GetAttachTransform(args.interactableObject);
        if (grabController == null) grabController = args.interactorObject.transform;
        previousControllerPos = grabController.position;
        hasGripDir = TryGetGripDir(grabController.position, out previousGripDir);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        grabController = null;
        hasGripDir = false;
    }

    // Направление от оси штурвала к руке в плоскости штурвала
    private bool TryGetGripDir(Vector3 handPos, out Vector3 dir)
    {
        Vector3 axis = WorldAxis;
        Vector3 v = handPos - wheelVisual.position;
        v -= axis * Vector3.Dot(v, axis);
        dir = v;
        return v.magnitude >= minGripRadius;
    }

    void Update()
    {
        if (isGrabbed && grabController != null && rimTracking)
        {
            // Приращение угла руки вокруг оси штурвала → приращение угла штурвала
            if (TryGetGripDir(grabController.position, out Vector3 dir))
            {
                if (hasGripDir)
                {
                    float dTheta = Vector3.SignedAngle(previousGripDir, dir, WorldAxis);
                    currentRudderAngle += dTheta / Mathf.Max(maxWheelAngle, 1f) * maxRudderAngle;
                    currentRudderAngle = Mathf.Clamp(currentRudderAngle, -maxRudderAngle, maxRudderAngle);
                }
                previousGripDir = dir;
                hasGripDir = true;
            }
            else hasGripDir = false;
        }
        else if (isGrabbed && grabController != null)
        {
            Vector3 delta = grabController.position - previousControllerPos;
            float horizontalDelta = Vector3.Dot(delta, wheelVisual.right);
            currentRudderAngle += horizontalDelta * sensitivity;
            currentRudderAngle = Mathf.Clamp(currentRudderAngle, -maxRudderAngle, maxRudderAngle);
            previousControllerPos = grabController.position;
        }
        else if (autoReturn && returnSpeed > 0f && Mathf.Abs(currentRudderAngle) > 0.1f)
        {
            currentRudderAngle = Mathf.MoveTowards(currentRudderAngle, 0f, returnSpeed * Time.deltaTime);
        }

        float wheelAngle = (currentRudderAngle / maxRudderAngle) * maxWheelAngle;
        wheelVisual.localRotation = initialRotation * Quaternion.AngleAxis(wheelAngle, rotationAxis);
    }
}
