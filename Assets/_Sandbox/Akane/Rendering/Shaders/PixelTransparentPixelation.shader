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

        TEXTURE2D_X(_PixelEffectBeforeTex);
        TEXTURE2D_X(_PixelEffectAfterTex);
        TEXTURE2D_X(_PixelEffectDeltaTex);

        float4 _PixelEffectLowResTexelSize;

        // Transparent描画前の色を取得する。
        float4 SampleBefore(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(
                _PixelEffectBeforeTex,
                sampler_PointClamp,
                uv);
        }

        // Transparent描画後の色を取得する。
        float4 SampleAfter(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(
                _PixelEffectAfterTex,
                sampler_PointClamp,
                uv);
        }

        // 透明描画前後のRGB差分を取得する。
        float3 GetEffectDelta(float2 uv)
        {
            float3 beforeColor = SampleBefore(uv).rgb;
            float3 afterColor = SampleAfter(uv).rgb;

            return afterColor - beforeColor;
        }

        // Pass 0：透明描画の差分を低解像度で取得する。
        float4 FragExtract(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;
            float2 texelSize = _PixelEffectLowResTexelSize.xy;

            // ドット領域内の4点から平均差分を計算する。
            float3 delta = 0.0;

            delta += GetEffectDelta(
                uv + texelSize * float2(-0.25, -0.25));

            delta += GetEffectDelta(
                uv + texelSize * float2(0.25, -0.25));

            delta += GetEffectDelta(
                uv + texelSize * float2(-0.25, 0.25));

            delta += GetEffectDelta(
                uv + texelSize * float2(0.25, 0.25));

            delta *= 0.25;

            // 負の差分も保存するため、ここではsaturateしない。
            return float4(delta, 0.0);
        }

        // Pass 1：元のOpaque色へドット化した差分を合成する。
        float4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;

            float4 beforeColor = SampleBefore(uv);

            // Point Samplingによって低解像度の差分を拡大する。
            float3 delta = SAMPLE_TEXTURE2D_X(
                _PixelEffectDeltaTex,
                sampler_PointClamp,
                uv).rgb;

            // 元の不透明画面は高解像度のまま維持する。
            float3 finalColor = beforeColor.rgb + delta;

            return float4(max(finalColor, 0.0), beforeColor.a);
        }

        ENDHLSL

        Pass
        {
            Name "ExtractTransparentDelta"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragExtract

            ENDHLSL
        }

        Pass
        {
            Name "CompositeTransparentDelta"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragComposite

            ENDHLSL
        }
    }
}
