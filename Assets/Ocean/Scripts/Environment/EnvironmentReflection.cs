// ============================================================
// EnvironmentReflection.cs — Отражения окружения по текущему небу (B4)
//
// До B4 отражения брались из запечённых данных шаблона VR Template
// (LightingData SampleScene): мостик, буи и цели ночью отражали дневное небо.
// Теперь — одна Reflection Probe «только небо» (culling mask — ничего, объём
// 40 км — все объекты сцены внутри), Realtime, обновление из скрипта:
// перерисовывается, когда WeatherController меняет освещение (переходы погоды
// и времени суток), не чаще minInterval; грани — по одной за кадр
// (time slicing IndividualFaces), чтобы не было пика времени кадра.
// Вне переходов не рисуется (ноль затрат).
// Объект и параметры пробы создаёт Tools/VR Bridge/Setup B4 Night.
// ============================================================
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(ReflectionProbe))]
public class EnvironmentReflection : MonoBehaviour
{
    [Tooltip("Наименьший интервал между перерисовками, с")]
    public float minInterval = 0.25f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private int renders;

    /// <summary>Сколько раз проба перерисовывалась (замер F8: была ли перерисовка во время замера).</summary>
    public int Renders => renders;

    private ReflectionProbe probe;
    private int renderId = -1;
    private int renderedVersion = int.MinValue;
    private float lastStart = -100f;

    void Start()
    {
        probe = GetComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
    }

    void Update()
    {
        if (probe == null) return;
        if (renderId >= 0 && !probe.IsFinishedRendering(renderId)) return;
        WeatherController w = WeatherController.Instance;
        int version = w != null ? w.LightingVersion : 0;
        if (version == renderedVersion || Time.unscaledTime - lastStart < minInterval) return;
        renderedVersion = version;
        lastStart = Time.unscaledTime;
        renderId = probe.RenderProbe();
        renders++;
    }
}
