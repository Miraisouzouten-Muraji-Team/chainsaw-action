
Shader "Custom/VolumetricFog"
{
    Properties
    {
        [HideInInspector] _BlitTexture("Blit Texture", 2D) = "white" {}
        [HideInInspector] _BlitMipLevel("Blit Mip Level", Float) = 0

        [HideInInspector] _PixelEffectEnabled("Pixel Effect Enabled", Float) = 0
        [HideInInspector] _SceneNormalPreviewEnabled("Scene Normal Preview Enabled", Float) = 0

        // ----------------------------------------------------
        // Fog
        // ----------------------------------------------------

        _FogColor("Fog Color", Color) = (0.5, 0.6, 0.65, 1.0)
        _Density("Density", Range(0.0, 0.2)) = 0.02
        _MaxDistance("Max Distance", Float) = 100.0
        _FogStartDistance("Fog Start Distance", Float) = 5.0

        // 高さによる霧の減衰。
        _FogHeight("Fog Height", Float) = 0.0
        _HeightFalloff("Height Falloff", Range(0.0, 1.0)) = 0.02

        // 空に適用する霧。
        _SkyMaxDistance("Sky Max Distance", Float) = 30.0
        _SkyFogStrength("Sky Fog Strength", Range(0.0, 1.0)) = 0.4

        // 背景の暗くなり方を調整する。
        // 1=物理的な透過率、0=背景を暗くしない。
        _FogTransmittanceStrength("Fog Transmittance Strength", Range(0.0, 1.0)) = 1.0

        // ----------------------------------------------------
        // Directional / Ambient Light
        // ----------------------------------------------------

        // 太陽光による散乱光の強さ。
        _LightIntensity("Directional Scattering Intensity", Range(0.0, 5.0)) = 0.2

        // Directional LightのShadow Mapを反映する強さ。
        _DirectionalShadowStrength("Directional Shadow Strength", Range(0.0, 1.0)) = 1.0

        // 霧全体の環境光。
        _AmbientIntensity("Ambient Intensity", Range(0.0, 1.0)) = 0.03

        // DirectionalとAmbientに適用する共通倍率。
        _ScatteringIntensity("Scattering Intensity", Range(0.0, 5.0)) = 1.0

        // ----------------------------------------------------
        // Point / Spot Light
        // ----------------------------------------------------

        [Toggle] _PointLightVolumeEnabled("Point Light Volume Enabled", Float) = 0

        // Point / Spot Lightの散乱光の強さ。
        _PointLightScatteringIntensity("Point Light Scattering Intensity", Range(0.0, 10.0)) = 2.0

        // ----------------------------------------------------
        // Scattering / Ray March
        // ----------------------------------------------------

        _Anisotropy("Anisotropy", Range(-0.9, 0.9)) = 0.5

        // レイマーチングの目標間隔。
        _RayStepLength("Ray Step Length", Range(0.25, 4.0)) = 1.0

        // 1画素あたりの最大サンプル数。
        _SampleCount("Max Sample Count", Range(8, 128)) = 64

        // ----------------------------------------------------
        // Debug
        // ----------------------------------------------------

        [Enum(Off, 0, PointOnly, 1, VolumeMax, 2, DistanceRanges, 3)]
        _PointLightDebugMode("Point Light Debug Mode", Float) = 0

        _PointLightDebugGain("Point Light Debug Gain", Range(0.1, 20.0)) = 5.0
        _PointLightDebugDistance("Point Light Debug Distance", Float) = 30.0
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
            Name "VolumetricFog"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Directional LightのShadow Mapに対応する。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_SAMPLE_COUNT 128
            #define MAX_VOLUME_SLICES 128

            // Pixel Captureの深度テクスチャ。
            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);

            // Near / Middle / Farの統合深度。
            TEXTURE2D(_PixelCompositeDepthTex);

            // Point / Spot Lightの3D照明テクスチャ。
            TEXTURE3D(_PointLightVolumeTex);

            // Point Light Volumeの最大計算距離。
            float _PointVolumeMaxDistance;

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;

                float _Density;
                float _MaxDistance;
                float _FogStartDistance;

                float _FogHeight;
                float _HeightFalloff;

                float _SkyMaxDistance;
                float _SkyFogStrength;
                float _FogTransmittanceStrength;

                float _LightIntensity;
                float _DirectionalShadowStrength;
                float _AmbientIntensity;
                float _ScatteringIntensity;

                float _PointLightVolumeEnabled;
                float _PointLightScatteringIntensity;

                float _Anisotropy;
                float _RayStepLength;
                float _SampleCount;

                float _PixelEffectEnabled;
                float _SceneNormalPreviewEnabled;

                float _PointLightDebugMode;
                float _PointLightDebugGain;
                float _PointLightDebugDistance;
            CBUFFER_END

            // ----------------------------------------------------
            // Fog Density
            // ----------------------------------------------------

            // ワールド座標から霧の密度を計算する。
            float GetFogDensity(float3 positionWS)
            {
                float heightDifference = max(
                    positionWS.y - _FogHeight,
                    0.0);

                float heightDensity = exp(
                    -heightDifference * _HeightFalloff);

                return max(_Density, 0.0) * heightDensity;
            }

            // ----------------------------------------------------
            // Scattering Phase
            // ----------------------------------------------------

            // 散乱する光の方向による倍率を計算する。
            float HenyeyGreenstein(
                float cosTheta,
                float anisotropy)
            {
                float g = clamp(
                    anisotropy,
                    -0.9,
                    0.9);

                float g2 = g * g;

                float denominator = max(
                    1.0 + g2 - 2.0 * g * cosTheta,
                    0.0001);

                return INV_FOUR_PI *
                    (1.0 - g2) /
                    pow(denominator, 1.5);
            }

            // ----------------------------------------------------
            // Depth
            // ----------------------------------------------------

            // 深度がSkyを表しているか判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // Pixel ON/OFFに応じて使用する深度を切り替える。
            float GetCompositeDepth(
                float2 colorUv,
                float2 depthUv)
            {
                // Pixel OFFではURPの標準深度を使用する。
                if (_PixelEffectEnabled < 0.5)
                {
                    return SampleSceneDepth(depthUv);
                }

                // Pixel ONでは合成済みの深度を参照する。
                return SAMPLE_TEXTURE2D(
                    _PixelCompositeDepthTex,
                    sampler_PointClamp,
                    colorUv).r;
            }

            // ----------------------------------------------------
            // Camera
            // ----------------------------------------------------

            // カメラの前方ベクトルをワールド座標で取得する。
            float3 GetCameraForwardWS()
            {
                return normalize(
                    mul(
                        (float3x3)UNITY_MATRIX_I_V,
                        float3(0.0, 0.0, -1.0)));
            }

            // ----------------------------------------------------
            // Point / Spot Light Volume
            // ----------------------------------------------------

            // 3D照明テクスチャから散乱光を取得する。
            // 平行投影と透視投影の両方に対応する。
            float3 SamplePointLightScattering(
                float2 screenUv,
                float viewDistance)
            {
                if (_PointLightVolumeEnabled < 0.5)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                float maxDistance =
                    _PointVolumeMaxDistance;

                if (maxDistance <= 0.0001)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // 3D照明の計算範囲外は参照しない。
                if (viewDistance < 0.0 ||
                    viewDistance > maxDistance)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // カメラ前方距離を0～1へ変換する。
                float normalizedDepth = saturate(
                    viewDistance / maxDistance);

                float3 volumeUv = float3(
                    saturate(screenUv),
                    normalizedDepth);

                // 3D照明を線形補間して取得する。
                float3 pointLighting = SAMPLE_TEXTURE3D_LOD(
                    _PointLightVolumeTex,
                    sampler_LinearClamp,
                    volumeUv,
                    0).rgb;

                return pointLighting *
                    _PointLightScatteringIntensity;
            }

            // ----------------------------------------------------
            // Debug Volume
            // ----------------------------------------------------

            // Compute Shaderが作成した3D照明を直接表示する。
            // Mode 2: 全奥行きの最大値。
            // Mode 3: 近距離を赤、遠距離を緑で表示する。
            float4 GetPointLightDebugVolume(float2 screenUv)
            {
                if (_PointLightVolumeEnabled < 0.5 ||
                    _PointVolumeMaxDistance <= 0.0001)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                uint volumeWidth;
                uint volumeHeight;
                uint volumeDepth;

                _PointLightVolumeTex.GetDimensions(
                    volumeWidth,
                    volumeHeight,
                    volumeDepth);

                int sliceCount = min(
                    (int)volumeDepth,
                    MAX_VOLUME_SLICES);

                if (sliceCount <= 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float3 maxLighting =
                    float3(0.0, 0.0, 0.0);

                float nearLighting = 0.0;
                float farLighting = 0.0;

                [loop]
                for (int slice = 0;
                     slice < MAX_VOLUME_SLICES;
                     ++slice)
                {
                    if (slice >= sliceCount)
                    {
                        break;
                    }

                    // 各奥行き層の中心を参照する。
                    float normalizedDepth =
                        ((float)slice + 0.5) /
                        (float)sliceCount;

                    float viewDistance =
                        normalizedDepth * _PointVolumeMaxDistance;

                    float3 lighting = SAMPLE_TEXTURE3D_LOD(
                        _PointLightVolumeTex,
                        sampler_LinearClamp,
                        float3(screenUv, normalizedDepth),
                        0).rgb;

                    // 全奥行きのRGB最大値を記録する。
                    maxLighting = max(
                        maxLighting,
                        lighting);

                    // ライト色に依存せず明るさを判定する。
                    float brightness = max(
                        lighting.r,
                        max(lighting.g, lighting.b));

                    if (viewDistance <= _PointLightDebugDistance)
                    {
                        nearLighting = max(
                            nearLighting,
                            brightness);
                    }
                    else
                    {
                        farLighting = max(
                            farLighting,
                            brightness);
                    }
                }

                // Mode 2: 3D照明の最大値を表示する。
                if (_PointLightDebugMode < 2.5)
                {
                    return float4(
                        saturate(
                            maxLighting * _PointLightDebugGain),
                        1.0);
                }

                // Mode 3: 赤=近距離、緑=遠距離。
                return float4(
                    saturate(
                        nearLighting * _PointLightDebugGain),
                    saturate(
                        farLighting * _PointLightDebugGain),
                    0.0,
                    1.0);
            }

            // ----------------------------------------------------
            // Fragment
            // ----------------------------------------------------

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ----------------------------------------------------
                // 1. Screen UV / Scene Color
                // ----------------------------------------------------

                float2 colorUv =
                    input.texcoord.xy;

                float2 depthUv =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    colorUv,
                    _BlitMipLevel);

                // Sceneビューの通常表示では重複適用しない。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return sceneColor;
                }

                // ----------------------------------------------------
                // 2. Debug Volume
                // ----------------------------------------------------

                // 3D照明を直接確認するデバッグ表示。
                // 通常のFog計算は実行しない。
                if (_PointLightDebugMode > 1.5)
                {
                    return half4(
                        GetPointLightDebugVolume(depthUv));
                }

                // ----------------------------------------------------
                // 3. Scene Depth
                // ----------------------------------------------------

                float rawDepth =
                    GetCompositeDepth(colorUv, depthUv);

                bool isSky =
                    IsSkyDepth(rawDepth);

                float deviceDepth = rawDepth;

                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawDepth);
                #endif

                // Skyでは無限遠の座標を復元しない。
                // 視線を求めるためNear Planeを使用する。
                if (isSky)
                {
                    #if UNITY_REVERSED_Z
                        deviceDepth = 1.0;
                    #else
                        deviceDepth = UNITY_NEAR_CLIP_VALUE;
                    #endif
                }

                // ----------------------------------------------------
                // 4. World Position
                // ----------------------------------------------------

                float3 surfacePositionWS =
                    ComputeWorldSpacePosition(
                        depthUv,
                        deviceDepth,
                        UNITY_MATRIX_I_VP);

                float3 cameraPositionWS =
                    _WorldSpaceCameraPos;

                float3 cameraForwardWS =
                    GetCameraForwardWS();

                // ----------------------------------------------------
                // 5. Ray Origin / Direction
                // ----------------------------------------------------

                float3 rayOriginWS;
                float3 rayDirection;
                float surfaceDistance;

                if (unity_OrthoParams.w > 0.5)
                {
                    // 平行投影では視線方向が全画素で一定。
                    rayDirection = cameraForwardWS;

                    float3 cameraToSurface =
                        surfacePositionWS - cameraPositionWS;

                    surfaceDistance = max(
                        dot(cameraToSurface, rayDirection),
                        0.0);

                    // 画素ごとのXY位置を維持し、
                    // カメラ前方平面からレイを開始する。
                    rayOriginWS =
                        surfacePositionWS -
                        rayDirection * surfaceDistance;
                }
                else
                {
                    // 透視投影ではカメラ位置からレイを開始する。
                    rayOriginWS = cameraPositionWS;

                    float3 cameraToSurface =
                        surfacePositionWS - rayOriginWS;

                    surfaceDistance =
                        length(cameraToSurface);

                    rayDirection =
                        cameraToSurface /
                        max(surfaceDistance, 0.0001);
                }

                // 表面までの距離が無効な場合は描画しない。
                if (!isSky && surfaceDistance < 0.0001)
                {
                    return sceneColor;
                }

                // ----------------------------------------------------
                // 6. Ray March Distance
                // ----------------------------------------------------

                // 空と通常の物体でFogの計算距離を切り替える。
                float rayLength = isSky
                    ? min(_SkyMaxDistance, _MaxDistance)
                    : min(surfaceDistance, _MaxDistance);

                rayLength = max(
                    rayLength,
                    0.0);

                float fogStartDistance = clamp(
                    _FogStartDistance,
                    0.0,
                    rayLength);

                float fogRayLength = max(
                    rayLength - fogStartDistance,
                    0.0);

                // 計算する霧がない場合は終了する。
                if (fogRayLength <= 0.0001)
                {
                    if (_PointLightDebugMode > 0.5)
                    {
                        return half4(0.0, 0.0, 0.0, 1.0);
                    }

                    return sceneColor;
                }

                // ----------------------------------------------------
                // 7. Adaptive Sample Count
                // ----------------------------------------------------

                // 1画素で実行できる最大回数。
                int maxSampleCount = clamp(
                    (int)round(_SampleCount),
                    8,
                    MAX_SAMPLE_COUNT);

                // 目標の標本間隔。
                float targetStepLength = max(
                    _RayStepLength,
                    0.01);

                // 計算距離から必要な標本数を決める。
                int sampleCount = clamp(
                    (int)ceil(fogRayLength / targetStepLength),
                    1,
                    maxSampleCount);

                // 必ずレイ全体を覆うよう間隔を計算する。
                float stepLength =
                    fogRayLength / (float)sampleCount;

                // ----------------------------------------------------
                // 8. Accumulation
                // ----------------------------------------------------

                // カメラまで届く背景光の割合。
                float transmittance = 1.0;

                // 全ライトの散乱光。
                float3 scattering =
                    float3(0.0, 0.0, 0.0);

                // Point Onlyデバッグ用の散乱光。
                float3 pointOnlyScattering =
                    float3(0.0, 0.0, 0.0);

                // ----------------------------------------------------
                // 9. Directional Light Preparation
                // ----------------------------------------------------

                // Directional Lightの方向と色は、
                // この画素のレイマーチング中は変化しない。
                Light directionalLight =
                    GetMainLight();

                float directionalCosTheta = clamp(
                    dot(
                        rayDirection,
                        directionalLight.direction),
                    -1.0,
                    1.0);

                // 異方性0を倍率1とした散乱方向の補正。
                // 以前の最低1倍の制限は設けない。
                float directionalPhase = min(
                    HenyeyGreenstein(
                        directionalCosTheta,
                        _Anisotropy) / INV_FOUR_PI,
                    4.0);

                // 影以外のDirectional Lightの寄与を事前計算。
                float3 directionalBaseScattering =
                    _FogColor.rgb *
                    directionalLight.color.rgb *
                    _LightIntensity *
                    directionalPhase;

                // Ambientはサンプル位置で変化しない。
                float3 ambientScattering =
                    _FogColor.rgb *
                    _AmbientIntensity;

                // Directionalの影計算が必要か。
                bool directionalEnabled =
                    _LightIntensity > 0.0001;

                // Point / Spotの計算が必要か。
                bool pointEnabled =
                    _PointLightVolumeEnabled > 0.5;

                // Point Onlyデバッグを使うか。
                bool pointDebugEnabled =
                    _PointLightDebugMode > 0.5 &&
                    _PointLightDebugMode < 1.5;

                // ----------------------------------------------------
                // 10. Ray March
                // ----------------------------------------------------

                [loop]
                for (int i = 0; i < MAX_SAMPLE_COUNT; ++i)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    // サンプル位置を計算する。
                    float distanceAlongRay =
                        fogStartDistance +
                        ((float)i + 0.5) * stepLength;

                    float3 samplePositionWS =
                        rayOriginWS +
                        rayDirection * distanceAlongRay;

                    // -----------------------------------------------
                    // Fog Density
                    // -----------------------------------------------

                    float density =
                        GetFogDensity(samplePositionWS);

                    // 霧が存在しない位置の計算を省略する。
                    if (density <= 0.000001)
                    {
                        continue;
                    }

                    // -----------------------------------------------
                    // Directional Light Scattering
                    // -----------------------------------------------

                    float3 directScattering =
                        float3(0.0, 0.0, 0.0);

                    if (directionalEnabled)
                    {
                        // 現在の位置でShadow Mapを参照する。
                        float4 shadowCoord =
                            TransformWorldToShadowCoord(
                                samplePositionWS);

                        float shadowAttenuation =
                            GetMainLight(
                                shadowCoord).shadowAttenuation;

                        // 影の強さを調整する。
                        shadowAttenuation = lerp(
                            1.0,
                            shadowAttenuation,
                            saturate(_DirectionalShadowStrength));

                        directScattering =
                            directionalBaseScattering *
                            shadowAttenuation;
                    }

                    // -----------------------------------------------
                    // Point / Spot Light Scattering
                    // -----------------------------------------------

                    float3 pointScattering =
                        float3(0.0, 0.0, 0.0);

                    if (pointEnabled)
                    {
                        // 3D格子と共通のカメラ前方距離。
                        float viewDistance = dot(
                            samplePositionWS - cameraPositionWS,
                            cameraForwardWS);

                        float3 pointLighting =
                            SamplePointLightScattering(
                                depthUv,
                                viewDistance);

                        pointScattering =
                            _FogColor.rgb *
                            pointLighting;
                    }

                    // -----------------------------------------------
                    // Total Scattering
                    // -----------------------------------------------

                    // DirectionalとAmbientは共通倍率を適用。
                    // Point / Spotは専用倍率で独立調整する。
                    float3 scatteredLight =
                        (ambientScattering + directScattering) *
                        _ScatteringIntensity +
                        pointScattering;

                    // -----------------------------------------------
                    // Beer-Lambert
                    // -----------------------------------------------

                    // この区間を通過する光の減衰率。
                    float extinction =
                        density * stepLength;

                    float stepTransmittance =
                        exp(-extinction);

                    // この区間で散乱する光の割合。
                    float scatteredAmount =
                        1.0 - stepTransmittance;

                    // 手前側の霧による遮蔽を反映する。
                    float scatteringWeight =
                        transmittance *
                        scatteredAmount;

                    // 全ライトの散乱光を蓄積する。
                    scattering +=
                        scatteringWeight *
                        scatteredLight;

                    // Point OnlyではPoint / Spotだけを蓄積する。
                    // Scattering Intensityを二重適用しない。
                    if (pointDebugEnabled)
                    {
                        pointOnlyScattering +=
                            scatteringWeight *
                            pointScattering;
                    }

                    // 次の標本に向けて透過率を更新する。
                    transmittance *= stepTransmittance;

                    // ほぼ完全に遮蔽されたら早期終了する。
                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }

                // ----------------------------------------------------
                // 11. Sky Fog Strength
                // ----------------------------------------------------

                float fogStrength = isSky
                    ? saturate(_SkyFogStrength)
                    : 1.0;

                // ----------------------------------------------------
                // 12. Debug Point Only
                // ----------------------------------------------------

                if (pointDebugEnabled)
                {
                    // 実際のFog積算を通したPoint / Spotの散乱光。
                    float3 debugPointLighting =
                        pointOnlyScattering *
                        fogStrength *
                        _PointLightDebugGain;

                    return half4(
                        saturate(debugPointLighting),
                        1.0);
                }

                // ----------------------------------------------------
                // 13. Final Composite
                // ----------------------------------------------------

                // 背景が暗くなる割合を調整する。
                // 初期値1では従来どおりの減衰になる。
                float appliedTransmittance = lerp(
                    1.0,
                    transmittance,
                    fogStrength *
                    saturate(_FogTransmittanceStrength));

                // 散乱光は背景の透過率とは独立して合成する。
                float3 appliedScattering =
                    scattering *
                    fogStrength;

                // 元の画面色と散乱光を合成する。
                float3 finalColor =
                    sceneColor.rgb *
                    appliedTransmittance +
                    appliedScattering;

                // HDRを維持して後続のBloomへ渡す。
                return half4(
                    finalColor,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}
