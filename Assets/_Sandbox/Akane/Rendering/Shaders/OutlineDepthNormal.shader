Shader "Custom/OutlineDepthNormal"
{
    Properties
    {
        // ============================================================
        // Outline
        // ============================================================

        _OutlineColor(
            "Outline Color",
            Color) = (0.02, 0.02, 0.02, 1.0)

        // 出力解像度上での線幅。
        //
        // 例えばMiddleが320x180なら
        // 1 = 320x180上で1px。
        _OutlineWidthPixels(
            "Outline Width Pixels",
            Range(1.0, 4.0)) = 1.0


        // ============================================================
        // Depth Edge
        // ============================================================

        // 奥行き差を輪郭として扱う閾値。
        _DepthThreshold(
            "Depth Threshold",
            Range(0.0001, 0.2)) = 0.01

        // Depth輪郭の強さ。
        //
        // Hybrid時には0にすることで、
        // 外周をBackface側だけに担当させられる。
        _DepthStrength(
            "Depth Strength",
            Range(0.0, 1.0)) = 1.0


        // ============================================================
        // Normal Edge
        // ============================================================

        // 法線差を輪郭として扱う閾値。
        _NormalThreshold(
            "Normal Threshold",
            Range(0.0, 1.0)) = 0.2

        // Normal輪郭の強さ。
        _NormalStrength(
            "Normal Strength",
            Range(0.0, 1.0)) = 1.0
    }


    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off


        Pass
        {
            Name "OutlineDepthNormal"

            HLSLPROGRAM

            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag


            // ========================================================
            // URP
            // ========================================================

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


            // ========================================================
            // Constants
            // ========================================================

            #define MAX_OUTLINE_WIDTH 4


            // ========================================================
            // Material
            // ========================================================

            CBUFFER_START(UnityPerMaterial)

                float4 _OutlineColor;

                float _OutlineWidthPixels;

                float _DepthThreshold;
                float _DepthStrength;

                float _NormalThreshold;
                float _NormalStrength;

            CBUFFER_END


            // ========================================================
            // Sky判定
            // ========================================================

            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z

                    return
                        rawDepth <= 0.0001;

                #else

                    return
                        rawDepth >= 0.9999;

                #endif
            }


            // ========================================================
            // 1点のDepth / Normal差を調べる
            // ========================================================

            float2 GetEdgeDifference(
                float2 centerUv,
                float2 sampleUv,
                float centerDepth,
                float3 centerNormal)
            {
                float sampleRawDepth =
                    SampleSceneDepth(
                        sampleUv);

                bool sampleIsSky =
                    IsSkyDepth(
                        sampleRawDepth);


                // ----------------------------------------------------
                // Depth
                // ----------------------------------------------------

                float sampleDepth =
                    LinearEyeDepth(
                        sampleRawDepth,
                        _ZBufferParams);

                float depthDifference =
                    abs(
                        sampleDepth -
                        centerDepth);

                // カメラから遠いほど同じ1m差の影響が
                // 大きくなりすぎないよう相対値にする。
                depthDifference /=
                    max(
                        centerDepth,
                        0.0001);


                // ----------------------------------------------------
                // Normal
                // ----------------------------------------------------

                float normalDifference =
                    0.0;


                // Skyには有効なSurface Normalが無いため、
                // 外周判定はDepth側に任せる。
                //
                // これによりHybrid時にDepthStrengthを0にすると
                // 外周を背面法だけへ任せられる。
                if (!sampleIsSky)
                {
                    float3 sampleNormal =
                        SampleSceneNormals(
                            sampleUv);

                    sampleNormal =
                        normalize(
                            sampleNormal);

                    normalDifference =
                        1.0 -
                        saturate(
                            dot(
                                centerNormal,
                                sampleNormal));
                }


                return
                    float2(
                        depthDifference,
                        normalDifference);
            }


            // ========================================================
            // Fragment
            // ========================================================

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(
                    input);


                // ====================================================
                // 1. Color
                // ====================================================

                float2 colorUv =
                    input.texcoord.xy;

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        colorUv,
                        _BlitMipLevel);


                // ====================================================
                // 2. Screen UV
                // ====================================================

                float2 screenUv =
                    input.positionCS.xy /
                    _ScaledScreenParams.xy;


                // ====================================================
                // 3. Center Depth
                // ====================================================

                float centerRawDepth =
                    SampleSceneDepth(
                        screenUv);


                // Sky自体には線を描かない。
                //
                // オブジェクト側のピクセルからSkyを検出することで
                // オブジェクト内側へ外周線が出る。
                if (IsSkyDepth(centerRawDepth))
                {
                    return sceneColor;
                }


                float centerDepth =
                    LinearEyeDepth(
                        centerRawDepth,
                        _ZBufferParams);


                // ====================================================
                // 4. Center Normal
                // ====================================================

                float3 centerNormal =
                    SampleSceneNormals(
                        screenUv);

                centerNormal =
                    normalize(
                        centerNormal);


                // ====================================================
                // 5. 1 pixelのUVサイズ
                //
                // 現在描画しているRenderTarget基準。
                //
                // Near RTならNearの1px。
                // Middle RTならMiddleの1px。
                // Far RTならFarの1px。
                // ====================================================

                float2 texelSize =
                    1.0 /
                    _ScaledScreenParams.xy;


                // ====================================================
                // 6. Outline Width
                // ====================================================

                int outlineWidth =
                    clamp(
                        (int)round(
                            _OutlineWidthPixels),
                        1,
                        MAX_OUTLINE_WIDTH);


                float maxDepthDifference =
                    0.0;

                float maxNormalDifference =
                    0.0;


                // ====================================================
                // 7. 周囲を調べる
                //
                // 8方向。
                //
                // Width 2なら、
                // 1px地点と2px地点を両方調べる。
                // ====================================================

                [loop]
                for (
                    int radius = 1;
                    radius <= MAX_OUTLINE_WIDTH;
                    ++radius)
                {
                    if (radius > outlineWidth)
                    {
                        break;
                    }


                    float2 offset =
                        texelSize *
                        (float)radius;


                    // -----------------------------------------------
                    // Right
                    // -----------------------------------------------

                    float2 difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                offset.x,
                                0.0),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Left
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                -offset.x,
                                0.0),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Up
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                0.0,
                                offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Down
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                0.0,
                                -offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Right Up
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                offset.x,
                                offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Right Down
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                offset.x,
                                -offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Left Up
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                -offset.x,
                                offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);


                    // -----------------------------------------------
                    // Left Down
                    // -----------------------------------------------

                    difference =
                        GetEdgeDifference(
                            screenUv,
                            screenUv +
                            float2(
                                -offset.x,
                                -offset.y),
                            centerDepth,
                            centerNormal);

                    maxDepthDifference =
                        max(
                            maxDepthDifference,
                            difference.x);

                    maxNormalDifference =
                        max(
                            maxNormalDifference,
                            difference.y);
                }


                // ====================================================
                // 8. Edge判定
                // ====================================================

                float depthEdge =
                    step(
                        _DepthThreshold,
                        maxDepthDifference)
                    *
                    _DepthStrength;


                float normalEdge =
                    step(
                        _NormalThreshold,
                        maxNormalDifference)
                    *
                    _NormalStrength;


                float edge =
                    saturate(
                        max(
                            depthEdge,
                            normalEdge));


                // ====================================================
                // 9. Outline合成
                // ====================================================

                float outlineAmount =
                    edge *
                    _OutlineColor.a;


                float3 finalColor =
                    lerp(
                        sceneColor.rgb,
                        _OutlineColor.rgb,
                        outlineAmount);


                return
                    half4(
                        finalColor,
                        sceneColor.a);
            }

            ENDHLSL
        }
    }
}
