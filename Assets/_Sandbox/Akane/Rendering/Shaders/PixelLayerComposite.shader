
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

        Cull Off
        ZTest Always
        ZWrite On
        Blend Off

        Pass
        {
            Name "PixelLayerComposite"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/_Sandbox/Akane/Rendering/Shaders/Includes/PixelOpaqueSelection.hlsl"

            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);

            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);

            TEXTURE2D(_SceneNormalTex);

            float _PixelEffectEnabled;
            float _SceneNormalPreviewEnabled;

            // ColorとDepthを同時に出力する。
            struct CompositeOutput
            {
                half4 Color : SV_Target;
                float Depth : SV_Depth;
            };

            // 描画対象がない場合のDepthを取得する。
            float GetPixelFarDepth()
            {
                #if UNITY_REVERSED_Z
                    return 0.0;
                #else
                    return 1.0;
                #endif
            }

            // Capture済みColorとDepthをメイン画面へ統合する。
            CompositeOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv,
                    _BlitMipLevel);

                CompositeOutput output;
                output.Color = sceneColor;
                output.Depth = GetPixelFarDepth();

                // Sceneビューの通常解像度プレビュー。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    output.Color = SAMPLE_TEXTURE2D(
                        _SceneNormalTex,
                        sampler_LinearClamp,
                        uv);

                    return output;
                }

                // Pixel OFFの場合は通常画面を返す。
                if (_PixelEffectEnabled < 0.5)
                {
                    return output;
                }

                // Near / Middle / FarのOpaque Depthを取得する。
                float nearDepth = SAMPLE_TEXTURE2D(
                    _PixelNearDepthTex,
                    sampler_PointClamp,
                    uv).r;

                float middleDepth = SAMPLE_TEXTURE2D(
                    _PixelMiddleDepthTex,
                    sampler_PointClamp,
                    uv).r;

                float farDepth = SAMPLE_TEXTURE2D(
                    _PixelFarDepthTex,
                    sampler_PointClamp,
                    uv).r;

                // 最も手前のOpaqueを選択する。
                float selectedDepth;

                int selectedLayer = SelectOpaqueLayer(
                    nearDepth,
                    middleDepth,
                    farDepth,
                    selectedDepth);

                // Opaqueがない場合は背景色と最遠Depthを返す。
                // Transparentはこの後のURP標準Passで描画する。
                if (selectedLayer < 0)
                {
                    return output;
                }

                half4 selectedColor;

                if (selectedLayer == 0)
                {
                    selectedColor = SAMPLE_TEXTURE2D(
                        _PixelNearTex,
                        sampler_PointClamp,
                        uv);
                }
                else if (selectedLayer == 1)
                {
                    selectedColor = SAMPLE_TEXTURE2D(
                        _PixelMiddleTex,
                        sampler_PointClamp,
                        uv);
                }
                else
                {
                    selectedColor = SAMPLE_TEXTURE2D(
                        _PixelFarTex,
                        sampler_PointClamp,
                        uv);
                }

                // 統合Opaqueの色。
                output.Color = half4(
                    selectedColor.rgb,
                    sceneColor.a);

                // 統合OpaqueのDepthを実際の深度バッファへ書く。
                // 後続のTransparentはこのDepthを使って前後判定する。
                output.Depth = selectedDepth;

                return output;
            }

            ENDHLSL
        }
    }
}
