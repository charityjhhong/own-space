// Panel glass over passthrough (no grab-pass blur on Quest): a rounded rect in metres (radius h/2 = pill),
// light fill + thin rim. Premultiplied, ZWrite Off, Transparent queue (after the opaque shell); the written
// alpha lets the room show through over passthrough and stays opaque over the virtual shell.
Shader "OwnSpace/PanelGlass"
{
    Properties
    {
        _BaseColor ("Fill, top (straight alpha)", Color) = (1, 1, 1, 0.12)
        _BaseColor2 ("Fill, bottom (straight alpha; = top for a flat fill)", Color) = (1, 1, 1, 0.12)
        _RimColor ("Rim (straight alpha)", Color) = (1, 1, 1, 0.35)
        _Pattern ("Lines: horizontal count x, vertical count y, width z (0-1 of the size)", Vector) = (0, 0, 0, 0)
        _PatternColor ("Pattern lines", Color) = (0, 0, 0, 0)
        _Glyph ("Glyph: 1 = eye (the Real disc)", Float) = 0
        _GlyphColor ("Glyph", Color) = (0, 0, 0, 0)
        _IconMap ("Icon atlas (content's UIIcons: white on transparent)", 2D) = "white" {}
        _Icon ("Icon atlas rect: scale xy, offset zw", Vector) = (1, 1, 0, 0)
        _IconOn ("1 = an icon quad (atlas alpha, tinted); 2 = a thumbnail quad (full colour)", Float) = 0
        _ThumbMap ("Furniture thumbnail atlas (content's FurnitureThumbs)", 2D) = "white" {}
        _Clip ("Row clip in the element's x (m): min x, max y, fade z, on w", Vector) = (0, 0, 0, 0)
        _Rect ("Size x y (m), corner radius z (m), rim width w (m)", Vector) = (0.1, 0.06, 0.012, 0.0015)
    }
    SubShader
    {
        // Drawn after the world (Transparent+100) and never depth-tested against it (ZTest Always): real furniture kept
        // visible writes depth (PassthroughHole), and a seated user's desk cut the panel off (device day, 6c6b59a).
        // The panel's own quads and text sort by distance among themselves as before (no depth writes).
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glass"
            ZWrite Off ZTest Always Cull Back
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BaseColor2;
                half4 _RimColor;
                float4 _Pattern;
                half4 _PatternColor;
                float _Glyph;
                half4 _GlyphColor;
                float4 _Clip;
                float4 _Icon;
                float _IconOn;
                float4 _Rect;
            CBUFFER_END
            TEXTURE2D(_IconMap);
            SAMPLER(sampler_IconMap);
            TEXTURE2D(_ThumbMap);
            SAMPLER(sampler_ThumbMap);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // Distance (0-1 units) to the nearest of n evenly spaced lines across [0, 1].
            float LineDistance(float t, float n) { return abs(frac(t * (n + 1.0) + 0.5) - 0.5) / (n + 1.0); }

            // Signed distance to a lens (vesica): circles of radius r at (+-d, 0); iq's formula.
            float Vesica(float2 p, float r, float d)
            {
                p = abs(p);
                float b = sqrt(r * r - d * d);
                return ((p.y - b) * d > p.x * b) ? length(p - float2(0.0, b)) : length(p - float2(-d, 0.0)) - r;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // An icon quad (same material, so no extra draw call setup): the atlas cell's alpha in the glyph colour.
                if (_IconOn > 1.5) // a furniture thumbnail: the atlas cell in its own colours
                {
                    half4 t = SAMPLE_TEXTURE2D(_ThumbMap, sampler_ThumbMap, i.uv * _Icon.xy + _Icon.zw);
                    return half4(t.rgb * t.a, t.a);
                }
                if (_IconOn > 0.5)
                {
                    half a = SAMPLE_TEXTURE2D(_IconMap, sampler_IconMap, i.uv * _Icon.xy + _Icon.zw).a * _GlyphColor.a;
                    return half4(_GlyphColor.rgb * a, a);
                }
                float2 half_ = _Rect.xy * 0.5;
                float r = min(_Rect.z, min(half_.x, half_.y));
                float2 q = abs((i.uv - 0.5) * _Rect.xy) - half_ + r;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; // < 0 inside, metres
                float aa = max(fwidth(d), 1e-5);
                half inside = saturate(0.5 - d / aa);
                half rim = saturate(1.0 - (-d - _Rect.w) / aa) * inside; // the band within w of the edge
                half4 c = lerp(_BaseColor2, _BaseColor, i.uv.y); // vertical gradient (uv.y = 1 at the top)
                // Finish patterns: plank, tile and stripe lines in the accent colour.
                float w = _Pattern.z * 0.5, faa = max(fwidth(i.uv.y), 1e-5);
                half lines = 0;
                if (_Pattern.x > 0) lines = max(lines, 1 - smoothstep(w - faa, w + faa, LineDistance(i.uv.y, _Pattern.x)));
                if (_Pattern.y > 0) lines = max(lines, 1 - smoothstep(w - faa, w + faa, LineDistance(i.uv.x, _Pattern.y)));
                c = lerp(c, _PatternColor, lines * _PatternColor.a);
                // The eye glyph (icons/eye.svg): an almond outline and a pupil ring, about 60% of the disc.
                if (_Glyph > 0.5)
                {
                    float2 q = (i.uv - 0.5) * 2.0 / 0.6;
                    float gaa = max(fwidth(q.x), 1e-5);
                    float eye = abs(Vesica(q.yx, 0.944, 0.444));
                    float pupil = abs(length(q) - 0.25);
                    half glyph = 1 - smoothstep(0.0625 - gaa, 0.0625 + gaa, min(eye, pupil));
                    c = lerp(c, _GlyphColor, glyph * _GlyphColor.a);
                }
                c = lerp(c, _RimColor, rim);
                c.a *= inside;
                // A scrolling row without a Mask: fade out toward the row's edges, nothing past them.
                if (_Clip.w > 0.5)
                {
                    float x = (i.uv.x - 0.5) * _Rect.x;
                    float f = max(_Clip.z, 1e-5);
                    c.a *= saturate((x - _Clip.x) / f) * saturate((_Clip.y - x) / f);
                }
                return half4(c.rgb * c.a, c.a); // premultiplied
            }
            ENDHLSL
        }
    }
}
