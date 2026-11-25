// SomniumAssetto shared shader code (included by AcPerPixel / AcMultilayer / AcSky after URP Shadows.hlsl, which brings Core.hlsl).
//
// Colour: Assetto Corsa shades in gamma space on the raw texel values (DX11, no sRGB textures). Our kn5 textures are uploaded LINEAR
// (raw stored values, Game/World/AcTextures), the maths below runs on those values exactly as AC's per-pixel shaders do, and AcOut()
// turns the gamma-space result into the linear value URP writes (the sRGB target encodes it back) - the Halo Dreamport recipe.
// Globals (Game/World/AcMaterials.SetLight, from the track's lighting.ini and the weather's colorCurves.ini / weather.ini x HDR_OFF_MULT):
#ifndef SOMNIUM_ASSETTO_COMMON
#define SOMNIUM_ASSETTO_COMMON

float4 _AcSunDir;       // xyz towards the sun (Unity world)
float4 _AcSunColor;     // rgb sun light (AC units x HDR_OFF_MULT)
float4 _AcAmbient;      // rgb ambient light
float4 _AcSkyZenith;    // rgb sky colour overhead (colorCurves SKY)
float4 _AcSkyHorizon;   // rgb sky colour at the horizon (colorCurves HORIZON)
float4 _AcFogColor;     // rgb fog colour (weather.ini [FOG] COLOR x HDR_OFF_MULT)
float4 _AcFogParams;    // x = 1 / DISTANCE (0 = no fog), y = BLEND (exponent of the distance ratio [BELIEF]), z = 1 when set

// sRGB decode (gamma-space value -> linear), as the sRGB target will encode it back
half3 AcLinear(half3 c)
{
    half3 lo = c / 12.92h;
    half3 hi = pow(max((c + 0.055h) / 1.055h, 0.0h), 2.4h);
    return half3(c.r <= 0.04045h ? lo.r : hi.r, c.g <= 0.04045h ? lo.g : hi.g, c.b <= 0.04045h ? lo.b : hi.b);
}
half3 AcOut(half3 g) { return AcLinear(saturate(g)); }

// AC draws no sky texture: the sky is the horizon colour blending to the zenith colour; below the horizon a dim ground tone (ours).
half3 AcSkyColor(float3 dir)
{
    float h = dir.y;
    half3 c;
    if (h >= 0.0)
        c = lerp((half3)_AcSkyHorizon.rgb, (half3)_AcSkyZenith.rgb, (half)sqrt(saturate(h)));
    else
        c = lerp((half3)_AcSkyHorizon.rgb, (half3)_AcAmbient.rgb * 0.25h, (half)saturate(-h * 4.0));
    return c;
}

half AcFogAmount(float3 positionWS)
{
    if (_AcFogParams.x <= 0.0) return 0.0h;
    float d = length(positionWS - GetCameraPositionWS());
    return (half)pow(saturate(d * _AcFogParams.x), max(_AcFogParams.y, 0.05));
}

half3 AcFog(half3 c, float3 positionWS)
{
    return lerp(c, (half3)_AcFogColor.rgb, AcFogAmount(positionWS));
}

// URP main-light shadow (the "Ac Sun" directional light); 1 = lit. Without the shadow keywords this is 1.
// With screen-space shadows (_MAIN_LIGHT_SHADOWS_SCREEN) URP samples _ScreenSpaceShadowmapTexture at a SCREEN position (its GetShadowCoord
// uses ComputeScreenPos(positionCS)); recomputed per pixel here from the world position (the projective divide happens in the sampler).
half AcShadow(float3 positionWS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
#endif
    half s = MainLightRealtimeShadow(shadowCoord);
    return lerp(s, 1.0h, GetMainLightShadowFade(positionWS));
}

// ksFlags: the flag cloth waves along its normal (frequency, distortion from the material). // ours: the wave shape
float3 AcWave(float3 positionOS, float3 normalOS, float2 uv, float4 wave)
{
    if (wave.x <= 0.0 || wave.y <= 0.0) return positionOS;
    float ph = _Time.y * wave.x * 0.25 + dot(positionOS, float3(1.3, 0.7, 1.1)) * 2.0;
    return positionOS + normalOS * (sin(ph) * wave.y * saturate(abs(uv.x) * 2.0));
}

#endif
