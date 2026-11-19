Shader "Hidden/CubemapDepthCapture"
{
    Properties
    {
        _DepthFar("Depth Far", Float) = 1000
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DepthFar;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionWS =
                    TransformObjectToWorld(input.positionOS.xyz);

                output.positionHCS =
                    TransformWorldToHClip(output.positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 撮影地点から物体までの距離
                float depth = distance(
                    input.positionWS,
                    _WorldSpaceCameraPos
                );

            // 0～1の範囲に正規化
            depth = saturate(depth / _DepthFar);

            // Rチャンネルに距離を保存
            return half4(depth, 0, 0, 1);
        }

        ENDHLSL
    }
    }
}
