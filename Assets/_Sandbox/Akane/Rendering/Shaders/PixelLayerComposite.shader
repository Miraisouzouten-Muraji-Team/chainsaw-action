Shader "Custom/PixelLayerComposite"
{
    Properties
    {
        [HideInInspector] _PixelNearTex("Near", 2D) = "black" {}
        [HideInInspector] _PixelMiddleTex("Middle", 2D) = "black" {}
        [HideInInspector] _PixelFarTex("Far", 2D) = "black" {}
        [HideInInspector] _PixelNearDepthTex("Near Depth", 2D) = "white" {}
        [HideInInspector] _PixelMiddleDepthTex("Middle Depth", 2D) = "white" {}
        [HideInInspector] _PixelFarDepthTex("Far Depth", 2D) = "white" {}
        [HideInInspector] _PixelNearNormalTex("Near Normal", 2D) = "black" {}
        [HideInInspector] _PixelMiddleNormalTex("Middle Normal", 2D) = "black" {}
        [HideInInspector] _PixelFarNormalTex("Far Normal", 2D) = "black" {}
        [HideInInspector] _PixelEffectEnabled("Pixel Effect Enabled", Float) = 1
        [HideInInspector] _SceneNormalTex("Scene Normal", 2D) = "black" {}
        [HideInInspector] _SceneNormalPreviewEnabled("Scene Normal Preview Enabled", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelLayerComposite"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/_Sandbox/Akane/Rendering/Shaders/PixelOpaqueSelection.hlsl"

            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);
            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);
            TEXTURE2D(_SceneNormalTex);

            float _PixelEffectEnabled;
            float _SceneNormalPreviewEnabled;

            // Depthに基づいて最前面のOpaqueの色を返す。
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);

                // Sceneビューの通常解像度プレビューを優先する。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return SAMPLE_TEXTURE2D(_SceneNormalTex, sampler_LinearClamp, uv);
                }

                // Pixel OFFでは元の画面をそのまま返す。
                if (_PixelEffectEnabled < 0.5) return sceneColor;

                // 各描画層のDepthを取得する。
                float nearDepth = SAMPLE_TEXTURE2D(_PixelNearDepthTex, sampler_PointClamp, uv).r;
                float middleDepth = SAMPLE_TEXTURE2D(_PixelMiddleDepthTex, sampler_PointClamp, uv).r;
                float farDepth = SAMPLE_TEXTURE2D(_PixelFarDepthTex, sampler_PointClamp, uv).r;

                // 共通の選択関数で最前面のOpaqueを決定する。
                float selectedDepth;
                int selectedLayer = SelectOpaqueLayer(nearDepth, middleDepth, farDepth, selectedDepth);

                // 不透明物が存在しない場合は背景を返す。
                if (selectedLayer < 0) return sceneColor;

                // 選択した層に対応するColorを取得する。
                half4 selectedColor;

                if (selectedLayer == 0)
                {
                    selectedColor = SAMPLE_TEXTURE2D(_PixelNearTex, sampler_PointClamp, uv);
                }
                else if (selectedLayer == 1)
                {
                    selectedColor = SAMPLE_TEXTURE2D(_PixelMiddleTex, sampler_PointClamp, uv);
                }
                else
                {
                    selectedColor = SAMPLE_TEXTURE2D(_PixelFarTex, sampler_PointClamp, uv);
                }

                // Opaque専用なのでAlphaによる背景合成を行わない。
                return half4(selectedColor.rgb, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
