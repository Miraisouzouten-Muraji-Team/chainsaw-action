Shader "Custom/VolumetricFog"
{
    Properties
    {
        // 霧そのものの色
        _FogColor(
            "Fog Color",
            Color) = (0.5, 0.55, 0.6, 1.0)

        // 霧の濃さ
        _Density(
            "Density",
            Range(0.0, 0.2)) = 0.015

        // カメラからどこまで霧を計算するか
        _MaxDistance(
            "Max Distance",
            Float) = 50.0

        // この高さを基準に霧を配置する
        _FogHeight(
            "Fog Height",
            Float) = 0.0

        // 高くなるにつれて霧をどれくらい薄くするか
        _HeightFalloff(
            "Height Falloff",
            Range(0.0, 1.0)) = 0.05

        // Directional Lightによる霧の明るさ
        _LightIntensity(
            "Light Intensity",
            Range(0.0, 5.0)) = 0.5

        // 影になっている場所などにも残る最低限の明るさ
        _AmbientIntensity(
            "Ambient Intensity",
            Range(0.0, 1.0)) = 0.1

        // 最終的な散乱光全体の強さ
        _ScatteringIntensity(
            "Scattering Intensity",
            Range(0.0, 2.0)) = 1.0
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

            // Main Light Shadow
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            // Soft Shadow
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // レイマーチ回数。
            // まずは32。
            // 後で低解像度化するなら増やす余地あり。
            #define SAMPLE_COUNT 32

            CBUFFER_START(UnityPerMaterial)

                float4 _FogColor;

                float _Density;
                float _MaxDistance;

                float _FogHeight;
                float _HeightFalloff;

                float _LightIntensity;
                float _AmbientIntensity;
                float _ScatteringIntensity;

            CBUFFER_END

            // ------------------------------------------------------------
            // 指定したワールド座標における霧の密度を取得
            // ------------------------------------------------------------
            float GetFogDensity(float3 positionWS)
            {
                // FogHeightより上へ行くほど薄くする。
                float heightDifference =
                    max(
                        positionWS.y - _FogHeight,
                        0.0);

                float heightDensity =
                    exp(
                        -heightDifference *
                        _HeightFalloff);

                return
                    max(_Density, 0.0) *
                    heightDensity;
            }

            // ------------------------------------------------------------
            // Fragment Shader
            // ------------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv =
                    input.texcoord.xy;

                // ========================================================
                // 1. 現在のシーンカラーを取得
                // ========================================================

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv,
                        _BlitMipLevel);

                // ========================================================
                // 2. Depthから表面のワールド座標を復元
                // ========================================================

                float rawDepth =
                    SampleSceneDepth(uv);

                float3 surfacePositionWS =
                    ComputeWorldSpacePosition(
                        uv,
                        rawDepth,
                        UNITY_MATRIX_I_VP);

                float3 cameraPositionWS =
                    _WorldSpaceCameraPos;

                float3 cameraToSurface =
                    surfacePositionWS -
                    cameraPositionWS;

                float surfaceDistance =
                    length(cameraToSurface);

                // 数値不安定を防ぐ
                if (surfaceDistance < 0.0001)
                {
                    return sceneColor;
                }

                float3 rayDirection =
                    cameraToSurface /
                    surfaceDistance;

                // オブジェクトまでの距離、
                // またはMaxDistanceまでしか計算しない。
                float rayLength =
                    min(
                        surfaceDistance,
                        _MaxDistance);

                float stepLength =
                    rayLength /
                    SAMPLE_COUNT;

                // ========================================================
                // 3. Volumetric Fogの積算
                // ========================================================

                // カメラまでどれくらい光が残っているか。
                // 1 = 全部通る
                // 0 = 完全に霧で遮られる
                float transmittance =
                    1.0;

                // 霧によってカメラ方向へ散乱した光
                float3 scattering =
                    float3(
                        0.0,
                        0.0,
                        0.0);

                [loop]
                for (
                    int i = 0;
                    i < SAMPLE_COUNT;
                    ++i)
                {
                    // 各区間の中央をサンプリングする。
                    float distanceAlongRay =
                        (
                            (float)i +
                            0.5
                        )
                        * stepLength;

                    float3 samplePositionWS =
                        cameraPositionWS +
                        rayDirection *
                        distanceAlongRay;

                    // ----------------------------------------------------
                    // 霧密度
                    // ----------------------------------------------------

                    float density =
                        GetFogDensity(
                            samplePositionWS);

                    // 密度0なら計算不要
                    if (density <= 0.00001)
                    {
                        continue;
                    }

                    // ----------------------------------------------------
                    // Main Directional Light + Shadow
                    // ----------------------------------------------------

                    float4 shadowCoord =
                        TransformWorldToShadowCoord(
                            samplePositionWS);

                    Light mainLight =
                        GetMainLight(
                            shadowCoord);

                    float shadowAttenuation =
                        mainLight.shadowAttenuation;

                    float distanceAttenuation =
                        mainLight.distanceAttenuation;

                    // ----------------------------------------------------
                    // この1ステップでどれだけ光が減衰するか
                    // Beer-Lambert
                    // ----------------------------------------------------

                    float extinction =
                        density *
                        stepLength;

                    float stepTransmittance =
                        exp(
                            -extinction);

                    // この区間で霧に吸収・散乱された割合
                    float scatteredAmount =
                        1.0 -
                        stepTransmittance;

                    // ----------------------------------------------------
                    // Directional Light
                    // ----------------------------------------------------

                    float3 directLight =
                        mainLight.color.rgb *
                        shadowAttenuation *
                        distanceAttenuation *
                        _LightIntensity;

                    // ----------------------------------------------------
                    // 環境光
                    // ----------------------------------------------------

                    float3 ambientLight =
                        _FogColor.rgb *
                        _AmbientIntensity;

                    // ----------------------------------------------------
                    // 散乱光
                    //
                    // 修正前:
                    //
                    // FogColor + Light
                    //
                    // ではなく、
                    //
                    // FogColor × Light
                    //
                    // とする。
                    // ----------------------------------------------------

                    float3 directScattering =
                        _FogColor.rgb *
                        directLight;

                    float3 scatteredLight =
                        (
                            ambientLight +
                            directScattering
                        )
                        * _ScatteringIntensity;

                    // ----------------------------------------------------
                    // カメラまで届く散乱光を積算
                    // ----------------------------------------------------

                    scattering +=
                        transmittance *
                        scatteredAmount *
                        scatteredLight;

                    // ----------------------------------------------------
                    // 次のステップへ向けて透過率を更新
                    // ----------------------------------------------------

                    transmittance *=
                        stepTransmittance;

                    // ほぼ何も見えなくなったら終了。
                    // 無駄なレイマーチを減らす。
                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }

                // ========================================================
                // 4. 元のシーンと霧を合成
                // ========================================================

                // 元画面:
                // 霧によって減衰
                //
                // +
                //
                // 霧から届いた散乱光
                float3 finalColor =
                    sceneColor.rgb *
                    transmittance +
                    scattering;

                // HDRをBloomなどへ渡したいので
                // saturate()はしない。
                return half4(
                    finalColor,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}
