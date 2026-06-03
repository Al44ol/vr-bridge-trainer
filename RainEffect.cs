// ============================================================
// RainEffect.cs — Дождь (частицы) при шторме
// Повесить на объект Ship. Автоматически следит за seaState.
// ============================================================
using UnityEngine;

public class RainEffect : MonoBehaviour
{
    [Header("Ссылки")]
    public ShipMotion shipMotion;

    [Header("Настройки дождя")]
    public float stormThreshold = 0.5f; // дождь начинается при seaState > 0.5
    public float maxParticles = 3000f;
    public float rainArea = 30f;        // область дождя (метры)
    public float rainHeight = 15f;      // высота спавна капель

    private ParticleSystem rainSystem;

    void Start()
    {
        CreateRainSystem();
    }

    void Update()
    {
        if (shipMotion == null || rainSystem == null) return;

        float sea = shipMotion.seaState;
        var emission = rainSystem.emission;

        if (sea > stormThreshold)
        {
            float intensity = (sea - stormThreshold) / (1f - stormThreshold);
            emission.rateOverTime = maxParticles * intensity;

            if (!rainSystem.isPlaying)
                rainSystem.Play();
        }
        else
        {
            emission.rateOverTime = 0;
            if (rainSystem.isPlaying && rainSystem.particleCount == 0)
                rainSystem.Stop();
        }
    }

    void CreateRainSystem()
    {
        GameObject rainObj = new GameObject("RainParticles");
        rainObj.transform.SetParent(transform);
        rainObj.transform.localPosition = new Vector3(0, rainHeight, 0);

        rainSystem = rainObj.AddComponent<ParticleSystem>();
        var main = rainSystem.main;
        main.startLifetime = 1.5f;
        main.startSpeed = 15f;
        main.startSize = 0.03f;
        main.startColor = new Color(0.7f, 0.75f, 0.85f, 0.4f);
        main.maxParticles = (int)maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.5f;

        var shape = rainSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(rainArea, 0.1f, rainArea);

        var emission = rainSystem.emission;
        emission.rateOverTime = 0;

        // Простой материал для капель
        var renderer = rainObj.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        renderer.material.color = new Color(0.7f, 0.75f, 0.85f, 0.3f);

        rainSystem.Stop();
    }
}
