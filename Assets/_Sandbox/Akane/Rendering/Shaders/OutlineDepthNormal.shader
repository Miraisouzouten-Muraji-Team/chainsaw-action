Shader "Custom/OutlineDepthNormal"
{
    Properties
    {
        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.02, 0.02, 0.02, 1.0)
        _OutlineWidthPixels("Outline Width Pixels", Range(1, 4)) = 1

        [Header(Depth Edge)]
        _DepthThreshold("Depth Threshold", Range(0.0001, 0.2)) = 0.08
        _DepthStrength("Depth Strength", Range(0, 1)) = 1

        [Header(Normal Edge)]
        _NormalThreshold("Normal Threshold", Range(0, 1)) = 0.2
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.5
        _NormalDepthThreshold("Normal Depth Threshold", Range(0, 1)) = 0.1

        [Header(Composite)]
        [Toggle] _UseCompositeTextures("Use Composite Textures", Float) = 0
        [HideInInspector] _PixelCompositeDepthTex("Composite Depth", 2D) = "black" {}
        [HideInInspector] _PixelCompositeNormalTex("Composite Normal", 2D) = "black" {}

        // Sceneビューの通常プレビューでは統合Outlineを適用しない。
        [HideInInspector] _SceneNormalPreviewEnabled("Scene Normal Preview Enabled", Float) = 0

        [Header(Debug)]
        [Enum(Off,0,Normals,1,NormalEdges,2)]
        _DebugView("Debug View", Float) = 0
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_OUTLINE_WIDTH 4

            // Opaque Compositeで生成した統合DepthとWorld Normal。
            TEXTURE2D(_PixelCompositeDepthTex);
            TEXTURE2D(_PixelCompositeNormalTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidthPixels;
                float _DepthThreshold;
                float _DepthStrength;
                float _NormalThreshold;
                float _NormalStrength;
                float _NormalDepthThreshold;
                float _UseCompositeTextures;
                float _DebugView;
                float _SceneNormalPreviewEnabled;   // Sceneビューの通常プレビュー判定
            CBUFFER_END

            // SkyのDepthか判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // 有効なWorld Normalか判定する。
            bool IsValidNormal(float3 normalWS)
            {
                float lengthSquared = dot(normalWS, normalWS);
                return lengthSquared > 0.25 && lengthSquared < 2.0;
            }

            // Pixel ONでは統合Depth、Pixel OFFではURPのDepthを取得する。
            float SampleOutlineDepth(float2 colorUv, float2 screenUv)
            {
                if (_UseCompositeTextures > 0.5)
                {
                    return SAMPLE_TEXTURE2D(_PixelCompositeDepthTex, sampler_PointClamp, saturate(colorUv)).r;
                }

                return SampleSceneDepth(saturate(screenUv));
            }

            // Pixel ONでは統合World Normal、Pixel OFFではURPのWorld Normalを取得する。
            float3 SampleOutlineNormal(float2 colorUv, float2 screenUv)
            {
                if (_UseCompositeTextures > 0.5)
                {
                    return SAMPLE_TEXTURE2D(_PixelCompositeNormalTex, sampler_PointClamp, saturate(colorUv)).xyz;
                }

                return SampleSceneNormals(saturate(screenUv));
            }

            // 中心のピクセルより奥にあるDepthとの境界を検出する。
            float GetDepthEdge(float2 sampleColorUv, float2 sampleScreenUv, float centerDepth)
            {
                // 現在の描画方式に対応するDepthを取得する。
                float sampleRawDepth = SampleOutlineDepth(sampleColorUv, sampleScreenUv);

                // 隣接ピクセルがSkyなら中心オブジェクトの輪郭とする。
                if (IsSkyDepth(sampleRawDepth))
                {
                    return 1.0;
                }

                // Raw Depthを視点からの距離へ変換する。
                float sampleDepth = LinearEyeDepth(sampleRawDepth, _ZBufferParams);
                float depthDifference = (sampleDepth - centerDepth) / max(centerDepth, 0.0001);

                // 指定したDepth差以上なら輪郭とする。
                return step(_DepthThreshold, depthDifference);
            }

            // 中心と隣接ピクセルのWorld Normalの差を検出する。
            float GetNormalEdge(float2 sampleColorUv, float2 sampleScreenUv, float centerDepth, float3 centerNormal, bool centerNormalValid)
            {
                // 中心のNormalが無効なら輪郭計算しない。
                if (!centerNormalValid)
                {
                    return 0.0;
                }

                // 隣接ピクセルのDepthを取得する。
                float sampleRawDepth = SampleOutlineDepth(sampleColorUv, sampleScreenUv);

                // Skyとの境界はDepthによる輪郭検出へ任せる。
                if (IsSkyDepth(sampleRawDepth))
                {
                    return 0.0;
                }

                // 大きく奥行きが異なる面はNormal比較から除外する。
                float sampleDepth = LinearEyeDepth(sampleRawDepth, _ZBufferParams);
                float depthDifference = abs(sampleDepth - centerDepth) / max(centerDepth, 0.0001);

                if (depthDifference > _NormalDepthThreshold)
                {
                    return 0.0;
                }

                // 隣接ピクセルのWorld Normalを取得する。
                float3 sampleNormal = SampleOutlineNormal(sampleColorUv, sampleScreenUv);

                // 無効なNormalは比較しない。
                if (!IsValidNormal(sampleNormal))
                {
                    return 0.0;
                }

                sampleNormal = normalize(sampleNormal);

                // Normalの向きが異なるほど値が大きくなる。
                float normalDifference = 1.0 - saturate(dot(centerNormal, sampleNormal));

                // 閾値以上のNormal差を輪郭とする。
                return step(_NormalThreshold, normalDifference);
            }

            // DepthとNormalを使ってスクリーンスペースの輪郭を描画する。
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Color合成用と通常Depth参照用のUVを別々に取得する。
                float2 colorUv = input.texcoord.xy;
                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;

                // 合成済みの画面色を取得する。
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, colorUv, _BlitMipLevel);

                // Sceneビューの通常プレビューには既に通常描画のOutlineが含まれる。
                // 統合Outlineを重ねず、元の画面色を返す。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return sceneColor;
                }

                // 現在の描画方式に対応する中心Depthを取得する。
                float centerRawDepth = SampleOutlineDepth(colorUv, screenUv);

                // 描画対象が存在しない場所にはアウトラインを描画しない。
                if (IsSkyDepth(centerRawDepth))
                {
                    return sceneColor;
                }

                // 中心DepthとWorld Normalを取得する。
                float centerDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
                float3 centerNormal = SampleOutlineNormal(colorUv, screenUv);
                bool centerNormalValid = IsValidNormal(centerNormal);

                // Normalが有効な場合だけ正規化する。
                if (centerNormalValid)
                {
                    centerNormal = normalize(centerNormal);
                }
                else
                {
                    centerNormal = float3(0.0, 0.0, 0.0);
                }

                // Debug View 1：中心ピクセルのWorld Normalを可視化する。
                if (_DebugView > 0.5 && _DebugView < 1.5)
                {
                    float3 debugColor = centerNormalValid ? centerNormal * 0.5 + 0.5 : float3(1.0, 0.0, 1.0);
                    return half4(debugColor, sceneColor.a);
                }

                // 画面解像度から1ピクセルあたりのUV幅を計算する。
                float2 texelSize = 1.0 / _ScaledScreenParams.xy;
                int outlineWidth = clamp((int)round(_OutlineWidthPixels), 1, MAX_OUTLINE_WIDTH);

                float depthEdge = 0.0;
                float normalEdge = 0.0;

                // 指定した線幅まで隣接ピクセルを比較する。
                [loop]
                for (int radius = 1; radius <= MAX_OUTLINE_WIDTH; ++radius)
                {
                    if (radius > outlineWidth)
                    {
                        break;
                    }

                    float2 offset = texelSize * (float)radius;

                    // Depth：上下左右を比較して、手前側の輪郭を検出する。
                    depthEdge = max(depthEdge, GetDepthEdge(colorUv + float2(offset.x, 0.0), screenUv + float2(offset.x, 0.0), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(colorUv + float2(-offset.x, 0.0), screenUv + float2(-offset.x, 0.0), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(colorUv + float2(0.0, offset.y), screenUv + float2(0.0, offset.y), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(colorUv + float2(0.0, -offset.y), screenUv + float2(0.0, -offset.y), centerDepth));

                    // Normal：右と上を比較して、面の向きが変わる境界を検出する。
                    normalEdge = max(normalEdge, GetNormalEdge(colorUv + float2(offset.x, 0.0), screenUv + float2(offset.x, 0.0), centerDepth, centerNormal, centerNormalValid));
                    normalEdge = max(normalEdge, GetNormalEdge(colorUv + float2(0.0, offset.y), screenUv + float2(0.0, offset.y), centerDepth, centerNormal, centerNormalValid));
                }

                // Inspectorで指定された強度を適用する。
                depthEdge *= _DepthStrength;
                normalEdge *= _NormalStrength;

                // Debug View 2：Normalによる輪郭だけを白黒表示する。
                if (_DebugView >= 1.5)
                {
                    return half4(normalEdge.xxx, sceneColor.a);
                }

                // DepthとNormalの輪郭を統合する。
                float edge = saturate(max(depthEdge, normalEdge));
                float outlineAmount = edge * _OutlineColor.a;

                // 元の画面色にアウトラインの色を適用する。
                float3 finalColor = lerp(sceneColor.rgb, _OutlineColor.rgb, outlineAmount);
                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
