// ============================================================
// NavLight.shader — навигационные огни: billboard постоянного углового размера (B3)
//
// Все огни сцены — один меш (NavLightField): у каждого огня 4 вершины с одной
// позицией центра (сцена), uv0 — угол квадрата, uv1.x — tg половины углового
// размера, uv1.y — наименьший радиус, м; цвет вершины: rgb — цвет огня,
// a — уровень яркости 0…1 (закон Алларда, сектор, проблеск — считает C#).
// Квадрат разворачивается к камере в вершинном шейдере: радиус = max(d·tg(θ/2), r_min),
// d — расстояние до камеры своего глаза, оси — строки матрицы вида своего глаза
// (Single Pass Instanced: UNITY_MATRIX_V и _WorldSpaceCameraPos — свои для каждого глаза).
// Поэтому огонь на 3 милях занимает те же ~2 пикселя, что и на 300 м, а яркость
// задаётся уровнем, а не размером.
// Аддитивное смешение, ZTest включён (корпус и надстройка своего судна огни
// перекрывают), ZWrite выключен, очередь после воды (3000). Туман URP не
// применяется: ослабление в атмосфере уже учтено (K = 0,05^(1852/V)).
// ============================================================
Shader "VRBridgeTrainer/NavLight"
{
    Properties
    {
        _Intensity ("Яркость", Float) = 1.5
        _CoreSize ("Доля ядра", Range(0.05, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "NavLight"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _CoreSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
                float2 size : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 center = TransformObjectToWorld(v.positionOS.xyz);
                float dist = distance(center, _WorldSpaceCameraPos);
                float radius = max(dist * v.size.x, v.size.y);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 c = v.corner * 2.0 - 1.0;
                float3 world = center + (right * c.x + up * c.y) * radius;
                o.positionCS = TransformWorldToHClip(world);
                o.color = v.color;
                o.uv = c;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r2 = dot(i.uv, i.uv);
                // Ядро — яркий диск, ореол — мягкий спад до края квадрата
                float core = saturate((1.0 - r2 / (_CoreSize * _CoreSize)) * 3.0);
                float halo = saturate(1.0 - r2);
                halo *= halo;
                float a = saturate(core + 0.45 * halo) * i.color.a * _Intensity;
                return half4(i.color.rgb * a, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
