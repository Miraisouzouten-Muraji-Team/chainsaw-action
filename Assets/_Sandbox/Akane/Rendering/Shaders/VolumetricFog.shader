
Shader "Custom/VolumetricFog"
{
    Properties
    {
        // ============================================================
        // Pixel Rendering
        // ============================================================

        [HideInInspector] _PixelNearTex("Near Color", 2D) = "black" {}
        [HideInInspector] _PixelMiddleTex("Middle Color", 2D) = "black" {}
        [HideInInspector] _PixelFarTex("Far Color", 2D) = "black" {}

        [HideInInspector] _PixelNearDepthTex("Near Depth", 2D) = "white" {}
        [HideInInspector] _PixelMiddleDepthTex("Middle Depth", 2D) = "white" {}
        [HideInInspector] _PixelFarDepthTex("Far Depth", 2D) = "white" {}

        [HideInInspector] _PixelEffectEnabled("Pixel Effect Enabled", Float) = 0

        // ============================================================
        // Fog
        // ============================================================

        // 霧の色と基本密度。
        _FogColor("Fog Color", Color) = (0.6, 0.65, 0.7, 1.0)
        _Density("Density", Range(0.0, 0.2)) = 0.02

        // 霧を計算する最大距離と開始距離。
        // 開始距離を設けることで、カメラ周辺が靄っぽくなるのを防ぐ。
        _MaxDistance("Max Distance", Float) = 100.0
        _FogStartDistance("Fog Start Distance", Float) = 5.0

        // ============================================================
        // Height Fog
        // ============================================================

        // 霧の基準高さ。
        _FogHeight("Fog Height", Float) = 0.0

        // 基準高さより上に行くほど霧を薄くする。
        _HeightFalloff("Height Falloff", Range(0.0, 1.0)) = 0.02

        // ============================================================
        // Sky Fog
        // ============================================================

        // Sky方向のレイマーチ最大距離。
        // 通常のMaxDistanceより短くして過剰な霧を抑える。
        _SkyMaxDistance("Sky Max Distance", Float) = 30.0

        // Skyに適用する霧の強さ。
        // 0 = 適用しない / 1 = 通常の霧と同じ。
        _SkyFogStrength("Sky Fog Strength", Range(0.0, 1.0)) = 0.4

        // ============================================================
        // Lighting
        // ============================================================

        // Directional Lightによる散乱光。
        _LightIntensity("Light Intensity", Range(0.0, 10.0)) = 0.5

        // 影の中にも存在する環境光。
        _AmbientIntensity("Ambient Intensity", Range(0.0, 1.0)) = 0.0

        // 散乱光全体の倍率。
        _ScatteringIntensity("Scattering Intensity", Range(0.0, 5.0)) = 1.5

        // ============================================================
        // God Ray
        // ============================================================

        // Henyey-Greensteinの異方性。
        // 正の値ほど前方散乱を強調する。
        _Anisotropy("Anisotropy", Range(-0.9, 0.9)) = 0.5

        // ============================================================
        // Ray March
        // ============================================================

        // レイマーチングのサンプル数。
        _SampleCount("Sample Count", Range(8, 64)) = 32
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        // Full Screen Pass用。
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

            // ========================================================
            // Shadow Variants
            // ========================================================

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // ========================================================
            // Includes
            // ========================================================

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ========================================================
            // Constants
            // ========================================================

            #define MAX_SAMPLE_COUNT 64

            // INV_FOUR_PIはインクルード先の定義を使用する。

            // ========================================================
            // Textures
            // ========================================================

            TEXTURE2D(_PixelNearTex);
            TEXTURE2D(_PixelMiddleTex);
            TEXTURE2D(_PixelFarTex);

            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);

            // ========================================================
            // Material Parameters
            // ========================================================

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

                float _Anisotropy;
                float _SampleCount;

                float _PixelEffectEnabled;

            CBUFFER_END

            // ========================================================
            // Fog Density
            //
            // 指定したワールド座標における霧密度を計算する。
            // 高い位置ほど指数関数的に薄くする。
            // ========================================================

            float GetFogDensity(float3 positionWS)
            {
                float heightDifference = max(
                    positionWS.y - _FogHeight,
                    0.0);

                float heightDensity = exp(
                    -heightDifference * _HeightFalloff);

                return max(_Density, 0.0) * heightDensity;
            }

            // ========================================================
            // Henyey-Greenstein Phase Function
            //
            // 光が視線方向にどの程度散乱するかを計算する。
            // ========================================================

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

                return INV_FOUR_PI
                    * (1.0 - g2)
                    / pow(denominator, 1.5);
            }

            // ========================================================
            // Sky Depth
            //
            // Depth Bufferの値からSkyを判定する。
            // Reversed Zと通常のZ Bufferの両方に対応する。
            // ========================================================

            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // ========================================================
            // Composite Depth
            //
            // Main CameraのDepthを基準とし、
            // Pixel Layer Compositeと同じ順番で深度を上書きする。
            //
            // 合成順:
            // Main → Far → Middle → Near
            //
            // 各レイヤーに描画物が存在し、
            // かつSky以外の深度がある場合のみ上書きする。
            // ========================================================

            float GetCompositeDepth(
                float2 colorUv,
                float2 depthUv)
            {
                // Main Cameraの深度を取得する。
                float result = SampleSceneDepth(depthUv);

                // Pixel Renderingが無効ならMain Cameraの深度を使用。
                if (_PixelEffectEnabled < 0.5)
                {
                    return result;
                }

                // ----------------------------------------------------
                // Far
                // ----------------------------------------------------

                float farAlpha = SAMPLE_TEXTURE2D(
                    _PixelFarTex,
                    sampler_PointClamp,
                    colorUv).a;

                if (farAlpha > 0.01)
                {
                    float farDepth = SAMPLE_TEXTURE2D(
                        _PixelFarDepthTex,
                        sampler_PointClamp,
                        colorUv).r;

                    if (!IsSkyDepth(farDepth))
                    {
                        result = farDepth;
                    }
                }

                // ----------------------------------------------------
                // Middle
                // ----------------------------------------------------

                float middleAlpha = SAMPLE_TEXTURE2D(
                    _PixelMiddleTex,
                    sampler_PointClamp,
                    colorUv).a;

                if (middleAlpha > 0.01)
                {
                    float middleDepth = SAMPLE_TEXTURE2D(
                        _PixelMiddleDepthTex,
                        sampler_PointClamp,
                        colorUv).r;

                    if (!IsSkyDepth(middleDepth))
                    {
                        result = middleDepth;
                    }
                }

                // ----------------------------------------------------
                // Near
                // ----------------------------------------------------

                float nearAlpha = SAMPLE_TEXTURE2D(
                    _PixelNearTex,
                    sampler_PointClamp,
                    colorUv).a;

                if (nearAlpha > 0.01)
                {
                    float nearDepth = SAMPLE_TEXTURE2D(
                        _PixelNearDepthTex,
                        sampler_PointClamp,
                        colorUv).r;

                    if (!IsSkyDepth(nearDepth))
                    {
                        result = nearDepth;
                    }
                }

                return result;
            }

            // ========================================================
            // Fragment Shader
            // ========================================================

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ====================================================
                // 1. Screen UV
                // ====================================================

                // BlitTextureのカラー取得用。
                float2 colorUv = input.texcoord.xy;

                // Depth取得・ワールド座標復元用。
                float2 depthUv =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                // ====================================================
                // 2. Scene Color
                // ====================================================

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    colorUv,
                    _BlitMipLevel);

                // ====================================================
                // 3. Scene Depth
                // ====================================================

                float rawDepth = GetCompositeDepth(
                    colorUv,
                    depthUv);

                float deviceDepth = rawDepth;

                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawDepth);
                #endif

                // ====================================================
                // 4. Surface World Position
                // ====================================================

                float3 surfacePositionWS = ComputeWorldSpacePosition(
                    depthUv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);

                float3 cameraPositionWS = _WorldSpaceCameraPos;

                // ====================================================
                // 5. Camera → Surface
                // ====================================================

                float3 cameraToSurface =
                    surfacePositionWS - cameraPositionWS;

                float surfaceDistance = length(cameraToSurface);

                // 距離がほぼ0の場合は霧を適用しない。
                if (surfaceDistance < 0.0001)
                {
                    return sceneColor;
                }

                float3 rayDirection =
                    cameraToSurface / surfaceDistance;

                // ====================================================
                // 6. Sky判定
                // ====================================================

                bool isSky = IsSkyDepth(rawDepth);

                // ====================================================
                // 7. Ray March Distance
                //
                // 通常のオブジェクトは表面まで、
                // SkyはSkyMaxDistanceまで計算する。
                // ====================================================

                float rayLength;

                if (isSky)
                {
                    rayLength = min(
                        _SkyMaxDistance,
                        _MaxDistance);
                }
                else
                {
                    rayLength = min(
                        surfaceDistance,
                        _MaxDistance);
                }

                rayLength = max(rayLength, 0.0);

                // ====================================================
                // 8. Fog Start Distance
                //
                // カメラ周辺が霧で覆われないように、
                // FogStartDistanceより先だけを計算する。
                // ====================================================

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

                // ====================================================
                // 9. Sample Count
                // ====================================================

                int sampleCount = clamp(
                    (int)round(_SampleCount),
                    8,
                    MAX_SAMPLE_COUNT);

                float stepLength =
                    fogRayLength / max((float)sampleCount, 1.0);

                // ====================================================
                // 10. Accumulation
                // ====================================================

                // カメラまで届く元の光の割合。
                float transmittance = 1.0;

                // 霧からカメラに届く散乱光。
                float3 scattering = float3(0.0, 0.0, 0.0);

                // ====================================================
                // 11. Ray March
                // ====================================================

                [loop]
                for (int i = 0; i < MAX_SAMPLE_COUNT; ++i)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    // ------------------------------------------------
                    // Sample Position
                    // ------------------------------------------------

                    float distanceAlongRay =
                        fogStartDistance
                        + ((float)i + 0.5) * stepLength;

                    float3 samplePositionWS =
                        cameraPositionWS
                        + rayDirection * distanceAlongRay;

                    // ------------------------------------------------
                    // Fog Density
                    // ------------------------------------------------

                    float density = GetFogDensity(samplePositionWS);

                    if (density <= 0.000001)
                    {
                        continue;
                    }

                    // ------------------------------------------------
                    // Main Light Shadow
                    //
                    // 1 = 光が届いている
                    // 0 = 遮蔽物による影
                    // ------------------------------------------------

                    float4 shadowCoord = TransformWorldToShadowCoord(
                        samplePositionWS);

                    Light mainLight = GetMainLight(shadowCoord);

                    float shadowAttenuation =
                        mainLight.shadowAttenuation;

                    // ------------------------------------------------
                    // Forward Scattering
                    // ------------------------------------------------

                    float cosTheta = clamp(
                        dot(rayDirection, mainLight.direction),
                        -1.0,
                        1.0);

                    float hgPhase = HenyeyGreenstein(
                        cosTheta,
                        _Anisotropy);

                    // 異方性0のときを基準倍率1とする。
                    float phaseRatio = hgPhase / INV_FOUR_PI;

                    // 前方散乱を強調する。
                    float phaseBoost = max(phaseRatio, 1.0);

                    // 極端な白飛びを防ぐ。
                    phaseBoost = min(phaseBoost, 8.0);

                    // ------------------------------------------------
                    // Beer-Lambert
                    // ------------------------------------------------

                    float extinction = density * stepLength;

                    float stepTransmittance = exp(-extinction);

                    float scatteredAmount =
                        1.0 - stepTransmittance;

                    // ------------------------------------------------
                    // Ambient Scattering
                    // ------------------------------------------------

                    float3 ambientLight =
                        _FogColor.rgb * _AmbientIntensity;

                    // ------------------------------------------------
                    // Directional Light Scattering
                    //
                    // Directional Lightなので
                    // distanceAttenuationは使用しない。
                    // ------------------------------------------------

                    float3 directScattering =
                        _FogColor.rgb
                        * mainLight.color.rgb
                        * shadowAttenuation
                        * _LightIntensity
                        * phaseBoost;

                    // ------------------------------------------------
                    // Total Scattered Light
                    // ------------------------------------------------

                    float3 scatteredLight =
                        (ambientLight + directScattering)
                        * _ScatteringIntensity;

                    // ------------------------------------------------
                    // Accumulate Scattering
                    // ------------------------------------------------

                    scattering +=
                        transmittance
                        * scatteredAmount
                        * scatteredLight;

                    // ------------------------------------------------
                    // Update Transmittance
                    // ------------------------------------------------

                    transmittance *= stepTransmittance;

                    // 十分に減衰した場合は計算を終了する。
                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }

                // ====================================================
                // 12. Sky Fog Strength
                //
                // Skyに対してだけ霧の強さを調整する。
                // 通常のオブジェクトには1.0を使用する。
                // ====================================================

                float fogStrength = isSky ? saturate(_SkyFogStrength) : 1.0;

                // ====================================================
                // 13. Apply Fog Strength
                // ====================================================

                // 0 = 元画面を完全に残す
                // 1 = 計算した透過率をそのまま適用する
                float appliedTransmittance = lerp(1.0, transmittance, fogStrength);

                float3 appliedScattering = scattering * fogStrength;

                // ====================================================
                // 14. Final Composite
                // ====================================================

                float3 finalColor =
                    sceneColor.rgb * appliedTransmittance
                    + appliedScattering;

                // HDR値をBloomへ渡すためsaturateはしない。
                return half4(
                    finalColor,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}
