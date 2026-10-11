Shader "Custom/Pixel3D"
{
    Properties
    {
        _BaseMap("Base Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        // 仮想的な描画解像度
        // 320x180くらいにするとかなりドット感が出る
        _PixelResolution("Pixel Resolution", Vector) = (320, 180, 0, 0)

        // 明るさを何段階に分けるか
        _LightSteps("Light Steps", Range(2, 16)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _PixelResolution;
                float _LightSteps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // オブジェクト座標 → クリップ座標
                float4 positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                // -------------------------------------------------
                // 頂点位置を仮想ピクセルにスナップ
                // -------------------------------------------------

                // クリップ座標 → NDC
                float2 ndc = positionCS.xy / positionCS.w;

                // -1～1 → 0～1
                float2 screenUV = ndc * 0.5 + 0.5;

                // 仮想解像度上のピクセル位置
                float2 pixelPos =
                    screenUV * _PixelResolution.xy;

                // ピクセル単位に丸める
                pixelPos = floor(pixelPos + 0.5);

                // ピクセル位置 → 0～1
                screenUV =
                    pixelPos / _PixelResolution.xy;

                // 0～1 → -1～1
                ndc = screenUV * 2.0 - 1.0;

                // NDC → クリップ座標
                positionCS.xy =
                    ndc * positionCS.w;

                output.positionCS = positionCS;

                output.normalWS =
                    TransformObjectToWorldNormal(input.normalOS);

                output.uv =
                    TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // テクスチャ取得
                half4 texColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv
                    );

                float3 normal =
                    normalize(input.normalWS);

                // URPのメインライト
                Light mainLight =
                    GetMainLight();

                float3 lightDir =
                    normalize(mainLight.direction);

                // Lambert
                float NdotL =
                    saturate(dot(normal, lightDir));

                // -------------------------------------------------
                // 明るさを段階化
                // -------------------------------------------------

                NdotL =
                    floor(NdotL * _LightSteps)
                    / _LightSteps;

                // 暗すぎないように環境光を少し足す
                float lighting =
                    0.25 + NdotL * 0.75;

                half3 color =
                    texColor.rgb *
                    _BaseColor.rgb *
                    lighting *
                    mainLight.color;

                return half4(
                    color,
                    texColor.a * _BaseColor.a
                );
            }

            ENDHLSL
        }
    }
}