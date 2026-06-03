// ============================================================
// OceanSetup.cs — Генератор меша океана (runtime)
// Создаёт подразделённую плоскость с достаточным количеством
// вершин для корректного отображения волн Герстнера.
//
// Использование:
//   1. Создайте пустой GameObject "Ocean"
//   2. Добавьте этот компонент
//   3. Назначьте материал с шейдером VRBridgeTrainer/OceanSurface
//   4. Нажмите Play
// ============================================================
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanSetup : MonoBehaviour
{
    [Header("Размеры меша")]
    [Tooltip("Количество вершин на сторону. 200 = 40 000 вершин — хватает для PC-VR.")]
    [Range(50, 300)]
    public int resolution = 200;

    [Tooltip("Размер стороны плоскости в метрах. 500 м — горизонт не видно.")]
    public float size = 500f;

    [Header("Материал")]
    [Tooltip("Перетащите сюда материал с шейдером OceanSurface.")]
    public Material oceanMaterial;

    void Awake()
    {
        GenerateMesh();
        ApplyMaterial();
    }

    /// <summary>
    /// Генерация подразделённой плоскости (XZ) с центром в (0, 0, 0).
    /// </summary>
    void GenerateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "OceanMesh";

        // UInt32 нужен если вершин > 65 535
        int vertCount = resolution * resolution;
        if (vertCount > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs      = new Vector2[vertCount];
        Vector3[] normals  = new Vector3[vertCount];

        float halfSize = size * 0.5f;
        float step     = size / (resolution - 1);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                vertices[i] = new Vector3(
                    x * step - halfSize,
                    0f,
                    z * step - halfSize
                );
                uvs[i]     = new Vector2((float)x / (resolution - 1),
                                         (float)z / (resolution - 1));
                normals[i] = Vector3.up;
            }
        }

        // Треугольники
        int quadCount  = (resolution - 1) * (resolution - 1);
        int[] triangles = new int[quadCount * 6];
        int t = 0;

        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int i = z * resolution + x;

                triangles[t++] = i;
                triangles[t++] = i + resolution;
                triangles[t++] = i + 1;

                triangles[t++] = i + 1;
                triangles[t++] = i + resolution;
                triangles[t++] = i + resolution + 1;
            }
        }

        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.normals   = normals;
        mesh.triangles = triangles;

        // Bounds делаем большими, чтобы Unity не обрезал меш при смещении вершин
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 50f, size));

        GetComponent<MeshFilter>().mesh = mesh;
    }

    void ApplyMaterial()
    {
        if (oceanMaterial != null)
        {
            GetComponent<MeshRenderer>().material = oceanMaterial;
        }
        else
        {
            Debug.LogWarning("[OceanSetup] Материал не назначен! Перетащите OceanMaterial в поле Material.");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Для удобства: показываем границы океана в Scene View.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.4f, 0.7f, 0.3f);
        Gizmos.DrawWireCube(transform.position, new Vector3(size, 0.1f, size));
    }
#endif
}
