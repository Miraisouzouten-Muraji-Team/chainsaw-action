
Shader "Prototype/RiftA/PreviewPixelBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _PixelBlend ("Pixel Blend", Range(0, 1)) = 0
        _PixelSize ("Pixel Size", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _PixelBlend;
                float _PixelSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 元の解像度で取得する。
                half4 original = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, input.uv);

                // 指定した画素サイズで座標を固定する。
                float2 resolution = max(
                    floor(_MainTex_TexelSize.zw / max(_PixelSize, 1.0)),
                    float2(1.0, 1.0));

                float2 pixelUV =
                    (floor(input.uv * resolution) + 0.5) / resolution;

                half4 pixelated = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, pixelUV);

                // ドット化の強さを0～1で変える。
                return lerp(
                    original,
                    pixelated,
                    saturate(_PixelBlend)) * input.color;
            }
            ENDHLSL
        }
    }
}
