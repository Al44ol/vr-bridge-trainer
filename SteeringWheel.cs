// ============================================================
// SteeringWheel.cs — Штурвал (автоподписка на XR события)
// Просто добавь на объект — всё работает.
// Требует: XR Simple Interactable + любой Collider (Is Trigger)
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class SteeringWheel : MonoBehaviour
{
    [Header("Параметры")]
    public float maxRudderAngle = 35f;
    public float sensitivity = 200f;
    public float returnSpeed = 15f;

    [Header("Визуал")]
    [Tooltip("Если пусто — вращается сам этот объект")]
    public Transform wheelVisual;
    public Vector3 rotationAxis = Vector3.forward;

    [Header("Состояние")]
    [SerializeField] private float currentRudderAngle = 0f;
    [SerializeField] private bool isGrabbed = false;

    private Transform grabController;
    private Vector3 previousControllerPos;
    private Quaternion initialRotation;

    public float RudderAngle => currentRudderAngle;

    void Start()
    {
        if (wheelVisual == null)
            wheelVisual = transform;

        initialRotation = wheelVisual.localRotation;

        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        grabController = args.interactorObject.transform;
        previousControllerPos = grabController.position;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        grabController = null;
    }

    void Update()
    {
        if (isGrabbed && grabController != null)
        {
            Vector3 delta = grabController.position - previousControllerPos;
            float horizontalDelta = Vector3.Dot(delta, wheelVisual.right);
            currentRudderAngle += horizontalDelta * sensitivity;
            currentRudderAngle = Mathf.Clamp(currentRudderAngle, -maxRudderAngle, maxRudderAngle);
            previousControllerPos = grabController.position;
        }
        else if (returnSpeed > 0f && Mathf.Abs(currentRudderAngle) > 0.1f)
        {
            currentRudderAngle = Mathf.MoveTowards(currentRudderAngle, 0f, returnSpeed * Time.deltaTime);
        }

        float wheelAngle = (currentRudderAngle / maxRudderAngle) * 180f;
        wheelVisual.localRotation = initialRotation * Quaternion.AngleAxis(wheelAngle, rotationAxis);
    }
}
