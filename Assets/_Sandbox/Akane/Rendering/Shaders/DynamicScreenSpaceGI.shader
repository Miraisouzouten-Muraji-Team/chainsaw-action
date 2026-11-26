
Shader "Project/DynamicScreenSpaceGI"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        #define GI_MAX_SAMPLES 32
        #define GI_GOLDEN_ANGLE 2.39996323

        // デバッグモード。
        // 0 : 通常のGI計算
        // 1 : 入力画面色
        // 2 : 放射輝度
        // 3 : サンプル有効率
        // 4 : 遮蔽判定なしのGI
        // 5 : Inspector設定値
        // 6 : 遮蔽による光量損失率
        #define GI_GATHER_DEBUG 0

        // 各PassへC#側から直接渡されるテクスチャ。
        TEXTURE2D(_GiSourceTexture);
        TEXTURE2D_FLOAT(_GiDepthTexture);
        TEXTURE2D(_GiNormalTexture);
        TEXTURE2D(_DynamicGiTexture);

        CBUFFER_START(UnityPerMaterial)
            // 描画方式。
            float _GiUseComposite;

            // GI品質。
            float _GiRadius;
            float _GiIntensity;
            float _GiSampleCount;

            // 放射輝度・反射光。
            float _GiSourceThreshold;
            float _GiMaxRadiance;
            float _GiNormalBias;
            float _GiOcclusionBias;

            // 境界を考慮したぼかし。
            float _GiDepthSigma;
            float _GiNormalPower;
            float4 _GiBlurDirection;
            float4 _GiLowTexelSize;

            // デバッグ・表示設定。
            float _GiDebugView;
            float _GiPixelate;
        CBUFFER_END

        // フルスクリーン描画用の頂点出力。
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        // GI計算で使用する表面情報。
        struct GiSurface
        {
            float3 positionWS;
            float3 normalWS;
            float valid;
        };

        // ====================================================
        // Vertex
        // ====================================================

        // フルスクリーントライアングルを生成する。
        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            output.uv = GetFullScreenTriangleTexCoord(vertexID);
            return output;
        }

        // ====================================================
        // Depth / Normal
        // ====================================================

        // UVが画面外にあるかを判定する。
        bool IsOutsideGiUV(float2 uv)
        {
            return any(uv < 0.0) || any(uv >= 1.0);
        }

        // Skyboxなどの無効なDepthを判定する。
        bool IsGiSky(float rawDepth)
        {
            #if UNITY_REVERSED_Z
                return rawDepth <= 0.0001;
            #else
                return rawDepth >= 0.9999;
            #endif
        }

        // C#側から渡されたDepthを取得する。
        float GetGiDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_GiDepthTexture, sampler_PointClamp, uv).r;
        }

        // Raw Depthをカメラからの距離へ変換する。
        // 平行投影・透視投影の両方に対応する。
        float GetGiEyeDepth(float rawDepth)
        {
            if (unity_OrthoParams.w > 0.5)
            {
                #if UNITY_REVERSED_Z
                    return lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
                #else
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                #endif
            }

            return LinearEyeDepth(rawDepth, _ZBufferParams);
        }

        // C#側から渡されたNormalを取得する。
        float4 GetGiNormal(float2 uv)
        {
            float4 normalData = SAMPLE_TEXTURE2D(_GiNormalTexture, sampler_PointClamp, uv);

            // Pixel ONでは統合済みのWorld Normalを使用する。
            // A成分にはNear/Middle/Farの描画層IDが格納されている。
            if (_GiUseComposite > 0.5)
            {
                return normalData;
            }

            // Pixel OFFではURPのNormal保存形式に対応する。
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 packedNormal = Unpack888ToFloat2(normalData.xyz);
                float2 octNormal = packedNormal * 2.0 - 1.0;
                normalData.xyz = UnpackNormalOctQuadEncode(octNormal);
            #endif

            // 通常描画では描画層IDを使用しない。
            normalData.a = 1.0;
            return normalData;
        }

        // 有効な表面法線かを判定する。
        bool IsValidGiNormal(float4 normalData)
        {
            if (_GiUseComposite > 0.5 && normalData.a < 0.5)
            {
                return false;
            }

            float normalLength = dot(normalData.xyz, normalData.xyz);
            return normalLength > 0.25 && normalLength < 2.0;
        }

        // ====================================================
        // World Position / Surface
        // ====================================================

        // Raw Depthと逆ViewProjection行列からワールド座標を復元する。
        float3 GetGiWorldPosition(float2 uv, float rawDepth)
        {
            float deviceDepth = rawDepth;

            #if !UNITY_REVERSED_Z
                deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif

            return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
        }

        // Depth・NormalからGI計算用の表面情報を生成する。
        GiSurface GetGiSurface(float2 uv)
        {
            GiSurface surface;
            surface.positionWS = 0.0;
            surface.normalWS = float3(0.0, 0.0, 1.0);
            surface.valid = 0.0;

            if (IsOutsideGiUV(uv))
            {
                return surface;
            }

            float rawDepth = GetGiDepth(uv);

            if (IsGiSky(rawDepth))
            {
                return surface;
            }

            float4 normalData = GetGiNormal(uv);

            if (!IsValidGiNormal(normalData))
            {
                return surface;
            }

            surface.positionWS = GetGiWorldPosition(uv, rawDepth);
            surface.normalWS = normalize(normalData.xyz);
            surface.valid = 1.0;

            return surface;
        }

        // ====================================================
        // Radiance
        // ====================================================

        // 画面色から反射光の元になる放射輝度を取得する。
        float3 GetGiSourceRadiance(float2 uv)
        {
            float3 sourceColor = SAMPLE_TEXTURE2D(_GiSourceTexture, sampler_LinearClamp, uv).rgb;

            // 暗い色を除外し、極端に強い発光による過剰な反射を抑える。
            float3 radiance = max(sourceColor - _GiSourceThreshold, 0.0);

            return min(radiance, _GiMaxRadiance);
        }

        // ====================================================
        // Occlusion
        // ====================================================

        // 受光面と反射面の間に遮蔽物が存在するかを近似判定する。
        bool IsGiOccluded(
            float2 receiverUv,
            float2 emitterUv,
            float3 receiverWS,
            float3 emitterWS,
            float3 cameraForward)
        {
            // 2面間の中間地点を調査する。
            float2 middleUv = (receiverUv + emitterUv) * 0.5;

            if (IsOutsideGiUV(middleUv))
            {
                return true;
            }

            float blockerDepth = GetGiDepth(middleUv);

            // 背景の場合は遮蔽物なしとする。
            if (IsGiSky(blockerDepth))
            {
                return false;
            }

            float3 blockerWS = GetGiWorldPosition(middleUv, blockerDepth);
            float3 middleWS = (receiverWS + emitterWS) * 0.5;

            // カメラ方向の距離差から遮蔽を判定する。
            float projectedGap = dot(middleWS - blockerWS, cameraForward);

            return projectedGap > _GiOcclusionBias;
        }

        // ====================================================
        // Pass 0 : GI Gather
        // ====================================================

        // 周辺の可視面から間接光を集める。
        float4 GatherGi(Varyings input) : SV_Target
        {
            float2 uv = input.uv;

            // 入力画面色の確認。
            #if GI_GATHER_DEBUG == 1
                return float4(
                    SAMPLE_TEXTURE2D(_GiSourceTexture, sampler_LinearClamp, uv).rgb,
                    1.0);
            #endif

            // 放射輝度の確認。
            #if GI_GATHER_DEBUG == 2
                return float4(GetGiSourceRadiance(uv), 1.0);
            #endif

            // Inspector設定値の確認。
            #if GI_GATHER_DEBUG == 5
                return float4(
                    saturate(_GiIntensity / 4.0),
                    saturate(_GiNormalBias * 2.0),
                    saturate(_GiRadius / 4.0),
                    1.0);
            #endif

            // 受光面の情報を取得する。
            GiSurface receiver = GetGiSurface(uv);

            if (receiver.valid < 0.5)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            if (_GiIntensity <= 0.0)
            {
                return float4(0.0, 0.0, 0.0, 1.0);
            }

            int sampleCount = (int)clamp(_GiSampleCount, 4.0, (float)GI_MAX_SAMPLES);
            float radius = max(_GiRadius, 0.1);

            // カメラの前方方向を取得する。
            float3 cameraForward = normalize(
                mul((float3x3)UNITY_MATRIX_I_V, float3(0.0, 0.0, -1.0)));

            // 射影行列からワールド単位の半径をUV単位へ変換する。
            float4x4 projection = UNITY_MATRIX_P;
            float2 projectionScale = abs(float2(projection._m00, projection._m11));
            float2 uvRadius;

            if (unity_OrthoParams.w > 0.5)
            {
                // 平行投影では奥行きに依存せず調査半径が一定になる。
                uvRadius = radius * projectionScale * 0.5;
            }
            else
            {
                // 透視投影ではカメラからの距離に応じて調査半径を変更する。
                float eyeDepth = max(GetGiEyeDepth(GetGiDepth(uv)), 0.01);
                uvRadius = radius * projectionScale / (2.0 * eyeDepth);
            }

            // 間接光の積算値。
            float3 indirect = 0.0;
            float3 unoccludedIndirect = 0.0;

            // デバッグ用のサンプル通過数。
            float validSurfaceCount = 0.0;
            float validGeometryCount = 0.0;
            float visibleSampleCount = 0.0;

            // 黄金角を使用して円形の調査範囲にサンプルを配置する。
            [loop]
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (i + 0.5) / sampleCount;
                float angle = i * GI_GOLDEN_ANGLE;
                float2 direction = float2(cos(angle), sin(angle));

                // 画面UV上で直接サンプリング位置を計算する。
                float2 sampleUv = uv + direction * uvRadius * sqrt(t);

                if (IsOutsideGiUV(sampleUv))
                {
                    continue;
                }

                // 反射面の情報を取得する。
                GiSurface emitter = GetGiSurface(sampleUv);

                if (emitter.valid < 0.5)
                {
                    continue;
                }

                validSurfaceCount += 1.0;

                // 受光面と反射面の位置関係を計算する。
                float3 delta = emitter.positionWS - receiver.positionWS;
                float dist2 = dot(delta, delta);

                // 極端に近い面や調査範囲外の面を除外する。
                if (dist2 < 0.0025 || dist2 > radius * radius * 4.0)
                {
                    continue;
                }

                float dist = sqrt(dist2);
                float3 towardsEmitter = delta / dist;

                // 受光面が反射面を向いている割合。
                float receiverFacing = saturate(
                    dot(receiver.normalWS, towardsEmitter) + _GiNormalBias);

                // 反射面が受光面を向いている割合。
                float emitterFacing = saturate(
                    dot(emitter.normalWS, -towardsEmitter) + _GiNormalBias);

                // 距離と表面の向きによる光の減衰。
                float geometry = receiverFacing * emitterFacing / (1.0 + dist2);

                if (geometry <= 0.00001)
                {
                    continue;
                }

                validGeometryCount += 1.0;

                // 反射面の放射輝度から間接光への寄与を求める。
                float3 sourceRadiance = GetGiSourceRadiance(sampleUv);
                float3 contribution = sourceRadiance * geometry;

                // 遮蔽なしの積算値をデバッグ用に保持する。
                unoccludedIndirect += contribution;

                // 遮蔽物が存在する場合は間接光へ加算しない。
                if (IsGiOccluded(
                    uv,
                    sampleUv,
                    receiver.positionWS,
                    emitter.positionWS,
                    cameraForward))
                {
                    continue;
                }

                visibleSampleCount += 1.0;
                indirect += contribution;
            }

            // 各計算条件を通過したサンプルの割合を可視化する。
            // R : 有効な表面
            // G : 距離・法線判定を通過
            // B : 遮蔽判定を通過
            #if GI_GATHER_DEBUG == 3
                return float4(
                    validSurfaceCount / sampleCount,
                    validGeometryCount / sampleCount,
                    visibleSampleCount / sampleCount,
                    1.0);
            #endif

            // 遮蔽判定によって失われた光量の割合を可視化する。
            // R : 遮蔽によって失われた光
            // G : 遮蔽後に残った光
            #if GI_GATHER_DEBUG == 6
                const float3 LUMINANCE = float3(0.2126, 0.7152, 0.0722);

                float unoccluded = dot(unoccludedIndirect, LUMINANCE);
                float occluded = dot(indirect, LUMINANCE);

                if (unoccluded <= 0.00001)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float visibility = saturate(occluded / unoccluded);

                return float4(1.0 - visibility, visibility, 0.0, 1.0);
            #endif

            // 遮蔽判定なしのGIを確認する。
            #if GI_GATHER_DEBUG == 4
                indirect = unoccludedIndirect;
            #endif

            // 円形調査範囲の投影面積を利用した近似補正。
            // 円の面積のPIとLambert拡散反射の1/PIを相殺する。
            float sampleAreaWeight = (radius * radius) / sampleCount;

            // サンプル面積とGI強度を反映する。
            float3 result = indirect * sampleAreaWeight * _GiIntensity;

            // RGB : 間接光
            // A   : 受光面の有効性
            return float4(max(result, 0.0), 1.0);
        }

        // ====================================================
        // Pass 1 : Bilateral Blur
        // ====================================================

        // 深度差・法線差を考慮してGIをぼかす。
        // 水平・垂直方向の計2回実行する。
        float4 BlurGi(Varyings input) : SV_Target
        {
            float2 uv = input.uv;

            // 中心画素のDepthとNormalを取得する。
            float centerDepth = GetGiDepth(uv);

            if (IsGiSky(centerDepth))
            {
                return 0.0;
            }

            float4 centerNormalData = GetGiNormal(uv);

            if (!IsValidGiNormal(centerNormalData))
            {
                return 0.0;
            }

            float3 centerNormal = normalize(centerNormalData.xyz);
            float centerEyeDepth = GetGiEyeDepth(centerDepth);

            // C#側から指定された方向へサンプリングする。
            float2 texelOffset = _GiBlurDirection.xy * _GiLowTexelSize.xy;

            float sigma = max(_GiDepthSigma, 0.05);
            float normalPower = max(_GiNormalPower, 0.01);

            float3 sum = 0.0;
            float totalWeight = 0.0;

            // 5点のぼかし処理。
            // コンパイラーへの負担を抑えるためループ展開を強制しない。
            [loop]
            for (int offset = -2; offset <= 2; offset++)
            {
                float2 sampleUv = uv + texelOffset * offset;

                if (IsOutsideGiUV(sampleUv))
                {
                    continue;
                }

                float sampleDepth = GetGiDepth(sampleUv);

                if (IsGiSky(sampleDepth))
                {
                    continue;
                }

                float4 sampleNormalData = GetGiNormal(sampleUv);

                if (!IsValidGiNormal(sampleNormalData))
                {
                    continue;
                }

                // 異なる描画層へのぼかしによる光漏れを抑える。
                if (_GiUseComposite > 0.5 &&
                    abs(sampleNormalData.a - centerNormalData.a) > 0.5)
                {
                    continue;
                }

                float4 gi = SAMPLE_TEXTURE2D(_GiSourceTexture, sampler_PointClamp, sampleUv);

                if (gi.a < 0.5)
                {
                    continue;
                }

                // カメラ方向の深度差による重み。
                float sampleEyeDepth = GetGiEyeDepth(sampleDepth);
                float depthDifference = abs(centerEyeDepth - sampleEyeDepth);

                float depthWeight = exp(
                    -(depthDifference * depthDifference) / (2.0 * sigma * sigma));

                // 表面法線の向きによる重み。
                float3 sampleNormal = normalize(sampleNormalData.xyz);
                float normalWeight = pow(
                    saturate(dot(centerNormal, sampleNormal)),
                    normalPower);

                // 中心画素からの距離によるガウス重み。
                int absoluteOffset = abs(offset);

                float spatialWeight =
                    absoluteOffset == 0 ? 0.40 :
                    absoluteOffset == 1 ? 0.24 : 0.06;

                float weight = spatialWeight * depthWeight * normalWeight;

                sum += gi.rgb * weight;
                totalWeight += weight;
            }

            // 有効なサンプルが存在しない場合は間接光なしとする。
            if (totalWeight <= 0.00001)
            {
                return float4(0.0, 0.0, 0.0, 1.0);
            }

            // 積算した間接光を重みの合計で正規化する。
            return float4(sum / totalWeight, 1.0);
        }

        // ====================================================
        // Pass 2 : Composite
        // ====================================================

        // ぼかし済みの間接光を元の画面へ合成する。
        float4 CompositeGi(Varyings input) : SV_Target
        {
            float2 uv = input.uv;

            // デバッグ時にIntensityが0なら完全な黒を出力する。
            if (_GiDebugView > 0.5 && _GiIntensity <= 0.0)
            {
                return float4(0.0, 0.0, 0.0, 1.0);
            }

            float4 sceneColor = SAMPLE_TEXTURE2D(_GiSourceTexture, sampler_LinearClamp, uv);

            float rawDepth = GetGiDepth(uv);
            float4 normalData = GetGiNormal(uv);

            // 背景などの無効な表面にはGIを適用しない。
            if (IsGiSky(rawDepth) || !IsValidGiNormal(normalData))
            {
                if (_GiDebugView > 0.5)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                return sceneColor;
            }

            float4 gi;

            // ドット絵表現の有無に応じて補間方法を切り替える。
            if (_GiPixelate > 0.5)
            {
                gi = SAMPLE_TEXTURE2D(_DynamicGiTexture, sampler_PointClamp, uv);
            }
            else
            {
                gi = SAMPLE_TEXTURE2D(_DynamicGiTexture, sampler_LinearClamp, uv);
            }

            float3 indirect = max(gi.rgb, 0.0);

            // デバッグ時のみ間接光を表示する。
            // GI計算用テクスチャには増幅を適用しない。
            if (_GiDebugView > 0.5)
            {
                #if GI_GATHER_DEBUG == 0 || GI_GATHER_DEBUG == 4
                    return float4(indirect * 4.0, 1.0);
                #else
                    return float4(indirect, 1.0);
                #endif
            }

            // 通常描画では元のHDR画面色へ間接光を加算する。
            return float4(sceneColor.rgb + indirect, sceneColor.a);
        }

        ENDHLSL

        // 間接光の計算。
        Pass
        {
            Name "Dynamic GI Gather"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment GatherGi
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }

        // 深度・法線を考慮したぼかし。
        Pass
        {
            Name "Dynamic GI Bilateral Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurGi
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }

        // 元の画面への間接光合成。
        Pass
        {
            Name "Dynamic GI Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeGi
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }

    Fallback Off
}
