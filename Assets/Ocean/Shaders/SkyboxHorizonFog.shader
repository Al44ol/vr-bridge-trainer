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
//
// B4 — ночное небо (SkyWeather; по умолчанию _SkyNight = 0 — небо как до B4):
//   _SkyNight — доля ночного слоя: градиент от цвета тумана у горизонта
//               к _SkyNightZenith, звёзды, луна; плавно смешивается с дневной кубмапой;
//   звёзды процедурные (внешних ассетов нет): направление → грань куба,
//               сетка STAR_CELLS × STAR_CELLS ячеек, звезда в ячейке с вероятностью
//               STAR_DENSITY; распределение блеска N(<m) ∝ 10^(0,47m) → поток F = u^(−0,85),
//               яркость на экране — логарифмическая; размер ~1 пиксель по fwidth (без
//               мерцания при повороте головы); к горизонту гаснут (атмосферная экстинкция);
//   луна — диск ≈ 0,3° с ореолом, направление _SkyMoonDir.xyz, яркость — .w;
//   _SkyTwilight — тёплое свечение у горизонта со стороны солнца (сумерки).
// Тучи шторма рисуются поверх ночного слоя — закрывают звёзды и луну.
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

            // Ночь (B4, SkyWeather.cs)
            float  _SkyNight;
            float4 _SkyNightZenith;
            float  _SkyStars;
            float4 _SkyMoonDir;        // xyz — на луну, w — яркость диска
            float4 _SkyMoonColor;
            float  _SkyTwilight;
            float4 _SkyTwilightColor;
            float4 _SkySunDir;         // xyz — на солнце

            #define CLOUD_CELLS 64.0   // период шума туч в ячейках
            #define STAR_CELLS 512.0   // ячеек на ребро грани куба: ячейка ≈ 0,2° ≈ 5 пикселей шлема
            #define STAR_DENSITY 0.0032 // доля ячеек со звездой: ≈ 5000 звёзд на сфере
            #define MOON_RADIUS 0.0052  // угловой радиус диска луны, рад (≈ 0,3°; реальный 0,26°)

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

            float4 Hash43(float3 p)
            {
                float4 q = frac(float4(p.xyzx) * float4(0.1031, 0.1030, 0.0973, 0.1099));
                q += dot(q, q.wzxy + 33.33);
                return frac((q.xxyz + q.yzzw) * q.zywx);
            }

            // Звёзды: грань куба → ячейка → одна звезда в ячейке (центр 0,2…0,8, чтобы не резалась краем)
            half3 Stars(float3 dir)
            {
                float3 a = abs(dir);
                float2 uv; float face;
                if (a.x >= a.y && a.x >= a.z) { uv = dir.zy / a.x; face = dir.x > 0 ? 0 : 1; }
                else if (a.y >= a.z)          { uv = dir.xz / a.y; face = dir.y > 0 ? 2 : 3; }
                else                          { uv = dir.xy / a.z; face = dir.z > 0 ? 4 : 5; }
                float2 g = (uv * 0.5 + 0.5) * STAR_CELLS;
                float2 cell = floor(g);
                float2 f = g - cell;
                float px = max(fwidth(g.x), fwidth(g.y));           // ячеек на пиксель (до ветвления)
                float4 h = Hash43(float3(cell, face * 1031.0 + 7.0));
                if (h.x > STAR_DENSITY) return 0;

                float u = h.x / STAR_DENSITY;                       // 0…1 — ранг по блеску
                float flux = pow(max(u, 2e-4), -0.85);              // F = u^(−0,85): 1 … ≈ 1400
                float level = 0.12 + 0.88 * saturate(log10(flux) / 3.0);
                float2 c = 0.2 + 0.6 * h.yz;
                float d = length(f - c) / max(px, 1e-4);            // расстояние в пикселях
                float core = exp(-d * d * 1.6);
                // Цвет — от «тёплых» до «холодных» звёзд
                half3 tint = lerp(half3(1.0, 0.86, 0.72), half3(0.78, 0.86, 1.0), h.w);
                return tint * (core * level);
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

                // Ночь (B4): тёмный градиент, звёзды, луна — смешивание с дневной кубмапой.
                // Без ветвления: внутри есть производные (fwidth), днём _SkyNight = 0.
                {
                    float up = saturate(dir.y);
                    half3 night = lerp(unity_FogColor.rgb, _SkyNightZenith.rgb, sqrt(up));
                    night += Stars(dir) * (_SkyStars * smoothstep(0.0, 0.12, dir.y));

                    // Луна: диск с мягким краем (по пикселю) и ореол
                    float3 md = _SkyMoonDir.xyz / max(length(_SkyMoonDir.xyz), 1e-4);   // без NaN, пока глобал не задан
                    float  dm = length(dir - md);                    // ≈ угол, рад
                    float  aa = max(fwidth(dm), 1e-5);
                    float  disc = 1.0 - smoothstep(MOON_RADIUS - aa, MOON_RADIUS + aa, dm);
                    float  halo = 0.10 * exp(-dm / 0.035) + 0.03 * exp(-dm / 0.25);
                    night += _SkyMoonColor.rgb * (_SkyMoonDir.w * (disc + halo)) * step(-0.02, md.y);

                    sky = lerp(sky, night, saturate(_SkyNight));
                }

                // Сумерки: свечение у горизонта, сильнее со стороны солнца
                if (_SkyTwilight > 0.001)
                {
                    float2 hz = normalize(dir.xz + 1e-5);
                    float2 sz = normalize(_SkySunDir.xz + 1e-5);
                    float  toward = saturate(dot(hz, sz) * 0.5 + 0.5);
                    float  band = exp(-max(dir.y, 0.0) / 0.12);
                    sky += _SkyTwilightColor.rgb * (_SkyTwilight * band * (0.2 + 0.8 * toward * toward));
                }

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
