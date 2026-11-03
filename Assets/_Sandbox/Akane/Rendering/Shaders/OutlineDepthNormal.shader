
Shader "Custom/OutlineDepthNormal"
{
    Properties
    {
        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.02, 0.02, 0.02, 1.0)
        _OutlineWidthPixels("Outline Width Pixels", Range(1, 4)) = 1

        [Header(Depth Edge)]
        _DepthThreshold("Depth Threshold", Range(0.001, 1.0)) = 0.08
        _DepthStrength("Depth Strength", Range(0, 1)) = 1

        [Header(Normal Edge)]
        _NormalThreshold("Normal Threshold", Range(0, 1)) = 0.2
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.5
        _NormalDepthThreshold("Normal Depth Threshold", Range(0, 1)) = 0.1

        [Header(Composite)]
        [Toggle] _UseCompositeTextures("Use Composite Textures", Float) = 0
        [HideInInspector] _PixelCompositeDepthTex("Composite Depth", 2D) = "black" {}
        [HideInInspector] _PixelCompositeNormalTex("Composite Normal", 2D) = "black" {}
        [HideInInspector] _SceneNormalPreviewEnabled("Scene Normal Preview Enabled", Float) = 0

        // 各描画層の元解像度。Renderer Featureから設定する。
        [HideInInspector] _PixelOutlineNearResolution("Near Resolution", Vector) = (640,360,0,0)
        [HideInInspector] _PixelOutlineMiddleResolution("Middle Resolution", Vector) = (320,180,0,0)
        [HideInInspector] _PixelOutlineFarResolution("Far Resolution", Vector) = (160,90,0,0)

        [Header(Debug)]
        [Enum(Off,0,Normals,1,NormalEdges,2,DepthEdges,3,CombinedEdges,4,LayerID,5)]
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
                float _SceneNormalPreviewEnabled;

                float4 _PixelOutlineNearResolution;
                float4 _PixelOutlineMiddleResolution;
                float4 _PixelOutlineFarResolution;
            CBUFFER_END

            // Skyに対応するDepthか判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // World Normalの値が有効か判定する。
            bool IsValidNormal(float3 normalWS)
            {
                float normalLength = dot(normalWS, normalWS);
                return normalLength > 0.25 && normalLength < 2.0;
            }

            // テクスチャの範囲外か判定する。
            bool IsOutsideUV(float2 uv)
            {
                return any(uv < 0.0) || any(uv >= 1.0);
            }

            // 統合DepthまたはURP標準Depthを取得する。
            float SampleOutlineDepth(float2 uv)
            {
                if (_UseCompositeTextures > 0.5)
                {
                    return SAMPLE_TEXTURE2D(
                        _PixelCompositeDepthTex,
                        sampler_PointClamp,
                        saturate(uv)).r;
                }

                return SampleSceneDepth(saturate(uv));
            }

            // World Normalと描画層IDを取得する。
            // A成分は0：背景、1：Near、2：Middle、3：Far。
            float4 SampleOutlineNormalData(float2 uv)
            {
                if (_UseCompositeTextures > 0.5)
                {
                    return SAMPLE_TEXTURE2D(
                        _PixelCompositeNormalTex,
                        sampler_PointClamp,
                        saturate(uv));
                }

                return float4(SampleSceneNormals(saturate(uv)), 1.0);
            }

            // 描画層IDに対応するCapture解像度を取得する。
            float2 GetCaptureResolution(float layerId)
            {
                if (layerId < 1.5)
                {
                    return max(_PixelOutlineNearResolution.xy, 1.0);
                }

                if (layerId < 2.5)
                {
                    return max(_PixelOutlineMiddleResolution.xy, 1.0);
                }

                return max(_PixelOutlineFarResolution.xy, 1.0);
            }

            // Raw Depthを視点からの距離へ変換する。
            float GetOutlineLinearDepth(float rawDepth)
            {
                // 平行投影ではNear/Farから線形深度を復元する。
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        return lerp(
                            _ProjectionParams.z,
                            _ProjectionParams.y,
                            rawDepth);
                    #else
                        return lerp(
                            _ProjectionParams.y,
                            _ProjectionParams.z,
                            rawDepth);
                    #endif
                }

                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // 投影方式に合わせた深度差を計算する。
            float GetOutlineDepthDifference(
                float sampleDepth,
                float centerDepth)
            {
                float depthDifference = sampleDepth - centerDepth;

                // 平行投影の場合はワールド単位の奥行き差。
                if (unity_OrthoParams.w > 0.5)
                {
                    return depthDifference;
                }

                // 透視投影の場合は視点距離に対する相対差。
                return depthDifference / max(centerDepth, 0.0001);
            }

            // DepthとNormalの輪郭を1方向について判定する。
            void EvaluateOutlineNeighbor(
                float2 sampleUv,
                float centerDepth,
                float3 centerNormal,
                bool centerNormalValid,
                float centerLayerId,
                inout float depthEdge,
                inout float normalEdge)
            {
                if (IsOutsideUV(sampleUv))
                {
                    return;
                }

                float sampleRawDepth = SampleOutlineDepth(sampleUv);

                // 隣接ピクセルが背景なら外周とする。
                if (IsSkyDepth(sampleRawDepth))
                {
                    depthEdge = 1.0;
                    return;
                }

                float sampleDepth = GetOutlineLinearDepth(sampleRawDepth);

                float depthDifference = GetOutlineDepthDifference(
                    sampleDepth,
                    centerDepth);

                // 中心より奥の面との境界を検出する。
                depthEdge = max(
                    depthEdge,
                    step(_DepthThreshold, depthDifference));

                float4 sampleNormalData = SampleOutlineNormalData(sampleUv);

                // Pixel ON時には描画層の違いも確認する。
                if (_UseCompositeTextures > 0.5)
                {
                    bool differentLayer =
                        abs(sampleNormalData.a - centerLayerId) > 0.5;

                    // 描画層が異なり、隣接面が手前でなければ境界とする。
                    if (differentLayer && depthDifference >= -0.0001)
                    {
                        depthEdge = 1.0;
                    }
                }

                if (!centerNormalValid)
                {
                    return;
                }

                // 深度が大きく異なる面ではNormalを比較しない。
                if (abs(depthDifference) > _NormalDepthThreshold)
                {
                    return;
                }

                float3 sampleNormal = sampleNormalData.xyz;

                if (!IsValidNormal(sampleNormal))
                {
                    return;
                }

                sampleNormal = normalize(sampleNormal);

                // 法線の角度差から面の境界を検出する。
                float normalDifference =
                    1.0 - saturate(dot(centerNormal, sampleNormal));

                normalEdge = max(
                    normalEdge,
                    step(_NormalThreshold, normalDifference));
            }

            // 描画層の元ピクセル境界までの距離を、画面ピクセル単位で返す。
            float GetDistanceToPixelBorder(
                float2 pixelFraction,
                float2 captureResolution,
                float2 direction)
            {
                float2 scale = _ScaledScreenParams.xy / captureResolution;

                if (direction.x > 0.5)
                {
                    return (1.0 - pixelFraction.x) * scale.x;
                }

                if (direction.x < -0.5)
                {
                    return pixelFraction.x * scale.x;
                }

                if (direction.y > 0.5)
                {
                    return (1.0 - pixelFraction.y) * scale.y;
                }

                return pixelFraction.y * scale.y;
            }

            // Pixel ON時、元解像度の境界付近にだけアウトラインを描画する。
            void EvaluatePixelLayerEdge(
                float2 colorUv,
                float2 captureResolution,
                float2 direction,
                float outlineWidth,
                float centerDepth,
                float3 centerNormal,
                bool centerNormalValid,
                float centerLayerId,
                inout float depthEdge,
                inout float normalEdge)
            {
                // 現在の画面ピクセルが属するCapture側のピクセル。
                float2 sourcePosition = colorUv * captureResolution;
                float2 sourcePixel = floor(sourcePosition);
                float2 pixelFraction = frac(sourcePosition);

                // 指定線幅の範囲外には輪郭を描かない。
                float borderDistance = GetDistanceToPixelBorder(
                    pixelFraction,
                    captureResolution,
                    direction);

                if (borderDistance > outlineWidth)
                {
                    return;
                }

                // 元解像度のピクセル中心。
                float2 sourceUv =
                    (sourcePixel + 0.5) / captureResolution;

                // 元解像度で1ピクセル隣を参照する。
                float2 neighborUv =
                    sourceUv + direction / captureResolution;

                EvaluateOutlineNeighbor(
                    neighborUv,
                    centerDepth,
                    centerNormal,
                    centerNormalValid,
                    centerLayerId,
                    depthEdge,
                    normalEdge);
            }

            // 統合Depth/Normalと元の画面色を用いてOutlineを描画する。
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 colorUv = input.texcoord.xy;
                float2 screenUv =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    colorUv,
                    _BlitMipLevel);

                // Sceneビューの通常プレビューでは二重適用しない。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return sceneColor;
                }

                // Pixel ON/OFFで参照するDepth用UVを分ける。
                float2 outlineUv = _UseCompositeTextures > 0.5
                    ? colorUv
                    : screenUv;

                float centerRawDepth = SampleOutlineDepth(outlineUv);

                if (IsSkyDepth(centerRawDepth))
                {
                    if (_DebugView > 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, 1.0);
                    }

                    return sceneColor;
                }

                float4 centerNormalData =
                    SampleOutlineNormalData(outlineUv);

                float centerDepth =
                    GetOutlineLinearDepth(centerRawDepth);

                float3 centerNormal = centerNormalData.xyz;
                bool centerNormalValid = IsValidNormal(centerNormal);

                if (centerNormalValid)
                {
                    centerNormal = normalize(centerNormal);
                }
                else
                {
                    centerNormal = 0.0;
                }

                // Debug View 1：World Normal。
                if (_DebugView >= 0.5 && _DebugView < 1.5)
                {
                    float3 debugColor = centerNormalValid
                        ? centerNormal * 0.5 + 0.5
                        : float3(1.0, 0.0, 1.0);

                    return half4(debugColor, 1.0);
                }

                // Debug View 5：Near / Middle / Farの識別結果。
                if (_DebugView >= 4.5)
                {
                    float3 layerColor = float3(0.0, 0.0, 0.0);

                    if (centerNormalData.a < 1.5)
                    {
                        layerColor = float3(1.0, 0.0, 0.0);
                    }
                    else if (centerNormalData.a < 2.5)
                    {
                        layerColor = float3(0.0, 1.0, 0.0);
                    }
                    else
                    {
                        layerColor = float3(0.0, 0.0, 1.0);
                    }

                    return half4(layerColor, 1.0);
                }

                float depthEdge = 0.0;
                float normalEdge = 0.0;

                int outlineWidth = clamp(
                    (int)round(_OutlineWidthPixels),
                    1,
                    MAX_OUTLINE_WIDTH);

                if (_UseCompositeTextures > 0.5)
                {
                    // 現在表示しているオブジェクトの描画層を取得。
                    float layerId = centerNormalData.a;

                    // 描画層に対応する元解像度を取得。
                    float2 captureResolution =
                        GetCaptureResolution(layerId);

                    // 元解像度での1ピクセル差を比較する。
                    // 線幅は最終画面のピクセル単位で制限する。
                    EvaluatePixelLayerEdge(
                        colorUv, captureResolution,
                        float2(1.0, 0.0), outlineWidth,
                        centerDepth, centerNormal, centerNormalValid,
                        layerId, depthEdge, normalEdge);

                    EvaluatePixelLayerEdge(
                        colorUv, captureResolution,
                        float2(-1.0, 0.0), outlineWidth,
                        centerDepth, centerNormal, centerNormalValid,
                        layerId, depthEdge, normalEdge);

                    EvaluatePixelLayerEdge(
                        colorUv, captureResolution,
                        float2(0.0, 1.0), outlineWidth,
                        centerDepth, centerNormal, centerNormalValid,
                        layerId, depthEdge, normalEdge);

                    EvaluatePixelLayerEdge(
                        colorUv, captureResolution,
                        float2(0.0, -1.0), outlineWidth,
                        centerDepth, centerNormal, centerNormalValid,
                        layerId, depthEdge, normalEdge);
                }
                else
                {
                    // Pixel OFFでは従来どおり画面解像度を基準にする。
                    float2 texelSize =
                        1.0 / _ScaledScreenParams.xy;

                    [loop]
                    for (int radius = 1;
                         radius <= MAX_OUTLINE_WIDTH;
                         ++radius)
                    {
                        if (radius > outlineWidth)
                        {
                            break;
                        }

                        float2 offset = texelSize * radius;

                        EvaluateOutlineNeighbor(
                            screenUv + float2(offset.x, 0.0),
                            centerDepth, centerNormal, centerNormalValid,
                            1.0, depthEdge, normalEdge);

                        EvaluateOutlineNeighbor(
                            screenUv + float2(-offset.x, 0.0),
                            centerDepth, centerNormal, centerNormalValid,
                            1.0, depthEdge, normalEdge);

                        EvaluateOutlineNeighbor(
                            screenUv + float2(0.0, offset.y),
                            centerDepth, centerNormal, centerNormalValid,
                            1.0, depthEdge, normalEdge);

                        EvaluateOutlineNeighbor(
                            screenUv + float2(0.0, -offset.y),
                            centerDepth, centerNormal, centerNormalValid,
                            1.0, depthEdge, normalEdge);
                    }
                }

                depthEdge *= _DepthStrength;
                normalEdge *= _NormalStrength;

                // Debug View 2：Normal Edge。
                if (_DebugView >= 1.5 && _DebugView < 2.5)
                {
                    return half4(normalEdge.xxx, 1.0);
                }

                // Debug View 3：Depth Edge。
                if (_DebugView >= 2.5 && _DebugView < 3.5)
                {
                    return half4(depthEdge.xxx, 1.0);
                }

                float edge = saturate(max(depthEdge, normalEdge));

                // Debug View 4：最終的な輪郭マスク。
                if (_DebugView >= 3.5)
                {
                    return half4(edge.xxx, 1.0);
                }

                float outlineAmount = edge * _OutlineColor.a;

                float3 finalColor = lerp(
                    sceneColor.rgb,
                    _OutlineColor.rgb,
                    outlineAmount);

                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
