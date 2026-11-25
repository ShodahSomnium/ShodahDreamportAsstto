// SomniumAssetto/Sky - Assetto Corsa's procedural sky: the weather's colorCurves.ini HORIZON colour blending to its SKY (zenith) colour,
// a sun disc and glow in the SUN colour along the track's sun direction (AcCommon.hlsl globals). AC ships no sky textures. Drawn around
// the eye at the far plane (the dome mesh follows the camera in the vertex shader), queue 2501+ (URP depth priming: after the opaques),
// no depth write. Sun disc size / glow are ours.
Shader "SomniumAssetto/Sky"
{
    Properties
    {
        _SunSize ("Sun disc cos (ours)", Float) = 0.99985
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Geometry+501" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "AcCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _SunSize;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 dir = input.positionOS.xyz;
                float3 positionWS = GetCameraPositionWS() + dir * 1000.0;
                float4 clip = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    clip.z = clip.w * 0.000001;
                #else
                    clip.z = clip.w * 0.999999;
                #endif
                o.positionCS = clip;
                o.dir = dir;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.dir);
                half3 col = AcSkyColor(d);
                float3 L = normalize(_AcSunDir.xyz);
                float cs = dot(d, L);
                // glow around the sun and the disc itself (ours)
                half glow = (half)pow(saturate(cs), 64.0) * 0.25h;
                half disc = (half)smoothstep(_SunSize - 0.00005, _SunSize, cs);
                col += (half3)_AcSunColor.rgb * (glow * 0.25h + disc);
                // the fog of a surface 2 km away (ours) tints the sky near the horizon (a heavy-fog sky is all fog colour)
                float fogFar = _AcFogParams.x > 0.0 ? pow(saturate(2000.0 * _AcFogParams.x), max(_AcFogParams.y, 0.05)) : 0.0;
                float toZenith = fogFar >= 0.999 ? 1.0 : 1.0 - saturate(d.y * 2.0);
                col = lerp(col, (half3)_AcFogColor.rgb, (half)(fogFar * toZenith));
                return half4(AcOut(col), 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
