Shader "SignVR/PC/Rainy Window Glass"
{
    Properties
    {
        _GlassTint ("Glass Tint", Color) = (0.64, 0.72, 0.84, 1)
        _BaseOpacity ("Base Opacity", Range(0, 1)) = 0.20
        _DropletDensity ("Droplet Density", Range(20, 180)) = 104
        _StreakDensity ("Streak Density", Range(8, 60)) = 28
        _FlowSpeed ("Flow Speed", Range(0, 0.25)) = 0.045
        _Distortion ("Refraction Distortion", Range(0, 0.05)) = 0.012
        _NormalStrength ("Droplet Normal Strength", Range(0, 12)) = 5.5
        _HighlightStrength ("Highlight Strength", Range(0, 4)) = 1.35
        _WetDarkening ("Wet Darkening", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
        }

        Pass
        {
            Name "RainyWindowForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlassTint;
                half _BaseOpacity;
                half _DropletDensity;
                half _StreakDensity;
                half _FlowSpeed;
                half _Distortion;
                half _NormalStrength;
                half _HighlightStrength;
                half _WetDarkening;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 tangentWS : TEXCOORD2;
                half3 bitangentWS : TEXCOORD3;
                float2 uv : TEXCOORD4;
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float2 Hash22(float2 value)
            {
                float first = Hash21(value);
                return float2(first, Hash21(value + first + 19.19));
            }

            float BeadLayer(float2 uv, float density, float seed, float aspect)
            {
                float2 scaled = uv * float2(density, density * aspect);
                float2 cell = floor(scaled);
                float2 local = frac(scaled) - 0.5;
                float2 randomValue = Hash22(cell + seed);
                float2 center = (randomValue - 0.5) * float2(0.72, 0.58);
                float2 delta = local - center;
                delta.y *= 1.15 + randomValue.x * 0.55;
                float radius = lerp(0.055, 0.13, randomValue.y);
                return smoothstep(radius, radius * 0.32, length(delta));
            }

            float StreakLayer(float2 uv, float timeValue)
            {
                float2 scaled = uv * float2(_StreakDensity, _StreakDensity * 0.58);
                scaled.y += timeValue * _FlowSpeed * (7.0 + _StreakDensity * 0.08);
                float2 cell = floor(scaled);
                float2 local = frac(scaled) - 0.5;
                float2 randomValue = Hash22(cell + 31.7);

                float2 center = float2((randomValue.x - 0.5) * 0.68, (randomValue.y - 0.5) * 0.36);
                float2 delta = local - center;
                float headRadius = lerp(0.09, 0.16, randomValue.x);
                float head = smoothstep(headRadius, headRadius * 0.28, length(delta * float2(1.0, 1.35)));

                float trailWidth = lerp(0.018, 0.045, randomValue.y);
                float trail = smoothstep(trailWidth, trailWidth * 0.25, abs(delta.x));
                trail *= smoothstep(0.48, 0.035, delta.y) * step(0.015, delta.y);
                trail *= lerp(0.18, 0.62, randomValue.x);

                float gate = smoothstep(0.38, 0.88, randomValue.y);
                return max(head, trail) * gate;
            }

            float RainHeight(float2 uv, float timeValue)
            {
                float micro = BeadLayer(uv, _DropletDensity, 7.3, 0.72) * 0.34;
                float medium = BeadLayer(uv + float2(0.071, 0.037), _DropletDensity * 0.43, 17.9, 0.68) * 0.74;
                float streak = StreakLayer(uv, timeValue);
                return saturate(micro + medium + streak);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.bitangentWS = cross(output.normalWS, output.tangentWS)
                    * input.tangentOS.w * GetOddNegativeScale();
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const float derivativeStep = 0.0015;
                float rain = RainHeight(input.uv, _Time.y);
                float rainX = RainHeight(input.uv + float2(derivativeStep, 0.0), _Time.y);
                float rainY = RainHeight(input.uv + float2(0.0, derivativeStep), _Time.y);

                half3 dropletNormalTS = normalize(half3(
                    (rain - rainX) * _NormalStrength,
                    (rain - rainY) * _NormalStrength,
                    derivativeStep
                ));
                half3 normalWS = normalize(
                    input.tangentWS * dropletNormalTS.x
                    + input.bitangentWS * dropletNormalTS.y
                    + input.normalWS * dropletNormalTS.z
                );

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 refractionOffset = dropletNormalTS.xy * _Distortion * (0.30 + rain * 0.70);
                half3 refractedScene = SampleSceneColor(saturate(screenUV + refractionOffset));

                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight();
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirectionWS)), 3.0h);
                half specular = pow(
                    saturate(dot(reflect(-mainLight.direction, normalWS), viewDirectionWS)),
                    72.0h
                );

                half3 wetScene = refractedScene * _GlassTint.rgb;
                wetScene *= 1.0h - _WetDarkening * (0.45h + rain * 0.55h);
                half highlight = rain * (specular * 0.78h + fresnel * 0.22h) * _HighlightStrength;
                half3 color = wetScene + highlight * lerp(half3(0.55h, 0.72h, 1.0h), mainLight.color, 0.45h);
                half alpha = saturate(_BaseOpacity + rain * 0.14h + fresnel * 0.10h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
