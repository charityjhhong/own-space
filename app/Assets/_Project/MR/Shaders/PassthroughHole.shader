// Writes transparent black and depth: with the passthrough underlay this shows the real room here
// and hides any virtual surface behind it. Drawn before opaque geometry (Geometry-10).
// _Cull: Off for flat holes (the zone's real floor). RealTops sets Front on its boxes, so only the far faces write:
// what is behind or under the box gives way, what is inside it stays.
Shader "OwnSpace/PassthroughHole"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-10" "RenderType" = "Opaque" }
        Pass
        {
            Name "Hole"
            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
