Shader "Custom/GodRay"
{
    Properties
    {
        _Intensity("Intensity", Float) = 1.0
        _Decay("Decay", Float) = 0.95
        _Density("Density", Float) = 1.0
        _Weight("Weight", Float) = 0.15
        _SunRadius("Sun Radius", Float) = 0.15
        _RayColor("Ray Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "GodRay"

            HLSLPROGRAM

            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_SAMPLE_COUNT 64

            float4 _SunScreenPosition;
            float4 _RayColor;

            float _Intensity;
            float _Decay;
            float _Density;
            float _Weight;
            float _SunRadius;

            int _SampleCount;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;

                // 描画済みのシーンカラー
                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv,
                        _BlitMipLevel);

                float sampleCount =
                    max((float)_SampleCount, 1.0);

                // 現在の画素から太陽へ向かう方向
                float2 deltaUv =
                    (_SunScreenPosition.xy - uv) *
                    (_Density / sampleCount);

                float2 sampleUv = uv;

                float illuminationDecay = 1.0;
                float ray = 0.0;

                [loop]
                for (int i = 0;
                    i < MAX_SAMPLE_COUNT;
                    ++i)
                {
                    if (i >= _SampleCount)
                    {
                        break;
                    }

                    sampleUv += deltaUv;

                    if (sampleUv.x < 0.0 ||
                        sampleUv.x > 1.0 ||
                        sampleUv.y < 0.0 ||
                        sampleUv.y > 1.0)
                    {
                        continue;
                    }

                    // Depthを取得
                    float rawDepth =
                        SampleSceneDepth(sampleUv);

                    float linearDepth =
                        Linear01Depth(
                            rawDepth,
                            _ZBufferParams);

                    // 遠景 = 空とみなす。
                    // オブジェクトなら0、空なら1に近くなる。
                    float skyMask =
                        smoothstep(
                            0.995,
                            1.0,
                            linearDepth);

                    // 太陽の周辺だけを光源として扱う。
                    float distanceToSun =
                        distance(
                            sampleUv,
                            _SunScreenPosition.xy);

                    float sunMask =
                        saturate(
                            1.0 -
                            distanceToSun /
                            max(_SunRadius, 0.0001));

                    float lightSample =
                        skyMask *
                        sunMask;

                    ray +=
                        lightSample *
                        illuminationDecay *
                        _Weight;

                    illuminationDecay *= _Decay;
                }

                float3 godRay =
                    ray *
                    _RayColor.rgb *
                    _Intensity;

                return half4(
                    sceneColor.rgb + godRay,
                    sceneColor.a);
            }

            ENDHLSL
        }
    }
}