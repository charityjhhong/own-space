// Floor and wall finish. Two modes: a solid color with one simple procedural pattern (no textures), or a tileable CC0 PBR set
// (albedo, normal, roughness; 1K, ASTC) projected from world position at a true scale in metres, so it fits any mesh without UVs.
// The pattern is projected from world position (floor: x/z, walls: along the wall), so it fits any
// mesh without UVs. Lit with URP's own PBR function, so the main light, placed lamps (additional lights,
// Forward and cluster/Forward+) and ambient all light it; shadows are off to stay inside the Quest budget.
Shader "OwnSpace/Finish"
{
    Properties
    {
        _BaseColor ("Base color", Color) = (0.8, 0.8, 0.8, 1)
        _AccentColor ("Accent color (lines, stripes, alternate tiles)", Color) = (0.6, 0.6, 0.6, 1)
        [Enum(Solid,0,Stripes,1,Checker,2,Planks,3,Tiles,4)] _Pattern ("Pattern", Float) = 0
        _Scale ("Pattern size (m)", Float) = 0.3
        _Line ("Line width (fraction of size)", Range(0, 0.5)) = 0.03
        [Toggle] _UseTex ("Use the texture set", Float) = 0
        [NoScaleOffset] _BaseMap ("Albedo", 2D) = "white" {}
        [NoScaleOffset] _NormalMap ("Normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap ("Roughness", 2D) = "white" {}
        _TexScale ("Texture repeat size (m)", Float) = 1.0
        _Gain ("Albedo gain (tinted greys)", Float) = 1.0
        _NormalStrength ("Normal strength", Range(0, 2)) = 1.0
        _MaxSmooth ("Highest smoothness the roughness map may give", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/MR/Shaders/ToneMatch.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _AccentColor;
                float _Pattern;
                float _Scale;
                float _Line;
                float _UseTex;
                float _TexScale;
                float _Gain;
                float _NormalStrength;
                float _MaxSmooth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 vertexLighting : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                ZERO_INITIALIZE(Varyings, output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
                #endif
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Plane coordinates in meters: x/z on floors and ceilings, along the wall on walls.
            float2 PlaneCoords(float3 p, float3 n)
            {
                float3 a = abs(n);
                if (a.y > 0.5) return p.xz;
                return a.x > a.z ? p.zy : p.xy;
            }

            half3 Pattern(float2 coords)
            {
                float2 p = coords / max(_Scale, 0.01);
                float accent = 0;
                half shade = 1;
                int mode = (int)(_Pattern + 0.5);
                if (mode == 1)
                {
                    accent = step(0.5, frac(p.x));
                }
                else if (mode == 2)
                {
                    accent = fmod(abs(floor(p.x) + floor(p.y)), 2);
                }
                else if (mode == 3)
                {
                    // Planks 4 times longer than wide, each row shifted; a thin seam in the accent color.
                    float row = floor(p.y);
                    float along = p.x * 0.25 + frac(row * 0.37);
                    float seam = max(step(frac(p.y), _Line), step(frac(along), _Line * 0.25));
                    shade = 1 - 0.06 * Hash(float2(row, floor(along)));
                    accent = seam;
                }
                else if (mode == 4)
                {
                    float2 f = frac(p);
                    accent = max(step(f.x, _Line), step(f.y, _Line));
                }
                return lerp(_BaseColor.rgb, _AccentColor.rgb, accent) * shade;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 albedo;
                half smoothness = 0.1; // matte
                if (_UseTex > 0.5)
                {
                    // Tangent frame from the plane projection: floors u = world x; walls u = along the wall; v = cross(n, u) (up on walls).
                    float3 t = abs(n.y) > 0.5 ? float3(1, 0, 0) : normalize(cross(float3(0, 1, 0), n));
                    float3 b = cross(n, t);
                    float2 uv = float2(dot(input.positionWS, t), dot(input.positionWS, b)) / max(_TexScale, 0.05);
                    albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb * _Gain;
                    smoothness = saturate(1.0 - SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uv).r) * _MaxSmooth; // a satin finish, never glossy
                    half3 nt = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv).xyz * 2.0 - 1.0;
                    n = normalize(t * nt.x * _NormalStrength + b * nt.y * _NormalStrength + n * max(nt.z, 0.2));
                }
                else albedo = _Pattern < 0.5 ? _BaseColor.rgb : Pattern(PlaneCoords(input.positionWS, n));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = float4(0, 0, 0, 0);
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = ToneMatch(color.rgb, input.positionCS);   // sits the surface with passthrough (MR/Shaders/ToneMatch.hlsl)
                return color;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
