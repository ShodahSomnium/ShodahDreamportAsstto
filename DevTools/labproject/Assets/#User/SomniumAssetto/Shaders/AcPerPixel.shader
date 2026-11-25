// SomniumAssetto/PerPixel - Assetto Corsa's ksPerPixel shader family (ksPerPixel, NM, AT, AT_NM, MultiMap, MultiMap_AT, MultiMap_NMDetail,
// MultiMapSimpleRefl, Reflection, SimpleRefl, NM_UVMult, NM_UV2, ksTree, ksGrass, ksFlags, GL unlit, and the car shaders
// ksPerPixelMultiMap_damage_dirt, ksTyres, ksBrakeDisc, ksWindscreen ...). Written by Game/World/AcMaterials from the kn5 material.
// No material keywords (nothing for the upload to strip): float switches only. URP main-light shadow keywords as URP Lit (if the upload
// strips them the surfaces are simply unshadowed). Passes: Forward, ShadowCaster, DepthOnly, DepthNormals (depth priming is forced in the
// client), stereo instancing in each.
//
// AC's maths, gamma space on the raw texels (AcCommon.hlsl):
//   albedo   = txDiffuse (x txDetail(uv * detailUVMultiplier) where txDiffuse alpha is low, when useDetail) [BELIEF on the detail blend]
//   normal   = txNormal (DirectX convention, uv x normalMult) + txNormalDetail x detailNormalBlend
//   light    = ambient x ksAmbient + sun x ksDiffuse x N.L x shadow
//   colour   = albedo x light + sun x ksSpecular x txMaps.r x (N.H)^(ksSpecularEXP x txMaps.g) x shadow
//   reflect  = sky colour along the mirror direction x fresnel (fresnelC + (1 - fresnelC)(1 - N.V)^fresnelEXP, capped at fresnelMaxLevel) x txMaps.b,
//              blended (isAdditive 0) or added (isAdditive 1/2)
//   + ksEmissive x albedo, then AC's distance fog; alpha = txDiffuse.a x alpha (ksPerPixelAlpha) for blended materials, alpha test at ksAlphaRef.
// Iteration 2 (DAMAGE_DESIGN 2.1 / 2.2), float switches only, nothing costs when off:
//   damage paint (_DamageSet.x, ksPerPixelMultiMap_damage_dirt): m = txDamageMask(uv0) (R left, G rear, B right, A front), scratch weight
//     ws = saturate(m . _DamageScratch), dent weight wd = saturate(m . _DamageDent); normal = lerp(flat, txNormal (the DAMAGE NM), wd);
//     albedo = lerp(albedo, txDamage.rgb, txDamage.a x ws); reflection and specular x lerp(1, txNormal.a, ws) (NM alpha = scratched) [I blend]
//   broken glass (_DamageSet.w, ksBrokenGlass): alpha = txNormal.a (the install's content/texture/DAMAGE_GLASS.dds) x _GlassSet.x
//     (glassDamage; 0 on the shared material = invisible, G0); colour = the kn5's proxy txDiffuse lit as usual.
Shader "SomniumAssetto/PerPixel"
{
    Properties
    {
        _MainTex ("txDiffuse", 2D) = "white" {}
        _NormalTex ("txNormal", 2D) = "bump" {}
        _MapsTex ("txMaps (r spec, g gloss, b reflection)", 2D) = "white" {}
        _DetailTex ("txDetail", 2D) = "white" {}
        _DetailNormalTex ("txNormalDetail", 2D) = "bump" {}
        _Light ("x ksAmbient, y ksDiffuse, z ksSpecular, w ksSpecularEXP", Vector) = (0.4, 0.4, 0, 10)
        _Emissive ("ksEmissive (AC units)", Color) = (0, 0, 0, 0)
        _Fresnel ("x fresnelC, y fresnelEXP, z fresnelMaxLevel, w mode (0 off, 1 blend, 2 add)", Vector) = (0, 1, 0, 0)
        _DetailSet ("x useDetail, y detailUVMultiplier, z detailNormalBlend", Vector) = (0, 1, 0, 0)
        _Flags ("x alpha-test ref (0 off), y normal map, z txMaps, w unlit (GL)", Vector) = (0, 0, 0, 0)
        _Extra ("x alpha mult, y normal uv mult, z diffuse uv mult", Vector) = (1, 1, 1, 0)
        _Wave ("ksFlags: x frequency, y distortion; z sunSpecular, w sunSpecularEXP", Vector) = (0, 0, 0, 0)
        _DamageTex ("txDamage (scratches)", 2D) = "black" {}
        _DamageMaskTex ("txDamageMask (r left, g rear, b right, a front)", 2D) = "black" {}
        _DamageScratch ("scratch weight per mask channel (left, rear, right, front)", Vector) = (0, 0, 0, 0)
        _DamageDent ("dent weight per mask channel (left, rear, right, front)", Vector) = (0, 0, 0, 0)
        _DamageSet ("x damage paint on, w broken-glass mode", Vector) = (0, 0, 0, 0)
        _GlassSet ("x glassDamage", Vector) = (0, 0, 0, 0)
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
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
        TEXTURE2D(_MapsTex); SAMPLER(sampler_MapsTex);
        TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
        TEXTURE2D(_DetailNormalTex); SAMPLER(sampler_DetailNormalTex);
        TEXTURE2D(_DamageTex); SAMPLER(sampler_DamageTex);
        TEXTURE2D(_DamageMaskTex); SAMPLER(sampler_DamageMaskTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Light;
            float4 _Emissive;
            float4 _Fresnel;
            float4 _DetailSet;
            float4 _Flags;
            float4 _Extra;
            float4 _Wave;
            float4 _DamageScratch;
            float4 _DamageDent;
            float4 _DamageSet;
            float4 _GlassSet;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Cull;
        CBUFFER_END

        void AcAlphaTest(float2 uv)
        {
            if (_Flags.x > 0.0) clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _Extra.z).a - _Flags.x);
        }
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
                float3 posOS = AcWave(input.positionOS.xyz, input.normalOS, input.uv0, _Wave);
                o.positionWS = TransformObjectToWorld(posOS);
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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _Extra.z);
                if (_Flags.x > 0.0) clip(tex.a - _Flags.x);
                half3 albedo = tex.rgb;
                half alpha = saturate(tex.a * (half)_Extra.x);
                if (_DamageSet.w > 0.5)
                {
                    // ksBrokenGlass: invisible until this car's clone gets glassDamage > 0 (the shared material keeps 0 - G0)
                    clip(_GlassSet.x - 0.001);
                    alpha = saturate(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv).a * (half)_GlassSet.x);
                }
                half4 maps = half4(1.0h, 1.0h, 1.0h, 1.0h);
                if (_Flags.z > 0.5) maps = SAMPLE_TEXTURE2D(_MapsTex, sampler_MapsTex, uv);
                if (_DetailSet.x > 0.5)
                {
                    half4 det = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv * _DetailSet.y);
                    albedo *= lerp(det.rgb, half3(1.0h, 1.0h, 1.0h), tex.a);
                }
                bool dmgPaint = _DamageSet.x > 0.5;
                half ws = 0.0h, wd = 0.0h;
                if (dmgPaint)
                {
                    half4 dmask = SAMPLE_TEXTURE2D(_DamageMaskTex, sampler_DamageMaskTex, uv);
                    ws = saturate(dot(dmask, (half4)_DamageScratch));
                    wd = saturate(dot(dmask, (half4)_DamageDent));
                    half4 scr = SAMPLE_TEXTURE2D(_DamageTex, sampler_DamageTex, uv);
                    albedo = lerp(albedo, scr.rgb, scr.a * ws);
                }
                half4 nmS = half4(0.5h, 0.5h, 1.0h, 1.0h);
                if (_Flags.y > 0.5) nmS = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv * _Extra.y);
                half scratchK = dmgPaint ? lerp(1.0h, nmS.a, ws) : 1.0h;   // NM alpha: scratched = less reflective (Pipeline)
                maps.b *= scratchK;

                float3 n = normalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                if (_Flags.y > 0.5 && dot(input.tangentWS.xyz, input.tangentWS.xyz) > 1e-8)
                {
                    float3 t = normalize(input.tangentWS.xyz);
                    float3 b = cross(n, t) * input.tangentWS.w;
                    half3 nt = nmS.xyz * 2.0h - 1.0h;
                    if (dmgPaint) nt = lerp(half3(0.0h, 0.0h, 1.0h), nt, wd);   // dents only where damaged: undamaged = flat
                    if (_DetailSet.z > 0.0)
                    {
                        half3 dn = SAMPLE_TEXTURE2D(_DetailNormalTex, sampler_DetailNormalTex, uv * _DetailSet.y).xyz * 2.0h - 1.0h;
                        nt = half3(nt.xy + dn.xy * (half)_DetailSet.z, nt.z);
                    }
                    n = normalize(t * nt.x + b * nt.y + n * nt.z);
                }

                half3 col;
                if (_Flags.w > 0.5)
                {
                    col = albedo + (half3)_Emissive.rgb * albedo;
                }
                else
                {
                    float3 L = normalize(_AcSunDir.xyz);
                    float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                    half ndl = (half)saturate(dot(n, L));
                    half3 sun = (half3)_AcSunColor.rgb * AcShadow(input.positionWS);
                    half3 light = (half3)_AcAmbient.rgb * (half)_Light.x + sun * ((half)_Light.y * ndl);
                    col = albedo * light;
                    float3 H = normalize(L + V);
                    float specExp = max(_Light.w * (_Flags.z > 0.5 ? maps.g : 1.0h), 1.0);
                    half spec = (half)(pow(saturate(dot(n, H)), specExp) * _Light.z) * maps.r * scratchK * (ndl > 0.0h ? 1.0h : 0.0h);
                    col += sun * spec;
                    if (_Wave.z > 0.0) col += sun * (half)(pow(saturate(dot(n, H)), max(_Wave.w, 1.0)) * _Wave.z) * maps.r * scratchK * (ndl > 0.0h ? 1.0h : 0.0h);   // sunSpecular / sunSpecularEXP (car paint)
                    if (_Fresnel.w > 0.5)
                    {
                        float3 R = reflect(-V, n);
                        half3 env = AcSkyColor(R);
                        half f = (half)saturate(_Fresnel.x + (1.0 - _Fresnel.x) * pow(1.0 - saturate(dot(n, V)), _Fresnel.y));
                        f = min(f, (half)_Fresnel.z) * maps.b;
                        if (_Fresnel.w > 1.5) col += env * f;
                        else col = lerp(col, env, saturate(f));
                    }
                    col += (half3)_Emissive.rgb * albedo;
                }
                col = AcFog(col, input.positionWS);
                return half4(AcOut(col), alpha);
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
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 posOS = AcWave(input.positionOS.xyz, input.normalOS, input.uv0, _Wave);
                float3 positionWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                o.uv = input.uv0;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                AcAlphaTest(input.uv);
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
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(AcWave(input.positionOS.xyz, input.normalOS, input.uv0, _Wave));
                o.uv = input.uv0;
                return o;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                AcAlphaTest(input.uv);
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
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings o = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(AcWave(input.positionOS.xyz, input.normalOS, input.uv0, _Wave));
                o.uv = input.uv0;
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
                AcAlphaTest(input.uv);
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
