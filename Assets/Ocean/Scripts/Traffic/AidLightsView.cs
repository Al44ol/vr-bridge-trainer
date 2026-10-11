// ============================================================
// AidLightsView.cs — Огни знаков навигационного ограждения МАМС (B3)
//
// Висит рядом с WaterAreaView. Для каждого знака акватории — характеристика
// огня из данных B1 (AidToNavigation.Light: заданная или по умолчанию для типа,
// IalaMarks): «Fl R 4s», «Q(6)+LFl 15s» и т. п. → LightCharacter (Core).
// Огонь — круговой, на фонаре под топовой фигурой буя (качается вместе с буём),
// сила света — по номинальной дальности (Приложение I МППСС, п. 8, как у судовых
// огней), яркость — по закону Алларда с K по видимости (NavLightField).
// Горит, когда горят все огни (NavLightField: ночь или ограниченная видимость).
// Фазы знаков сдвинуты (как у несинхронизированных буёв), время — Time.time.
// Порядок −55: после WaterAreaView (−60), которое ставит буи в кадре.
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-55)]
public class AidLightsView : MonoBehaviour
{
    public WaterAreaView view;
    [Tooltip("Номинальная дальность огней знаков, мили (предварительное значение)")]
    public float nominalRangeNm = 3f;
    [Tooltip("Высота фонаря над ватерлинией модели буя (до масштаба), м")]
    public float lanternHeight = 3.12f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private int lightsCount;

    private LightCharacter[] characters = new LightCharacter[0];
    private double[] phases = new double[0];
    private WaterArea builtArea;
    private int builtRevision = int.MinValue;

    void Start()
    {
        if (view == null) view = GetComponent<WaterAreaView>();
    }

    void Update()
    {
        NavLightField field = NavLightField.Instance;
        if (field == null || !field.LightsOn || view == null || view.area == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Rebuild();

        double intensity = NavigationLights.IntensityForRange(nominalRangeNm);
        double t = Time.timeAsDouble;
        Vector3 eye = cam.transform.position;
        int n = Mathf.Min(view.BuoyCount, characters.Length);
        for (int i = 0; i < n; i++)
        {
            LightCharacter c = characters[i];
            if (!c.IsOn(t + phases[i])) continue;
            Transform tr = view.BuoyTransform(i);
            if (tr == null) continue;
            Vector3 p = tr.TransformPoint(new Vector3(0f, lanternHeight, 0f));
            float level = field.Level(intensity, (eye - p).magnitude);
            if (level > 0f) field.Submit(p, c.Color, level);
        }
    }

    private void Rebuild()
    {
        WaterArea a = view.area;
        if (a == builtArea && a.revision == builtRevision && characters.Length == a.aids.Count) return;
        builtArea = a;
        builtRevision = a.revision;
        characters = new LightCharacter[a.aids.Count];
        phases = new double[a.aids.Count];
        for (int i = 0; i < characters.Length; i++)
        {
            characters[i] = LightCharacter.Parse(a.aids[i] != null ? a.aids[i].Light : "");
            // Сдвиг фазы (золотое сечение — равномерно и детерминированно)
            phases[i] = (i * 0.6180339887 % 1.0) * Mathf.Max(0.001f, (float)characters[i].Period);
        }
        lightsCount = characters.Length;
    }
}
