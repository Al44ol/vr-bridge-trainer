// ============================================================
// OceanRadialMesh.cs — Радиальный меш океана до горизонта
//
// Заменяет OceanSetup (плоскость 500×500 м). Меш — набор колец
// вокруг центра (судна):
//   • внутренний диск (до innerRadius): кольца через innerStep метров,
//     число сегментов уменьшается вдвое к центру (швы 2:1 без трещин);
//   • внешняя часть: лог-полярная сетка — радиальный шаг растёт вместе
//     с дуговым, до outerRadius.
// При настройках по умолчанию: шаг ≤ 1 м в радиусе 50 м,
// ~200 м на краю (10 км), ≈ 98 тыс. вершин.
//
// Объект океана не должен быть дочерним к судну: он стоит в начале
// координат, а движение судна передаётся фазами волн (OceanWaves).
// Плотность сетки публикуется в шейдер, чтобы гасить волны,
// которые сетка уже не может отобразить (иначе — рябь вдали).
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(OceanWaves))]
public class OceanRadialMesh : MonoBehaviour
{
    [Header("Геометрия")]
    [Tooltip("Радиус океана, м")]
    public float outerRadius = 10000f;
    [Tooltip("Радиус плотной зоны вокруг судна, м")]
    public float innerRadius = 50f;
    [Tooltip("Шаг колец в плотной зоне, м")]
    public float innerStep = 1f;
    [Tooltip("Число сегментов по окружности во внешней части (кратно 32)")]
    public int segments = 320;
    [Tooltip("Минимальное число сегментов у центра")]
    public int minCenterSegments = 8;

    [Header("Отсечение")]
    [Tooltip("Запас bounds по вертикали, м (волны + качка)")]
    public float boundsHeight = 200f;

    private Mesh mesh;

    private static readonly int MinStepId   = Shader.PropertyToID("_OceanMeshMinStep");
    private static readonly int AngleStepId = Shader.PropertyToID("_OceanMeshAngleStep");

    public int VertexCount => mesh != null ? mesh.vertexCount : 0;

    void OnEnable()
    {
        Build();
    }

    void OnDisable()
    {
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }

    void OnValidate()
    {
        segments = Mathf.Max(32, segments / 32 * 32);
        minCenterSegments = Mathf.Max(3, minCenterSegments);
        innerStep = Mathf.Max(0.1f, innerStep);
        innerRadius = Mathf.Max(innerStep * 2f, innerRadius);
        outerRadius = Mathf.Max(innerRadius * 2f, outerRadius);
    }

    public void Build()
    {
        // Список колец: радиус и число сегментов
        var radii = new List<float>();
        var counts = new List<int>();

        // Внутренний диск: кольца через innerStep, сегменты делятся на 2 к центру
        int prevCount = 0;
        for (float r = innerStep; r < innerRadius - innerStep * 0.5f; r += innerStep)
        {
            int m = Mathf.Max(0, Mathf.FloorToInt(Mathf.Log(innerRadius / r, 2f)));
            int n = segments >> Mathf.Min(m, 30);
            n = Mathf.Max(n, minCenterSegments);
            if (prevCount > 0)
                n = Mathf.Clamp(n, prevCount, prevCount * 2);   // только швы 1:1 или 1:2
            radii.Add(r);
            counts.Add(n);
            prevCount = n;
        }

        // Внешняя часть: лог-полярная сетка, шаг кольца = дуговому шагу
        float growth = 1f + 2f * Mathf.PI / segments;
        float rOuter = innerRadius;
        while (true)
        {
            int n = prevCount > 0 ? Mathf.Clamp(segments, prevCount, prevCount * 2) : segments;
            radii.Add(rOuter);
            counts.Add(n);
            prevCount = n;
            if (rOuter >= outerRadius) break;
            rOuter = Mathf.Min(rOuter * growth, outerRadius);
        }

        // Вершины: центр + кольца
        int vertCount = 1;
        foreach (int n in counts) vertCount += n;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var ringStart = new int[radii.Count];

        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        int v = 1;
        for (int ring = 0; ring < radii.Count; ring++)
        {
            ringStart[ring] = v;
            int n = counts[ring];
            float r = radii[ring];
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                vertices[v] = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));
                normals[v] = Vector3.up;
                v++;
            }
        }

        // Треугольники (по часовой стрелке при взгляде сверху — лицом вверх)
        var tris = new List<int>(vertCount * 6);

        // Веер от центра к первому кольцу
        int n0 = counts[0];
        for (int i = 0; i < n0; i++)
        {
            tris.Add(0);
            tris.Add(ringStart[0] + (i + 1) % n0);
            tris.Add(ringStart[0] + i);
        }

        // Сшивка соседних колец (число сегментов na и nb, nb = na или 2·na)
        for (int ring = 0; ring < radii.Count - 1; ring++)
        {
            int na = counts[ring], nb = counts[ring + 1];
            int sa = ringStart[ring], sb = ringStart[ring + 1];
            int ia = 0, ib = 0;
            while (ia < na || ib < nb)
            {
                // Сравниваем углы следующих вершин: (ia+1)/na против (ib+1)/nb
                bool advanceA = ib >= nb || (ia < na && (long)(ia + 1) * nb < (long)(ib + 1) * na);
                if (advanceA)
                {
                    tris.Add(sa + ia % na);
                    tris.Add(sa + (ia + 1) % na);
                    tris.Add(sb + ib % nb);
                    ia++;
                }
                else
                {
                    tris.Add(sa + ia % na);
                    tris.Add(sb + (ib + 1) % nb);
                    tris.Add(sb + ib % nb);
                    ib++;
                }
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "OceanRadialMesh" };
            mesh.hideFlags = HideFlags.DontSave;   // не сохранять сгенерированный меш в сцену
        }
        mesh.Clear();
        mesh.indexFormat = vertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.SetTriangles(tris, 0, false);
        // Bounds с запасом, чтобы смещённые шейдером вершины не отсекались
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(outerRadius * 2f, boundsHeight, outerRadius * 2f));

        GetComponent<MeshFilter>().sharedMesh = mesh;

        // Плотность сетки для подавления волн, которые сетка не может отобразить:
        // шаг(r) ≈ max(innerStep, r · 2π/segments)
        Shader.SetGlobalFloat(MinStepId, innerStep);
        Shader.SetGlobalFloat(AngleStepId, 2f * Mathf.PI / segments);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.4f, 0.7f, 0.5f);
        DrawCircle(innerRadius);
        DrawCircle(outerRadius);
    }

    private void DrawCircle(float r)
    {
        Vector3 c = transform.position;
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= 64; i++)
        {
            float a = 2f * Mathf.PI * i / 64;
            Vector3 p = c + new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
#endif
}
