// Soft round glow for placed lamps and window light, drawn on a quad over a real or virtual surface.
// Procedural (no texture): additive color times a radial falloff, so it only brightens, never covers.
// Alpha: the blend adds colour only and leaves the framebuffer alpha alone (Zero One), and the fragment writes
// alpha 0. Under passthrough, alpha 0 means "show the real room", so black must never be written as opaque.
// Cheap on Quest: one quad, no lighting, no depth write. Keep few glows in view (budget in mood-presets.md).
Shader "OwnSpace/LightGlow"
{
    Properties
    {
        [HDR] _Color ("Glow color", Color) = (1, 0.72, 0.42, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.5
        _Power ("Falloff power (higher = tighter)", Range(0.5, 6)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One, Zero One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Power;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float r = length(input.uv - 0.5) * 2.0;
                float falloff = pow(saturate(1.0 - r), _Power);
                return half4(_Color.rgb * (falloff * _Intensity), 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
