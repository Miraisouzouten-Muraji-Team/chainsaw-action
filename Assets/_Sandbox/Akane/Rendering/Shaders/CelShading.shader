
Shader "Custom/CelShadingPost"
{
    Properties
    {
        [Header(Cel Shading)]
        _DarkThreshold("Dark Threshold", Range(0.0, 1.0)) = 0.3
        _LightThreshold("Light Threshold", Range(0.0, 1.0)) = 0.7

        _DarkBrightness("Dark Brightness", Range(0.0, 1.5)) = 0.3
        _MiddleBrightness("Middle Brightness", Range(0.0, 1.5)) = 0.65
        _LightBrightness("Light Brightness", Range(0.0, 1.5)) = 1.0

        _CelStrength("Cel Strength", Range(0.0, 1.0)) = 1.0

        [Header(PBR Compensation)]
        _LightingFloor("Lighting Floor", Range(0.05, 1.0)) = 0.25
        _MaxCorrection("Max Correction", Range(1.0, 4.0)) = 2.0
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
        Blend Off

        Pass
        {
            Name "CelShadingPost"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Main LightとShadow Mapのバリエーション。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DarkThreshold;
                float _LightThreshold;

                float _DarkBrightness;
                float _MiddleBrightness;
                float _LightBrightness;

                float _CelStrength;
                float _LightingFloor;
                float _MaxCorrection;
            CBUFFER_END

            // SkyboxのDepthか判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // メインライトの照明強度から3段階の目標輝度を取得する。
            float GetCelBrightness(float lightAmount)
            {
                float darkThreshold = min(_DarkThreshold, _LightThreshold);
                float lightThreshold = max(_DarkThreshold, _LightThreshold);

                if (lightAmount >= lightThreshold)
                {
                    return _LightBrightness;
                }

                if (lightAmount >= darkThreshold)
                {
                    return _MiddleBrightness;
                }

                return _DarkBrightness;
            }

            // 元のPBR照明による明るさを近似する。
            // 間接光の下限とメインライトによる連続的な明暗を考慮する。
            float GetEstimatedBrightness(float lightAmount)
            {
                return lerp(
                    _LightingFloor,
                    1.0,
                    saturate(lightAmount));
            }

            // セル階調に合わせるための補正倍率を求める。
            // 既存PBRの明暗を近似的に取り除いてから階調を適用する。
            float GetCelCorrection(float lightAmount)
            {
                float targetBrightness = GetCelBrightness(lightAmount);
                float estimatedBrightness = GetEstimatedBrightness(lightAmount);

                float correction = targetBrightness / max(estimatedBrightness, 0.0001);

                // 間接光・Emission・反射などが含まれるため、
                // 補正倍率の極端な変化を抑える。
                float correctionLimit = max(_MaxCorrection, 1.0);

                return clamp(
                    correction,
                    1.0 / correctionLimit,
                    correctionLimit);
            }

            // Deferredで計算した画面色をセル階調へ補正する。
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // フルスクリーンColor用のUV。
                float2 colorUv = input.texcoord.xy;

                // カメラのDepthとNormalを参照するUV。
                float2 screenUv =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                // Deferred Lighting後の画面色を取得する。
                float4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    colorUv,
                    _BlitMipLevel);

                // Cel Strengthが0の場合は計算を省略する。
                if (_CelStrength <= 0.0001)
                {
                    return sceneColor;
                }

                // Depthを取得し、Skyboxにはセルシェーディングを適用しない。
                float rawDepth = SampleSceneDepth(screenUv);

                if (IsSkyDepth(rawDepth))
                {
                    return sceneColor;
                }

                // World Position復元用のDepthへ変換する。
                float deviceDepth = rawDepth;

                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(
                        UNITY_NEAR_CLIP_VALUE,
                        1.0,
                        rawDepth);
                #endif

                // Depthからワールド座標を復元する。
                float3 positionWS = ComputeWorldSpacePosition(
                    screenUv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);

                // DeferredのNormal Textureからワールド法線を取得する。
                float3 normalWS = SampleSceneNormals(screenUv);

                // 無効な法線へのセル階調適用を防ぐ。
                if (dot(normalWS, normalWS) < 0.0001)
                {
                    return sceneColor;
                }

                normalWS = normalize(normalWS);

                // メインライトのShadow Mapを参照する。
                // Screen Space ShadowにもURP標準関数で対応する。
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // 法線と光方向からDiffuseの連続的な明るさを取得する。
                float ndotl = saturate(
                    dot(normalWS, mainLight.direction));

                // Shadow Mapによる遮光を含める。
                float lightAmount = saturate(
                    ndotl * mainLight.shadowAttenuation);

                // 3段階の目標輝度と元の照明近似から補正倍率を求める。
                float correction = GetCelCorrection(lightAmount);

                // PBRで計算済みの画面色へ補正を適用する。
                // 色相を維持し、ライティングの明暗をセル階調へ寄せる。
                float3 celColor = sceneColor.rgb * correction;

                // 元のPBRとセル階調を混合する。
                float3 finalColor = lerp(
                    sceneColor.rgb,
                    celColor,
                    saturate(_CelStrength));

                return float4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
