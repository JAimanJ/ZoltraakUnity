// Procedural magic circle: rings + rotating rune band + hexagram + inner square.
// Additive, unlit, double-sided. Drawn on a Quad.
Shader "Zoltraak/MagicCircle"
{
    Properties
    {
        _Color ("Color", Color) = (0.9, 0.95, 1, 1)
        _Intensity ("Intensity", Float) = 3
        _Reveal ("Reveal (0-1)", Float) = 1
        _Spin ("Spin", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define ZTAU 6.28318530718

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Reveal;
                float _Spin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            float2 rot(float2 p, float a)
            {
                float c = cos(a), s = sin(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float ring(float r, float radius, float w)
            {
                return 1.0 - smoothstep(0.0, w, abs(r - radius));
            }

            float seg(float2 p, float2 a, float2 b, float w)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return 1.0 - smoothstep(0.0, w, length(pa - ba * h));
            }

            float poly(float2 p, int n, float radius, float w, float offset)
            {
                float m = 0.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i < n)
                    {
                        float a0 = offset + ZTAU * i / n;
                        float a1 = offset + ZTAU * (i + 1) / n;
                        m = max(m, seg(p, radius * float2(cos(a0), sin(a0)), radius * float2(cos(a1), sin(a1)), w));
                    }
                }
                return m;
            }

            float hash(float x) { return frac(sin(x * 12.9898) * 43758.5453); }

            half4 frag (Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float r = length(p);
                clip(1.0 - r);

                float ang01 = frac(atan2(p.y, p.x) / ZTAU + 0.25);
                float shown = step(ang01, _Reveal);

                float m = 0.0;

                // Outer double ring
                m = max(m, ring(r, 0.96, 0.012));
                m = max(m, ring(r, 0.84, 0.010));

                // Rune band (rotates)
                float2 pr = rot(p, _Spin * 0.4);
                float a = frac(atan2(pr.y, pr.x) / ZTAU);
                float cell = floor(a * 40.0);
                float f = frac(a * 40.0);
                float glyph = step(0.2, f) * step(f, 0.35 + 0.5 * hash(cell));
                glyph *= step(abs(r - 0.90), 0.012 + 0.022 * hash(cell + 7.0));
                m = max(m, glyph);

                // Hexagram (counter-rotates)
                float2 ps = rot(p, -_Spin * 0.6);
                m = max(m, poly(ps, 3, 0.80, 0.012, 1.5708));
                m = max(m, poly(ps, 3, 0.80, 0.012, -1.5708));

                // Inner ring + square + core ring
                m = max(m, ring(r, 0.42, 0.010));
                m = max(m, poly(rot(p, _Spin), 4, 0.40, 0.010, 0.0));
                m = max(m, ring(r, 0.16, 0.012));

                // Soft core glow
                m = max(m, 0.5 * (1.0 - smoothstep(0.0, 0.14, r)));

                return half4(_Color.rgb * _Intensity * m * shown, 1.0);
            }
            ENDHLSL
        }
    }
}
