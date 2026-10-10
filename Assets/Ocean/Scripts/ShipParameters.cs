// ============================================================
// ShipParameters.cs — Параметры математической модели судна (A2)
//
// Значения по умолчанию — ЗАГЛУШКА для среднего торгового судна,
// калибруются на шаге A3 (по натурным испытаниям / формулярам манёвренности).
//
// Связь параметров с моделью (подробно — в ShipNavigation.cs):
//   поворот (Номото 1-го порядка): T·dr/dt + r = K·δ,
//       K = K'·U/L,  T = T'·L/U;
//   установившийся диаметр циркуляции D = 2L / (K'·δ_рад):
//       при K' = 0.8, δ = 35° → D ≈ 4.1·L;
//   скорость: du/dt = (u_зад − u)/τ, u_зад = SF·V_эксп (·k_зх на заднем ходу);
//   рулевая машина: |dδ/dt| ≤ ω_δ; СОЛАС гл. II-1 правило 29:
//       35° одного борта → 30° другого не более чем за 28 с → ω_δ ≥ 65/28 ≈ 2.32 °/с.
// ============================================================
using UnityEngine;

[CreateAssetMenu(fileName = "ShipParameters", menuName = "VR Bridge/Ship Parameters")]
public class ShipParameters : ScriptableObject
{
    [Header("Главные размерения (заглушка, калибруется в A3)")]
    [Tooltip("Длина судна L, м")]
    public float length = 150f;

    [Header("Ход")]
    [Tooltip("Эксплуатационная скорость V_эксп (телеграф «Полный вперёд»), узлы")]
    public float serviceSpeedKnots = 15f;
    [Tooltip("Постоянная времени разгона τ_разг, с")]
    public float accelerationTimeConstant = 90f;
    [Tooltip("Постоянная времени торможения τ_торм, с")]
    public float decelerationTimeConstant = 150f;
    [Tooltip("Коэффициент заднего хода k_зх: u_зад = SF·V_эксп·k_зх при SF < 0")]
    [Range(0f, 1f)] public float asternFactor = 0.6f;

    [Header("Поворотливость (модель Номото 1-го порядка)")]
    [Tooltip("Безразмерный коэффициент усиления K' (K = K'·U/L)")]
    public float nomotoKPrime = 0.8f;
    [Tooltip("Безразмерная постоянная времени T' (T = T'·L/U)")]
    public float nomotoTPrime = 2.0f;
    [Tooltip("Минимальная расчётная скорость U_min, м/с — только защита от деления на ноль")]
    public float minCalcSpeed = 0.05f;

    [Header("Рулевая машина")]
    [Tooltip("Максимальная перекладка руля δ_max, градусы на борт")]
    public float maxRudderAngle = 35f;
    [Tooltip("Скорость перекладки ω_δ, °/с. По СОЛАС: (35+30)/28 ≈ 2.32")]
    public float rudderRate = 65f / 28f;

    public const float KnotsToMs = 0.514444f;
    public float ServiceSpeedMs => serviceSpeedKnots * KnotsToMs;

    /// <summary>Теоретический диаметр установившейся циркуляции, м: D = 2L/(K'·δ).</summary>
    public float TheoreticalCircleDiameter(float rudderDeg)
    {
        float d = Mathf.Abs(rudderDeg) * Mathf.Deg2Rad;
        return d > 1e-4f ? 2f * length / (nomotoKPrime * d) : float.PositiveInfinity;
    }
}
