Shader "Custom/CelShadingPost"
{
    Properties
    {
        // ============================================================
        // Cel Shading
        // ============================================================

        // 暗 → 中へ切り替わる境界。
        _DarkThreshold(
            "Dark Threshold",
            Range(0.0, 1.0)) = 0.3

        // 中 → 明へ切り替わる境界。
        _LightThreshold(
            "Light Threshold",
            Range(0.0, 1.0)) = 0.7

        // 暗部の明るさ。
        _DarkBrightness(
            "Dark Brightness",
            Range(0.0, 1.0)) = 0.45

        // 中間部の明るさ。
        _MiddleBrightness(
            "Middle Brightness",
            Range(0.0, 1.0)) = 0.75

        // 明部の明るさ。
        _LightBrightness(
            "Light Brightness",
            Range(0.0, 1.5)) = 1.0

        // 元のPBR結果とCel Shading結果を
        // どの程度混ぜるか。
        //
        // 0 = 元のPBR
        // 1 = 完全にCel Shading
        _CelStrength(
            "Cel Strength",
            Range(0.0, 1.0)) = 1.0
    }


    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        // Full Screen PassなのでDepthへ書き込まない。
        ZWrite Off

        // 常に画面全体へ描画。
        ZTest Always

        Cull Off


        Pass
        {
            Name "CelShadingPost"


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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            // Full Screen Pass用。
            //
            // Vert
            // Varyings
            // _BlitTexture
            //
            // などを使用する。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


            // ========================================================
            // Material Parameters
            // ========================================================

            CBUFFER_START(UnityPerMaterial)

                float _DarkThreshold;
                float _LightThreshold;

                float _DarkBrightness;
                float _MiddleBrightness;
                float _LightBrightness;

                float _CelStrength;

            CBUFFER_END


            // ========================================================
            // Sky判定
            //
            // SkyboxにはCel Shadingを掛けない。
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
            // 3段階の明るさを決める
            // ========================================================

            float GetCelBrightness(float lightAmount)
            {
                // --------------------------------------------
                // 明部
                // --------------------------------------------

                if (lightAmount >= _LightThreshold)
                {
                    return
                        _LightBrightness;
                }


                // --------------------------------------------
                // 中間
                // --------------------------------------------

                if (lightAmount >= _DarkThreshold)
                {
                    return
                        _MiddleBrightness;
                }


                // --------------------------------------------
                // 暗部
                // --------------------------------------------

                return
                    _DarkBrightness;
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
                // 2. Depth / Normal用UV
                // ====================================================

                float2 screenUv =
                    input.positionCS.xy /
                    _ScaledScreenParams.xy;


                // ====================================================
                // 3. Deferred / PBR計算済みの画面色
                // ====================================================

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        colorUv,
                        _BlitMipLevel);


                // ====================================================
                // 4. Depth
                // ====================================================

                float rawDepth =
                    SampleSceneDepth(
                        screenUv);


                // Skyboxはそのまま返す。
                if (IsSkyDepth(rawDepth))
                {
                    return sceneColor;
                }


                // ====================================================
                // 5. World Position復元用Depth
                // ====================================================

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
                // 6. World Position
                //
                // Shadow Mapを参照するために必要。
                // ====================================================

                float3 positionWS =
                    ComputeWorldSpacePosition(
                        screenUv,
                        deviceDepth,
                        UNITY_MATRIX_I_VP);


                // ====================================================
                // 7. World Space Normal
                //
                // Deferred / Depth Normalから取得する。
                // ====================================================

                float3 normalWS =
                    SampleSceneNormals(
                        screenUv);

                normalWS =
                    normalize(
                        normalWS);


                // ====================================================
                // 8. Main Directional Light
                // ====================================================

                float4 shadowCoord =
                    TransformWorldToShadowCoord(
                        positionWS);


                Light mainLight =
                    GetMainLight(
                        shadowCoord);


                // ====================================================
                // 9. N dot L
                //
                // 面がDirectional Lightを
                // どれくらい向いているか。
                //
                // 1:
                //   正面から光が当たる
                //
                // 0:
                //   光に対して横向き / 裏向き
                // ====================================================

                float ndotl =
                    saturate(
                        dot(
                            normalWS,
                            mainLight.direction));


                // ====================================================
                // 10. Shadow
                //
                // Cast ShadowもCel Shading判定に反映する。
                //
                // 1:
                //   光が届く
                //
                // 0:
                //   遮蔽物の影
                // ====================================================

                float shadowAttenuation =
                    mainLight.shadowAttenuation;


                // ====================================================
                // 11. Cel Shading用の光量
                //
                // 法線による明暗
                // ×
                // Shadow Map
                // ====================================================

                float lightAmount =
                    ndotl *
                    shadowAttenuation;


                // ====================================================
                // 12. 3段階化
                // ====================================================

                float celBrightness =
                    GetCelBrightness(
                        lightAmount);


                // ====================================================
                // 13. PBR結果へCelの段階を掛ける
                //
                // PBR / Deferredで計算した色を捨てず、
                // 最後の明暗だけ段階化する。
                // ====================================================

                float3 celColor =
                    sceneColor.rgb *
                    celBrightness;


                // ====================================================
                // 14. 元のPBR結果と混ぜる
                // ====================================================

                float3 finalColor =
                    lerp(
                        sceneColor.rgb,
                        celColor,
                        _CelStrength);


                return half4(
                    finalColor,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}
