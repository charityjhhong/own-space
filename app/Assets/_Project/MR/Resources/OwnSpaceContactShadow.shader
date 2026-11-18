// A soft contact (blob) shadow on the floor under a virtual piece, so it sits on the floor instead of floating.
// No shadow maps: a flat 4 x 4 grid (ContactShadows.MeshFor) whose vertex alpha is 1 over the footprint and 0 a fixed
// 20 cm past it, plus small dark pads near the footprint's corners (legs). It writes premultiplied black, so over passthrough
// (eye buffer alpha 0) the compositor darkens the real floor by alpha, and over a virtual floor it darkens that.
// Loaded from Resources (ContactShadows) so it ships in builds; one shared material, instancing on.
Shader "OwnSpace/ContactShadow"
{
    Properties
    {
        _Strength ("Darkness over the footprint (0-1)", Range(0, 1)) = 0.55
        _Pad ("Extra darkness of the leg pads (0-1)", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ContactShadow"
            ZWrite Off
            ZTest LEqual
            Offset -1, -1 // a little toward the eye: never lost in the floor it lies on
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                half _Pad;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float2 half2 : TEXCOORD1; half fade : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.local = v.uv;    // metres from the footprint's centre
                o.half2 = v.uv2;   // the footprint's half size
                o.fade = v.color.a; // 1 over the footprint, 0 at the outer edge
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // The same rule as ContactShadows.Alpha (tested there).
                half f = saturate(i.fade);
                half a = _Strength * f * f * ((half)3.0 - (half)2.0 * f);
                float2 q = abs(i.local);
                float2 leg = max(i.half2 - 0.05, 0.0);
                half pad = _Pad * ((half)1.0 - smoothstep((half)0.0, (half)1.0, (half)saturate(length(q - leg) / 0.09)));
                a = a + ((half)1.0 - a) * pad;
                a *= a > (half)0.002; // no faint full-mesh rectangle
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }
}
