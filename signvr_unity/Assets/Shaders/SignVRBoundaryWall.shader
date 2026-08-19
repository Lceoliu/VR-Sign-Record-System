// Translucent "soft wall" for the measured hand-tracking frustum.
//
// A wireframe reads as engineering notation: eight floating lines do not group
// into a space, and give no sense of inside versus outside. This draws the
// frustum sides as barely-visible surfaces that only reveal themselves near the
// edges and, crucially, light up locally where a hand approaches them — so the
// wall behaves like something the teacher can touch rather than a diagram.
Shader "SignVR/Boundary Wall"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.18, 0.78, 0.82, 1)
        _ActiveColor ("Touch Color", Color) = (1, 0.52, 0.12, 1)
        _BaseAlpha ("Base Alpha", Range(0, 0.5)) = 0.05
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.28
        _EdgeWidth ("Edge Width", Range(0.01, 0.5)) = 0.12
        _TouchRadius ("Touch Radius", Range(0.02, 0.6)) = 0.22
        _TouchStrength ("Touch Strength", Range(0, 1)) = 0.85
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha One          // additive-ish: never darkens the room
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float3 world  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _ActiveColor;
            float _BaseAlpha;
            float _EdgeAlpha;
            float _EdgeWidth;
            float _TouchRadius;
            float _TouchStrength;
            float _GlobalAlpha;

            // Hand positions in world space; w < 0 means that hand is not tracked.
            float4 _HandLeft;
            float4 _HandRight;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float TouchAmount(float3 world, float4 hand)
            {
                if (hand.w < 0.5) return 0;
                float d = distance(world, hand.xyz);
                return 1.0 - smoothstep(0.0, _TouchRadius, d);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fade in toward the rim so the middle of the wall stays clear of
                // the signing space while the boundary itself stays readable.
                float2 d = min(i.uv, 1.0 - i.uv);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, min(d.x, d.y));

                float touch = max(TouchAmount(i.world, _HandLeft),
                                  TouchAmount(i.world, _HandRight));
                touch = pow(touch, 1.5) * _TouchStrength;

                float alpha = max(_BaseAlpha, edge * _EdgeAlpha);
                alpha = max(alpha, touch);

                fixed3 rgb = lerp(_Color.rgb, _ActiveColor.rgb, saturate(touch * 1.4));
                return fixed4(rgb * alpha * _GlobalAlpha, alpha * _GlobalAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
