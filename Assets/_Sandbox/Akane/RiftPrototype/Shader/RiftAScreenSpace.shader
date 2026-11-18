Shader "Prototype/RiftA/ScreenSpace"
{
    Properties
    {
        [PerRendererData] _MainTex ("Preview Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Open Progress", Range(0,1)) = 0
        _Ready ("3D Preview Ready", Float) = 0
        _Center ("Screen Center", Vector) = (0.5,0.5,0,0)
        _Aspect ("Screen Aspect", Float) = 1.7778
        _EdgeWidth ("Edge Width", Range(0.0001,0.04)) = 0.004
        _GlowWidth ("Glow Width", Range(0.001,0.12)) = 0.028
        [HDR] _EdgeColor ("Edge Glow", Color) = (0.15,0.8,2.4,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "RiftAOverlay"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Center;
                float4 _EdgeColor;
                float _Progress;
                float _Ready;
                float _Aspect;
                float _EdgeWidth;
                float _GlowWidth;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float progress = saturate(_Progress);
                if (progress <= 0.00001) return half4(0,0,0,0);
                float2 p = input.uv - _Center.xy;
                p.x *= _Aspect;
                float zigzag = sin(p.y * 29.0 + 0.3) * 0.026
                             + sin(p.y * 73.0 + 2.1) * 0.012
                             + sin(p.y * 149.0 + 0.5) * 0.005;
                float width = 2.1 * pow(progress, 1.32);
                float height = 0.025 + progress * 1.4;
                float noise = 1.0 + 0.12 * sin(p.y * 61.0) + 0.06 * sin(p.y * 127.0);
                float horizontal = abs(p.x - zigzag * (1.0 - 0.6 * progress)) - width * noise;
                float vertical = abs(p.y) - height;
                float distanceToEdge = max(horizontal, vertical);
                float inside = 1.0 - smoothstep(-_EdgeWidth, _EdgeWidth, distanceToEdge);
                float glow = (1.0 - smoothstep(0.0, _GlowWidth, abs(distanceToEdge)))
                             * (1.0 - inside) * 0.85;
                // ここではUVを拡大しない。カメラの移動で3D映像を毎フレーム更新する。
                half3 sceneColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                half3 color = lerp(half3(0.02,0.015,0.04), sceneColor, saturate(_Ready));
                half3 result = color * inside + _EdgeColor.rgb * glow;
                half alpha = saturate(inside + glow) * input.color.a * _Color.a;
                return half4(result * input.color.rgb * _Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
