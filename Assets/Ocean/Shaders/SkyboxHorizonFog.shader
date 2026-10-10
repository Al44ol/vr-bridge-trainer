// ============================================================
// SkyboxHorizonFog.shader — Кубмап-скайбокс с туманом у горизонта
//
// Аналог встроенного Skybox/Cubemap (те же _Tex, _Tint, _Exposure,
// _Rotation), но у горизонта небо плавно смешивается с цветом тумана
// URP (RenderSettings.fogColor), а ниже горизонта полностью равно ему.
// Вода у горизонта тоже уходит в fogColor (MixFog в OceanSurface),
// поэтому линии горизонта нет ни в одном погодном пресете,
// а край меша океана скрыт.
//
// Погода (SkyWeather.cs, глобальные переменные; по умолчанию 0 — обычное небо):
//   _SkyDarkening — затемнение кубмапы (ночь, шторм), 0..1;
//   _SkyOvercast  — сплошная облачность (шторм), 0..1: процедурные тучи
//                   (периодический шум на куполе), цвет — от цвета тумана;
//   _SkyCloudOffset — дрейф туч (по модулю периода шума).
// ============================================================
Shader "VRBridgeTrainer/SkyboxHorizonFog"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}

        [Header(Horizon Fog)]
        _HorizonFogHeight   ("Horizon Fog Height (sin of elevation)", Range(0.005, 0.5)) = 0.08
        _HorizonFogStrength ("Horizon Fog Strength", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Background"
            "RenderType"     = "Background"
            "PreviewType"    = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half  _Exposure;
                float _Rotation;
                half4 _Tex_HDR;
                float _HorizonFogHeight;
                float _HorizonFogStrength;
            CBUFFER_END

            // Погода — глобальные переменные (SkyWeather.cs)
            float  _SkyDarkening;
            float  _SkyOvercast;
            float4 _SkyCloudOffset;

            #define CLOUD_CELLS 64.0   // период шума туч в ячейках

            float Hash21(float2 i)
            {
                return frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
            }

            float PeriodicNoise(float2 p, float cells)
            {
                float2 i  = floor(p);
                float2 f  = p - i;
                float2 u  = f * f * (3.0 - 2.0 * f);
                float2 i0 = i - cells * floor(i / cells);
                float2 i1 = i0 + 1.0;
                i1 -= cells * floor(i1 / cells);
                float a = Hash21(i0);
                float b = Hash21(float2(i1.x, i0.y));
                float c = Hash21(float2(i0.x, i1.y));
                float d = Hash21(i1);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 4 октавы периодического шума (fbm), период сохраняется: cells·2ⁿ ячеек на октаву n
            float CloudFbm(float2 p)
            {
                float sum = 0.0, amp = 0.5, cells = CLOUD_CELLS;
                [unroll] for (int o = 0; o < 4; o++)
                {
                    sum += amp * PeriodicNoise(p, cells);
                    p *= 2.0; cells *= 2.0; amp *= 0.5;
                }
                return sum / 0.9375;
            }

            // Как unity_ColorSpaceDouble во встроенном шейдере (проект в Linear)
            #define COLOR_SPACE_DOUBLE 4.59479380

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundYInDegrees(float3 v, float degrees)
            {
                float alpha = degrees * PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, v.xz), v.y).xzy;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 rotated = RotateAroundYInDegrees(input.positionOS.xyz, _Rotation);
                output.positionCS = TransformObjectToHClip(rotated);
                output.dir = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 dir = normalize(input.dir);
                half4  tex = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, dir);
                half3  sky = DecodeHDREnvironment(tex, _Tex_HDR) * _Tint.rgb * COLOR_SPACE_DOUBLE * _Exposure;
                sky *= 1.0 - saturate(_SkyDarkening);

                // Тучи: проекция на плоский «потолок» над наблюдателем, гаснут к горизонту
                if (_SkyOvercast > 0.001 && dir.y > -0.05)
                {
                    float2 cuv = dir.xz / (max(dir.y, 0.0) + 0.12) * 1.5 + _SkyCloudOffset.xy;
                    float  n   = CloudFbm(cuv);
                    float  cov = saturate(_SkyOvercast);
                    float  mask = smoothstep(1.05 - cov * 1.1, 1.3 - cov * 1.1, n);
                    // Низ туч темнее, просветы светлее; общий тон — от цвета тумана
                    half3 cloud = unity_FogColor.rgb * lerp(1.5, 0.7, n);
                    sky = lerp(sky, cloud, mask * cov);
                }

                // Высота над горизонтом: 0 у горизонта, < 0 — ниже горизонта
                float horizon = 1.0 - smoothstep(0.0, _HorizonFogHeight, dir.y);
                sky = lerp(sky, unity_FogColor.rgb, horizon * _HorizonFogStrength);

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
