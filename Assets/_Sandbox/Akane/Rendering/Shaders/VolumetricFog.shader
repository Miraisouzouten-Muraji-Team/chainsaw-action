Shader "Custom/VolumetricFog"
{
    Properties
    {
        // ============================================================
        // Fog
        // ============================================================

        // 霧そのものの色。
        _FogColor(
            "Fog Color",
            Color) = (0.6, 0.65, 0.7, 1.0)

        // 霧の基本密度。
        // 大きくするほど画面全体の霧が濃くなる。
        _Density(
            "Density",
            Range(0.0, 0.2)) = 0.02

        // カメラから何m先まで霧を計算するか。
        _MaxDistance(
            "Max Distance",
            Float) = 100.0

        // 霧の基準となる高さ。
        _FogHeight(
            "Fog Height",
            Float) = 0.0

        // Fog Heightより上に行くほど
        // 霧をどの程度薄くするか。
        //
        // 0:
        //   高さに関係なく一定
        //
        // 大きくする:
        //   地面付近に霧が集まる
        _HeightFalloff(
            "Height Falloff",
            Range(0.0, 1.0)) = 0.02


        // ============================================================
        // Lighting
        // ============================================================

        // Directional Lightによって照らされた
        // 霧の明るさ。
        _LightIntensity(
            "Light Intensity",
            Range(0.0, 10.0)) = 3.0

        // 光が当たっていない場所にも残る
        // 最低限の霧の明るさ。
        //
        // God Rayを強く見せたい場合は小さめにする。
        _AmbientIntensity(
            "Ambient Intensity",
            Range(0.0, 1.0)) = 0.02

        // 散乱光全体の倍率。
        _ScatteringIntensity(
            "Scattering Intensity",
            Range(0.0, 5.0)) = 1.5


        // ============================================================
        // God Ray
        // ============================================================

        // 前方散乱の強さ。
        //
        // 0:
        //   方向による強調なし
        //
        // 0.5 ～ 0.8:
        //   太陽方向を見るほどGod Rayが強くなる
        //
        // 今回は既存God Rayを弱くせず、
        // 強調側にだけ使用する。
        _Anisotropy(
            "Anisotropy",
            Range(-0.9, 0.9)) = 0.0


        // ============================================================
        // Ray March
        // ============================================================

        // レイマーチ回数。
        //
        // 多い:
        //   滑らか
        //   重い
        //
        // 少ない:
        //   軽い
        //   縞が出やすい
        _SampleCount(
            "Sample Count",
            Range(8, 64)) = 32
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


            // ========================================================
            // Main Light Shadow
            // ========================================================

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH


            // ========================================================
            // URP
            // ========================================================

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // Full Screen Passで使用する
            // Vert / Varyings / _BlitTextureなど。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


            // ========================================================
            // Constants
            // ========================================================

            #define MAX_SAMPLE_COUNT 64

            // Henyey-Greensteinで使う 1 / (4π)
            #define INV_FOUR_PI 0.07957747


            // ========================================================
            // Material Parameters
            // ========================================================

            CBUFFER_START(UnityPerMaterial)

                float4 _FogColor;

                float _Density;
                float _MaxDistance;

                float _FogHeight;
                float _HeightFalloff;

                float _LightIntensity;
                float _AmbientIntensity;
                float _ScatteringIntensity;

                float _Anisotropy;

                float _SampleCount;

            CBUFFER_END


            // ========================================================
            // Fog Density
            //
            // 指定されたワールド座標の霧密度を返す。
            // ========================================================

            float GetFogDensity(float3 positionWS)
            {
                // FogHeightより上にいる距離。
                float heightDifference =
                    max(
                        positionWS.y - _FogHeight,
                        0.0);

                // 高くなるほど指数関数的に薄くする。
                float heightDensity =
                    exp(
                        -heightDifference *
                        _HeightFalloff);

                return
                    max(
                        _Density,
                        0.0)
                    *
                    heightDensity;
            }


            // ========================================================
            // Henyey-Greenstein
            //
            // 光がどの方向へ散乱しやすいかを計算する。
            // ========================================================

            float HenyeyGreenstein(
                float cosTheta,
                float anisotropy)
            {
                float g =
                    clamp(
                        anisotropy,
                        -0.9,
                        0.9);

                float g2 =
                    g * g;

                float denominator =
                    1.0 +
                    g2 -
                    2.0 *
                    g *
                    cosTheta;

                denominator =
                    max(
                        denominator,
                        0.0001);

                return
                    INV_FOUR_PI *
                    (1.0 - g2)
                    /
                    pow(
                        denominator,
                        1.5);
            }


            // ========================================================
            // Sky判定
            //
            // Depthに描画物が存在するかを判定する。
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
            // Fragment Shader
            // ========================================================

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(
                    input);


                // ====================================================
                // 1. Color用UV
                // ====================================================

                float2 colorUv =
                    input.texcoord.xy;


                // ====================================================
                // 2. Depth用UV
                // ====================================================

                float2 depthUv =
                    input.positionCS.xy /
                    _ScaledScreenParams.xy;


                // ====================================================
                // 3. 元画面
                // ====================================================

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        colorUv,
                        _BlitMipLevel);


                // ====================================================
                // 4. Depth取得
                // ====================================================

                float rawDepth =
                    SampleSceneDepth(
                        depthUv);

                float deviceDepth =
                    rawDepth;


                #if !UNITY_REVERSED_Z

                    deviceDepth =
                        lerp(
                            UNITY_NEAR_CLIP_VALUE,
                            1.0,
                            rawDepth);

                #endif


                // ====================================================
                // 5. World Position復元
                // ====================================================

                float3 surfacePositionWS =
                    ComputeWorldSpacePosition(
                        depthUv,
                        deviceDepth,
                        UNITY_MATRIX_I_VP);

                float3 cameraPositionWS =
                    _WorldSpaceCameraPos;


                // ====================================================
                // 6. Camera → Surface
                // ====================================================

                float3 cameraToSurface =
                    surfacePositionWS -
                    cameraPositionWS;

                float surfaceDistance =
                    length(
                        cameraToSurface);


                if (surfaceDistance < 0.0001)
                {
                    return sceneColor;
                }


                float3 rayDirection =
                    cameraToSurface /
                    surfaceDistance;


                // ====================================================
                // 7. Rayの長さ
                //
                // 物体:
                //   物体表面まで
                //
                // Sky:
                //   MaxDistanceまで
                // ====================================================

                bool isSky =
                    IsSkyDepth(
                        rawDepth);

                float rayLength =
                    isSky
                        ? _MaxDistance
                        : min(
                            surfaceDistance,
                            _MaxDistance);

                rayLength =
                    max(
                        rayLength,
                        0.0);


                // ====================================================
                // 8. Sample Count
                // ====================================================

                int sampleCount =
                    clamp(
                        (int)round(_SampleCount),
                        8,
                        MAX_SAMPLE_COUNT);

                float stepLength =
                    rayLength /
                    max(
                        (float)sampleCount,
                        1.0);


                // ====================================================
                // 9. 積算値
                // ====================================================

                // 霧を通った後に
                // 元画面がどれくらい残るか。
                float transmittance =
                    1.0;

                // 霧そのものからCameraへ届く光。
                float3 scattering =
                    float3(
                        0.0,
                        0.0,
                        0.0);


                // ====================================================
                // 10. Ray March
                // ====================================================

                [loop]
                for (
                    int i = 0;
                    i < MAX_SAMPLE_COUNT;
                    ++i)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }


                    // ------------------------------------------------
                    // 現在のサンプル距離
                    // ------------------------------------------------

                    float distanceAlongRay =
                        (
                            (float)i +
                            0.5
                        )
                        *
                        stepLength;


                    // ------------------------------------------------
                    // 現在のサンプル位置
                    // ------------------------------------------------

                    float3 samplePositionWS =
                        cameraPositionWS +
                        rayDirection *
                        distanceAlongRay;


                    // ------------------------------------------------
                    // Fog Density
                    // ------------------------------------------------

                    float density =
                        GetFogDensity(
                            samplePositionWS);


                    if (density <= 0.000001)
                    {
                        continue;
                    }


                    // =================================================
                    // 11. Shadow
                    // =================================================

                    float4 shadowCoord =
                        TransformWorldToShadowCoord(
                            samplePositionWS);


                    Light mainLight =
                        GetMainLight(
                            shadowCoord);


                    // 1:
                    //   光が当たっている
                    //
                    // 0:
                    //   遮蔽物の影
                    float shadowAttenuation =
                        mainLight.shadowAttenuation;


                    // =================================================
                    // 12. God Ray方向補正
                    // =================================================

                    // Cameraから奥方向。
                    //
                    // MainLight.directionは
                    // Sample地点から光源方向。
                    float cosTheta =
                        dot(
                            rayDirection,
                            mainLight.direction);

                    cosTheta =
                        clamp(
                            cosTheta,
                            -1.0,
                            1.0);


                    float hgPhase =
                        HenyeyGreenstein(
                            cosTheta,
                            _Anisotropy);


                    // anisotropy = 0 の状態を
                    // 基準倍率1として扱う。
                    float phaseRatio =
                        hgPhase /
                        INV_FOUR_PI;


                    // 既に出ているGod Rayを
                    // Anisotropyによって弱くしない。
                    //
                    // 太陽方向を見た場合のみ強くする。
                    float phaseBoost =
                        max(
                            phaseRatio,
                            1.0);


                    // 異常な白飛びを防ぐ。
                    phaseBoost =
                        min(
                            phaseBoost,
                            8.0);


                    // =================================================
                    // 13. Beer-Lambert
                    // =================================================

                    float extinction =
                        density *
                        stepLength;


                    // この1区間を通過した後に
                    // 残っている光。
                    float stepTransmittance =
                        exp(
                            -extinction);


                    // この区間で
                    // 霧に散乱された割合。
                    float scatteredAmount =
                        1.0 -
                        stepTransmittance;


                    // =================================================
                    // 14. Ambient Fog
                    // =================================================

                    float3 ambientLight =
                        _FogColor.rgb *
                        _AmbientIntensity;


                    // =================================================
                    // 15. Directional Lightによる散乱
                    //
                    // distanceAttenuationは使用しない。
                    //
                    // Directional Lightは太陽のような
                    // 平行光として扱う。
                    // =================================================

                    float3 directScattering =
                        _FogColor.rgb *
                        mainLight.color.rgb *
                        shadowAttenuation *
                        _LightIntensity *
                        phaseBoost;


                    // =================================================
                    // 16. この地点の最終散乱光
                    // =================================================

                    float3 scatteredLight =
                        (
                            ambientLight +
                            directScattering
                        )
                        *
                        _ScatteringIntensity;


                    // =================================================
                    // 17. Cameraへ届く散乱光を積算
                    // =================================================

                    scattering +=
                        transmittance *
                        scatteredAmount *
                        scatteredLight;


                    // =================================================
                    // 18. 次の区間へ
                    // =================================================

                    transmittance *=
                        stepTransmittance;


                    // ほぼ何も透過しないなら
                    // 残りは計算しない。
                    if (transmittance < 0.01)
                    {
                        break;
                    }
                }


                // ====================================================
                // 19. 最終合成
                // ====================================================

                float3 finalColor =
                    sceneColor.rgb *
                    transmittance +
                    scattering;


                // BloomへHDR値を渡したいため
                // saturate()はしない。
                return half4(
                    finalColor,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}
