// ============================================================
// SetupFastBoat.cs — Быстроходный катер (запрос 15)
//
// Tools/VR Bridge/Setup Fast Boat (повторный запуск безопасен):
//   1. ассет Assets/Ocean/Settings/ShipParameters_FastBoat (ShipPresets.FillFastBoat; если уже есть —
//      значения не трогаются, правки в инспекторе сохраняются);
//   2. материалы и модель корпуса (BoatMeshes) — Assets/Ocean/Materials/FastBoat/ (модель
//      перестраивается при каждом запуске);
//   3. Ship/FastBoat — корпус катера (выключен; включает ShipTypeSwitcher), коробки HullBox/BowBox
//      для шейдера воды, коллайдер палубы. Рулевая стойка мостика оказывается на 0,4 L от транца,
//      палуба — на высоте пола мостика (с катером судно опускается: палуба ≈ 1,2 м над водой);
//   4. рулевая стойка: части модели МОСТИКИ внутри объёма органов управления (штурвал, телеграф,
//      тифон, тумблер «СВЕТ», NavDisplay, панель погоды) с запасом — остаются на катере; всё
//      остальное с рендерами под Ship (кроме органов управления, XR Rig, меню и панелей) — скрывается.
//      Списки печатаются в консоль; если кусок стойки пропал или осталось лишнее — поправить
//      массивы keptConsole / hideOnBoat у Ship → ShipTypeSwitcher;
//   5. ShipTypeSwitcher на Ship: параметры, коробки, центры качки, сдвиг по высоте, лампы рубки и
//      укрытие от дождя (выключаются), места отсветов своих огней на катере.
// После запуска — Ctrl+S. Проверка в Play Mode: меню A → «Судно »» → «Тип судна».
// ============================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupFastBoat
{
    private const string ParametersPath = "Assets/Ocean/Settings/" + ShipPresets.FastBoatAssetName + ".asset";
    private const string BoatFolder = "Assets/Ocean/Materials/FastBoat";
    private const string MeshPath = BoatFolder + "/FastBoat_Hull.asset";
    /// <summary>Палуба катера над ватерлинией, м.</summary>
    private const float DeckHeight = 1.2f;
    /// <summary>Рулевая стойка — на этой доле длины от транца.</summary>
    private const float ConsoleStation = 0.4f;
    /// <summary>Запас объёма стойки по горизонтали и вверх, м.</summary>
    private const float ZoneMargin = 0.3f, ZoneTopMargin = 0.4f;

    [MenuItem("Tools/VR Bridge/Setup Fast Boat")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Fast Boat] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup Fast Boat]\n");
        Scene scene = SceneManager.GetActiveScene();

        ShipMotion motion = FindInScene<ShipMotion>(scene);
        ShipNavigation navigation = FindInScene<ShipNavigation>(scene);
        ShipWaterInteraction water = FindInScene<ShipWaterInteraction>(scene);
        SteeringWheel wheel = FindInScene<SteeringWheel>(scene);
        Transform rig = FindRig(scene);
        if (motion == null || navigation == null || water == null || wheel == null)
        {
            Debug.LogError("[Setup Fast Boat] Не найдены ShipMotion / ShipNavigation / ShipWaterInteraction / SteeringWheel — сцена не BridgeScene?");
            return;
        }
        Transform ship = motion.transform;
        if (Vector3.Distance(ship.lossyScale, Vector3.one) > 1e-3f)
            log.AppendLine($"  ! масштаб Ship {ship.lossyScale} ≠ 1 — высоты катера могут быть неточны");

        // ── 1. Параметры ──
        var p = AssetDatabase.LoadAssetAtPath<ShipParameters>(ParametersPath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<ShipParameters>();
            ShipPresets.FillFastBoat(p);
            AssetDatabase.CreateAsset(p, ParametersPath);
            log.AppendLine($"  + {ParametersPath}: L {p.length} м, {p.serviceSpeedKnots} уз, T_φ {p.rollPeriod:F1} с, T_z {p.heavePeriod:F1} с");
        }
        else log.AppendLine($"  = {ParametersPath} уже есть (значения не менялись): L {p.length} м, {p.serviceSpeedKnots} уз");

        // ── 2. Материалы и модель ──
        EnsureFolder(BoatFolder);
        Material[] mats =
        {
            Mat("FastBoat_Hull", new Color(0.92f, 0.93f, 0.95f), 0.55f),
            Mat("FastBoat_Bottom", new Color(0.45f, 0.09f, 0.07f), 0.3f),
            Mat("FastBoat_Deck", new Color(0.58f, 0.6f, 0.6f), 0.15f),
            Mat("FastBoat_Trim", new Color(0.16f, 0.17f, 0.19f), 0.4f),
        };
        Mesh built = BoatMeshes.Build(p.length, p.beam, p.draft, DeckHeight);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) { mesh = built; AssetDatabase.CreateAsset(mesh, MeshPath); }
        else { EditorUtility.CopySerialized(built, mesh); Object.DestroyImmediate(built); }
        log.AppendLine($"  + модель {MeshPath}: {mesh.vertexCount} вершин, {mesh.subMeshCount} подмеша");

        // ── 3. Стойка: объём органов управления (в системе Ship) ──
        float floorY = rig != null ? ship.InverseTransformPoint(rig.position).y : ship.InverseTransformPoint(wheel.transform.position).y - 1f;
        if (rig == null) log.AppendLine("  ! XR Origin не найден — пол принят на 1 м ниже штурвала");
        var controls = new List<Transform> { wheel.transform };
        Transform grip = FindDeep(ship, "WheelGrip");
        if (grip != null) controls.Add(grip);
        Transform telegraphHead = FindDeep(ship, "TelegraphHead");
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        controls.Add(telegraphHead != null ? telegraphHead : telegraph != null ? telegraph.transform : null);
        ShipHorn horn = FindInScene<ShipHorn>(scene);
        if (horn != null) controls.Add(horn.transform);
        BridgeLightSwitch lightSwitch = FindInScene<BridgeLightSwitch>(scene);
        if (lightSwitch != null) controls.Add(lightSwitch.transform);
        BridgeNavDisplay navDisplay = FindInScene<BridgeNavDisplay>(scene);
        if (navDisplay != null) controls.Add(navDisplay.transform);
        PanelToggle weatherPanel = FindInScene<PanelToggle>(scene);
        if (weatherPanel != null) controls.Add(weatherPanel.transform);
        controls.RemoveAll(t => t == null);

        // В объём — только то, что стоит у штурвала (не дальше 2 м по горизонтали): панель, сдвинутая
        // в сторону, не раздует объём на полрубки
        var zone = new LocalBox();
        Vector3 wheelLocal = ship.InverseTransformPoint(wheel.transform.position);
        foreach (Transform c in controls)
        {
            var one = new LocalBox();
            one.Add(c, ship);
            if (!one.Valid) continue;
            Vector3 d = 0.5f * (one.Min + one.Max) - wheelLocal;
            d.y = 0f;
            if (d.magnitude > 2f) { log.AppendLine($"  ! {c.name} в {d.magnitude:F1} м от штурвала — в объём стойки не включён"); continue; }
            zone.Point(one.Min);
            zone.Point(one.Max);
        }
        if (!zone.Valid)
        {
            Debug.LogError("[Setup Fast Boat] Не удалось определить объём рулевой стойки (нет рендеров у органов управления).");
            return;
        }
        Vector3 zoneSize = zone.Max - zone.Min;
        log.AppendLine($"  объём органов управления: {zoneSize.x:F2} × {zoneSize.y:F2} × {zoneSize.z:F2} м ({controls.Count} объектов), пол y = {floorY:F2} (Ship)");
        if (zoneSize.x > 3f || zoneSize.z > 3f)
            log.AppendLine("  ! объём больше 3 м — какой-то орган управления стоит далеко от стойки; проверить keptConsole");

        Transform bridgeModel = FindChild(ship, "МОСТИКИ");
        Transform oldModel = FindChild(ship, "Ship_Bridge_3D_Model");
        if (bridgeModel == null) log.AppendLine("  ! Ship/МОСТИКИ не найден — стойку из модели не выделить");

        // Поддеревья, которые не скрываются: органы управления, XR Rig, меню, панели, свет, катер
        var keepRoots = new HashSet<Transform>();
        void KeepSubtree(Component c)
        {
            if (c == null) return;
            Transform top = c.transform;
            while (top.parent != null && top.parent != ship) top = top.parent;
            // Если орган управления лежит внутри модели мостика — оставить только его самого
            keepRoots.Add(top.parent == ship && top != bridgeModel && top != oldModel ? top : c.transform);
        }
        foreach (Transform c in controls) KeepSubtree(c);
        foreach (BridgeMenu m in ship.GetComponentsInChildren<BridgeMenu>(true)) KeepSubtree(m);
        foreach (ScenarioPanel sp in ship.GetComponentsInChildren<ScenarioPanel>(true)) KeepSubtree(sp);
        if (rig != null && rig.IsChildOf(ship)) KeepSubtree(rig);
        BridgeLighting lighting = FindInScene<BridgeLighting>(scene);
        if (lighting != null && lighting.transform.IsChildOf(ship)) KeepSubtree(lighting);
        InstructorInputs inputs = FindInScene<InstructorInputs>(scene);
        if (inputs != null && inputs.transform.IsChildOf(ship)) KeepSubtree(inputs);
        HornButtonVisual hornVisual = FindInScene<HornButtonVisual>(scene);
        if (hornVisual != null && hornVisual.transform.IsChildOf(ship)) KeepSubtree(hornVisual);

        // ── 4. Катер ──
        Transform boat = FindChild(ship, "FastBoat");
        if (boat == null)
        {
            var go = new GameObject("FastBoat");
            Undo.RegisterCreatedObjectUndo(go, "Create FastBoat");
            go.transform.SetParent(ship, false);
            boat = go.transform;
            log.AppendLine("  + Ship/FastBoat");
        }
        else log.AppendLine("  = Ship/FastBoat уже есть — перестроен");
        keepRoots.Add(boat);

        WorldOrigin origin = FindInScene<WorldOrigin>(scene);
        Vector3 bow = origin != null ? origin.bowLocalAxis : Vector3.back;
        bow.y = 0f;
        bow = bow.sqrMagnitude > 1e-6f ? bow.normalized : Vector3.back;
        Vector3 stbd = Vector3.Cross(Vector3.up, bow);
        Vector3 bridgePivot = motion.pivotLocal;
        Vector3 consoleCenter = 0.5f * (zone.Min + zone.Max);
        // Стойка на ConsoleStation·L от транца → мидель на (0,5 − ConsoleStation)·L впереди стойки; ДП — как у мостика
        Vector3 boatOrigin = stbd * Vector3.Dot(bridgePivot, stbd)
                           + bow * (Vector3.Dot(consoleCenter, bow) + (0.5f - ConsoleStation) * p.length)
                           + Vector3.up * (floorY - DeckHeight);
        Undo.RecordObject(boat, "Setup FastBoat");
        boat.localPosition = boatOrigin;
        boat.localRotation = Quaternion.LookRotation(bow, Vector3.up);   // +Z модели — нос, +X — ПБ
        boat.localScale = Vector3.one;
        MeshFilter mf = GetOrAdd<MeshFilter>(boat);
        MeshRenderer mr = GetOrAdd<MeshRenderer>(boat);
        mf.sharedMesh = mesh;
        mr.sharedMaterials = mats;
        BoxCollider deck = GetOrAdd<BoxCollider>(boat);
        deck.center = new Vector3(0f, DeckHeight - 0.1f, -0.05f * p.length);
        deck.size = new Vector3(0.85f * p.beam, 0.2f, 0.85f * p.length);
        Transform hullBox = Box(boat, "HullBox", new Vector3(0f, 0.5f * (DeckHeight - p.draft), -0.125f * p.length),
                                new Vector3(0.88f * p.beam, DeckHeight + p.draft, 0.75f * p.length));
        Transform bowBox = Box(boat, "BowBox", new Vector3(0f, 0.5f * (DeckHeight - p.draft), 0.36f * p.length),
                               new Vector3(0.5f * p.beam, DeckHeight + p.draft, 0.22f * p.length));
        boat.gameObject.SetActive(false);   // включает ShipTypeSwitcher

        float floorAboveWater = floorY - bridgePivot.y;
        float offset = -(floorAboveWater - DeckHeight);
        log.AppendLine($"  катер: мидель (Ship) {boatOrigin}, пол мостика над водой {floorAboveWater:F2} м → на катере судно ниже на {-offset:F2} м (палуба {DeckHeight} м над водой)");
        if (floorAboveWater < DeckHeight || floorAboveWater > 30f)
            log.AppendLine("  ! высота пола над водой странная — проверить ShipMotion.pivotLocal (центр качки на ватерлинии)");

        // Стойка из модели мостика и что скрыть
        var kept = new List<Renderer>();
        var hide = new List<Renderer>();
        var nearMiss = new List<string>();
        Vector3 zMin = zone.Min - new Vector3(ZoneMargin, 0f, ZoneMargin), zMax = zone.Max + new Vector3(ZoneMargin, ZoneTopMargin, ZoneMargin);
        zMin.y = floorY - 0.15f;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
        {
            if (IsUnder(r.transform, keepRoots)) continue;
            if (bridgeModel != null && r.transform.IsChildOf(bridgeModel))
            {
                var b = new LocalBox();
                b.AddRenderer(r, ship);
                if (b.Valid && Inside(b, zMin, zMax)) { kept.Add(r); continue; }
                if (b.Valid && NearZone(b, zMin, zMax, 1f))
                    nearMiss.Add($"{r.name} ({Size(b)})");
            }
            hide.Add(r);
        }
        log.AppendLine($"  стойка из модели мостика: {kept.Count} частей — " + string.Join(", ", kept.ConvertAll(r => $"{r.name} ({Size(BoxOf(r, ship))})")));
        if (kept.Count == 0) log.AppendLine("  ! ни одной части модели не попало в объём стойки — на катере органы управления будут без тумбы; добавить вручную в keptConsole и убрать из hideOnBoat");
        if (nearMiss.Count > 0) log.AppendLine($"  рядом со стойкой, но скрываются ({nearMiss.Count}): " + string.Join(", ", nearMiss));
        log.AppendLine($"  на катере скрываются рендеров: {hide.Count} (только рендеры, коллайдеры остаются)");

        var deactivate = new List<GameObject>();
        if (lighting != null && lighting.cabinLamps != null)
            foreach (Light l in lighting.cabinLamps) if (l != null) deactivate.Add(l.gameObject);
        Transform shelter = FindDeep(ship, "RainShelter");
        if (shelter != null) deactivate.Add(shelter.gameObject);
        log.AppendLine($"  на катере выключаются: {string.Join(", ", deactivate.ConvertAll(g => g.name))}");

        // Отсветы своих огней — на места огней катера
        var glows = new List<Light>();
        var glowPos = new List<Vector3>();
        if (lighting != null && lighting.ownLightGlows != null)
            foreach (Light l in lighting.ownLightGlows)
            {
                if (l == null) continue;
                Vector3 model;
                if (l.name.Contains("Masthead")) model = BoatMeshes.MastheadLight(p.length, DeckHeight);
                else if (l.name.Contains("Starboard")) model = BoatMeshes.Sidelight(p.length, p.beam, DeckHeight, +1);
                else if (l.name.Contains("Port")) model = BoatMeshes.Sidelight(p.length, p.beam, DeckHeight, -1);
                else { glows.Add(l); glowPos.Add(l.transform.localPosition); continue; }
                glows.Add(l);
                glowPos.Add(l.transform.parent.InverseTransformPoint(boat.TransformPoint(model)));
            }

        // ── 5. Переключатель ──
        ShipTypeSwitcher sw = ship.GetComponent<ShipTypeSwitcher>();
        if (sw == null) { sw = Undo.AddComponent<ShipTypeSwitcher>(ship.gameObject); log.AppendLine("  + Ship/ShipTypeSwitcher"); }
        else log.AppendLine("  = ShipTypeSwitcher уже есть — списки обновлены");
        Undo.RecordObject(sw, "Setup ShipTypeSwitcher");
        sw.bridgeParameters = navigation.parameters;
        sw.boatParameters = p;
        sw.navigation = navigation;
        sw.motion = motion;
        sw.water = water;
        sw.runner = FindInScene<ScenarioRunner>(scene);
        sw.bridgeHullBox = water.hull;
        sw.bridgeBowBox = water.bowTip;
        sw.bridgePivot = bridgePivot;
        sw.boatRoot = boat.gameObject;
        sw.boatHullBox = hullBox;
        sw.boatBowBox = bowBox;
        sw.boatPivot = boatOrigin;
        sw.boatVerticalOffset = offset;
        sw.boatDeckHeight = DeckHeight;
        sw.hideOnBoat = hide.ToArray();
        sw.keptConsole = kept.ToArray();
        sw.deactivateOnBoat = deactivate.ToArray();
        sw.ownLightGlows = glows.ToArray();
        sw.boatGlowPositions = glowPos.ToArray();
        EditorUtility.SetDirty(sw);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        log.Append("Готово. Сохраните сцену (Ctrl+S). Play Mode: меню A → «Судно »» → «Тип судна: < модель 45 м / катер >».");
        Debug.Log(log.ToString());
    }

    // ─────────── Объёмы в системе Ship ───────────
    private struct LocalBox
    {
        public Vector3 Min, Max;
        public bool Valid;

        public void Point(Vector3 p)
        {
            if (!Valid) { Min = Max = p; Valid = true; return; }
            Min = Vector3.Min(Min, p);
            Max = Vector3.Max(Max, p);
        }

        public void Add(Transform t, Transform ship)
        {
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) AddRenderer(r, ship);
            if (t is RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                foreach (Vector3 w in c) Point(ship.InverseTransformPoint(w));
            }
        }

        public void AddRenderer(Renderer r, Transform ship)
        {
            // Границы меша в системе самого объекта (не зависят от того, включён ли рендер)
            MeshFilter f = r.GetComponent<MeshFilter>();
            Bounds b = f != null && f.sharedMesh != null ? f.sharedMesh.bounds : r.localBounds;
            if (b.size == Vector3.zero) return;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Point(ship.InverseTransformPoint(r.transform.TransformPoint(c)));
            }
        }
    }

    private static LocalBox BoxOf(Renderer r, Transform ship)
    {
        var b = new LocalBox();
        b.AddRenderer(r, ship);
        return b;
    }

    // Часть целиком внутри объёма стойки (с запасом)
    private static bool Inside(LocalBox b, Vector3 min, Vector3 max) =>
        b.Min.x >= min.x && b.Max.x <= max.x && b.Min.z >= min.z && b.Max.z <= max.z && b.Min.y >= min.y && b.Max.y <= max.y;

    private static bool NearZone(LocalBox b, Vector3 min, Vector3 max, float d) =>
        b.Max.x >= min.x - d && b.Min.x <= max.x + d && b.Max.z >= min.z - d && b.Min.z <= max.z + d && b.Min.y <= max.y + d;

    private static string Size(LocalBox b)
    {
        Vector3 s = b.Max - b.Min;
        return $"{s.x:F2}×{s.y:F2}×{s.z:F2} м";
    }

    private static bool IsUnder(Transform t, HashSet<Transform> roots)
    {
        for (Transform x = t; x != null; x = x.parent)
            if (roots.Contains(x)) return true;
        return false;
    }

    // ─────────── Объекты и ассеты ───────────
    // Без «??»: в редакторе GetComponent у отсутствующего компонента возвращает «фальшивый null»
    private static T GetOrAdd<T>(Transform t) where T : Component
    {
        T c = t.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(t.gameObject);
        return c;
    }

    private static Transform Box(Transform parent, string name, Vector3 center, Vector3 size)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            t = go.transform;
            t.SetParent(parent, false);
        }
        Undo.RecordObject(t, "Setup " + name);
        t.localPosition = center;
        t.localRotation = Quaternion.identity;
        t.localScale = size;   // ShipWaterInteraction: единичный куб × масштаб
        return t;
    }

    private static Material Mat(string name, Color color, float smoothness)
    {
        string path = BoatFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform c in parent) if (c.name == name) return c;
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
}
