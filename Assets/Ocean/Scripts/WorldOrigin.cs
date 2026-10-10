// ============================================================
// WorldOrigin.cs — Плавающее начало координат
//
// Судно и XR Origin всегда находятся около (0,0,0) сцены.
// «Истинная» позиция судна в мире хранится здесь в double
// (метры, восток/север) — float на десятках километров теряет точность.
//
// Система координат сцены: +X = восток, +Z = север.
// Внимание: нос модели судна смотрит в −Z (BowTip на z ≈ −23.5).
//
// Позицию задаёт навигация (на шаге A2 — ShipNavigation) через
// ShipPosition / SetShipPosition. Пока навигации нет — временный ход
// от машинного телеграфа (SpeedFactor · maxSpeedKnots) и отладка стрелками
// (Shift — быстрее).
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;

// WorldPosition (позиция в мире, double) с B1 лежит в Core/WorldPosition.cs.

[DefaultExecutionOrder(-100)]
public class WorldOrigin : MonoBehaviour
{
    public static WorldOrigin Instance { get; private set; }

    [Header("Истинная позиция судна (метры)")]
    [SerializeField] private double east;
    [SerializeField] private double north;

    [Tooltip("Судно (для определения направления носа). Если пусто — нос смотрит в −Z.")]
    public Transform ship;
    [Tooltip("Ось носа в локальных координатах судна. У текущей модели нос смотрит в −Z.")]
    public Vector3 bowLocalAxis = Vector3.back;

    [Header("Временный ход от телеграфа (до A2)")]
    [Tooltip("Если задан — судно идёт вперёд/назад по носу со скоростью SpeedFactor · maxSpeedKnots.")]
    public EngineTelegraph telegraph;
    [Tooltip("Скорость при «Full Ahead», узлы")]
    public float maxSpeedKnots = 15f;
    [Tooltip("Множитель для проверки (например 20 — чтобы быстро уйти на 20+ км). 1 = реальная скорость.")]
    public float testSpeedMultiplier = 1f;
    [Tooltip("Постоянная времени разгона/торможения, с")]
    public float speedTimeConstant = 4f;

    [Header("Отладка: движение стрелками")]
    [Tooltip("↑/↓ — вдоль носа судна, ←/→ — поперёк. Shift — быстрая скорость.")]
    public bool debugKeyboard = true;
    [Tooltip("Обычная скорость, м/с")]
    public float debugSpeed = 10f;
    [Tooltip("Скорость с Shift, м/с")]
    public float debugFastSpeed = 100f;

    /// <summary>
    /// Истинная позиция судна. Точка сцены (0,0,0) соответствует этой позиции.
    /// </summary>
    public WorldPosition ShipPosition
    {
        get => new WorldPosition(east, north);
        set { east = value.East; north = value.North; }
    }

    public void SetShipPosition(double eastMeters, double northMeters)
    {
        east = eastMeters;
        north = northMeters;
    }

    /// <summary>
    /// Мировая позиция → координаты сцены (относительно судна), y = 0.
    /// </summary>
    public Vector3 WorldToScene(WorldPosition p)
    {
        return new Vector3((float)(p.East - east), 0f, (float)(p.North - north));
    }

    /// <summary>
    /// Координаты сцены → мировая позиция.
    /// </summary>
    public WorldPosition SceneToWorld(Vector3 scenePos)
    {
        return new WorldPosition(east + scenePos.x, north + scenePos.z);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WorldOrigin] В сцене несколько WorldOrigin — используется первый.", this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Текущая скорость хода от телеграфа, м/с (вперёд по носу — положительная).</summary>
    public float TelegraphSpeed => telegraphSpeed;

    /// <summary>
    /// Скорость судна относительно воды вдоль носа, м/с (назад — отрицательная).
    /// Используется для пенного следа и буруна. На шаге A2 её задаёт навигация.
    /// </summary>
    public float ShipSpeed { get; set; }

    /// <summary>
    /// Позицию и скорость задаёт внешний модуль (ShipNavigation, шаг A2):
    /// временный ход от телеграфа и отладочные стрелки отключены.
    /// </summary>
    public bool ExternalControl { get; set; }

    private const float KnotsToMs = 0.514444f;
    private float telegraphSpeed;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // Навигация (A2) сама задаёт позицию и скорость
        if (ExternalControl)
            return;

        // Направление носа в горизонтальной плоскости
        Vector3 bow = ship != null ? ship.TransformDirection(bowLocalAxis) : bowLocalAxis;
        bow.y = 0f;
        if (bow.sqrMagnitude < 1e-6f)
            bow = Vector3.back;
        bow.Normalize();
        Vector3 starboard = Vector3.Cross(Vector3.up, bow);

        Vector3 move = Vector3.zero;

        // Ход от телеграфа с плавным разгоном (экспоненциальное приближение к заданной скорости)
        if (telegraph != null)
        {
            float target = telegraph.SpeedFactor * maxSpeedKnots * KnotsToMs * testSpeedMultiplier;
            float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, speedTimeConstant));
            telegraphSpeed = Mathf.Lerp(telegraphSpeed, target, blend);
            move += bow * telegraphSpeed;
        }

        // Отладка с клавиатуры
        var kb = Keyboard.current;
        if (debugKeyboard && kb != null)
        {
            float forward = 0f, right = 0f;
            if (kb.upArrowKey.isPressed)    forward += 1f;
            if (kb.downArrowKey.isPressed)  forward -= 1f;
            if (kb.rightArrowKey.isPressed) right   += 1f;
            if (kb.leftArrowKey.isPressed)  right   -= 1f;
            if (forward != 0f || right != 0f)
            {
                float speed = kb.shiftKey.isPressed ? debugFastSpeed : debugSpeed;
                move += (bow * forward + starboard * right).normalized * speed;
            }
        }

        ShipSpeed = Vector3.Dot(move, bow);
        east  += (double)move.x * dt;
        north += (double)move.z * dt;
    }
}
