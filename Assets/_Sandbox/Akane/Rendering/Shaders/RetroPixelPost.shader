Shader "Custom/RetroPixelPost"
{
    Properties
    {
        // RGBそれぞれを何段階にするか
        _ColorSteps(
            "Color Steps",
            Range(2, 32)) = 8

        // ディザリングの強さ
        _DitherStrength(
            "Dither Strength",
            Range(0, 10)) = 1

        // ディザリング1マスを画面上で何ピクセルにするか
        _DitherScale(
            "Dither Scale",
            Range(1, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "RetroPixelPost"

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ColorSteps;
                float _DitherStrength;
                float _DitherScale;
            CBUFFER_END

            // ------------------------------------------------------------
            // 4x4 Bayer Dithering
            // ------------------------------------------------------------
            float Bayer4x4(int2 pixelPosition)
            {
                static const float BAYER[16] =
                {
                     0.0,  8.0,  2.0, 10.0,
                    12.0,  4.0, 14.0,  6.0,
                     3.0, 11.0,  1.0,  9.0,
                    15.0,  7.0, 13.0,  5.0
                };

                int x =
                    pixelPosition.x & 3;

                int y =
                    pixelPosition.y & 3;

                int index =
                    y * 4 + x;

                return
                    BAYER[index] / 16.0;
            }

            // ------------------------------------------------------------
            // 色数を削減
            // ------------------------------------------------------------
            float3 QuantizeColor(
                float3 color,
                float steps,
                float dither)
            {
                steps =
                    max(
                        steps,
                        2.0);

                float maxValue =
                    steps - 1.0;

                // ディザリング値を加えてから量子化する
                color +=
                    dither /
                    maxValue;

                color =
                    saturate(color);

                color =
                    floor(
                        color *
                        maxValue +
                        0.5)
                    / maxValue;

                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv =
                    input.texcoord.xy;

                // --------------------------------------------------------
                // 1. 合成済み画面をそのまま取得
                //
                // ここでは低解像度化しない。
                // Near / Middle / Farの解像度差を維持する。
                // --------------------------------------------------------

                half4 color =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_PointClamp,
                        uv,
                        _BlitMipLevel);

                // --------------------------------------------------------
                // 2. ディザリング用の画面座標を作る
                // --------------------------------------------------------

                float ditherScale =
                    max(
                        _DitherScale,
                        1.0);

                float2 screenPixelPosition =
                    floor(
                        uv *
                        _ScreenParams.xy /
                        ditherScale);

                // --------------------------------------------------------
                // 3. Bayerディザリング
                // --------------------------------------------------------

                float dither =
                    Bayer4x4(
                        (int2)screenPixelPosition);

                // 0 ～ 1
                // ↓
                // -0.5 ～ +0.5
                dither -= 0.5;

                dither *=
                    _DitherStrength;

                // --------------------------------------------------------
                // 4. 色数削減
                // --------------------------------------------------------

                color.rgb =
                    QuantizeColor(
                        color.rgb,
                        _ColorSteps,
                        dither);

                return color;
            }

            ENDHLSL
        }
    }
}
