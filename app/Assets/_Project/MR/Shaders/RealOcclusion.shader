// Real things in front of virtual ones (device batch 10: the real desk, its things, the chair and the user's body were
// drawn over by the farther virtual wall). A URP Render Objects pass redraws the opaque geometry with this override
// after the opaques (ZTest LEqual, no depth write). Where Meta's Environment Depth puts a real surface in front of the
// virtual one, this fades colour and alpha toward zero, so the passthrough underlay shows through. Only real surfaces
// that are kept real count: inside my zone (up to a height), or just above a kept-real piece (the RG mask). The real
// walls behind the virtual shell never cut it. Content's shaders are untouched.
Shader "OwnSpace/RealOcclusion"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "RealOcclusion"
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend Zero SrcAlpha // colour and alpha are multiplied by the visibility

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Set by Meta's EnvironmentDepthManager.
            TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthReprojectionMatrices[2];
            float4 _EnvironmentDepthZBufferParams;

            // Set by RealOcclusion: R = my zone; G, B = the real band's top and bottom (fractions of _OwnSpaceRealHeights.z);
            // A = over a redesigned desk or bed, where only that band is real (the things on the new desk show).

            TEXTURE2D(_OwnSpaceRealMask);
            SAMPLER(sampler_OwnSpaceRealMask);
            float4 _OwnSpaceRealRect;    // min x, min z, 1 / size x, 1 / size z
            float4 _OwnSpaceRealHeights; // floor y, zone top (above the floor), kept-top scale, softness (m)

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 d = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(input.positionWS, 1.0));
                float2 uv = (d.xy / d.w + 1.0) * 0.5;
                if (any(uv < 0.0) || any(uv > 1.0)) return half4(1, 1, 1, 1);
                float virtualDepth = _EnvironmentDepthZBufferParams.x / (d.z / d.w + _EnvironmentDepthZBufferParams.y);
                float raw = SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv, unity_StereoEyeIndex, 0).r;
                float realDepth = _EnvironmentDepthZBufferParams.x / (raw * 2.0 - 1.0 + _EnvironmentDepthZBufferParams.y);
                float ahead = virtualDepth - realDepth; // > 0: the real surface is in front
                if (ahead <= 0.0 || realDepth <= 0.0) return half4(1, 1, 1, 1);

                // The real point, along the same view ray.
                float3 real = _WorldSpaceCameraPos + (input.positionWS - _WorldSpaceCameraPos) * (realDepth / max(virtualDepth, 1e-4));
                half4 mask = SAMPLE_TEXTURE2D_LOD(_OwnSpaceRealMask, sampler_OwnSpaceRealMask, (real.xz - _OwnSpaceRealRect.xy) * _OwnSpaceRealRect.zw, 0);
                float h = real.y - _OwnSpaceRealHeights.x;
                float bandTop = mask.g * _OwnSpaceRealHeights.z, bandBottom = mask.b * _OwnSpaceRealHeights.z;
                bool kept = h > 0.03 && (mask.a > 0.5
                    ? (h > bandBottom && h < bandTop)                                  // a redesigned desk or bed: only what sits on it
                    : ((mask.r > 0.5 && h < _OwnSpaceRealHeights.y) || h < bandTop)); // my zone, or a kept-real piece and its band
                if (!kept) return half4(1, 1, 1, 1);
                // Not "half visible": on the headset (Vulkan, GLES3) that line did not compile ("unrecognized identifier
                // 'min16float'"), the whole shader was dropped and the pass drew with the magenta error shader (build dd3521d).
                float shown = 1.0 - saturate(ahead / _OwnSpaceRealHeights.w);
                return half4(shown, shown, shown, shown);
            }
            ENDHLSL
        }
    }
}
