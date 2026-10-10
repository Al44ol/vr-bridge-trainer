// ============================================================
// SetupRain.cs — Дождь в шторм (запрос 1, часть 3)
// Меню: Tools/VR Bridge/Setup Rain
//
// Делает (идемпотентно):
//   1. текстуру капли Assets/Ocean/Textures/RainDrop.png и материал
//      Assets/Ocean/Materials/Rain.mat (URP Particles/Unlit, прозрачный,
//      очередь 3100 — после прозрачной воды). Материал-ассет нужен, чтобы
//      шейдер частиц не вырезался из сборки (раньше был Shader.Find);
//   2. RainShelter — BoxCollider-триггер (слой Ignore Raycast), дочерний к Ship:
//      объём рубки, внутри которого капли уничтожаются. Размер берётся из
//      габаритов модели мостика (лог) — подгоните в Scene View. Если объект
//      уже есть, размер не трогается;
//   3. Rain (корень сцены) с RainEffect: камера XR, WorldOrigin, OceanWaves,
//      материал, RainShelter, уровень воды = y OceanRadial;
//   4. WeatherController.rainEffect → Rain (дождь только в пресете storm).
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupRain
{
    private const string TexturePath = "Assets/Ocean/Textures/RainDrop.png";
    private const string MaterialPath = "Assets/Ocean/Materials/Rain.mat";
    private const int IgnoreRaycastLayer = 2;
    private const float MaxShelterSize = 60f;   // м — больше уже не рубка, а вся модель

    [MenuItem("Tools/VR Bridge/Setup Rain")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Rain] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("Setup Rain");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup Rain] Сцена «{scene.name}»:\n");

        // ── 1. Текстура и материал ──
        Texture2D tex = EnsureTexture(log);
        Material mat = EnsureMaterial(tex, log);

        // ── 2. Объём рубки ──
        ShipMotion ship = FindInScene<ShipMotion>(scene);
        BoxCollider shelter = null;
        if (ship != null) shelter = EnsureShelter(ship.transform, log);
        else log.AppendLine("  ! Ship (ShipMotion) не найден — RainShelter не создан");

        // ── 3. Объект Rain ──
        RainEffect rain = FindInScene<RainEffect>(scene);
        if (rain == null)
        {
            var go = new GameObject("Rain");
            Undo.RegisterCreatedObjectUndo(go, "Setup Rain");
            rain = Undo.AddComponent<RainEffect>(go);
            log.AppendLine("  + Rain (RainEffect) в корне сцены");
        }
        else log.AppendLine("  = RainEffect уже есть на " + rain.name);

        Undo.RecordObject(rain, "Setup Rain");
        rain.followTarget = FindXRCamera(scene);
        rain.worldOrigin = FindInScene<WorldOrigin>(scene);
        rain.oceanWaves = FindInScene<OceanWaves>(scene);
        rain.rainMaterial = mat;
        rain.shelters = shelter != null ? new Collider[] { shelter } : new Collider[0];
        // Первая версия была слишком бледной: обновляем только нетронутые старые значения
        if (Mathf.Approximately(rain.dropWidth, 0.012f) && Mathf.Approximately(rain.brightness, 1.8f)
            && Mathf.Approximately(rain.alpha, 0.35f))
        {
            rain.dropWidth = 0.02f;
            rain.brightness = 3f;
            rain.alpha = 0.6f;
            log.AppendLine("  + капли заметнее: толщина 0.02 м, яркость 3, альфа 0.6");
        }
        OceanRadialMesh ocean = FindInScene<OceanRadialMesh>(scene);
        if (ocean != null) rain.waterLevelY = ocean.transform.position.y;
        log.AppendLine($"  + RainEffect: камера = {Name(rain.followTarget)}, WorldOrigin = {Name(rain.worldOrigin)}, " +
                       $"OceanWaves = {Name(rain.oceanWaves)}, вода y = {rain.waterLevelY:F1} м");
        if (rain.followTarget == null) log.AppendLine("  ! камера XR не найдена — будет Camera.main");

        // ── 4. Погода ──
        WeatherController weather = FindInScene<WeatherController>(scene);
        if (weather != null)
        {
            if (weather.rainEffect != rain)
            {
                Undo.RecordObject(weather, "Setup Rain");
                weather.rainEffect = rain;
                log.AppendLine("  + WeatherController.rainEffect → Rain (дождь только в шторм)");
            }
            else log.AppendLine("  = WeatherController уже связан с дождём");
        }
        else log.AppendLine("  ! WeatherController не найден");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Проверьте размер RainShelter в Scene View и сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    // Мягкое круглое пятно: при растяжении по скорости — штрих с мягкими концами
    private static Texture2D EnsureTexture(StringBuilder log)
    {
        // Генерируется всегда (результат детерминирован): первая версия имела ошибку —
        // Mathf.SmoothStep(a, b, t) в Unity интерполирует между a и b, а не как smoothstep
        // в шейдере, из-за чего непрозрачность в центре была 0.2 вместо 1.
        Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
        const int size = 64;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float edge = Mathf.Clamp01((1f - r) / 0.2f);          // плавно в 0 у края круга
            edge = edge * edge * (3f - 2f * edge);
            float a = Mathf.Exp(-r * r * 3f) * edge;
            t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        byte[] png = t.EncodeToPNG();
        Object.DestroyImmediate(t);
        if (File.Exists(TexturePath) && SameBytes(File.ReadAllBytes(TexturePath), png))
        {
            log.AppendLine("  = текстура капли актуальна");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }
        File.WriteAllBytes(TexturePath, png);
        AssetDatabase.ImportAsset(TexturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        log.AppendLine("  + текстура " + TexturePath + " (центр непрозрачный)");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    private static Material EnsureMaterial(Texture2D tex, StringBuilder log)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null)
        {
            if (mat.GetTexture("_BaseMap") != tex || mat.renderQueue < 3050)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_QueueOffset", 50f);
                mat.renderQueue = 3050;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
                log.AppendLine("  + Rain.mat: текстура и очередь 3050 (после воды)");
            }
            else log.AppendLine("  = материал Rain.mat уже есть");
            return mat;
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            log.AppendLine("  ! шейдер URP Particles/Unlit не найден — материал не создан");
            return null;
        }
        mat = new Material(shader) { name = "Rain" };
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0.6f, 0.65f, 0.7f, 0.35f));
        // Прозрачный, альфа-смешивание (как выбор Surface Type = Transparent, Blend = Alpha в инспекторе)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetFloat("_QueueOffset", 50f);
        mat.renderQueue = 3050;   // после воды (Transparent = 3000, ZWrite Off); в рантайме RainEffect ставит 3100
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.CreateAsset(mat, MaterialPath);
        log.AppendLine("  + материал " + MaterialPath);
        return mat;
    }

    private static BoxCollider EnsureShelter(Transform ship, StringBuilder log)
    {
        Transform existing = ship.Find("RainShelter");
        if (existing != null)
        {
            var box = existing.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 s = Vector3.Scale(box.size, existing.lossyScale);
                log.AppendLine($"  = RainShelter уже есть ({s.x:F1} × {s.y:F1} × {s.z:F1} м) — размер не меняю");
                return box;
            }
        }

        // Габариты моделей мостика в системе Ship
        Bounds? chosen = null;
        string chosenName = null;
        foreach (string name in new[] { "МОСТИКИ", "Ship_Bridge_3D_Model" })
        {
            Transform model = ship.Find(name);
            if (model == null) { log.AppendLine($"  · модель {name} не найдена"); continue; }
            Bounds? b = LocalBounds(model, ship);
            if (b == null) { log.AppendLine($"  · {name}: нет мешей"); continue; }
            Vector3 s = Vector3.Scale(b.Value.size, ship.lossyScale);
            log.AppendLine($"  · {name}: {s.x:F1} × {s.y:F1} × {s.z:F1} м, центр {b.Value.center}");
            if (chosen == null && s.x < MaxShelterSize && s.y < MaxShelterSize && s.z < MaxShelterSize)
            {
                chosen = b;
                chosenName = name;
            }
        }

        var go = new GameObject("RainShelter") { layer = IgnoreRaycastLayer };
        Undo.RegisterCreatedObjectUndo(go, "Setup Rain");
        go.transform.SetParent(ship, false);
        var collider = Undo.AddComponent<BoxCollider>(go);
        collider.isTrigger = true;   // XRI по умолчанию игнорирует триггеры, плюс слой Ignore Raycast
        if (chosen != null)
        {
            go.transform.localPosition = chosen.Value.center;
            collider.size = chosen.Value.size;
            log.AppendLine($"  + RainShelter по габаритам {chosenName} — проверьте, что коробка охватывает рубку, " +
                           "но не открытые крылья мостика");
        }
        else
        {
            // Запасной вариант: коробка вокруг места оператора
            Vector3 c = ship.InverseTransformPoint(Camera.main != null ? Camera.main.transform.position : ship.position);
            go.transform.localPosition = c;
            collider.size = new Vector3(12f, 4f, 8f);
            log.AppendLine("  + RainShelter 12 × 4 × 8 м вокруг камеры (модель мостика не подошла) — подгоните вручную");
        }
        return collider;
    }

    private static Bounds? LocalBounds(Transform model, Transform space)
    {
        Bounds? result = null;
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (result == null) result = new Bounds(p, Vector3.zero);
                else { Bounds b = result.Value; b.Encapsulate(p); result = b; }
            }
        }
        return result;
    }

    private static Transform FindXRCamera(Scene scene)
    {
        Camera fallback = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
        {
            if (cam.CompareTag("MainCamera")) return cam.transform;
            if (fallback == null && cam.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>(true) != null) fallback = cam;
        }
        return fallback != null ? fallback.transform : null;
    }

    private static string Name(Object o) => o != null ? o.name : "—";

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }
}
