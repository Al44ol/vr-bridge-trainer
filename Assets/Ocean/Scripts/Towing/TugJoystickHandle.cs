// ============================================================
// TugJoystickHandle.cs — Рукоятка джойстика своего буксира на пульте (запрос 19.4а)
//
// Строится кодом (OwnTugDrive, Setup не нужен) на месте TelegraphHead — телеграф, штурвал и рычаг
// подруливающего на буксире скрыты. Основание, гофр, стержень и рукоятка (Knob — единственный коллайдер
// взаимодействия, XRSimpleInteractable). Система рукоятки: +Z — к носу, +X — на ПБ, +Y — вверх.
// Управление (как телеграф и рычаг подруливающего — рука или луч):
//   • рука рядом (≤ maxTrackingDistance от оси) — наклон рукоятки следует за рукой относительно точки хвата:
//     вперёд/назад — ход, вбок — лагом (до TugJoystick.MaxTiltDeg); поворот кисти вокруг вертикали — разворот
//     (до MaxTwistDeg, по часовой сверху — нос вправо);
//   • луч — смещение точки попадания вперёд/вбок на rayFullOffset — полный наклон; поворот — так же кистью.
// При отпускании рукоятка остаётся, где оставили (как телеграф, решение 19.4а); в ноль — меню B «Джойстик».
// Положение кодом — SetState (меню, сброс): рукоятка доворачивается сама.
// 19.4а-4: ход рукоятки 35° / 45° (TugJoystick), луч — 0,25 м на полный наклон; ступени (detents, по умолчанию):
// рука ведёт рукоятку плавно (Raw), упор — по ближайшей ступени 25 / 50 / 75 / 100 % (TugJoystick.Snap); переход на
// ступень — щелчок и короткая вибрация контроллера в этой руке; отпустили — рукоятка встаёт в ступень, как телеграф.
// Без ступеней (меню B «Рукоятка: плавно») — прежнее непрерывное управление.
// ============================================================
using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TugJoystickHandle : MonoBehaviour
{
    [Tooltip("Если рука дальше от оси (м) — управление смещением луча")]
    public float maxTrackingDistance = 0.6f;
    [Tooltip("Смещение точки луча на полный наклон, м (19.4а-4: было 0,15)")]
    public float rayFullOffset = 0.25f;
    [Tooltip("19.4а-4: ступени упора 25 / 50 / 75 / 100 % (как телеграф); выкл — плавно")]
    public bool detents = true;
    [Tooltip("Вибрация контроллера на переходе ступени: сила 0…1, длительность, с")]
    public float hapticAmplitude = 0.35f, hapticDuration = 0.03f;
    [Tooltip("Высота оси качания над основанием, м; длина стержня до рукоятки, м")]
    public float pivotHeight = 0.07f, stickLength = 0.16f;

    /// <summary>Рукоятку отпустили (19.4а-3: возврат в ноль, если тумблер «УДЕРЖ. КУРС» выключен).</summary>
    public event Action Released;

    /// <summary>Положение сменилось: кодом (меню, сброс) или рукой.</summary>
    public event Action<bool> Changed;

    public JoystickState State => state;
    public bool IsGrabbed => grab != null;
    /// <summary>Положение рукоятки, куда ведёт рука (до ступени; без хвата — State).</summary>
    public JoystickState Raw => grab != null ? raw : state;

    private JoystickState state, raw;
    private XRBaseInputInteractor hapticHand;
    private Transform pivot, twistPart;
    private Transform grab, grabHand;           // точка хвата (рука / точка луча) и сам контроллер (поворот кисти)
    private bool tracking;
    private JoystickState grabStart;
    private Vector3 grabStartLocal;
    private Vector3 grabStartForward;
    private AudioSource audioSource;
    private AudioClip clickClip;
    private int lastClickStep;

    /// <summary>Построить рукоятку (вызывается один раз, объект ещё выключен).</summary>
    public void Build(Material bodyMat, Material knobMat)
    {
        Part(PrimitiveType.Cylinder, "Base", transform, new Vector3(0f, 0.015f, 0f), new Vector3(0.12f, 0.015f, 0.12f), bodyMat);
        Part(PrimitiveType.Cylinder, "Boot", transform, new Vector3(0f, 0.045f, 0f), new Vector3(0.07f, 0.02f, 0.07f), bodyMat);
        pivot = new GameObject("Pivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, pivotHeight, 0f);
        twistPart = new GameObject("Twist").transform;
        twistPart.SetParent(pivot, false);
        Part(PrimitiveType.Cylinder, "Stick", twistPart, new Vector3(0f, 0.5f * stickLength, 0f), new Vector3(0.024f, 0.5f * stickLength, 0.024f), bodyMat);
        Transform knob = Part(PrimitiveType.Capsule, "Knob", twistPart, new Vector3(0f, stickLength + 0.02f, 0f), new Vector3(0.05f, 0.04f, 0.05f), knobMat, keepCollider: true);
        // Метка на рукоятке — к носу: видно поворот
        Part(PrimitiveType.Cube, "Mark", twistPart, new Vector3(0f, stickLength + 0.065f, 0.018f), new Vector3(0.01f, 0.006f, 0.03f), bodyMat);

        var col = knob.GetComponent<Collider>();
        if (col is CapsuleCollider cap) cap.radius = 0.9f;   // хват чуть шире рукоятки
        var interactable = gameObject.AddComponent<XRSimpleInteractable>();
        interactable.colliders.Clear();
        interactable.colliders.Add(col);
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
        clickClip = MakeClick();
    }

    private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        grab = args.interactorObject.GetAttachTransform(args.interactableObject);
        grabHand = args.interactorObject.transform;
        if (grab == null) grab = grabHand;
        hapticHand = args.interactorObject as XRBaseInputInteractor;
        grabStart = raw = state;
        grabStartLocal = transform.InverseTransformPoint(grab.position);
        grabStartForward = HandForward();
        tracking = Vector3.Distance(grab.position, pivot.position) <= maxTrackingDistance;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        grab = grabHand = null;
        hapticHand = null;
        Released?.Invoke();
    }

    // Направление кисти в плоскости основания (для поворота рукоятки)
    private Vector3 HandForward()
    {
        Vector3 f = transform.InverseTransformDirection(grabHand != null ? grabHand.forward : Vector3.forward);
        f.y = 0f;
        return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
    }

    void Update()
    {
        if (grab != null)
        {
            Vector3 local = transform.InverseTransformPoint(grab.position);
            double fwd, stb;
            if (tracking)
            {
                // Наклон — угол руки вокруг оси качания относительно положения при хвате
                Vector3 p0 = grabStartLocal - pivot.localPosition, p1 = local - pivot.localPosition;
                fwd = grabStart.Surge * TugJoystick.MaxTiltDeg + (Atan(p1.z, p1.y) - Atan(p0.z, p0.y));
                stb = grabStart.Sway * TugJoystick.MaxTiltDeg + (Atan(p1.x, p1.y) - Atan(p0.x, p0.y));
            }
            else
            {
                Vector3 d = local - grabStartLocal;
                fwd = (grabStart.Surge + d.z / rayFullOffset) * TugJoystick.MaxTiltDeg;
                stb = (grabStart.Sway + d.x / rayFullOffset) * TugJoystick.MaxTiltDeg;
            }
            double twist = grabStart.Yaw * TugJoystick.MaxTwistDeg + Vector3.SignedAngle(grabStartForward, HandForward(), Vector3.up);
            raw = TugJoystick.FromHandle(fwd, stb, twist);
            JoystickState s = detents ? TugJoystick.Snap(raw) : raw;   // 19.4а-4: упор — по ближайшей ступени
            if (s.DistanceTo(state) > 1e-4)
            {
                state = s;
                if (Click() && hapticHand != null && hapticAmplitude > 0f) hapticHand.SendHapticImpulse(hapticAmplitude, hapticDuration);
                Changed?.Invoke(false);
            }
        }
        if (pivot != null)
        {
            // В руке — куда ведёт рука; отпустили — доворот в ступень (state)
            JoystickState shown = grab != null ? raw : state;
            Quaternion target = Quaternion.AngleAxis((float)(shown.Surge * TugJoystick.MaxTiltDeg), Vector3.right)
                              * Quaternion.AngleAxis((float)(-shown.Sway * TugJoystick.MaxTiltDeg), Vector3.forward);
            Quaternion twistTarget = Quaternion.AngleAxis((float)(shown.Yaw * TugJoystick.MaxTwistDeg), Vector3.up);
            float k = grab != null ? 1f : Mathf.Clamp01(Time.deltaTime * 12f);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, target, k);
            twistPart.localRotation = Quaternion.Slerp(twistPart.localRotation, twistTarget, k);
        }
    }

    /// <summary>Ступени упора вкл/выкл (меню B «Рукоятка: ступени / плавно»); при включении — встать в ближайшую ступень.</summary>
    public void SetDetents(bool on)
    {
        detents = on;
        if (on) SetState(TugJoystick.Snap(state));
    }

    private static double Atan(float a, float b) => Math.Atan2(a, Math.Max(1e-4, b)) * 180.0 / Math.PI;

    /// <summary>Поставить положение кодом (меню «Джойстик», сброс): рукоятка доворачивается сама, хват прерывается.</summary>
    public void SetState(JoystickState s)
    {
        if (grab != null) { grabStart = raw = s; grabStartLocal = transform.InverseTransformPoint(grab.position); grabStartForward = HandForward(); }
        if (s.DistanceTo(state) < 1e-9) return;
        state = s;
        Click();
        Changed?.Invoke(true);
    }

    // Щелчок при переходе на другую ступень упора по любой оси (19.4а-4: ступени — 25 % упора, не хода рукоятки);
    // true — щёлкнуло
    private bool Click()
    {
        int step = (TugJoystick.NearestStep(state.Surge) + 8) * 100 + (TugJoystick.NearestStep(state.Sway) + 8) * 10 + TugJoystick.NearestStep(state.Yaw);
        if (step == lastClickStep) return false;
        lastClickStep = step;
        if (audioSource != null && clickClip != null && audioSource.isActiveAndEnabled) audioSource.PlayOneShot(clickClip);
        return true;
    }

    private static AudioClip MakeClick()
    {
        const int rate = 44100;
        int len = (int)(rate * 0.04f);
        var data = new float[len];
        var rnd = new System.Random(194);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            data[i] = (Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.25f + ((float)rnd.NextDouble() * 2f - 1f) * 0.08f) * Mathf.Exp(-t * 90f) * 0.5f;
        }
        AudioClip clip = AudioClip.Create("JoystickClick", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
