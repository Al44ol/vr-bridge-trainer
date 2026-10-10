// ============================================================
// ShipWaterInteraction.cs — Взаимодействие корпуса судна с водой
//
// Передаёт в шейдер воды (глобальные переменные):
//   • два прямоугольника — горизонтальные контуры корпуса (Hull)
//     и носовой оконечности (BowTip). Внутри контура вода не рисуется,
//     у борта амплитуда волн плавно уменьшается;
//   • систему координат судна (центр, направление носа), выступ носа
//     и кормы, полуширину и скорость хода — для пенного следа за кормой
//     (центральная турбулентная полоса + «усы» волн Кельвина, 19.47°)
//     и буруна у носа.
//
// Контуры берутся из Transform примитивов-кубов (единичный куб × scale),
// каждый кадр — поэтому учитываются качка (ShipMotion) и, на шаге A2, курс.
// ============================================================
using UnityEngine;

[ExecuteAlways]
public class ShipWaterInteraction : MonoBehaviour
{
    [Header("Корпус (кубы-примитивы)")]
    public Transform hull;
    public Transform bowTip;

    [Header("Судно")]
    [Tooltip("Источник направления носа и скорости хода")]
    public WorldOrigin worldOrigin;

    private static readonly int HullA0Id   = Shader.PropertyToID("_OceanHullA0");
    private static readonly int HullA1Id   = Shader.PropertyToID("_OceanHullA1");
    private static readonly int HullB0Id   = Shader.PropertyToID("_OceanHullB0");
    private static readonly int HullB1Id   = Shader.PropertyToID("_OceanHullB1");
    private static readonly int ShipFrameId = Shader.PropertyToID("_OceanShipFrame");
    private static readonly int ShipDimsId  = Shader.PropertyToID("_OceanShipDims");

    // Прямоугольник «вне сцены», если объекта нет
    private static readonly Vector4 FarBox0 = new Vector4(1e6f, 1e6f, 1f, 0f);
    private static readonly Vector4 FarBox1 = new Vector4(0f, 1f, 0.01f, 0.01f);

    void OnEnable()  { Push(); }
    void LateUpdate() { Push(); }

    void OnDisable()
    {
        // Без компонента — корпус не маскирует воду
        Shader.SetGlobalVector(HullA0Id, FarBox0);
        Shader.SetGlobalVector(HullA1Id, FarBox1);
        Shader.SetGlobalVector(HullB0Id, FarBox0);
        Shader.SetGlobalVector(HullB1Id, FarBox1);
        Shader.SetGlobalVector(ShipDimsId, Vector4.zero);
    }

    private void Push()
    {
        PushBox(hull, HullA0Id, HullA1Id);
        PushBox(bowTip, HullB0Id, HullB1Id);

        WorldOrigin origin = worldOrigin != null ? worldOrigin : WorldOrigin.Instance;
        if (hull == null)
        {
            Shader.SetGlobalVector(ShipDimsId, Vector4.zero);
            return;
        }

        // Направление носа в горизонтальной плоскости
        Vector3 bowAxis = origin != null ? origin.bowLocalAxis : Vector3.back;
        Transform shipT = origin != null && origin.ship != null ? origin.ship : hull.parent;
        Vector3 bow3 = shipT != null ? shipT.TransformDirection(bowAxis) : bowAxis;
        Vector2 bow = new Vector2(bow3.x, bow3.z);
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : new Vector2(0f, -1f);
        Vector2 beam = new Vector2(bow.y, -bow.x);

        Vector2 center = new Vector2(hull.position.x, hull.position.z);

        // Выступ носа/кормы и полуширина — по углам обоих прямоугольников
        float fore = 0f, aft = 0f, halfBeam = 0f;
        Extents(hull, center, bow, beam, ref fore, ref aft, ref halfBeam);
        if (bowTip != null)
            Extents(bowTip, center, bow, beam, ref fore, ref aft, ref halfBeam);

        float speed = origin != null ? origin.ShipSpeed : 0f;

        Shader.SetGlobalVector(ShipFrameId, new Vector4(center.x, center.y, bow.x, bow.y));
        Shader.SetGlobalVector(ShipDimsId, new Vector4(fore, aft, halfBeam, speed));
    }

    // Прямоугольник: xy = центр, zw = ось X куба (гориз.); второй вектор: xy = ось Z куба, zw = полуразмеры по X и Z (м)
    private static void PushBox(Transform box, int id0, int id1)
    {
        if (box == null || !box.gameObject.activeInHierarchy)
        {
            Shader.SetGlobalVector(id0, FarBox0);
            Shader.SetGlobalVector(id1, FarBox1);
            return;
        }

        GetBox(box, out Vector2 c, out Vector2 ax, out Vector2 az, out Vector2 half);
        Shader.SetGlobalVector(id0, new Vector4(c.x, c.y, ax.x, ax.y));
        Shader.SetGlobalVector(id1, new Vector4(az.x, az.y, half.x, half.y));
    }

    private static void GetBox(Transform box, out Vector2 center, out Vector2 axisX, out Vector2 axisZ, out Vector2 half)
    {
        center = new Vector2(box.position.x, box.position.z);
        Vector3 s = box.lossyScale;
        Vector3 rx = box.right, rz = box.forward;
        axisX = new Vector2(rx.x, rx.z);
        axisZ = new Vector2(rz.x, rz.z);
        // Длина проекции ребра на горизонталь (крен/дифферент уменьшают её незначительно)
        half = new Vector2(Mathf.Abs(s.x) * 0.5f * axisX.magnitude, Mathf.Abs(s.z) * 0.5f * axisZ.magnitude);
        axisX = axisX.sqrMagnitude > 1e-6f ? axisX.normalized : Vector2.right;
        axisZ = axisZ.sqrMagnitude > 1e-6f ? axisZ.normalized : Vector2.up;
    }

    private static void Extents(Transform box, Vector2 shipCenter, Vector2 bow, Vector2 beam,
                                ref float fore, ref float aft, ref float halfBeam)
    {
        GetBox(box, out Vector2 c, out Vector2 ax, out Vector2 az, out Vector2 half);
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            Vector2 corner = c + ax * (half.x * sx) + az * (half.y * sz) - shipCenter;
            float s = Vector2.Dot(corner, bow);
            fore = Mathf.Max(fore, s);
            aft = Mathf.Max(aft, -s);
            halfBeam = Mathf.Max(halfBeam, Mathf.Abs(Vector2.Dot(corner, beam)));
        }
    }
}
