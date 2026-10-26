// DCGO VR - hologram look for small projected models (your breeding Digimon above the duel disk).
// Pass 1 writes depth only, pass 2 adds light on the front-most surface, so the figure reads as
// one glowing shape instead of seeing through itself. Unlit, additive, single-pass instanced ready.
Shader "DCGO/DSTS/Hologram"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _Color ("Tint", Color) = (0.3, 0.85, 1, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1.1
        _Rim ("Rim Glow", Range(0, 4)) = 1.4
        _LineDensity ("Scan Lines per metre", Float) = 140
        _LineSpeed ("Scan Line Speed", Float) = 0.04
        _Flicker ("Flicker", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _Color;
            half   _Intensity;
            half   _Rim;
            float  _LineDensity;
            float  _LineSpeed;
            half   _Flicker;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3  normalWS   : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert (Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
            o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "HoloDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            half4 fragDepth (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Holo"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half lum = dot(tex, half3(0.3, 0.59, 0.11));

                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half rim = pow(1.0 - saturate(abs(dot(n, v))), 2.0) * _Rim;

                float phase = (i.positionWS.y - _Time.y * _LineSpeed) * _LineDensity;
                half scan = 0.72 + 0.28 * sin(phase * 6.2831853);

                float tick = floor(_Time.y * 18.0);
                half flicker = 1.0 - _Flicker * step(0.93, frac(sin(tick * 12.9898) * 43758.5453));

                half3 color = _Color.rgb * (lum * 0.85 + 0.12 + rim) * scan * flicker * _Intensity;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
