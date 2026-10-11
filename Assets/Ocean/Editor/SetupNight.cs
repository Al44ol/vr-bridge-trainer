// ============================================================
// SetupNight.cs — Ночь: освещение сцены и мостика (B4)
// Меню: Tools/VR Bridge/Setup B4 Night (идемпотентно). После запуска — Ctrl+S.
//
//   1. URP-ассеты проекта: дополнительные источники света — Per Pixel, до 4 на объект
//      (до B4 выключены: лампы рубки были невозможны). Вода их не считает (свой шейдер).
//   2. Освещение сцены. До B4 BridgeScene ссылалась на запечённые данные шаблона
//      VR Template (Assets/Scenes/SampleScene/LightingData.asset): рассеянный свет,
//      световые пробы и отражения — дневные и чужие, во время работы не пересчитывались.
//      Ссылка снимается (сам ассет не трогается), рассеянный свет — градиент (Trilight,
//      цвета задаёт WeatherController), старые Reflection Probe и Light Probe Group
//      ОТКЛЮЧАЮТСЯ (не удаляются).
//   3. Lighting/Moon — направленный источник луны (без теней, включает WeatherController).
//   4. Lighting/EnvironmentProbe — Reflection Probe «только небо», Realtime, из скрипта
//      (EnvironmentReflection), объём 40 км.
//   5. SkyWeather → WeatherController (ночное небо).
//   6. Ship/BridgeLighting — свет рубки (2 точечных источника под подволоком), подсветка
//      приборов (NavDisplay, полоса глубины, панель погоды, шкала телеграфа — emission
//      включается в материалах Telegraph_Ahead/Astern/Stop с чёрным цветом: днём без изменений),
//      отсвет своих огней (топовый, бортовые).
//   7. Ship/BridgeLightSwitch — тумблер «СВЕТ» рядом с кнопкой гудка (XRSimpleInteractable).
//   8. Ship/SettingsMenu — меню «Параметры» (BridgeMenu, кнопка Y левого контроллера).
// Места ламп, тумблера и отсветов — приблизительные (по XR Origin, штурвалу и кнопке
// гудка): подвинуть мышью в Scene View, повторный запуск их не перезаписывает.
// ============================================================
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupNight
{
    private const string MaterialDir = "Assets/Ocean/Materials/Bridge";
    private const float CeilingHeight = 2.3f;     // подволок над полом рубки, м (приблизительно)

    [MenuItem("Tools/VR Bridge/Setup B4 Night")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup B4] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        WeatherController weather = FindInScene<WeatherController>(scene);
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        if (weather == null || motion == null)
        {
            Debug.LogError("[Setup B4] Не найдены WeatherController или ShipMotion.");
            return;
        }
        Transform ship = motion.transform;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup B4 Night");
        var log = new StringBuilder($"[Setup B4] Сцена «{scene.name}»:\n");

        // ── 1. URP: дополнительные источники ──
        SetupPipelines(log);

        // ── 2. Освещение сцены ──
        LightingDataAsset lda = Lightmapping.lightingDataAsset;
        if (lda != null)
        {
            log.AppendLine($"  − ссылка на запечённые данные «{AssetDatabase.GetAssetPath(lda)}» снята (ассет не удалён; вернуть — окно Lighting → Lighting Data)");
            Lightmapping.lightingDataAsset = null;
        }
        else log.AppendLine("  = запечённых данных освещения нет");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.58f, 0.7f);
        RenderSettings.ambientEquatorColor = new Color(0.325f, 0.39f, 0.44f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.18f);
        log.AppendLine("  = рассеянный свет: градиент (Trilight), цвета — WeatherController");
        Transform lighting = FindRootByName(scene, "Lighting");
        if (lighting == null)
        {
            lighting = new GameObject("Lighting").transform;
            Undo.RegisterCreatedObjectUndo(lighting.gameObject, "Create Lighting");
        }
        DisableChild(lighting, "Reflection Probe", log);
        DisableChild(lighting, "Light Probe Group", log);

        // ── 3. Луна ──
        Light moon = weather.moonLight;
        if (moon == null)
        {
            var go = new GameObject("Moon");
            Undo.RegisterCreatedObjectUndo(go, "Create Moon");
            go.transform.SetParent(lighting, false);
            moon = go.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.None;
            moon.lightmapBakeType = LightmapBakeType.Realtime;
            moon.intensity = 0f;
            moon.color = new Color(0.62f, 0.72f, 0.95f);
            moon.enabled = false;
            log.AppendLine("  + Lighting/Moon — направленный источник луны (без теней)");
        }
        else log.AppendLine("  = луна уже есть");
        Undo.RecordObject(weather, "Setup WeatherController");
        weather.moonLight = moon;
        EditorUtility.SetDirty(weather);

        // ── 4. Отражения ──
        EnvironmentReflection env = FindInScene<EnvironmentReflection>(scene);
        if (env == null)
        {
            var go = new GameObject("EnvironmentProbe");
            Undo.RegisterCreatedObjectUndo(go, "Create EnvironmentProbe");
            go.transform.SetParent(lighting, false);
            ReflectionProbe p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.size = new Vector3(40000f, 40000f, 40000f);
            p.cullingMask = 0;                         // только небо
            p.clearFlags = ReflectionProbeClearFlags.Skybox;
            p.resolution = 128;
            p.importance = 1;
            p.intensity = 1f;
            p.boxProjection = false;
            env = go.AddComponent<EnvironmentReflection>();
            log.AppendLine("  + Lighting/EnvironmentProbe — отражения «только небо», Realtime, из скрипта (40 км)");
        }
        else log.AppendLine("  = EnvironmentProbe уже есть");

        // ── 5. Небо ──
        SkyWeather sky = FindInScene<SkyWeather>(scene);
        if (sky != null)
        {
            Undo.RecordObject(sky, "Setup SkyWeather");
            sky.weather = weather;
            EditorUtility.SetDirty(sky);
            log.AppendLine("  = SkyWeather → WeatherController (ночное небо)");
        }
        else log.AppendLine("  ! SkyWeather не найден — ночного неба не будет (Setup Console and Sky)");

        // ── 6. Освещение мостика ──
        Transform rig = FindRig(scene);
        SteeringWheel wheel = FindInScene<SteeringWheel>(scene);
        ShipHorn horn = FindInScene<ShipHorn>(scene);
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        BridgeNavDisplay nav = FindInScene<BridgeNavDisplay>(scene);

        BridgeLighting bl = FindInScene<BridgeLighting>(scene);
        if (bl == null)
        {
            var go = new GameObject("BridgeLighting");
            Undo.RegisterCreatedObjectUndo(go, "Create BridgeLighting");
            go.transform.SetParent(ship, false);
            bl = go.AddComponent<BridgeLighting>();
            log.AppendLine("  + Ship/BridgeLighting");
        }
        else log.AppendLine("  = BridgeLighting уже есть");
        Undo.RecordObject(bl, "Setup BridgeLighting");

        float floorY = rig != null ? rig.position.y : ship.position.y;
        Vector3 console = wheel != null ? wheel.transform.position : ship.position + ship.forward * -2f;
        Vector3 player = rig != null ? rig.position : console + ship.forward * 2f;
        if (bl.cabinLamps == null || bl.cabinLamps.Length == 0)
        {
            Vector3 c1 = new Vector3(console.x, floorY + CeilingHeight, console.z) + (player - console).WithY(0f) * 0.25f;
            Vector3 c2 = new Vector3(player.x, floorY + CeilingHeight, player.z);
            bl.cabinLamps = new[]
            {
                PointLight("CabinLamp_Console", bl.transform, c1, 4.5f, bl.redColor),
                PointLight("CabinLamp_Center", bl.transform, c2, 4.5f, bl.redColor)
            };
            log.AppendLine($"  + лампы рубки (2) под подволоком, {CeilingHeight} м над полом (подвинуть в Scene View)");
        }
        if (bl.ownLightGlows == null || bl.ownLightGlows.Length == 0)
        {
            // Приблизительно: топовый — над баком впереди рубки, бортовые — на крыльях мостика (B ≈ 8,7 м)
            Vector3 fwd = -ship.forward;                    // нос модели — −Z
            Vector3 stbd = Vector3.Cross(Vector3.up, fwd).normalized;
            // Точка на диаметральной линии (через место игрока) на уровне пульта, на полу рубки
            Vector3 mid = player + fwd * Vector3.Dot(console - player, fwd);
            mid.y = floorY;
            Light mast = PointLight("OwnMastheadGlow", bl.transform, mid + fwd * 6f + Vector3.up * 5.5f, 12f, Color.white);
            mast.intensity = 0.8f;
            Light green = PointLight("OwnSidelightStarboardGlow", bl.transform, mid + stbd * 4.3f + Vector3.up * 1.4f, 3f, new Color(0.2f, 1f, 0.45f));
            green.intensity = 0.6f;
            Light red = PointLight("OwnSidelightPortGlow", bl.transform, mid - stbd * 4.3f + Vector3.up * 1.4f, 3f, new Color(1f, 0.12f, 0.06f));
            red.intensity = 0.6f;
            bl.ownLightGlows = new[] { mast, green, red };
            log.AppendLine("  + отсвет своих огней: топовый (над баком), бортовые (крылья) — подвинуть в Scene View");
        }

        // Панели приборов и сигнализация
        var panels = new System.Collections.Generic.List<RectTransform>();
        var alarms = new System.Collections.Generic.List<RectTransform>();
        if (nav != null && nav.transform is RectTransform navRt)
        {
            panels.Add(navRt);
            Transform depth = FindDeep(nav.transform, "DepthAlarm");
            if (depth is RectTransform depthRt) alarms.Add(depthRt);
        }
        else log.AppendLine("  ! NavDisplay (BridgeNavDisplay) не найден — подсветка без панели курса");
        Transform weatherPanel = FindDeep(ship, "WeatherPanel");
        if (weatherPanel is RectTransform wpRt) panels.Add(wpRt);
        bl.instrumentPanels = panels.ToArray();
        bl.alarmPanels = alarms.ToArray();

        // Шкала телеграфа — emission
        var emitters = new System.Collections.Generic.List<Renderer>();
        if (telegraph != null && telegraph.transform.parent != null)
        {
            Transform scale = FindDeep(telegraph.transform.parent, "Scale");
            if (scale != null)
                foreach (Renderer r in scale.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("Sector_")) { emitters.Add(r); EnableEmission(r.sharedMaterial, log); }
        }
        if (emitters.Count == 0) log.AppendLine("  ! шкала телеграфа (TelegraphHead/…/Scale/Sector_*) не найдена — без свечения");
        bl.instrumentEmitters = emitters.ToArray();
        EditorUtility.SetDirty(bl);
        log.AppendLine($"  = подсветка приборов: панелей {panels.Count}, сигнализаций {alarms.Count}, шкал {emitters.Count}");

        // ── 7. Тумблер ──
        BridgeLightSwitch sw = FindInScene<BridgeLightSwitch>(scene);
        if (sw == null)
        {
            sw = BuildSwitch(ship, horn != null ? horn.transform : null, wheel != null ? wheel.transform : null, log);
            log.AppendLine("  + Ship/BridgeLightSwitch — тумблер «СВЕТ» рядом с кнопкой гудка (подвинуть в Scene View)");
        }
        else log.AppendLine("  = тумблер уже есть");
        Undo.RecordObject(sw, "Setup BridgeLightSwitch");
        sw.lighting = bl;
        EditorUtility.SetDirty(sw);

        // ── 8. Меню «Параметры» (Y) ──
        BridgeMenu settings = null;
        BridgeMenu main = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BridgeMenu m in root.GetComponentsInChildren<BridgeMenu>(true))
            {
                if (m.menuId == BridgeMenu.SettingsMenuId) settings = m;
                else if (string.IsNullOrEmpty(m.menuId)) main = m;
            }
        if (settings == null)
        {
            var go = new GameObject("SettingsMenu");
            Undo.RegisterCreatedObjectUndo(go, "Create SettingsMenu");
            go.transform.SetParent(main != null ? main.transform.parent : ship, false);
            settings = go.AddComponent<BridgeMenu>();
            settings.menuId = BridgeMenu.SettingsMenuId;
            settings.menuTitle = "Параметры мостика";
            settings.toggleBinding = "<XRController>{LeftHand}/secondaryButton";
            if (main != null)
            {
                settings.distanceFromHead = main.distanceFromHead;
                settings.downOffset = main.downOffset;
                settings.canvasScale = main.canvasScale;
            }
            log.AppendLine("  + Ship/SettingsMenu — меню «Параметры» на кнопке Y левого контроллера");
        }
        else log.AppendLine("  = меню «Параметры» уже есть");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        log.AppendLine("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    // URP: дополнительные источники Per Pixel, до 4 на объект — во всех ассетах конвейера проекта
    private static void SetupPipelines(StringBuilder log)
    {
        var assets = new System.Collections.Generic.HashSet<RenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            RenderPipelineAsset a = QualitySettings.GetRenderPipelineAssetAt(i);
            if (a != null) assets.Add(a);
        }
        foreach (RenderPipelineAsset a in assets)
        {
            var so = new SerializedObject(a);
            SerializedProperty mode = so.FindProperty("m_AdditionalLightsRenderingMode");
            SerializedProperty limit = so.FindProperty("m_AdditionalLightsPerObjectLimit");
            if (mode == null || limit == null) { log.AppendLine($"  ! {a.name}: поля дополнительных источников не найдены"); continue; }
            int wasMode = mode.intValue, wasLimit = limit.intValue;
            mode.intValue = 1;                         // LightRenderingMode.PerPixel
            limit.intValue = Mathf.Max(limit.intValue, 4);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(a);
            log.AppendLine($"  = URP «{a.name}»: дополнительные источники {wasMode} → Per Pixel, на объект {wasLimit} → {limit.intValue}");
        }
    }

    private static Light PointLight(string name, Transform parent, Vector3 worldPos, float range, Color color)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.color = color;
        l.intensity = 0.5f;
        l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        return l;
    }

    private static BridgeLightSwitch BuildSwitch(Transform ship, Transform horn, Transform wheel, StringBuilder log)
    {
        Directory.CreateDirectory(MaterialDir);
        Material baseMat = Lit("Bridge_SwitchBase", new Color(0.12f, 0.13f, 0.15f), 0.4f, false, log);
        Material leverMat = Lit("Bridge_SwitchLever", new Color(0.7f, 0.72f, 0.75f), 0.75f, false, log);
        Material indMat = Lit("Bridge_SwitchIndicator", new Color(0.3f, 0.05f, 0.03f), 0.6f, true, log);
        Material labelBack = Lit("Bridge_SwitchLabel", new Color(0.05f, 0.05f, 0.06f), 0.3f, false, log);

        var root = new GameObject("BridgeLightSwitch");
        Undo.RegisterCreatedObjectUndo(root, "Create BridgeLightSwitch");
        root.transform.SetParent(ship, false);
        if (horn != null)
        {
            // Рядом с кнопкой гудка, дальше от телеграфа (≈ 13 см), с тем же наклоном к оператору
            root.transform.localPosition = horn.localPosition + new Vector3(0.13f, 0f, 0f);
            root.transform.localRotation = horn.localRotation;
        }
        else if (wheel != null)
        {
            root.transform.localPosition = wheel.localPosition + new Vector3(0.3f, 0.25f, -0.05f);
            root.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            log.AppendLine("  ! кнопка гудка не найдена — тумблер поставлен по штурвалу");
        }

        // Основание, рычажок на оси, индикатор, надпись
        Part(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.006f, 0f), Quaternion.identity, new Vector3(0.05f, 0.012f, 0.07f), baseMat);
        var pivot = new GameObject("LeverPivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, 0.012f, 0f);
        Part(PrimitiveType.Cylinder, "Lever", pivot, new Vector3(0f, 0.018f, 0f), Quaternion.identity, new Vector3(0.008f, 0.018f, 0.008f), leverMat);
        Part(PrimitiveType.Sphere, "Tip", pivot, new Vector3(0f, 0.037f, 0f), Quaternion.identity, Vector3.one * 0.013f, leverMat);
        Renderer ind = Part(PrimitiveType.Sphere, "Indicator", root.transform, new Vector3(0f, 0.013f, 0.026f), Quaternion.identity, Vector3.one * 0.011f, indMat)
            .GetComponent<Renderer>();
        Part(PrimitiveType.Cube, "LabelPlate", root.transform, new Vector3(0f, 0.002f, 0.05f), Quaternion.identity, new Vector3(0.06f, 0.004f, 0.018f), labelBack);
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(root.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.0045f, 0.05f);
        labelGo.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);   // текстом вверх по наклону пульта
        var tmp = labelGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "СВЕТ";
        tmp.rectTransform.sizeDelta = new Vector2(0.055f, 0.014f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.01f;
        tmp.fontSizeMax = 2f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.85f, 0.85f, 0.8f);

        // Взаимодействие: один коллайдер на корне (как у кнопки гудка)
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.025f, 0.01f);
        col.size = new Vector3(0.06f, 0.05f, 0.09f);
        root.AddComponent<XRSimpleInteractable>();
        BridgeLightSwitch sw = root.AddComponent<BridgeLightSwitch>();
        sw.lever = pivot;
        sw.indicator = ind;
        return sw;
    }

    private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static Material Lit(string name, Color color, float smoothness, bool emission, StringBuilder log)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { log.AppendLine($"  ! шейдер URP Lit не найден для {name}"); return null; }
        m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
        log.AppendLine($"  + материал {path}");
        return m;
    }

    // Emission с чёрным цветом: днём материал выглядит как раньше, ночью цвет задаёт BridgeLighting
    private static void EnableEmission(Material m, StringBuilder log)
    {
        if (m == null || m.IsKeywordEnabled("_EMISSION")) return;
        Undo.RecordObject(m, "Enable Emission");
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        log.AppendLine($"  = emission включён (чёрный) в материале «{m.name}»");
    }

    private static void DisableChild(Transform parent, string name, StringBuilder log)
    {
        Transform t = parent.Find(name);
        if (t == null) return;
        if (!t.gameObject.activeSelf) { log.AppendLine($"  = «{name}» уже отключён"); return; }
        Undo.RecordObject(t.gameObject, "Disable " + name);
        t.gameObject.SetActive(false);
        log.AppendLine($"  − «{parent.name}/{name}» отключён (данные шаблона; не удалён)");
    }

    private static Transform FindRootByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root.transform;
        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static Transform FindRig(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "XROrigin") return mb.transform;
        return null;
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

    private static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
}
