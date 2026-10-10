// ============================================================
// SetupTelegraphModel.cs — Модель машинного телеграфа
// Меню: Tools/VR Bridge/Setup Telegraph Model
//
// Строит из примитивов телеграф «барабанного» типа в стилистике мостика:
// корпус, барабан со шкалой (зелёный — вперёд, красный — назад, белый — стоп),
// боковой рычаг с рукояткой. Взяться можно только за рукоятку (шар на конце
// рычага); рычаг следует за рукой (EngineTelegraph.angleTracking).
//
// Делает (идемпотентно):
//   1. создаёт TelegraphHead рядом с объектом Telegraph (не внутри: у Telegraph
//      неравномерный масштаб 0.04×0.25×0.04, он исказил бы модель);
//   2. положения рычага — равномерно через 15° (−60° «Полный вперёд» … +60° «Полный назад»);
//   3. EngineTelegraph.leverVisual = ось нового рычага, коллайдер взаимодействия —
//      только рукоятка; старый BoxCollider выключается, старый LeverVisual скрывается.
// Чтобы перестроить модель — удалите TelegraphHead и запустите пункт снова.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class SetupTelegraphModel
{
    private const string HeadName = "TelegraphHead";
    private const string MaterialFolder = "Assets/Ocean/Materials";

    // Размеры (м)
    private const float DrumRadius = 0.12f;
    private const float DrumHalfLength = 0.08f;
    private const float DrumHeight = 0.14f;      // ось барабана над основанием
    private const float LeverSideX = -0.11f;     // рычаг справа от оператора (оператор смотрит в −Z, его правая сторона — −X)
    private const float LeverLength = 0.27f;
    private const float AngleStep = 15f;

    [MenuItem("Tools/VR Bridge/Setup Telegraph Model")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Telegraph] Выйдите из Play Mode и запустите снова.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        EngineTelegraph telegraph = FindInScene<EngineTelegraph>(scene);
        if (telegraph == null)
        {
            Debug.LogError("[Telegraph] В сцене нет EngineTelegraph.");
            return;
        }

        Undo.SetCurrentGroupName("Setup Telegraph Model");
        int undoGroup = Undo.GetCurrentGroup();
        var log = new StringBuilder($"[Telegraph] Сцена «{scene.name}»:\n");

        // После Setup A2.2 Telegraph лежит внутри TelegraphHead, до него — рядом
        Transform parent = telegraph.transform.parent;
        Transform head = null;
        if (parent != null && parent.name == HeadName) { head = parent; parent = head.parent; }
        else if (parent != null) head = parent.Find(HeadName);
        Transform pivot;
        Collider knobCollider;

        // ── 1. Модель ──
        if (head == null)
        {
            head = BuildHead(telegraph, parent, out pivot, out knobCollider);
            log.AppendLine("  + построена модель " + HeadName);
        }
        else
        {
            pivot = head.Find("LeverPivot");
            Transform knob = pivot != null ? pivot.Find("Knob") : null;
            knobCollider = knob != null ? knob.GetComponent<Collider>() : null;
            log.AppendLine("  = модель " + HeadName + " уже есть");
        }
        if (pivot == null || knobCollider == null)
        {
            Debug.LogError("[Telegraph] В TelegraphHead нет LeverPivot/Knob — удалите TelegraphHead и запустите снова.");
            return;
        }

        // ── 2. Положения рычага через 15° ──
        Undo.RecordObject(telegraph, "Setup EngineTelegraph");
        int stopIndex = FindStopIndex(telegraph);
        bool anglesChanged = false;
        for (int i = 0; i < telegraph.positions.Length; i++)
        {
            float angle = (i - stopIndex) * AngleStep;
            if (!Mathf.Approximately(telegraph.positions[i].leverAngle, angle))
            {
                var p = telegraph.positions[i];
                p.leverAngle = angle;
                telegraph.positions[i] = p;
                anglesChanged = true;
            }
        }
        if (anglesChanged) log.AppendLine($"  + положения рычага: шаг {AngleStep}°");

        // ── 3. Связи ──
        if (telegraph.leverVisual != pivot || !telegraph.angleTracking || telegraph.leverRotationAxis != Vector3.right)
        {
            telegraph.leverVisual = pivot;
            telegraph.leverRotationAxis = Vector3.right;
            telegraph.angleTracking = true;
            log.AppendLine("  + EngineTelegraph: leverVisual = LeverPivot, рычаг следует за рукой");
        }
        EditorUtility.SetDirty(telegraph);

        var interactable = telegraph.GetComponent<XRSimpleInteractable>();
        if (interactable != null)
        {
            var so = new SerializedObject(interactable);
            SerializedProperty colliders = so.FindProperty("m_Colliders");
            if (colliders != null && (colliders.arraySize != 1 || colliders.GetArrayElementAtIndex(0).objectReferenceValue != knobCollider))
            {
                colliders.arraySize = 1;
                colliders.GetArrayElementAtIndex(0).objectReferenceValue = knobCollider;
                so.ApplyModifiedProperties();
                log.AppendLine("  + взяться можно только за рукоятку (коллайдер Knob)");
            }
        }

        foreach (var box in telegraph.GetComponents<BoxCollider>())
        {
            if (box.enabled)
            {
                Undo.RecordObject(box, "Disable old telegraph collider");
                box.enabled = false;
                log.AppendLine("  + старый BoxCollider телеграфа выключен (не удалён)");
            }
        }

        Transform oldLever = telegraph.transform.Find("LeverVisual");
        if (oldLever != null && oldLever.gameObject.activeSelf)
        {
            Undo.RecordObject(oldLever.gameObject, "Hide old lever");
            oldLever.gameObject.SetActive(false);
            log.AppendLine("  + старый LeverVisual скрыт (не удалён)");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    private static Transform BuildHead(EngineTelegraph telegraph, Transform parent, out Transform pivot, out Collider knobCollider)
    {
        Material housingMat = GetMaterial("Telegraph_Housing", new Color(0.16f, 0.18f, 0.21f), 0.4f);
        Material drumMat    = GetMaterial("Telegraph_Drum",    new Color(0.82f, 0.84f, 0.86f), 0.5f);
        Material aheadMat   = GetMaterial("Telegraph_Ahead",   new Color(0.10f, 0.55f, 0.25f), 0.3f);
        Material asternMat  = GetMaterial("Telegraph_Astern",  new Color(0.75f, 0.12f, 0.12f), 0.3f);
        Material stopMat    = GetMaterial("Telegraph_Stop",    new Color(0.95f, 0.95f, 0.95f), 0.3f);
        Material leverMat   = GetMaterial("Telegraph_Lever",   new Color(0.62f, 0.65f, 0.68f), 0.7f);
        Material knobMat    = GetMaterial("Telegraph_Knob",    new Color(0.08f, 0.08f, 0.10f), 0.6f);

        var headGo = new GameObject(HeadName);
        Undo.RegisterCreatedObjectUndo(headGo, "Create TelegraphHead");
        Transform head = headGo.transform;
        head.SetParent(parent, false);
        // Основание — там, где стоял низ старого рычага
        Transform t = telegraph.transform;
        head.localPosition = t.localPosition + new Vector3(0f, -0.204f, 0f);
        head.localRotation = Quaternion.identity;

        // Корпус и боковые щёки барабана
        Part(PrimitiveType.Cube, "Housing", head, new Vector3(0f, 0.07f, 0f), Quaternion.identity,
             new Vector3(0.20f, 0.14f, 0.28f), housingMat);
        Quaternion axisX = Quaternion.Euler(0f, 0f, 90f);   // ось цилиндра → X
        Part(PrimitiveType.Cylinder, "Drum", head, new Vector3(0f, DrumHeight, 0f), axisX,
             new Vector3(DrumRadius * 2f, DrumHalfLength, DrumRadius * 2f), drumMat);
        Part(PrimitiveType.Cylinder, "CheekLeft", head, new Vector3(DrumHalfLength + 0.005f, DrumHeight, 0f), axisX,
             new Vector3(0.27f, 0.005f, 0.27f), housingMat);
        Part(PrimitiveType.Cylinder, "CheekRight", head, new Vector3(-DrumHalfLength - 0.005f, DrumHeight, 0f), axisX,
             new Vector3(0.27f, 0.005f, 0.27f), housingMat);

        // Шкала на барабане: сектор на каждое положение
        int stopIndex = FindStopIndex(telegraph);
        float arc = DrumRadius * AngleStep * Mathf.Deg2Rad * 0.92f;
        var scale = new GameObject("Scale").transform;
        scale.SetParent(head, false);
        scale.localPosition = new Vector3(0f, DrumHeight, 0f);
        for (int i = 0; i < telegraph.positions.Length; i++)
        {
            var pos = telegraph.positions[i];
            float angle = (i - stopIndex) * AngleStep;
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.right);
            Vector3 radial = rot * Vector3.up;
            Material mat = pos.speedFactor > 0f ? aheadMat : pos.speedFactor < 0f ? asternMat : stopMat;

            Transform tile = Part(PrimitiveType.Cube, "Sector_" + i, scale, radial * (DrumRadius + 0.002f), rot,
                                  new Vector3(DrumHalfLength * 1.7f, 0.004f, arc), mat);

            // Подпись лежит на поверхности сектора и читается со стороны оператора
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(scale, false);
            Vector3 up = rot * Vector3.back;        // «верх» текста — от оператора
            Vector3 forward = -radial;              // текст смотрит наружу
            labelGo.transform.localPosition = radial * (DrumRadius + 0.0045f);
            labelGo.transform.localRotation = Quaternion.LookRotation(forward, up);
            var text = labelGo.AddComponent<TextMeshPro>();
            text.text = Label(pos.name);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 0.18f;
            text.color = pos.speedFactor == 0f ? Color.black : Color.white;
            text.rectTransform.sizeDelta = new Vector2(DrumHalfLength * 1.6f, arc * 0.9f);
            text.raycastTarget = false;
            _ = tile;
        }

        // Рычаг: ось на линии оси барабана, справа от оператора
        var pivotGo = new GameObject("LeverPivot");
        pivot = pivotGo.transform;
        pivot.SetParent(head, false);
        pivot.localPosition = new Vector3(LeverSideX, DrumHeight, 0f);
        pivot.localRotation = Quaternion.identity;

        Part(PrimitiveType.Cylinder, "Hub", pivot, Vector3.zero, axisX, new Vector3(0.05f, 0.012f, 0.05f), leverMat);
        Part(PrimitiveType.Cylinder, "Arm", pivot, new Vector3(-0.015f, LeverLength * 0.5f, 0f), Quaternion.identity,
             new Vector3(0.018f, LeverLength * 0.5f, 0.018f), leverMat);
        // Указатель — показывает на сектор шкалы
        Part(PrimitiveType.Cube, "Pointer", pivot, new Vector3(0.035f, DrumRadius + 0.012f, 0f), Quaternion.identity,
             new Vector3(0.07f, 0.006f, 0.006f), knobMat);
        Transform knob = Part(PrimitiveType.Sphere, "Knob", pivot, new Vector3(-0.015f, LeverLength, 0f), Quaternion.identity,
                              Vector3.one * 0.055f, knobMat, keepCollider: true);
        var sphere = knob.GetComponent<SphereCollider>();
        sphere.radius = 0.9f;   // ~5 см — легко попасть рукой
        knobCollider = sphere;

        return head;
    }

    private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Quaternion localRot,
                                  Vector3 localScale, Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider)
            Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    private static Material GetMaterial(string name, Color color, float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Ocean", "Materials");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(lit) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static int FindStopIndex(EngineTelegraph telegraph)
    {
        for (int i = 0; i < telegraph.positions.Length; i++)
            if (Mathf.Approximately(telegraph.positions[i].speedFactor, 0f))
                return i;
        return telegraph.positions.Length / 2;
    }

    private static string Label(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("stop"))      return "СТОП";
        if (n.Contains("full"))      return "ПОЛНЫЙ";
        if (n.Contains("half"))      return "СРЕДНИЙ";
        if (n.Contains("dead slow")) return "С. МАЛЫЙ";
        if (n.Contains("slow"))      return "МАЛЫЙ";
        return name;
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
