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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);

            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);

            TEXTURE2D(_SceneNormalTex);

            float _PixelEffectEnabled;
            float _SceneNormalPreviewEnabled;

            // Skyまたは描画物が存在しない深度を判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // すべてのCameraで投影設定を揃えているため、
            // Raw Depthを直接比較できる。
            bool IsCloserDepth(float candidateDepth, float currentDepth)
            {
                #if UNITY_REVERSED_Z
                    return candidateDepth > currentDepth;
                #else
                    return candidateDepth < currentDepth;
                #endif
            }

            // 有効な描画ピクセルから最も手前のものを選択する。
            // 深度を持たない透明オブジェクトは、深度を持つ描画より奥として扱う。
            // 深度を持たない候補同士はNear側を優先する。
            void SelectNearestPixel(
                half4 candidateColor,
                float candidateDepth,
                inout half4 selectedColor,
                inout float selectedDepth,
                inout bool selectedPixel)
            {
                if (candidateColor.a <= 0.01)
                {
                    return;
                }
            
                bool candidateIsSky = IsSkyDepth(candidateDepth);
                bool selectedIsSky = IsSkyDepth(selectedDepth);
            
                bool shouldSelect = false;
            
                if (!candidateIsSky)
                {
                    // 有効なDepthを持つ不透明オブジェクト。
                    shouldSelect =
                        selectedIsSky ||
                        IsCloserDepth(candidateDepth, selectedDepth);
                }
                else if (selectedIsSky)
                {
                    // Depthがない透明オブジェクト。
                    // Far → Middle → Nearの順に処理するため、
                    // 同条件では後から来るNear側を優先する。
                    shouldSelect = true;
                }
            
                if (!shouldSelect)
                {
                    return;
                }
            
                selectedColor = candidateColor;
                selectedDepth = candidateDepth;
                selectedPixel = true;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 colorUv = input.texcoord.xy;
                float2 depthUv = input.positionCS.xy / _ScaledScreenParams.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, colorUv, _BlitMipLevel);

                // SceneビューのPixel OFF時は、通常解像度のRTを表示する。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return SAMPLE_TEXTURE2D(_SceneNormalTex, sampler_LinearClamp,colorUv);
                }

                if (_PixelEffectEnabled < 0.5)
                {
                    return sceneColor;
                }

                float sceneDepth = SampleSceneDepth(depthUv);

                half4 selectedColor = sceneColor;
                float selectedDepth = sceneDepth;
                bool selectedPixel = false;

                // Far
                half4 farColor = SAMPLE_TEXTURE2D(_PixelFarTex, sampler_PointClamp, colorUv);
                float farDepth = SAMPLE_TEXTURE2D(_PixelFarDepthTex, sampler_PointClamp, colorUv).r;
                SelectNearestPixel(farColor, farDepth, selectedColor, selectedDepth, selectedPixel);

                // Middle
                half4 middleColor = SAMPLE_TEXTURE2D(_PixelMiddleTex, sampler_PointClamp, colorUv);
                float middleDepth = SAMPLE_TEXTURE2D(_PixelMiddleDepthTex, sampler_PointClamp, colorUv).r;
                SelectNearestPixel(middleColor, middleDepth, selectedColor, selectedDepth, selectedPixel);

                // Near
                half4 nearColor = SAMPLE_TEXTURE2D(_PixelNearTex, sampler_PointClamp, colorUv);
                float nearDepth = SAMPLE_TEXTURE2D(_PixelNearDepthTex, sampler_PointClamp, colorUv).r;
                SelectNearestPixel(nearColor, nearDepth, selectedColor, selectedDepth, selectedPixel);

                if (!selectedPixel)
                {
                    return sceneColor;
                }

                // 基本は不透明オブジェクト向け。
                // エッジのAlphaを利用して元の背景と合成する。
                // Capture RTはPremultiplied Alphaである前提。
                // RGBに対してAlphaを二重に乗算しない。
                half alpha = saturate(selectedColor.a);
                
                half3 finalColor =
                    selectedColor.rgb +
                    sceneColor.rgb * (1.0 - alpha);
                
                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
