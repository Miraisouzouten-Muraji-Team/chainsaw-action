
Shader "Custom/SceneViewPixelPreview"
{
    Properties
    {
        _NearDistance("Near Distance", Float) = 10
        _FarDistance("Far Distance", Float) = 30
        _NearPixelSize("Near Pixel Size", Range(1, 16)) = 3
        _MiddlePixelSize("Middle Pixel Size", Range(1, 24)) = 6
        _FarPixelSize("Far Pixel Size", Range(1, 32)) = 12
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SceneViewPixelPreview"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _NearDistance;
                float _FarDistance;
                float _NearPixelSize;
                float _MiddlePixelSize;
                float _FarPixelSize;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                float2 depthUv = input.positionCS.xy / _ScaledScreenParams.xy;

                float rawDepth = SampleSceneDepth(depthUv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0001;
                #else
                    bool isSky = rawDepth >= 0.9999;
                #endif

                float pixelSize = _FarPixelSize;

                if (!isSky)
                {
                    if (eyeDepth < _NearDistance)
                    {
                        pixelSize = _NearPixelSize;
                    }
                    else if (eyeDepth < _FarDistance)
                    {
                        pixelSize = _MiddlePixelSize;
                    }
                }

                pixelSize = max(1.0, round(pixelSize));

                float2 resolution = _ScaledScreenParams.xy;
                float2 snappedUv = (floor(uv * resolution / pixelSize) + 0.5) * pixelSize / resolution;

                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, saturate(snappedUv), _BlitMipLevel);
            }

            ENDHLSL
        }
    }
}
