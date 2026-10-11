
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

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidthPixels;
                float _DepthThreshold;
                float _DepthStrength;
                float _NormalThreshold;
                float _NormalStrength;
                float _NormalDepthThreshold;
                float _DebugView;
            CBUFFER_END

            // Skyの深度か判定する。
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 0.0001;
                #else
                    return rawDepth >= 0.9999;
                #endif
            }

            // 無効なNormalを除外する。
            bool IsValidNormal(float3 normalWS)
            {
                float lengthSquared = dot(normalWS, normalWS);
                return lengthSquared > 0.25 && lengthSquared < 2.0;
            }

            // Depth：中心ピクセルが手前にあるときだけ輪郭にする。
            float GetDepthEdge(float2 sampleUv, float centerDepth)
            {
                float sampleRawDepth = SampleSceneDepth(saturate(sampleUv));

                // Skyは中心のオブジェクトより奥にある。
                if (IsSkyDepth(sampleRawDepth))
                {
                    return 1.0;
                }

                float sampleDepth = LinearEyeDepth(sampleRawDepth, _ZBufferParams);

                // 正の値なら中心ピクセルの方が手前。
                float depthDifference = (sampleDepth - centerDepth) / max(centerDepth, 0.0001);

                return step(_DepthThreshold, depthDifference);
            }

            // Normal：面の向きが変わる部分だけ輪郭にする。
            float GetNormalEdge(float2 sampleUv, float centerDepth, float3 centerNormal, bool centerNormalValid)
            {
                if (!centerNormalValid)
                {
                    return 0.0;
                }

                sampleUv = saturate(sampleUv);

                float sampleRawDepth = SampleSceneDepth(sampleUv);

                // Skyとの境界はDepthに任せる。
                if (IsSkyDepth(sampleRawDepth))
                {
                    return 0.0;
                }

                float sampleDepth = LinearEyeDepth(sampleRawDepth, _ZBufferParams);
                float depthDifference = abs(sampleDepth - centerDepth) / max(centerDepth, 0.0001);

                // 奥行きが大きく違う面はNormalで比較しない。
                if (depthDifference > _NormalDepthThreshold)
                {
                    return 0.0;
                }

                float3 sampleNormal = SampleSceneNormals(sampleUv);

                if (!IsValidNormal(sampleNormal))
                {
                    return 0.0;
                }

                sampleNormal = normalize(sampleNormal);

                float normalDifference = 1.0 - saturate(dot(centerNormal, sampleNormal));

                return step(_NormalThreshold, normalDifference);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 colorUv = input.texcoord.xy;
                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;

                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, colorUv, _BlitMipLevel);

                float centerRawDepth = SampleSceneDepth(screenUv);

                // Skyにはアウトラインを描画しない。
                if (IsSkyDepth(centerRawDepth))
                {
                    return sceneColor;
                }

                float centerDepth = LinearEyeDepth(centerRawDepth, _ZBufferParams);
                float3 centerNormal = SampleSceneNormals(screenUv);
                bool centerNormalValid = IsValidNormal(centerNormal);

                if (centerNormalValid)
                {
                    centerNormal = normalize(centerNormal);
                }
                else
                {
                    centerNormal = float3(0.0, 0.0, 0.0);
                }

                // Normal Textureを色で確認する。
                if (_DebugView > 0.5 && _DebugView < 1.5)
                {
                    float3 debugColor = centerNormalValid
                        ? centerNormal * 0.5 + 0.5
                        : float3(1.0, 0.0, 1.0);

                    return half4(debugColor, sceneColor.a);
                }

                float2 texelSize = 1.0 / _ScaledScreenParams.xy;
                int outlineWidth = clamp((int)round(_OutlineWidthPixels), 1, MAX_OUTLINE_WIDTH);

                float depthEdge = 0.0;
                float normalEdge = 0.0;

                [loop]
                for (int radius = 1; radius <= MAX_OUTLINE_WIDTH; ++radius)
                {
                    if (radius > outlineWidth)
                    {
                        break;
                    }

                    float2 offset = texelSize * (float)radius;

                    // Depth：上下左右を確認し、中心が手前の場合だけ線を描く。
                    depthEdge = max(depthEdge, GetDepthEdge(screenUv + float2(offset.x, 0.0), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(screenUv + float2(-offset.x, 0.0), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(screenUv + float2(0.0, offset.y), centerDepth));
                    depthEdge = max(depthEdge, GetDepthEdge(screenUv + float2(0.0, -offset.y), centerDepth));

                    // Normal：右と上だけ比較して、境界の片側に線を寄せる。
                    normalEdge = max(normalEdge, GetNormalEdge(screenUv + float2(offset.x, 0.0), centerDepth, centerNormal, centerNormalValid));
                    normalEdge = max(normalEdge, GetNormalEdge(screenUv + float2(0.0, offset.y), centerDepth, centerNormal, centerNormalValid));
                }

                depthEdge *= _DepthStrength;
                normalEdge *= _NormalStrength;

                // Normalの輪郭だけ確認する。
                if (_DebugView >= 1.5)
                {
                    return half4(normalEdge.xxx, sceneColor.a);
                }

                float edge = saturate(max(depthEdge, normalEdge));
                float outlineAmount = edge * _OutlineColor.a;
                float3 finalColor = lerp(sceneColor.rgb, _OutlineColor.rgb, outlineAmount);

                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}
