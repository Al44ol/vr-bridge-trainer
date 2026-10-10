// ============================================================
// OceanSurface.shader — URP водная поверхность с волнами Герстнера
// VR-совместимый (Single Pass Instanced)
// Для ВКР: «Применение VR/AR-технологий для обучения операциям
//           на морских тренажёрах»
//
// A1 (плавающее начало координат):
//  • параметры 4 волн и их фазы приходят из OceanWaves (C#) глобальными
//    переменными; фазы уже учитывают смещение судна в мире и время;
//  • амплитуда гаснет с расстоянием (_WaveFadeStart → _WaveFadeEnd);
//  • каждая волна дополнительно гаснет там, где её не может отобразить
//    сетка (вершины) или пиксель (нормали) — против ряби и мерцания;
//  • нормали считаются попиксельно по точной производной Герстнера.
// A1.1:
//  • 4 волны ряби — только в нормалях (попиксельно);
//  • барашки: периодический шум на гребнях, привязан к воде (сдвиг из C#),
//    два слоя попеременно гаснут и появляются в случайных местах;
//  • корпус: внутри контура вода не рисуется, у борта волны гасятся;
//  • пенный след за кормой (полоса + «усы» Кельвина) и бурун у носа.
// ============================================================
Shader "VRBridgeTrainer/OceanSurface"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.12, 0.45, 0.55, 0.80)
        _DeepColor    ("Deep Color",    Color) = (0.02, 0.08, 0.18, 0.95)
        _FresnelPower ("Fresnel Power",  Range(1, 10)) = 4.0

        [Header(Surface)]
        _Smoothness    ("Smoothness",     Range(0, 1))  = 0.92
        _NormalStrength("Normal Strength", Range(0, 2))  = 1.0

        [Header(Waves)]
        _WaveIntensity ("Wave Intensity (0=calm 1=storm)", Range(0, 1)) = 0.3
        _WaveSpeed     ("Wave Speed (used by OceanWaves.cs)", Range(0.1, 3)) = 1.0

        [Header(Distance Fade)]
        _WaveFadeStart ("Wave Fade Start (m)", Float) = 1500
        _WaveFadeEnd   ("Wave Fade End (m)",   Float) = 6000

        [Header(Foam)]
        _FoamColor     ("Foam Color",     Color) = (0.85, 0.90, 0.95, 1.0)
        _FoamThreshold ("Foam Threshold (crest steepness)", Range(0, 2)) = 0.6
        _FoamAmount    ("Foam Patches Amount", Range(0, 2)) = 1.0
        _ShipFoam      ("Wake and Bow Foam",   Range(0, 2)) = 1.0

        [Header(Hull)]
        _HullDampMin   ("Wave Damping at Hull (0..1)", Range(0, 1)) = 0.25
        _HullDampWidth ("Damping Width (m)", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // VR: Single Pass Instanced
            #pragma multi_compile_instancing

            // Shadows & fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ─────────── Structures ───────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;   // смещённая позиция
                float3 flatXZFade : TEXCOORD1;   // xy = исходная XZ вершины, z = затухание по расстоянию
                float  fogFactor  : TEXCOORD2;
                float2 crestDamp  : TEXCOORD3;   // x = крутизна гребня Σ k·a·sin f (пена), y = гашение у борта
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ─────────── Material properties ───────────
            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _FresnelPower;
                float  _Smoothness;
                float  _NormalStrength;
                float  _WaveIntensity;
                float  _WaveSpeed;       // в шейдере не используется: скорость учтена в фазах (OceanWaves.cs)
                float  _WaveFadeStart;
                float  _WaveFadeEnd;
                float4 _FoamColor;
                float  _FoamThreshold;
                float  _FoamAmount;
                float  _ShipFoam;
                float  _HullDampMin;
                float  _HullDampWidth;
            CBUFFER_END

            // ─────────── Глобальные параметры волн (OceanWaves.cs) ───────────
            // Каждая компонента float4 — одна из 4 волн.
            float4 _OceanWaveDirX;    // направление, X (восток)
            float4 _OceanWaveDirZ;    // направление, Z (север)
            float4 _OceanWaveK;       // волновое число k = 2π/λ
            float4 _OceanWaveAmp;     // k·a при _WaveIntensity = 1 (безразмерная крутизна)
            float4 _OceanWaveExp;     // показатель: амплитуда ∝ _WaveIntensity^exp
            float4 _OceanWavePhases;  // фаза: k·dot(D, позиция судна) − ω·t, mod 2π

            // Рябь — те же параметры, только для нормалей
            float4 _OceanDetailDirX;
            float4 _OceanDetailDirZ;
            float4 _OceanDetailK;
            float4 _OceanDetailAmp;
            float4 _OceanDetailExp;
            float4 _OceanDetailPhases;

            // Пена-ориентиры: направление ветра и сдвиг шума (м, уже по модулю периода)
            float4 _OceanWindDir;     // xy = направление распространения волн (восток, север)
            float4 _OceanFoamOffset;  // xy = (u0, v0) слоя A, zw — слоя B
            float4 _OceanFoamBlend;   // x = вес слоя A, y = вес слоя B

            // Плотность сетки океана (OceanRadialMesh.cs): шаг(r) = max(min, r·angle)
            float  _OceanMeshMinStep;
            float  _OceanMeshAngleStep;

            // Корпус (ShipWaterInteraction.cs): два прямоугольника
            //   *0: xy = центр, zw = ось X;  *1: xy = ось Z, zw = полуразмеры (м)
            float4 _OceanHullA0, _OceanHullA1;
            float4 _OceanHullB0, _OceanHullB1;
            // Судно: frame xy = центр, zw = направление носа; dims = (выступ носа, выступ кормы, полуширина, скорость м/с)
            float4 _OceanShipFrame;
            float4 _OceanShipDims;

            // Согласовано с OceanWaves.FoamPeriod / FoamStretch
            #define FOAM_PERIOD  240.0
            #define FOAM_STRETCH 2.5

            // Отклик амплитуды каждой волны на интенсивность волнения
            float4 IntensityResponse(float4 exponent)
            {
                return pow(max(_WaveIntensity, 1e-4).xxxx, max(exponent, 0.01));
            }

            // Подавление волны, когда шаг дискретизации больше четверти её длины
            float4 WaveAntiAlias(float4 lambda, float sampleStep)
            {
                return 1.0 - smoothstep(lambda * 0.25, lambda * 0.5, sampleStep);
            }

            // Знаковое расстояние (м) от точки до прямоугольника: < 0 — внутри
            float BoxSDF(float2 p, float4 b0, float4 b1)
            {
                if (b1.z <= 0.0 || b1.w <= 0.0)
                    return 1e6;                         // корпус не задан
                float2 d = p - b0.xy;
                float2 local = float2(dot(d, b0.zw), dot(d, b1.xy));
                float2 q = abs(local) - b1.zw;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            float HullSDF(float2 p)
            {
                return min(BoxSDF(p, _OceanHullA0, _OceanHullA1), BoxSDF(p, _OceanHullB0, _OceanHullB1));
            }

            // Периодический value-noise: p в ячейках, период = cells ячеек
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

            // Шум пены, привязанный к воде. Две октавы: ячейки 6 м и 2 м
            // (период 240 м = 40 и 120 ячеек), пятна вытянуты вдоль ветра.
            // Октавы мельче пикселя заменяются средним значением (без мерцания).
            float FoamNoise(float2 uvBase, float2 offset, float footprint)
            {
                float2 uv = uvBase + offset;
                uv.x /= FOAM_STRETCH;
                float w1 = 1.0 - smoothstep(3.0, 6.0, footprint);
                float w2 = 1.0 - smoothstep(1.0, 2.0, footprint);
                float n1 = lerp(0.5, PeriodicNoise(uv / 6.0, FOAM_PERIOD / 6.0), w1);
                float n2 = lerp(0.5, PeriodicNoise(uv / 2.0, FOAM_PERIOD / 2.0), w2);
                return 0.6 * n1 + 0.4 * n2;
            }

            // ─────────── Vertex shader ───────────
            // Волна Герстнера (для каждой из 4 волн, векторно):
            //   f = k·dot(D, xz) + φ,   a = (k·a) / k
            //   смещение: xz += D·a·cos f,  y += a·sin f
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS    = TransformObjectToWorld(input.positionOS.xyz);
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));

                // Расстояние от центра океана (судна), а не от камеры:
                // одинаково для обоих глаз — нет стереорасхождения геометрии
                float dist = length(posWS.xz - centerWS.xz);
                float fade = 1.0 - smoothstep(_WaveFadeStart, _WaveFadeEnd, dist);

                // Гашение волн у борта: от _HullDampMin у корпуса до 1 на расстоянии _HullDampWidth
                float hullDamp = lerp(_HullDampMin, 1.0, smoothstep(0.0, max(_HullDampWidth, 0.01), HullSDF(posWS.xz)));

                float4 k      = max(_OceanWaveK, 1e-4);
                float4 lambda = TWO_PI / k;

                // Гасим волны, которые сетка в этом месте не может отобразить
                float  meshStep = max(_OceanMeshMinStep, dist * _OceanMeshAngleStep);
                float4 ka = IntensityResponse(_OceanWaveExp) * _OceanWaveAmp * fade * hullDamp
                          * WaveAntiAlias(lambda, meshStep);
                float4 a  = ka / k;

                float4 f = k * (_OceanWaveDirX * posWS.x + _OceanWaveDirZ * posWS.z) + _OceanWavePhases;
                float4 s, c;
                sincos(f, s, c);

                float3 disp;
                disp.x = dot(_OceanWaveDirX * a, c);
                disp.z = dot(_OceanWaveDirZ * a, c);
                disp.y = dot(a, s);

                output.flatXZFade = float3(posWS.xz, fade);
                posWS += disp;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                output.crestDamp  = float2(dot(ka, s), hullDamp);

                return output;
            }

            // ─────────── Fragment shader ───────────
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ── Корпус: внутри контура воды нет ──
                float hullDist = HullSDF(input.positionWS.xz);
                clip(hullDist);

                float2 xz   = input.flatXZFade.xy;
                float  fade = input.flatXZFade.z;

                // Размер пикселя на воде (м): волны мельче пикселя гасим — нет мерцания
                float footprint = max(length(ddx(xz)), length(ddy(xz)));

                // ── Нормаль Герстнера попиксельно (точная производная) ──
                //   N = (−Σ Dx·ka·cos f,  1 − Σ ka·sin f,  −Σ Dz·ka·cos f)
                float4 k  = max(_OceanWaveK, 1e-4);
                float4 ka = IntensityResponse(_OceanWaveExp) * _OceanWaveAmp * fade * input.crestDamp.y
                          * WaveAntiAlias(TWO_PI / k, footprint);
                float4 f = k * (_OceanWaveDirX * xz.x + _OceanWaveDirZ * xz.y) + _OceanWavePhases;
                float4 s, c;
                sincos(f, s, c);

                // Рябь: только нормали
                float4 kd  = max(_OceanDetailK, 1e-4);
                float4 kad = IntensityResponse(_OceanDetailExp) * _OceanDetailAmp * fade
                           * WaveAntiAlias(TWO_PI / kd, footprint);
                float4 fd = kd * (_OceanDetailDirX * xz.x + _OceanDetailDirZ * xz.y) + _OceanDetailPhases;
                float4 sd, cd;
                sincos(fd, sd, cd);

                float3 n = float3(-dot(_OceanWaveDirX * ka, c) - dot(_OceanDetailDirX * kad, cd),
                                  1.0 - dot(ka, s) - dot(kad, sd),
                                  -dot(_OceanWaveDirZ * ka, c) - dot(_OceanDetailDirZ * kad, cd));

                float3 N = normalize(lerp(float3(0, 1, 0), normalize(n), _NormalStrength));
                float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Френель: чем более пологий угол взгляда, тем темнее/глубже цвет
                float fresnel   = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                half4  waterCol = lerp(_ShallowColor, _DeepColor, fresnel);

                // Основной источник света
                Light mainLight = GetMainLight();
                float NdotL     = saturate(dot(N, mainLight.direction));

                // Блик (Blinn-Phong)
                float3 H     = normalize(mainLight.direction + V);
                float  NdotH = saturate(dot(N, H));
                float  spec  = pow(NdotH, exp2(10.0 * _Smoothness + 1.0)) * _Smoothness;

                // Ambient (Spherical Harmonics)
                float3 ambient = SampleSH(N);

                // Итоговый цвет
                float3 diffuse = waterCol.rgb * (ambient + mainLight.color.rgb * NdotL * 0.5);
                float3 specCol = mainLight.color.rgb * spec;

                // ── Пена ──
                // Координаты вдоль ветра (u) и поперёк (v); два слоя шума (A, B) с разными сдвигами
                float2 wind   = _OceanWindDir.xy;
                float2 uvBase = float2(dot(xz, wind), dot(xz, float2(wind.y, -wind.x)));
                float  nA = FoamNoise(uvBase, _OceanFoamOffset.xy, footprint);
                float  nB = FoamNoise(uvBase, _OceanFoamOffset.zw, footprint);
                float  noise = nA * _OceanFoamBlend.x + nB * _OceanFoamBlend.y;

                // 1) На крутых гребнях: Σ k·a·sin f выше порога, разбита шумом
                float foamStart = _FoamThreshold * 0.4;
                float crestFoam = saturate((input.crestDamp.x - foamStart) / 0.2) * (0.5 + noise);

                // 2) Барашки: только на гребнях (крутизна > ~0.02) и в случайных местах.
                //    Каждый слой гаснет, когда его вес → 0, и появляется уже в другом месте.
                float coverage  = 0.5 * pow(saturate(_WaveIntensity), 1.2) * _FoamAmount;   // штиль ~2 %, шторм ~40 %
                float threshold = 1.0 - coverage;
                float crestMask = smoothstep(0.02, 0.12, input.crestDamp.x);
                float capA = smoothstep(threshold, threshold + 0.08, nA) * _OceanFoamBlend.x;
                float capB = smoothstep(threshold, threshold + 0.08, nB) * _OceanFoamBlend.y;
                float patchFoam = max(capA, capB) * crestMask * fade;

                // 3) След за кормой и бурун у носа (сила ∝ скорости хода)
                float2 shipC   = _OceanShipFrame.xy;
                float2 bow     = _OceanShipFrame.zw;
                float  fore    = _OceanShipDims.x;
                float  aftExt  = _OceanShipDims.y;
                float  halfB   = max(_OceanShipDims.z, 0.1);
                float  U       = max(_OceanShipDims.w, 0.0);
                float  speed01 = saturate(U / 8.0);

                float2 d     = input.positionWS.xz - shipC;
                float  along = dot(d, bow);                               // > 0 — к носу
                float  side  = abs(dot(d, float2(bow.y, -bow.x)));        // расстояние от диаметральной плоскости
                float  aft   = -(along + aftExt);                         // расстояние за кормой

                float wake = 0.0;
                if (aft > 0.0 && U > 0.05)
                {
                    float wakeLen = 40.0 + 25.0 * U;                      // длина следа, м
                    float decay   = pow(saturate(1.0 - aft / wakeLen), 2.0);
                    // Центральная турбулентная полоса — расширяется за кормой
                    float turbHalf = halfB * (0.8 + aft * 0.04);
                    float central  = 1.0 - smoothstep(turbHalf * 0.5, turbHalf, side);
                    // «Усы» волн Кельвина: половина угла клина 19.47°, tg = 0.354
                    float armPos = halfB + aft * 0.354;
                    float armW   = 1.0 + aft * 0.06;
                    float arms   = (1.0 - smoothstep(0.0, armW, abs(side - armPos))) * 0.7;
                    wake = max(central, arms) * decay;
                }
                // Бурун: пена вдоль борта, сильнее к носу
                float bowZone = smoothstep(-aftExt * 0.3, fore, along);
                float bowFoam = (1.0 - smoothstep(0.0, 1.5 + 2.5 * speed01, hullDist)) * (0.3 + 0.7 * bowZone);
                float shipFoam = max(wake, bowFoam) * speed01 * (0.4 + noise) * _ShipFoam;

                float foam = saturate(max(max(crestFoam, patchFoam), shipFoam));
                float3 finalColor = lerp(diffuse + specCol, _FoamColor.rgb * (ambient + mainLight.color.rgb * 0.6), foam);

                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, lerp(waterCol.a, 1.0, foam));
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
