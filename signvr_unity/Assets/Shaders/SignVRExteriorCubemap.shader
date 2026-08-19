Shader "SignVR/URP Exterior Cubemap"
{
    Properties
    {
        _Cubemap("Exterior Cubemap", Cube) = "black" {}
        _Tint("Daylight Tint", Color) = (0.82, 0.84, 0.84, 1)
        _EmissionStrength("Emission Strength", Range(0, 1)) = 0.55
        _Rotation("Y Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Background+50"
        }

        Pass
        {
            Name "ExteriorUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Cubemap);
            SAMPLER(sampler_Cubemap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _EmissionStrength;
                float _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 direction = normalize(input.positionWS - GetCameraPositionWS());
                float sineRotation;
                float cosineRotation;
                sincos(radians(_Rotation), sineRotation, cosineRotation);
                direction.xz = float2(
                    direction.x * cosineRotation - direction.z * sineRotation,
                    direction.x * sineRotation + direction.z * cosineRotation
                );

                half3 exterior = SAMPLE_TEXTURECUBE(_Cubemap, sampler_Cubemap, direction).rgb;
                half3 color = saturate(exterior * _Tint.rgb * _EmissionStrength);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
