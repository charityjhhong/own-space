// Furniture and prop surfaces: a base colour, a base map (albedo), an optional normal map and roughness map (Poly Haven 1K sets), lit with URP's PBR function (main light, placed lamps, ambient;
// no shadows, to stay inside the Quest budget), then ToneMatch so the piece sits with passthrough. Replaces "Universal Render Pipeline/Lit"
// for catalog pieces, because the stock shader cannot call ToneMatch. SRP Batcher compatible (all material data in UnityPerMaterial).
Shader "OwnSpace/Furniture"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _NormalMap ("Normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap ("Roughness", 2D) = "white" {}
        [Toggle] _UseMaps ("Use the normal and roughness maps", Float) = 0
        _NormalStrength ("Normal strength", Range(0, 2)) = 1.0
        [Toggle] _Recolor ("Recolour: keep the albedo detail, replace its colour by the base colour", Float) = 0
        _MeanLum ("Mean luminance of the albedo (for Recolor)", Float) = 0.5
        _Smoothness ("Smoothness (without a roughness map)", Range(0, 1)) = 0.2
        [Toggle] _GlowByAlbedo ("Emission follows the albedo detail (a lit fabric shade)", Float) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _BottomAO ("Bottom ambient occlusion (0 off, 1 full)", Range(0, 1)) = 1
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
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half _GlowByAlbedo;
                half _UseMaps;
                half _NormalStrength;
                half _Recolor;
                half _MeanLum;
                half4 _EmissionColor;
                half _BottomAO;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 vertexLighting : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half4 tangentWS : TEXCOORD4;
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                if (_Recolor > 0.5)
                {
                    // Keep the texture's light and dark detail, but take its colour from the base colour (a dark leather made terracotta).
                    half lum = dot(baseSample.rgb, half3(0.2126, 0.7152, 0.0722));
                    albedo = saturate(lum / max(_MeanLum, 0.02) * 0.6) * _BaseColor.rgb * 1.4;
                }
                // Soft bottom ambient occlusion: the lowest 13 cm above the floor darken to 0.75 at the base and fade to 1 (no texture sample).
                // The pieces stand on the floor with their pivot there, so height above the room floor is the height above the piece's feet.
                half aoHeight = (half)(input.positionWS.y - (_OS_RoomInfo.w > 0.5 ? _OS_RoomInfo.y : 0.0));
                albedo *= lerp((half)1.0, lerp((half)0.75, (half)1.0, smoothstep((half)0.0, (half)0.13, aoHeight)), _BottomAO);
                half smoothness = _Smoothness;
                if (_UseMaps > 0.5)
                {
                    half3 nt = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv).xyz * 2.0 - 1.0;
                    half3 t = normalize(input.tangentWS.xyz);
                    half3 b = cross(n, t) * input.tangentWS.w;
                    n = normalize(t * nt.x * _NormalStrength + b * nt.y * _NormalStrength + n * max(nt.z, 0.2));
                    smoothness = saturate(1.0 - SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, input.uv).r) * 0.7;
                }

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
                surface.emission = _EmissionColor.rgb;
                if (_GlowByAlbedo > 0.5) surface.emission *= clamp(dot(baseSample.rgb, half3(0.2126, 0.7152, 0.0722)) / max(_MeanLum, 0.02), 0.45, 1.25);   // the glow follows the weave, so it never washes out
                surface.normalTS = half3(0, 0, 1);
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = ToneMatch(color.rgb, input.positionCS);   // sits the piece with passthrough (MR/Shaders/ToneMatch.hlsl)
                return color;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
