Shader "Custom/UI/RadialZoomBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BlurStrength ("Blur Strength", Range(0, 0.35)) = 0
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
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _BlurStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv = input.uv;
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 center = float2(0.5, 0.5);

                half4 color = half4(0, 0, 0, 0);

                const int sampleCount = 9;

                // 画面中央に向かって複数回サンプリングする。
                [unroll]
                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / (sampleCount - 1);

                    float2 sampleUV = lerp(
                        input.uv,
                        center,
                        t * _BlurStrength
                    );

                    color += SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        sampleUV
                    );
                }

                color /= sampleCount;

                return color * input.color;
            }

            ENDHLSL
        }
    }
}
