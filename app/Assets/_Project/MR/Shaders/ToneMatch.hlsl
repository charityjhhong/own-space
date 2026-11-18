#ifndef OWNSPACE_TONEMATCH_INCLUDED
#define OWNSPACE_TONEMATCH_INCLUDED

// Realism pass H: makes a virtual surface sit with passthrough, in the surface's own shader (no extra pass, so the
// Quest's tile render pass stays intact). Call it on the final lit colour of every virtual surface:
//
//     #include "Assets/_Project/MR/Shaders/ToneMatch.hlsl"
//     color.rgb = ToneMatch(color.rgb, input.positionCS);   // positionCS: the fragment's SV_POSITION
//
// It lowers saturation and contrast a little, takes some of the mood's colour temperature and adds a very light
// grain. The numbers live in MR/Resources/ToneMatchSettings.asset; MoodApplier pushes them as these globals when
// the look changes. Until they are set (all zero) it returns the colour unchanged. Globals are outside
// UnityPerMaterial, so SRP Batcher compatibility is unaffected. Do not use it on UI (the panel stays crisp).

half4 _OS_ToneGrade; // saturation, contrast, grain, on (1)
half4 _OS_ToneTint;  // white-balance multiplier (rgb)

// The grade without grain (pure colour).
half3 ToneMatchGrade(half3 rgb)
{
    if (_OS_ToneGrade.w < (half)0.5) return rgb;
    half l = dot(rgb, half3(0.2126, 0.7152, 0.0722));
    rgb = lerp(l.xxx, rgb, _OS_ToneGrade.x);
    rgb = (half3)(0.18 * pow(max((float3)rgb, 0.0) / 0.18, (float)_OS_ToneGrade.y)); // flatter around mid-grey; black stays black
    return rgb * _OS_ToneTint.rgb;
}

// The grade plus a light per-pixel grain (positionCS.xy = pixel coordinates; animated with time).
half3 ToneMatch(half3 rgb, float4 positionCS)
{
    if (_OS_ToneGrade.w < (half)0.5) return rgb;
    rgb = ToneMatchGrade(rgb);
    float2 p = positionCS.xy + frac(_Time.y * 7.0) * 113.0;
    half n = (half)frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
    return max(rgb + n * _OS_ToneGrade.z, (half3)0);
}

// ---- Contact shading and grain for the room's surfaces (MR/ShellShading.cs pushes the room; the human, build 9bfbd72:
// "the wall corners lack realism") ----
// Call it instead of ToneMatch on the finish (floor, wall, ceiling) with the fragment's world position:
//
//     color.rgb = ToneMatchSurface(color.rgb, input.positionCS, input.positionWS);
//
// A soft darkening where surfaces meet (wall-wall, wall-floor, wall-ceiling), gone within _OS_Crease.y metres, a slight
// falloff toward wall-wall corners, and a fine value-noise grain anchored in world space (never crawls with the head).
// It only scales the colour, so the mood still sets how dark things are and a crease never goes black.

#define OS_MAX_CORNERS 16  // matches ShellShading.MaxCorners
float4 _OS_RoomEdges[OS_MAX_CORNERS]; // the room outline's corners (xy = world x, z)
float4 _OS_RoomInfo;      // corner count, floor y, ceiling y, on (1)
float4 _OS_Crease;        // wall crease strength (wall-wall, wall-floor), crease width (m), wall-ceiling strength, grain

half OS_Near(float d, float width) { return (half)(1.0 - smoothstep(0.0, width, max(d, 0.0))); }

float OS_Hash(float3 c) { return frac(sin(dot(c, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

// Smooth value noise in world space (cells of 1/scale metres).
float OS_Noise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = OS_Hash(i), n100 = OS_Hash(i + float3(1, 0, 0)), n010 = OS_Hash(i + float3(0, 1, 0)), n110 = OS_Hash(i + float3(1, 1, 0));
    float n001 = OS_Hash(i + float3(0, 0, 1)), n101 = OS_Hash(i + float3(1, 0, 1)), n011 = OS_Hash(i + float3(0, 1, 1)), n111 = OS_Hash(i + float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

half OS_SurfaceShade(float3 p)
{
    if (_OS_RoomInfo.w < 0.5) return 1;
    int n = min((int)_OS_RoomInfo.x, OS_MAX_CORNERS);
    float wall = 1e6, corner = 1e6;
    // A constant upper bound (OS_MAX_CORNERS) for the Quest compiler; the real count ends it early.
    [loop] for (int i = 0; i < OS_MAX_CORNERS; i++)
    {
        if (i >= n) break;
        float2 a = _OS_RoomEdges[i].xy, b = _OS_RoomEdges[i + 1 < n ? i + 1 : 0].xy, ab = b - a; // no integer modulus (slow on mobile)
        corner = min(corner, distance(p.xz, a));
        float t = saturate(dot(p.xz - a, ab) / max(dot(ab, ab), 1e-8));
        wall = min(wall, distance(p.xz, a + ab * t));
    }
    float w = _OS_Crease.y;
    half sWall = OS_Near(wall, w);
    half low = max(OS_Near(corner, w), OS_Near(p.y - _OS_RoomInfo.y, w) * sWall); // wall-wall, wall-floor
    half high = OS_Near(_OS_RoomInfo.z - p.y, w) * sWall;                           // wall-ceiling: the lightest
    // Exactly 1 away from the creases: the room is as bright as before (device feedback: "too dark").
    half shade = min(1 - (half)_OS_Crease.x * low, 1 - (half)_OS_Crease.z * high);
    // A soft, zero-mean paint variation (about 15 cm and 60 cm), none on the ceiling.
    half onCeiling = (half)step(_OS_RoomInfo.z - 0.02, p.y);
    half grain = (half)((OS_Noise(p * 6.5) - 0.5) * 0.6 + (OS_Noise(p * 1.6) - 0.5) * 0.4) * 2 * (half)_OS_Crease.w * (1 - onCeiling);
    return shade * (1 + grain);
}

half3 ToneMatchSurface(half3 rgb, float4 positionCS, float3 positionWS)
{
    return ToneMatch(rgb * OS_SurfaceShade(positionWS), positionCS);
}

#endif
