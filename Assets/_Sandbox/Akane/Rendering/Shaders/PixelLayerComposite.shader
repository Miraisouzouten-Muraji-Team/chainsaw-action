Shader "Custom/PixelLayerComposite"
{
    Properties
    {
        [HideInInspector]
        _PixelNearTex("Near", 2D) = "black" {}
    
        [HideInInspector]
        _PixelMiddleTex("Middle", 2D) = "black" {}
    
        [HideInInspector]
        _PixelFarTex("Far", 2D) = "black" {}
    
        [HideInInspector]
        _PixelEffectEnabled(
            "Pixel Effect Enabled",
            Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelLayerComposite"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);

            float _PixelEffectEnabled;

            half4 AlphaOver(
                half4 background,
                half4 foreground)
            {
                half3 rgb =
                    lerp(
                        background.rgb,
                        foreground.rgb,
                        foreground.a);

                half alpha =
                    foreground.a +
                    background.a *
                    (1.0h - foreground.a);

                return half4(rgb, alpha);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            
                float2 uv =
                    input.texcoord;
            
                half4 result =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv,
                        _BlitMipLevel);
            
                // Pixel OFFならMain Cameraの結果をそのまま返す
                if (_PixelEffectEnabled < 0.5)
                {
                    return result;
                }
            
                half4 farColor =
                    SAMPLE_TEXTURE2D(
                        _PixelFarTex,
                        sampler_PointClamp,
                        uv);
            
                half4 middleColor =
                    SAMPLE_TEXTURE2D(
                        _PixelMiddleTex,
                        sampler_PointClamp,
                        uv);
            
                half4 nearColor =
                    SAMPLE_TEXTURE2D(
                        _PixelNearTex,
                        sampler_PointClamp,
                        uv);
            
                result =
                    AlphaOver(
                        result,
                        farColor);
            
                result =
                    AlphaOver(
                        result,
                        middleColor);
            
                result =
                    AlphaOver(
                        result,
                        nearColor);
            
                return result;
            }

            ENDHLSL
        }
    }
}
