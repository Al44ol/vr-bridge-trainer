// ============================================================
// SetupA1Ocean.cs — Настройка сцены для шага A1
// Меню: Tools/VR Bridge/Setup A1 Ocean
//
// Делает (идемпотентно — повторный запуск ничего не ломает):
//   1. WorldOrigin — объект с плавающим началом координат;
//   2. отключает старый океан (GameObject с OceanSetup);
//   3. OceanRadial — радиальный меш океана + OceanWaves, в корне сцены;
//   4. far clip plane камер XR Origin → 12 000 м (near не трогаем);
//   5. скайбокс с туманом у горизонта (копия текущих настроек кубмапы).
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SetupA1Ocean
{
    private const string OceanMaterialPath  = "Assets/Ocean/OceanMaterial.mat";
    private const string SkyboxMaterialPath = "Assets/Ocean/SkyboxHorizonFog.mat";
    private const string SkyboxShaderName   = "VRBridgeTrainer/SkyboxHorizonFog";
    private const float  DefaultWaterLevel  = -5f;
    private const float  FarClip            = 12000f;

    [MenuItem("Tools/VR Bridge/Setup A1 Ocean")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup A1] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        var oceanMaterial = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
        if (oceanMaterial == null)
        {
            Debug.LogError($"[Setup A1] Не найден материал воды: {OceanMaterialPath}");
            return;
        }

        Undo.SetCurrentGroupName("Setup A1 Ocean");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Setup A1] Сцена «{scene.name}»:\n");

        // ── 1. WorldOrigin ──
        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        if (origin == null)
        {
            var go = new GameObject("WorldOrigin");
            Undo.RegisterCreatedObjectUndo(go, "Create WorldOrigin");
            origin = Undo.AddComponent<WorldOrigin>(go);
            log.AppendLine("  + создан WorldOrigin");
        }
        else log.AppendLine("  = WorldOrigin уже есть");

        ShipMotion ship = FindInScene<ShipMotion>(scene);
        if (ship != null && origin.ship == null)
        {
            Undo.RecordObject(origin, "Assign ship");
            origin.ship = ship.transform;
            log.AppendLine("  + WorldOrigin.ship = " + ship.name);
        }

        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        if (telegraph != null && origin.telegraph == null)
        {
            Undo.RecordObject(origin, "Assign telegraph");
            origin.telegraph = telegraph;
            log.AppendLine("  + WorldOrigin.telegraph = " + telegraph.name + " (ход от телеграфа)");
        }

        // ── 2. Старый океан ──
        float waterLevel = DefaultWaterLevel;
        OceanSetup oldOcean = FindInScene<OceanSetup>(scene);
        if (oldOcean != null)
        {
            waterLevel = oldOcean.transform.position.y;
            // Awake вызывается и у выключенного компонента, поэтому отключаем весь объект
            if (oldOcean.gameObject.activeSelf || oldOcean.enabled)
            {
                Undo.RecordObject(oldOcean, "Disable OceanSetup");
                oldOcean.enabled = false;
                Undo.RecordObject(oldOcean.gameObject, "Disable old Ocean");
                oldOcean.gameObject.SetActive(false);
                log.AppendLine($"  + старый океан «{oldOcean.name}» отключён (не удалён)");
            }
            else log.AppendLine($"  = старый океан «{oldOcean.name}» уже отключён");
        }

        // ── 3. Радиальный океан ──
        OceanRadialMesh radial = FindInScene<OceanRadialMesh>(scene);
        if (radial == null)
        {
            var go = new GameObject("OceanRadial");
            Undo.RegisterCreatedObjectUndo(go, "Create OceanRadial");
            go.transform.position = new Vector3(0f, waterLevel, 0f);
            radial = Undo.AddComponent<OceanRadialMesh>(go);   // MeshFilter и MeshRenderer добавятся сами
            log.AppendLine($"  + создан OceanRadial (y = {waterLevel})");
        }
        else log.AppendLine("  = OceanRadial уже есть");

        GameObject oceanGo = radial.gameObject;
        if (oceanGo.transform.parent != null)
        {
            Undo.SetTransformParent(oceanGo.transform, null, "Unparent OceanRadial");
            log.AppendLine("  + OceanRadial вынесен в корень сцены (не должен быть дочерним к судну)");
        }
        if (!oceanGo.activeSelf)
        {
            Undo.RecordObject(oceanGo, "Activate OceanRadial");
            oceanGo.SetActive(true);
        }

        var renderer = oceanGo.GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial != oceanMaterial || renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows)
        {
            Undo.RecordObject(renderer, "Setup ocean renderer");
            renderer.sharedMaterial = oceanMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            log.AppendLine("  + материал OceanMaterial назначен");
        }

        OceanWaves waves = oceanGo.GetComponent<OceanWaves>();
        if (waves == null)
        {
            waves = Undo.AddComponent<OceanWaves>(oceanGo);
            log.AppendLine("  + добавлен OceanWaves");
        }
        if (waves.oceanMaterial != oceanMaterial || waves.worldOrigin != origin)
        {
            Undo.RecordObject(waves, "Setup OceanWaves");
            waves.oceanMaterial = oceanMaterial;
            waves.worldOrigin = origin;
            log.AppendLine("  + OceanWaves: ссылки на материал и WorldOrigin");
        }
        radial.Build();
        log.AppendLine($"    вершин в меше океана: {radial.VertexCount}");

        // ── 4. Far clip plane камер ──
        int camsChanged = 0, camsTotal = 0;
        foreach (Camera cam in FindAllInScene<Camera>(scene))
        {
            if (!cam.CompareTag("MainCamera"))
                continue;
            camsTotal++;
            if (cam.farClipPlane < FarClip)
            {
                Undo.RecordObject(cam, "Set far clip");
                float old = cam.farClipPlane;
                cam.farClipPlane = FarClip;
                // Камера внутри префаба XR Origin — фиксируем как override в сцене
                PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
                camsChanged++;
                log.AppendLine($"  + камера «{cam.name}»: far {old} → {FarClip} м (near {cam.nearClipPlane} без изменений)");
            }
        }
        if (camsTotal == 0)
            Debug.LogWarning("[Setup A1] Не найдена камера с тегом MainCamera — far clip не изменён.");
        else if (camsChanged == 0)
            log.AppendLine($"  = far clip камер уже ≥ {FarClip} м");

        // ── 5. Скайбокс с туманом у горизонта ──
        Shader skyShader = Shader.Find(SkyboxShaderName);
        if (skyShader == null)
        {
            Debug.LogError($"[Setup A1] Не найден шейдер {SkyboxShaderName} — скайбокс не изменён.");
        }
        else
        {
            var skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (skyMaterial == null)
            {
                skyMaterial = new Material(skyShader) { name = "SkyboxHorizonFog" };
                Material current = RenderSettings.skybox;
                if (current != null && current.shader != skyShader)
                {
                    CopyTexture(current, skyMaterial, "_Tex");
                    CopyColor(current, skyMaterial, "_Tint");
                    CopyFloat(current, skyMaterial, "_Exposure");
                    CopyFloat(current, skyMaterial, "_Rotation");
                    log.AppendLine($"  + создан {SkyboxMaterialPath} (настройки скопированы из «{current.name}»)");
                }
                else log.AppendLine($"  + создан {SkyboxMaterialPath}");
                AssetDatabase.CreateAsset(skyMaterial, SkyboxMaterialPath);
                AssetDatabase.SaveAssets();
            }

            if (RenderSettings.skybox != skyMaterial)
            {
                string prev = RenderSettings.skybox != null ? RenderSettings.skybox.name : "нет";
                RenderSettings.skybox = skyMaterial;
                log.AppendLine($"  + скайбокс: «{prev}» → «{skyMaterial.name}» (старый материал не удалён)");
            }
            else log.AppendLine("  = скайбокс уже настроен");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    // ─────────── Поиск в активной сцене (включая неактивные объекты) ───────────
    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    private static List<T> FindAllInScene<T>(Scene scene) where T : Component
    {
        var list = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            list.AddRange(root.GetComponentsInChildren<T>(true));
        return list;
    }

    private static void CopyTexture(Material from, Material to, string prop)
    {
        if (from.HasProperty(prop)) to.SetTexture(prop, from.GetTexture(prop));
    }

    private static void CopyColor(Material from, Material to, string prop)
    {
        if (from.HasProperty(prop)) to.SetColor(prop, from.GetColor(prop));
    }

    private static void CopyFloat(Material from, Material to, string prop)
    {
        if (from.HasProperty(prop)) to.SetFloat(prop, from.GetFloat(prop));
    }
}
