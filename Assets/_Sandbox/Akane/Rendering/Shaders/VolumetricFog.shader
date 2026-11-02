Shader "Custom/VolumetricFog"
{
    Properties
    {
        // Pixel Rendering
        [HideInInspector] _PixelNearTex("Near Color", 2D) = "black" {}
        [HideInInspector] _PixelMiddleTex("Middle Color", 2D) = "black" {}
        [HideInInspector] _PixelFarTex("Far Color", 2D) = "black" {}

        [HideInInspector] _PixelNearDepthTex("Near Depth", 2D) = "white" {}
        [HideInInspector] _PixelMiddleDepthTex("Middle Depth", 2D) = "white" {}
        [HideInInspector] _PixelFarDepthTex("Far Depth", 2D) = "white" {}

        [HideInInspector] _PixelCompositeDepthTex("Composite Depth", 2D) = "black" {}
        [HideInInspector] _SceneNormalPreviewEnabled("Scene Normal Preview Enabled", Float) = 0
        _PixelEffectEnabled("Pixel Effect Enabled", Float) = 0

        // Fog
        _FogColor("Fog Color", Color) = (0.6, 0.65, 0.7, 1.0)
        _Density("Density", Range(0.0, 0.2)) = 0.02
        _MaxDistance("Max Distance", Float) = 100.0
        _FogStartDistance("Fog Start Distance", Float) = 5.0

        // Height Fog
        _FogHeight("Fog Height", Float) = 0.0
        _HeightFalloff("Height Falloff", Range(0.0, 1.0)) = 0.02

        // Sky Fog
        _SkyMaxDistance("Sky Max Distance", Float) = 30.0
        _SkyFogStrength("Sky Fog Strength", Range(0.0, 1.0)) = 0.4

        // Directional Light
        _LightIntensity("Light Intensity", Range(0.0, 10.0)) = 0.5
        _AmbientIntensity("Ambient Intensity", Range(0.0, 1.0)) = 0.0
        _ScatteringIntensity("Scattering Intensity", Range(0.0, 5.0)) = 1.5

        // Point Light
        [Toggle] _PointLightVolumeEnabled("Point Light Volume Enabled", Float) = 0
        _PointLightScatteringIntensity("Point Light Scattering Intensity", Range(0.0, 10.0)) = 1.0

        // God Ray
        _Anisotropy("Anisotropy", Range(-0.9, 0.9)) = 0.5

        // Ray March
        _SampleCount("Sample Count", Range(8, 64)) = 32
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

            // Directional LightのShadow Mapに対応。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_SAMPLE_COUNT 64

            // 既存のPixel Capture用Texture。
            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);

            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);

            // Near / Middle / Farを統合したDepth。
            TEXTURE2D(_PixelCompositeDepthTex);

            // Compute Shaderが生成するPoint Lightの散乱光。
            TEXTURE3D(_PointLightVolumeTex);

            // Renderer Featureから設定する最大距離。
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

                float _LightIntensity;
                float _AmbientIntensity;
                float _ScatteringIntensity;

                float _PointLightVolumeEnabled;
                float _PointLightScatteringIntensity;

                float _Anisotropy;
                float _SampleCount;

                float _PixelEffectEnabled;
                float _SceneNormalPreviewEnabled;
            CBUFFER_END

            /// <summary>
            /// ワールド座標における霧の密度を計算する。
            /// </summary>
            float GetFogDensity(float3 positionWS)
            {
                float heightDifference = max(
                    positionWS.y - _FogHeight,
                    0.0);

                float heightDensity = exp(
                    -heightDifference * _HeightFalloff);

                return max(_Density, 0.0) * heightDensity;
            }

            /// <summary>
            /// 光の散乱方向による強さを計算する。
            /// </summary>
            float HenyeyGreenstein(float cosTheta, float anisotropy)
            {
                float g = clamp(anisotropy, -0.9, 0.9);
                float g2 = g * g;

                float denominator = max(
                    1.0 + g2 - 2.0 * g * cosTheta,
                    0.0001);

                return INV_FOUR_PI
                    * (1.0 - g2)
                    / pow(denominator, 1.5);
            }

            /// <summary>
            /// Depth Bufferの値がSkyに相当するか判定する。
            /// </summary>
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            /// <summary>
            /// Pixel ON/OFFに対応したDepthを取得する。
            /// </summary>
            float GetCompositeDepth(float2 colorUv, float2 depthUv)
            {
                // Pixel OFFではURP標準のDepthを使用する。
                if (_PixelEffectEnabled < 0.5)
                {
                    return SampleSceneDepth(depthUv);
                }

                // Pixel ONでは統合Depthを使用する。
                return SAMPLE_TEXTURE2D(
                    _PixelCompositeDepthTex,
                    sampler_PointClamp,
                    colorUv).r;
            }

            /// <summary>
            /// カメラの正面方向をワールド空間で取得する。
            /// </summary>
            float3 GetCameraForwardWS()
            {
                return normalize(
                    mul(
                        (float3x3)UNITY_MATRIX_I_V,
                        float3(0.0, 0.0, -1.0)));
            }

            /// <summary>
            /// Point Lightの3D照明テクスチャを参照する。
            /// 平行投影時のみ使用する。
            /// </summary>
            float3 SamplePointLightScattering(
                float2 screenUv,
                float viewDistance)
            {
                if (_PointLightVolumeEnabled < 0.5)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // 現在のCompute Shaderは平行投影専用。
                if (unity_OrthoParams.w < 0.5)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // GPUで生成した3D格子の最大距離。
                float maxDistance = _PointVolumeMaxDistance;

                if (maxDistance <= 0.0001)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // 最大距離より先はPoint Lightを適用しない。
                if (viewDistance < 0.0 || viewDistance > maxDistance)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                // 画面座標と奥行きを3Dテクスチャ座標に変換する。
                float normalizedDepth = saturate(
                    viewDistance / maxDistance);

                float3 volumeUv = float3(
                    saturate(screenUv),
                    normalizedDepth);

                // 3D格子の照明結果を補間して取得する。
                float3 pointLighting = SAMPLE_TEXTURE3D_LOD(
                    _PointLightVolumeTex,
                    sampler_LinearClamp,
                    volumeUv,
                    0).rgb;

                return pointLighting * _PointLightScatteringIntensity;
            }

            /// <summary>
            /// 既存のDirectional LightとPoint Lightの散乱光を計算する。
            /// </summary>
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ----------------------------------------------------
                // 1. Screen UV / Scene Color
                // ----------------------------------------------------

                float2 colorUv = input.texcoord.xy;
                float2 depthUv = input.positionCS.xy / _ScaledScreenParams.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    colorUv,
                    _BlitMipLevel);

                // Sceneビューの通常プレビューでは重複適用しない。
                if (_SceneNormalPreviewEnabled > 0.5)
                {
                    return sceneColor;
                }

                // ----------------------------------------------------
                // 2. Scene Depth
                // ----------------------------------------------------

                float rawDepth = GetCompositeDepth(colorUv, depthUv);
                bool isSky = IsSkyDepth(rawDepth);

                float deviceDepth = rawDepth;

                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawDepth);
                #endif

                // ----------------------------------------------------
                // 3. Surface World Position
                // ----------------------------------------------------

                float3 surfacePositionWS = ComputeWorldSpacePosition(
                    depthUv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);

                float3 cameraPositionWS = _WorldSpaceCameraPos;
                float3 cameraForwardWS = GetCameraForwardWS();

                // ----------------------------------------------------
                // 4. Ray Origin / Direction
                // ----------------------------------------------------

                float3 rayOriginWS;
                float3 rayDirection;
                float surfaceDistance;

                if (unity_OrthoParams.w > 0.5)
                {
                    // 平行投影では全ピクセルの視線方向が同じ。
                    rayDirection = cameraForwardWS;

                    float3 cameraToSurface =
                        surfacePositionWS - cameraPositionWS;

                    surfaceDistance = max(
                        dot(cameraToSurface, rayDirection),
                        0.0);

                    // カメラの前方平面上にレイ開始位置を配置。
                    // ピクセルごとのXY位置は保持する。
                    rayOriginWS =
                        surfacePositionWS
                        - rayDirection * surfaceDistance;
                }
                else
                {
                    // 透視投影ではカメラ位置から表面へ向かう。
                    rayOriginWS = cameraPositionWS;

                    float3 cameraToSurface =
                        surfacePositionWS - rayOriginWS;

                    surfaceDistance = length(cameraToSurface);

                    rayDirection = cameraToSurface /
                        max(surfaceDistance, 0.0001);
                }

                if (surfaceDistance < 0.0001)
                {
                    return sceneColor;
                }

                // ----------------------------------------------------
                // 5. Ray March Distance
                // ----------------------------------------------------

                // Skyは専用の距離、それ以外は表面まで計算する。
                float rayLength = isSky
                    ? min(_SkyMaxDistance, _MaxDistance)
                    : min(surfaceDistance, _MaxDistance);

                rayLength = max(rayLength, 0.0);

                // カメラ周辺の霧を除外する。
                float fogStartDistance = clamp(
                    _FogStartDistance,
                    0.0,
                    rayLength);

                float fogRayLength = max(
                    rayLength - fogStartDistance,
                    0.0);

                if (fogRayLength <= 0.0001)
                {
                    return sceneColor;
                }

                // ----------------------------------------------------
                // 6. Sample Count
                // ----------------------------------------------------

                int sampleCount = clamp(
                    (int)round(_SampleCount),
                    8,
                    MAX_SAMPLE_COUNT);

                float stepLength = fogRayLength / sampleCount;

                // ----------------------------------------------------
                // 7. Accumulation
                // ----------------------------------------------------

                // カメラまで届く元の光の割合。
                float transmittance = 1.0;

                // 霧からカメラへ届く散乱光。
                float3 scattering = float3(0.0, 0.0, 0.0);

                // ----------------------------------------------------
                // 8. Ray March
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
                        fogStartDistance
                        + ((float)i + 0.5) * stepLength;

                    float3 samplePositionWS =
                        rayOriginWS
                        + rayDirection * distanceAlongRay;

                    // -----------------------------------------------
                    // Fog Density
                    // -----------------------------------------------

                    float density = GetFogDensity(samplePositionWS);

                    if (density <= 0.000001)
                    {
                        continue;
                    }

                    // -----------------------------------------------
                    // Directional Light / Shadow
                    // -----------------------------------------------

                    float4 shadowCoord =
                        TransformWorldToShadowCoord(samplePositionWS);

                    Light mainLight = GetMainLight(shadowCoord);

                    float shadowAttenuation =
                        mainLight.shadowAttenuation;

                    // -----------------------------------------------
                    // Directional Light Scattering
                    // -----------------------------------------------

                    float cosTheta = clamp(
                        dot(rayDirection, mainLight.direction),
                        -1.0,
                        1.0);

                    float hgPhase = HenyeyGreenstein(
                        cosTheta,
                        _Anisotropy);

                    // 異方性0の散乱量を基準倍率1として扱う。
                    float phaseRatio = hgPhase / INV_FOUR_PI;
                    float phaseBoost = clamp(
                        max(phaseRatio, 1.0),
                        1.0,
                        8.0);

                    float3 directScattering =
                        _FogColor.rgb
                        * mainLight.color.rgb
                        * shadowAttenuation
                        * _LightIntensity
                        * phaseBoost;

                    // -----------------------------------------------
                    // Point Light Scattering
                    // -----------------------------------------------

                    // カメラの前方向に沿ったサンプル距離。
                    // 平行投影の3D格子の奥行きと対応する。
                    float viewDistance = dot(
                        samplePositionWS - cameraPositionWS,
                        cameraForwardWS);

                    // GPUで計算したPoint Lightの照明を取得。
                    float3 pointLighting = SamplePointLightScattering(
                        depthUv,
                        viewDistance);

                    // 霧の色をPoint Lightの散乱光に適用する。
                    float3 pointScattering =
                        _FogColor.rgb * pointLighting;

                    // -----------------------------------------------
                    // Ambient Scattering
                    // -----------------------------------------------

                    float3 ambientLight =
                        _FogColor.rgb * _AmbientIntensity;

                    // -----------------------------------------------
                    // Total Scattering
                    // -----------------------------------------------

                    float3 scatteredLight =
                        (
                            ambientLight
                            + directScattering
                            + pointScattering
                        )
                        * _ScatteringIntensity;

                    // -----------------------------------------------
                    // Beer-Lambert
                    // -----------------------------------------------

                    float extinction = density * stepLength;
                    float stepTransmittance = exp(-extinction);
                    float scatteredAmount = 1.0 - stepTransmittance;

                    // 霧からカメラへ届く光を蓄積する。
                    scattering +=
                        transmittance
                        * scatteredAmount
                        * scatteredLight;

                    // 次のサンプルまでの透過率を更新する。
                    transmittance *= stepTransmittance;

                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }

                // ----------------------------------------------------
                // 9. Sky Fog Strength
                // ----------------------------------------------------

                float fogStrength = isSky
                    ? saturate(_SkyFogStrength)
                    : 1.0;

                float appliedTransmittance = lerp(
                    1.0,
                    transmittance,
                    fogStrength);

                float3 appliedScattering =
                    scattering * fogStrength;

                // ----------------------------------------------------
                // 10. Final Composite
                // ----------------------------------------------------

                float3 finalColor =
                    sceneColor.rgb * appliedTransmittance
                    + appliedScattering;

                // HDR値を保持し、後続のBloomに渡す。
                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
