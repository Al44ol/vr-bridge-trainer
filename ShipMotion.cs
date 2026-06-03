// ============================================================
// ShipMotion.cs — Качка судна (крен, дифферент, рыскание, вертикальная)
//
// Присоединяется к родительскому объекту «Ship», который содержит
// мостик (bridge) и палубу (Cube/Deck). XR Rig тоже должен быть
// дочерним элементом Ship, чтобы игрок качался вместе с судном.
//
// Параметр seaState (0..1) управляет интенсивностью:
//   0.0 = штиль (почти нет движения)
//   0.3 = умеренное волнение
//   0.7 = сильное волнение
//   1.0 = шторм
//
// Этот же параметр должен совпадать с _WaveIntensity шейдера воды,
// чтобы визуальные волны и качка были синхронизированы.
// ============================================================
using UnityEngine;

public class ShipMotion : MonoBehaviour
{
    [Header("Состояние моря")]
    [Range(0f, 1f)]
    [Tooltip("0 = штиль, 1 = шторм. Синхронизируй с _WaveIntensity материала воды.")]
    public float seaState = 0.3f;

    [Header("Крен (Roll — наклон влево-вправо)")]
    [Tooltip("Максимальный угол крена при seaState=1 (градусы)")]
    public float rollAmplitude = 12f;
    [Tooltip("Период крена в секундах")]
    public float rollPeriod = 7f;

    [Header("Дифферент (Pitch — наклон нос-корма)")]
    [Tooltip("Максимальный угол дифферента при seaState=1 (градусы)")]
    public float pitchAmplitude = 5f;
    [Tooltip("Период дифферента в секундах")]
    public float pitchPeriod = 9f;

    [Header("Рыскание (Yaw — поворот вокруг вертикальной оси)")]
    [Tooltip("Максимальный угол рыскания при seaState=1 (градусы)")]
    public float yawAmplitude = 2f;
    [Tooltip("Период рыскания в секундах")]
    public float yawPeriod = 13f;

    [Header("Вертикальная качка (Heave)")]
    [Tooltip("Максимальная амплитуда подъёма/опускания в метрах при seaState=1")]
    public float heaveAmplitude = 1.5f;
    [Tooltip("Период вертикальной качки в секундах")]
    public float heavePeriod = 8f;

    [Header("Синхронизация с шейдером воды")]
    [Tooltip("Материал воды (OceanMaterial) — для автосинхронизации _WaveIntensity")]
    public Material oceanMaterial;

    // Исходная позиция и поворот (запоминаем при старте)
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    void Start()
    {
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;
    }

    void Update()
    {
        float t = Time.time;

        // Рассчитываем смещения с разными фазами для естественности
        float roll  = Mathf.Sin(t * 2f * Mathf.PI / rollPeriod)  * rollAmplitude  * seaState;
        float pitch = Mathf.Sin(t * 2f * Mathf.PI / pitchPeriod + 1.3f) * pitchAmplitude * seaState;
        float yaw   = Mathf.Sin(t * 2f * Mathf.PI / yawPeriod   + 2.7f) * yawAmplitude   * seaState;
        float heave = Mathf.Sin(t * 2f * Mathf.PI / heavePeriod  + 0.8f) * heaveAmplitude * seaState;

        // Добавляем вторую гармонику для реализма (не идеальная синусоида)
        roll  += Mathf.Sin(t * 2f * Mathf.PI / (rollPeriod  * 0.5f) + 0.5f) * rollAmplitude  * seaState * 0.2f;
        pitch += Mathf.Sin(t * 2f * Mathf.PI / (pitchPeriod * 0.6f) + 1.0f) * pitchAmplitude * seaState * 0.15f;

        // Применяем поворот
        Quaternion rocking = Quaternion.Euler(pitch, yaw, roll);
        transform.localRotation = initialRotation * rocking;

        // Применяем вертикальное смещение
        Vector3 pos = initialPosition;
        pos.y += heave;
        transform.localPosition = pos;

        // Синхронизация с шейдером воды
        if (oceanMaterial != null)
        {
            oceanMaterial.SetFloat("_WaveIntensity", seaState);
        }
    }

    /// <summary>
    /// Вызывается из UI/скрипта для смены погоды.
    /// Плавно меняет seaState за заданное время.
    /// </summary>
    public void SetSeaState(float targetState, float transitionTime = 3f)
    {
        StopAllCoroutines();
        StartCoroutine(TransitionSeaState(targetState, transitionTime));
    }

    private System.Collections.IEnumerator TransitionSeaState(float target, float duration)
    {
        float startState = seaState;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            seaState = Mathf.Lerp(startState, target, elapsed / duration);
            yield return null;
        }

        seaState = target;
    }
}
