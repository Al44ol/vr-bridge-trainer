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
    private const float ZoneMargin = 0.35f, ZoneTopMargin = 0.4f;
    /// <summary>Часть тумбы в плане не больше, м (пол, пульты у окон — больше).</summary>
    private const float MaxPartFootprint = 0.9f;

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
        float floorY = FindFloor(ship, rig, wheel.transform, log);
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

        // Стойка из модели мостика и что скрыть. Часть модели — тумба, если: середина в плане внутри объёма
        // органов управления с запасом, в плане не больше MaxPartFootprint (пол, пульты у окон — больше),
        // сверху не выше органов управления + запас, снизу — от пола (столб может уходить ниже пола игрока).
        // 12.10.2026: прежнее правило «целиком внутри объёма» отсеяло столб тумбы (Cylinder.1526, 1,29 м).
        ShipTypeSwitcher old = ship.GetComponent<ShipTypeSwitcher>();
        var forceKeep = new HashSet<Renderer>(old != null && old.forceKeep != null ? old.forceKeep : new Renderer[0]);
        var forceHide = new HashSet<Renderer>(old != null && old.forceHide != null ? old.forceHide : new Renderer[0]);
        var kept = new List<Renderer>();
        var hide = new List<Renderer>();
        var nearMiss = new List<string>();
        Vector3 zMin = zone.Min - new Vector3(ZoneMargin, 0f, ZoneMargin), zMax = zone.Max + new Vector3(ZoneMargin, ZoneTopMargin, ZoneMargin);
        zMin.y = floorY - 0.8f;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
        {
            if (IsUnder(r.transform, keepRoots)) continue;
            if (forceKeep.Contains(r)) { kept.Add(r); continue; }
            if (forceHide.Contains(r)) { hide.Add(r); continue; }
            if (bridgeModel != null && r.transform.IsChildOf(bridgeModel))
            {
                var b = new LocalBox();
                b.AddRenderer(r, ship);
                if (b.Valid && IsConsolePart(b, zMin, zMax)) { kept.Add(r); continue; }
                if (b.Valid && NearZone(b, zMin, zMax, 1f))
                    nearMiss.Add($"{r.name} ({Size(b)}, y {b.Min.y:F2}…{b.Max.y:F2}, {Offset(b, zMin, zMax):F2} м от стойки)");
            }
            hide.Add(r);
        }
        log.AppendLine($"  стойка из модели мостика: {kept.Count} частей — " + string.Join(", ", kept.ConvertAll(r => { LocalBox b = BoxOf(r, ship); return $"{r.name} ({Size(b)}, y {b.Min.y:F2}…{b.Max.y:F2})"; })));
        if (forceKeep.Count + forceHide.Count > 0) log.AppendLine($"  ручная правка: оставить {forceKeep.Count}, скрыть {forceHide.Count}");
        if (kept.Count == 0) log.AppendLine("  ! ни одной части модели не попало в объём стойки — выделить тумбу в Scene View и Tools/VR Bridge/Fast Boat: оставить выделенное");
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

    // ─────────── Ручная правка стойки ───────────
    // Выделить в Scene View / Hierarchy части модели мостика (или их родителя) и выбрать пункт:
    // рендеры переносятся между hideOnBoat и keptConsole и запоминаются (forceKeep / forceHide) —
    // повторный Setup Fast Boat правку не теряет. После правки — Ctrl+S.
    private const string KeepMenu = "Tools/VR Bridge/Fast Boat — оставить выделенное на катере";
    private const string HideMenu = "Tools/VR Bridge/Fast Boat — скрыть выделенное на катере";
    private const string PreviewMenu = "Tools/VR Bridge/Fast Boat — предпросмотр стойки (Scene View)";
    private static bool previewing;

    [MenuItem(KeepMenu)] private static void KeepSelected() => MoveSelected(true);
    [MenuItem(HideMenu)] private static void HideSelected() => MoveSelected(false);
    [MenuItem(KeepMenu, true)] private static bool ValidateKeep() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;
    [MenuItem(HideMenu, true)] private static bool ValidateHide() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;

    private static void MoveSelected(bool keep)
    {
        ShipTypeSwitcher sw = FindInScene<ShipTypeSwitcher>(SceneManager.GetActiveScene());
        if (sw == null) { Debug.LogWarning("[Fast Boat] ShipTypeSwitcher не найден — сначала Setup Fast Boat."); return; }
        var picked = new List<Renderer>();
        foreach (GameObject g in Selection.gameObjects)
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
                if (r.transform.IsChildOf(sw.transform) && (sw.boatRoot == null || !r.transform.IsChildOf(sw.boatRoot.transform)) && !picked.Contains(r))
                    picked.Add(r);
        if (picked.Count == 0) { Debug.LogWarning("[Fast Boat] В выделении нет рендеров судна."); return; }

        Undo.RecordObject(sw, keep ? "Fast Boat: оставить" : "Fast Boat: скрыть");
        var hide = new List<Renderer>(sw.hideOnBoat);
        var kept = new List<Renderer>(sw.keptConsole);
        var fk = new List<Renderer>(sw.forceKeep ?? new Renderer[0]);
        var fh = new List<Renderer>(sw.forceHide ?? new Renderer[0]);
        foreach (Renderer r in picked)
        {
            (keep ? hide : kept).Remove(r);
            (keep ? fh : fk).Remove(r);
            List<Renderer> to = keep ? kept : hide, force = keep ? fk : fh;
            if (!to.Contains(r)) to.Add(r);
            if (!force.Contains(r)) force.Add(r);
        }
        sw.hideOnBoat = hide.ToArray();
        sw.keptConsole = kept.ToArray();
        sw.forceKeep = fk.ToArray();
        sw.forceHide = fh.ToArray();
        EditorUtility.SetDirty(sw);
        EditorSceneManager.MarkSceneDirty(sw.gameObject.scene);
        if (previewing) { previewing = false; TogglePreview(); }
        Debug.Log($"[Fast Boat] {(keep ? "Оставлено на катере" : "Скрыто на катере")}: {picked.Count} — " +
                  string.Join(", ", picked.ConvertAll(r => r.name)) + ". Сохраните сцену (Ctrl+S).");
    }

    // Предпросмотр: в Scene View скрыть то, что скрывается на катере (видимость Scene View — не сохраняется в сцену)
    [MenuItem(PreviewMenu)]
    private static void TogglePreview()
    {
        ShipTypeSwitcher sw = FindInScene<ShipTypeSwitcher>(SceneManager.GetActiveScene());
        if (sw == null) { Debug.LogWarning("[Fast Boat] ShipTypeSwitcher не найден — сначала Setup Fast Boat."); return; }
        var objs = new List<GameObject>();
        foreach (Renderer r in sw.hideOnBoat) if (r != null) objs.Add(r.gameObject);
        foreach (Renderer r in sw.keptConsole) if (r != null) SceneVisibilityManager.instance.Show(r.gameObject, false);
        previewing = !previewing;
        if (previewing) SceneVisibilityManager.instance.Hide(objs.ToArray(), false);
        else SceneVisibilityManager.instance.Show(objs.ToArray(), false);
        Menu.SetChecked(PreviewMenu, previewing);
        Debug.Log(previewing
            ? $"[Fast Boat] Предпросмотр: в Scene View скрыто {objs.Count} рендеров мостика — видна стойка (катер в редакторе выключен). Лишнее/недостающее — выделить и «Fast Boat — скрыть/оставить выделенное»."
            : "[Fast Boat] Предпросмотр выключен.");
    }

    // ─────────── Пол ───────────
    // Пол, на котором игрок стоит в Play Mode: у XR Rig гравитация, он опускается на ближайший коллайдер
    // под собой. Положение XR Origin в сцене может быть выше (12.10.2026: 0,14 м — палуба катера вышла
    // выше настоящего пола, стойка оказалась «низко»). Луч вниз в точке игрока, мимо коллайдеров самого Rig.
    private static float FindFloor(Transform ship, Transform rig, Transform wheel, StringBuilder log)
    {
        Vector3 at = rig != null ? rig.position : wheel.position;
        float rigY = ship.InverseTransformPoint(at).y;
        Physics.SyncTransforms();
        Vector3 from = new Vector3(at.x, Mathf.Max(at.y, wheel.position.y) + 1f, at.z);
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            if (rig != null && h.collider.transform.IsChildOf(rig)) continue;
            if (!h.collider.transform.IsChildOf(ship)) continue;
            float y = ship.InverseTransformPoint(h.point).y;
            if (y > rigY + 0.3f) continue;   // не пол, а что-то над игроком (подволок, панель)
            log.AppendLine($"  пол под игроком: y = {y:F2} (Ship, коллайдер «{h.collider.name}»); XR Origin в сцене — y = {rigY:F2}");
            return y;
        }
        log.AppendLine($"  ! коллайдер пола под игроком не найден — пол по XR Origin, y = {rigY:F2}");
        return rigY;
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

    // Часть тумбы: середина в плане — в объёме стойки, в плане небольшая, по высоте — от пола до верха органов управления
    private static bool IsConsolePart(LocalBox b, Vector3 min, Vector3 max)
    {
        Vector3 c = 0.5f * (b.Min + b.Max), s = b.Max - b.Min;
        return c.x >= min.x && c.x <= max.x && c.z >= min.z && c.z <= max.z
            && s.x <= MaxPartFootprint && s.z <= MaxPartFootprint
            && b.Min.y >= min.y && b.Max.y <= max.y;
    }

    // Расстояние в плане от середины части до объёма стойки, м
    private static float Offset(LocalBox b, Vector3 min, Vector3 max)
    {
        Vector3 c = 0.5f * (b.Min + b.Max);
        float dx = Mathf.Max(0f, Mathf.Max(min.x - c.x, c.x - max.x));
        float dz = Mathf.Max(0f, Mathf.Max(min.z - c.z, c.z - max.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

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
