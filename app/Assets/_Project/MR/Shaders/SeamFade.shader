// The soft seams between real (passthrough) and virtual: the floor and ceiling around my zone, the walls where
// they cross the zone border, and the top of restyled walls under the real ceiling. Drawn over the virtual
// surface already in the eye buffer, it scales that pixel (colour and alpha, premultiplied) by the vertex alpha:
// alpha 0 shows passthrough, 1 the surface as rendered (its own texture and pattern, no flat colour step).
// Overlapping bands (zone corners, wall meets floor or ceiling) multiply, so they only ever fade further toward
// passthrough near the border and never punch through elsewhere. _BaseColor is unused (kept for the material).
Shader "OwnSpace/SeamFade"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.8, 0.78, 0.74, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" "RenderType" = "Opaque" }
        Pass
        {
            Name "Seam"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Zero SrcAlpha // dst * a, for rgb and alpha alike

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(1, 1, 1, i.color.a);
            }
            ENDHLSL
        }
    }
}
