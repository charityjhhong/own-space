// Test only: proves MR/Shaders/ToneMatch.hlsl compiles inside a URP shader (ToneMatchTests).
Shader "Hidden/OwnSpace/ToneMatchCheck"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Project/MR/Shaders/ToneMatch.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 Frag(Varyings i) : SV_Target { return half4(ToneMatch(half3(0.5, 0.4, 0.3), i.positionCS) + ToneMatchGrade(half3(0.2, 0.2, 0.2)), 1); }
            ENDHLSL
        }
    }
}
