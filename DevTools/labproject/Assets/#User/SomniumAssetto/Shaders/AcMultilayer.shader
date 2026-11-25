// SomniumAssetto/Multilayer - Assetto Corsa's road / terrain shaders ksMultilayer, ksMultilayer_fresnel_nm, ksMultilayer_objsp.
// Written by Game/World/AcMaterials from the kn5 material. No material keywords; URP main-light shadow keywords as URP Lit.
//
// AC's maths (gamma space on raw texels, AcCommon.hlsl):
//   mask     = txMask(uv)                                    (the four channels sum to ~1 - measured on magione)
//   detail   = txDetailR(w x multR) mask.r + txDetailG(w x multG) mask.g + txDetailB(w x multB) mask.b + txDetailA(w x multA) mask.a,
//              w = the world position (x, z) in metres. Lab 2026-11-01: uv x mult drew 0.6 m pebbles on magione's road (its UVs are long
//              strips for the mask: 35 m per u, 15 m per v) and 20:1 stretched grass (8 m per u, 185 m per v); world metres give 1.25 m
//              asphalt tiles (multR 0.8), 5 m grass tiles (0.2) [BELIEF from the pictures + the numbers; the shader is not documented]
//   albedo   = txDiffuse.rgb x detail.rgb x magicMult        (base textures are near white, the details carry the colour: grass-b 0.94 x
//                                                              grass9 0.31 green - measured)
//   specular = ksSpecular x detail.a x tarmacSpecularMultiplier, (N.H)^ksSpecularEXP
//   normal   = txDetailNM(uv x detailNMMult) when detailNMMult > 0 (fresnel_nm), txNormal when present
//   reflect  = sky colour x fresnel (fresnelC/EXP, capped at fresnelMaxLevel) x detail.a (fresnel_nm)
//   _objsp keeps 0 in multX and two numbers in their B vectors: used here as the (u, v) scale of each detail layer ON THE MESH UV (walls: brick 13.05 x 30) [BELIEF]; _Flags.x = 1.
Shader "SomniumAssetto/Multilayer"
{
    Properties
    {
        _MainTex ("txDiffuse", 2D) = "white" {}
        _MaskTex ("txMask", 2D) = "black" {}
        _DetailR ("txDetailR", 2D) = "white" {}
        _DetailG ("txDetailG", 2D) = "white" {}
        _DetailB ("txDetailB", 2D) = "white" {}
        _DetailA ("txDetailA", 2D) = "white" {}
        _DetailNM ("txDetailNM", 2D) = "bump" {}
        _NormalTex ("txNormal", 2D) = "bump" {}
        _MultU ("detail u scale (multR, multG, multB, multA)", Vector) = (1, 1, 1, 1)
        _MultV ("detail v scale", Vector) = (1, 1, 1, 1)
        _Light ("x ksAmbient, y ksDiffuse, z ksSpecular, w ksSpecularEXP", Vector) = (0.4, 0.4, 0, 10)
        _Emissive ("ksEmissive (AC units)", Color) = (0, 0, 0, 0)
        _Fresnel ("x fresnelC, y fresnelEXP, z fresnelMaxLevel, w mode", Vector) = (0, 1, 0, 0)
        _Layer ("x magicMult, y tarmacSpecularMultiplier, z detailNMMult, w detail normal on", Vector) = (1, 1, 0, 0)
        _Flags ("x objsp (details on the mesh uv), y txNormal on", Vector) = (0, 0, 0, 0)
        _SrcBlend ("Src blend", Float) = 1
        _DstBlend ("Dst blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "AcCommon.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        TEXTURE2D(_DetailR); SAMPLER(sampler_DetailR);
        TEXTURE2D(_DetailG); SAMPLER(sampler_DetailG);
        TEXTURE2D(_DetailB); SAMPLER(sampler_DetailB);
        TEXTURE2D(_DetailA); SAMPLER(sampler_DetailA);
        TEXTURE2D(_DetailNM); SAMPLER(sampler_DetailNM);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MultU;
            float4 _MultV;
            float4 _Light;
            float4 _Emissive;
            float4 _Fresnel;
            float4 _Layer;
            float4 _Flags;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Cull;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz, true), input.tangentOS.w * GetOddNegativeScale());
                o.uv = input.uv0;
                return o;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv);
                // details: world metres from above (AC x = -Unity x; the track sits at the AC origin), _objsp: the mesh uv
                float2 duv = _Flags.x > 0.5 ? uv : float2(-input.positionWS.x, input.positionWS.z);
                half4 dR = SAMPLE_TEXTURE2D(_DetailR, sampler_DetailR, duv * float2(_MultU.x, _MultV.x));
                half4 dG = SAMPLE_TEXTURE2D(_DetailG, sampler_DetailG, duv * float2(_MultU.y, _MultV.y));
                half4 dB = SAMPLE_TEXTURE2D(_DetailB, sampler_DetailB, duv * float2(_MultU.z, _MultV.z));
                half4 dA = SAMPLE_TEXTURE2D(_DetailA, sampler_DetailA, duv * float2(_MultU.w, _MultV.w));
                half4 detail = dR * mask.r + dG * mask.g + dB * mask.b + dA * mask.a;
                half3 albedo = base.rgb * detail.rgb * (half)_Layer.x;
                half specMask = detail.a;

                float3 n = normalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                if ((_Layer.w > 0.5 || _Flags.y > 0.5) && dot(input.tangentWS.xyz, input.tangentWS.xyz) > 1e-8)
                {
                    float3 t = normalize(input.tangentWS.xyz);
                    float3 b = cross(n, t) * input.tangentWS.w;
                    half3 nt = half3(0.0h, 0.0h, 1.0h);
                    if (_Flags.y > 0.5) nt = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv).xyz * 2.0h - 1.0h;
                    if (_Layer.w > 0.5)
                    {
                        half3 dn = SAMPLE_TEXTURE2D(_DetailNM, sampler_DetailNM, duv * _Layer.z).xyz * 2.0h - 1.0h;
                        nt = half3(nt.xy + dn.xy, nt.z);
                    }
                    n = normalize(t * nt.x + b * nt.y + n * nt.z);
                }

                float3 L = normalize(_AcSunDir.xyz);
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                half ndl = (half)saturate(dot(n, L));
                half3 sun = (half3)_AcSunColor.rgb * AcShadow(input.positionWS);
                half3 col = albedo * ((half3)_AcAmbient.rgb * (half)_Light.x + sun * ((half)_Light.y * ndl));
                float3 H = normalize(L + V);
                half spec = (half)(pow(saturate(dot(n, H)), max(_Light.w, 1.0)) * _Light.z * _Layer.y) * specMask * (ndl > 0.0h ? 1.0h : 0.0h);
                col += sun * spec;
                if (_Fresnel.w > 0.5)
                {
                    float3 R = reflect(-V, n);
                    half3 env = AcSkyColor(R);
                    half f = (half)saturate(_Fresnel.x + (1.0 - _Fresnel.x) * pow(1.0 - saturate(dot(n, V)), _Fresnel.y));
                    f = saturate(min(f, (half)_Fresnel.z) * specMask);
                    if (_Fresnel.w > 1.5) col += env * f;
                    else col = lerp(col, env, f);
                }
                col += (half3)_Emissive.rgb * albedo;
                col = AcFog(col, input.positionWS);
                return half4(AcOut(col), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings o = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return o;
            }

            void DepthNormalsFrag(DNVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(IS_FRONT_VFACE(face, n, -n), 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
