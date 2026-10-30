// DCGO VR - cheap Quest-friendly toon shader for runtime-loaded DSTS models.
// Replaces glTFast's PBR material: one texture fetch, main directional light only,
// two-tone cel band, flat ambient. No shadows, no extra lights, no reflections, no fog.
// Optional alpha clip (the game's "screen door" materials, e.g. the eye white's iris hole).
// Single-pass instanced stereo ready.
// _DSTS_EYE: the game's layered eyes in one pass - body / eye-white texture (UV0), then the
// iris / pupil texture (UV1, "OverlayNormalSampler" pNN), then the eyelid / expression sheet
// (OverlayColorSampler3 bNN, UV set _LidUV) on top by its alpha. Without this the eyelid
// sheet alone was cut out and the eyes looked empty.
Shader "DCGO/DSTS/MobileToon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.72,0.70,0.82,1)
        _ShadeStep ("Shade Step", Range(-1,1)) = 0.1
        _ShadeSoftness ("Shade Softness", Range(0.001,0.5)) = 0.04
        _Ambient ("Ambient", Range(0,1)) = 0.12
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _IrisMap ("Iris Map (eyes)", 2D) = "black" {}
        _IrisOn ("Iris On", Float) = 0
        _LidMap ("Eyelid Map (eyes)", 2D) = "black" {}
        _LidUV ("Eyelid UV set", Float) = 2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _ALPHATEST_ON
            #pragma multi_compile_local _ _DSTS_EYE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _ShadeColor;
                half   _ShadeStep;
                half   _ShadeSoftness;
                half   _Ambient;
                half   _Cutoff;
                float4 _IrisMap_ST;
                float4 _LidMap_ST;
                half   _IrisOn;
                half   _LidUV;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_IrisMap); SAMPLER(sampler_IrisMap);
            TEXTURE2D(_LidMap);  SAMPLER(sampler_LidMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float4 eyeUV      : TEXCOORD2; // xy iris, zw eyelid
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                #ifdef _DSTS_EYE
                    float2 lid = _LidUV > 1.5 ? v.uv2 : (_LidUV > 0.5 ? v.uv1 : v.uv);
                    o.eyeUV = float4(TRANSFORM_TEX(v.uv1, _IrisMap), TRANSFORM_TEX(lid, _LidMap));
                #endif
                return o;
            }

            half4 frag (Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #ifdef _DSTS_EYE
                    half4 iris = SAMPLE_TEXTURE2D(_IrisMap, sampler_IrisMap, i.eyeUV.xy);
                    albedo.rgb = lerp(albedo.rgb, iris.rgb, iris.a * _IrisOn);
                    half4 lidColor = SAMPLE_TEXTURE2D(_LidMap, sampler_LidMap, i.eyeUV.zw);
                    albedo.rgb = lerp(albedo.rgb, lidColor.rgb, lidColor.a);
                    albedo.a = 1;
                #endif
                #ifdef _ALPHATEST_ON
                    clip(albedo.a - _Cutoff);
                #endif

                half3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half lit = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, ndl);
                half3 band = lerp(_ShadeColor.rgb, half3(1, 1, 1), lit);

                half3 color = albedo.rgb * (band * light.color + _Ambient);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
