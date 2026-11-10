Shader "Custom/PixelTransparentPixelation"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE

        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_PixelEffectBlackTex);
        TEXTURE2D_X(_PixelEffectWhiteTex);
        TEXTURE2D_X(_PixelEffectDataTex);

        float4 _PixelEffectTexelSize;

        // 黒背景に描画した透明エフェクトの色を取得する。
        float3 SampleBlack(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(
                _PixelEffectBlackTex,
                sampler_PointClamp,
                saturate(uv)).rgb;
        }

        // 白背景に描画した透明エフェクトの色を取得する。
        float3 SampleWhite(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(
                _PixelEffectWhiteTex,
                sampler_PointClamp,
                saturate(uv)).rgb;
        }

        // 標準的なAlpha/Additive Blendから背景の透過率を復元する。
        float GetTransmittance(float3 blackColor, float3 whiteColor)
        {
            float3 difference = whiteColor - blackColor;

            return saturate(
                (difference.r + difference.g + difference.b) / 3.0);
        }

        // Pass 0：透明エフェクトの色情報と透過率を低解像度化する。
        float4 FragExtract(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;
            float2 texelSize = _PixelEffectTexelSize.xy;

            float2 offsets[4] =
            {
                float2(-0.25, -0.25),
                float2( 0.25, -0.25),
                float2(-0.25,  0.25),
                float2( 0.25,  0.25)
            };

            float3 contribution = 0.0;
            float transmittance = 0.0;

            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float2 sampleUv = uv + offsets[i] * texelSize;

                float3 blackColor = SampleBlack(sampleUv);
                float3 whiteColor = SampleWhite(sampleUv);

                // 背景に依存しない色の寄与を累積する。
                contribution += blackColor;

                // 背景が透過する割合を累積する。
                transmittance += GetTransmittance(
                    blackColor,
                    whiteColor);
            }

            contribution *= 0.25;
            transmittance *= 0.25;

            // RGB：透明エフェクトの色の寄与
            // A：背景の透過率
            return float4(contribution, transmittance);
        }

        // Pass 1：低解像度の透明エフェクトをOpaqueへ合成する。
        float4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;

            // 元のOpaque Colorはそのままの解像度で取得する。
            float4 opaqueColor = SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_PointClamp,
                uv);

            // 透明エフェクトだけをPoint Samplingで拡大する。
            float4 effectData = SAMPLE_TEXTURE2D_X(
                _PixelEffectDataTex,
                sampler_PointClamp,
                uv);

            float3 finalColor =
                opaqueColor.rgb * effectData.a +
                effectData.rgb;

            return float4(max(finalColor, 0.0), opaqueColor.a);
        }

        ENDHLSL

        Pass
        {
            Name "ExtractTransparentContribution"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragExtract
            ENDHLSL
        }

        Pass
        {
            Name "CompositeTransparentContribution"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
