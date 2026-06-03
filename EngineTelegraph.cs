// ============================================================
// EngineTelegraph.cs — Машинный телеграф (автоподписка на XR)
// Просто добавь на объект — всё работает.
// ============================================================
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class EngineTelegraph : MonoBehaviour
{
    [System.Serializable]
    public struct TelegraphPosition
    {
        public string name;
        public float speedFactor;
        public float leverAngle;
    }

    [Header("Позиции")]
    public TelegraphPosition[] positions = new TelegraphPosition[]
    {
        new TelegraphPosition { name = "Full Ahead",       speedFactor =  1.0f,  leverAngle = -60f },
        new TelegraphPosition { name = "Half Ahead",       speedFactor =  0.65f, leverAngle = -40f },
        new TelegraphPosition { name = "Slow Ahead",       speedFactor =  0.4f,  leverAngle = -20f },
        new TelegraphPosition { name = "Dead Slow Ahead",  speedFactor =  0.2f,  leverAngle = -10f },
        new TelegraphPosition { name = "Stop",             speedFactor =  0.0f,  leverAngle =   0f },
        new TelegraphPosition { name = "Dead Slow Astern", speedFactor = -0.2f,  leverAngle =  10f },
        new TelegraphPosition { name = "Slow Astern",      speedFactor = -0.4f,  leverAngle =  20f },
        new TelegraphPosition { name = "Half Astern",      speedFactor = -0.65f, leverAngle =  40f },
        new TelegraphPosition { name = "Full Astern",      speedFactor = -1.0f,  leverAngle =  60f },
    };

    [Header("Визуал")]
    public Transform leverVisual;
    public Vector3 leverRotationAxis = Vector3.right;

    [Header("Состояние")]
    [SerializeField] private int currentIndex = 4;
    [SerializeField] private string currentPosition = "Stop";
    [SerializeField] private float currentSpeedFactor = 0f;

    private bool isGrabbed;
    private Transform grabController;
    private Vector3 previousControllerPos;
    private float accumulatedDelta;
    private float switchThreshold = 0.08f;
    private AudioSource audioSource;
    private AudioClip clickClip;

    public float SpeedFactor => currentSpeedFactor;
    public string PositionName => currentPosition;

    void Start()
    {
        if (leverVisual == null) leverVisual = transform;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
        GenerateClickSound();
        ApplyPosition(currentIndex);

        var interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        grabController = args.interactorObject.transform;
        previousControllerPos = grabController.position;
        accumulatedDelta = 0f;
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
            float verticalDelta = Vector3.Dot(delta, leverVisual.up);
            accumulatedDelta += verticalDelta;

            if (accumulatedDelta > switchThreshold && currentIndex > 0)
            {
                currentIndex--;
                ApplyPosition(currentIndex);
                audioSource.PlayOneShot(clickClip);
                accumulatedDelta = 0f;
            }
            else if (accumulatedDelta < -switchThreshold && currentIndex < positions.Length - 1)
            {
                currentIndex++;
                ApplyPosition(currentIndex);
                audioSource.PlayOneShot(clickClip);
                accumulatedDelta = 0f;
            }

            previousControllerPos = grabController.position;
        }

        float targetAngle = positions[currentIndex].leverAngle;
        Quaternion targetRot = Quaternion.AngleAxis(targetAngle, leverRotationAxis);
        leverVisual.localRotation = Quaternion.Lerp(leverVisual.localRotation, targetRot, Time.deltaTime * 10f);
    }

    private void ApplyPosition(int index)
    {
        currentPosition = positions[index].name;
        currentSpeedFactor = positions[index].speedFactor;
    }

    private void GenerateClickSound()
    {
        int rate = 44100;
        int len = (int)(rate * 0.05f);
        float[] data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Exp(-t * 80f);
            data[i] = (Mathf.Sin(2f * Mathf.PI * 800f * t) * 0.3f +
                       (Random.value * 2f - 1f) * 0.1f) * env * 0.5f;
        }
        clickClip = AudioClip.Create("Click", len, 1, rate, false);
        clickClip.SetData(data, 0);
    }
}
