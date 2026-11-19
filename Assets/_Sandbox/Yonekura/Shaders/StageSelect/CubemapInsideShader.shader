//Shader "Custom/CubemapInside"
//{
//    Properties
//    {
//        _Cube("Stage Cubemap", Cube) = "" {}
//        _ForwardOffset("Forward Offset", Float) = 0
//    }
//
//    SubShader
//    {
//        Tags
//        {
//            "RenderPipeline" = "UniversalPipeline"
//            "Queue" = "Geometry"
//            "RenderType" = "Opaque"
//        }
//
//        Pass
//        {
//            Cull Front
//            ZWrite On
//            ZTest LEqual
//
//            HLSLPROGRAM
//
//            #pragma vertex vert
//            #pragma fragment frag
//
//            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
//
//            TEXTURECUBE(_Cube);
//            SAMPLER(sampler_Cube);
//
//            CBUFFER_START(UnityPerMaterial)
//                float _ForwardOffset;
//            CBUFFER_END
//
//            struct Attributes
//            {
//                float4 positionOS : POSITION;
//            };
//
//            struct Varyings
//            {
//                float4 positionHCS : SV_POSITION;
//                float3 direction : TEXCOORD0;
//            };
//
//            Varyings vert(Attributes input)
//            {
//                Varyings output;
//
//                output.positionHCS =
//                    TransformObjectToHClip(input.positionOS.xyz);
//
//                // 球体の中心から頂点へ向かう方向
//                output.direction = input.positionOS.xyz;
//
//                return output;
//            }
//
//            half4 frag(Varyings input) : SV_Target
//            {
//                // Cubemapを参照する方向
//                float3 direction = normalize(input.direction);
//
//                // 上下の方向を反転
//                //direction.y *= -1.0;
//
//                // カメラの前進量に応じてCubemapの参照方向を補正
//                direction += float3(0, 0, _ForwardOffset);
//                direction = normalize(direction);
//
//                // Cubemapから色を取得
//                half4 color = SAMPLE_TEXTURECUBE(
//                     _Cube,
//                     sampler_Cube,
//                     direction
//                );
//
//                 return color;
//            }
//
//            ENDHLSL
//        }
//    }
//}



Shader "Custom/CubemapInside"
{
    Properties
    {
        _Cube("Stage Cubemap", Cube) = "" {}
        _DepthCube("Depth Cubemap", Cube) = "" {}
        _CapturePosition("Capture Position", Vector) = (0, 0, -5, 0)
        _DepthFar("Depth Far", Float) = 1000
        _ParallaxStrength("Parallax Strength", Range(0, 1)) = 1
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
                Cull Front
                ZWrite On
                ZTest LEqual

                HLSLPROGRAM

                #pragma vertex vert
                #pragma fragment frag

                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

                TEXTURECUBE(_Cube);
                SAMPLER(sampler_Cube);

                TEXTURECUBE(_DepthCube);
                SAMPLER(sampler_DepthCube);

                CBUFFER_START(UnityPerMaterial)
                    float4 _CapturePosition;
                    float _DepthFar;
                    float _ParallaxStrength;
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
                    // 現在のカメラから画面上のピクセルへ向かう方向
                    float3 cameraPosition = _WorldSpaceCameraPos;

                    float3 rayDirection = normalize(
                        input.positionWS - cameraPosition
                    );

                    float3 capturePosition = _CapturePosition.xyz;

                    float3 cameraOffset =
                        cameraPosition - capturePosition;

                    // 最初は現在の視線方向を使用
                    float3 sourceDirection = rayDirection;

                    bool valid = true;

                    // 深度情報を使って参照方向を繰り返し補正
                    [unroll]
                    for (int i = 0; i < 4; i++)
                    {
                        // 現在の方向に対応する深度を取得
                        float depthValue = SAMPLE_TEXTURECUBE_LOD(
                            _DepthCube,
                            sampler_DepthCube,
                            sourceDirection,
                            0
                        ).r;

                        // 正規化された深度を実際の距離へ戻す
                        float radius = max(
                            depthValue * _DepthFar,
                            0.01
                        );

                        // 現在の視線と深度球体の交点を計算
                        float b = dot(
                            cameraOffset,
                            rayDirection
                        );

                        float c = dot(
                            cameraOffset,
                            cameraOffset
                        ) - radius * radius;

                        float discriminant = b * b - c;

                        if (discriminant < 0)
                        {
                            valid = false;
                            break;
                        }

                        float root = sqrt(discriminant);

                        float tNear = -b - root;
                        float tFar = -b + root;

                        float t = tNear > 0 ? tNear : tFar;

                        if (t <= 0)
                        {
                            valid = false;
                            break;
                        }

                        // 現在のカメラから見た推定位置
                        float3 worldPosition =
                            cameraPosition + rayDirection * t;

                        // 撮影地点から推定位置への方向
                        sourceDirection = normalize(
                            worldPosition - capturePosition
                        );
                    }

                    // 補正できなかった場合は元の方向を使用
                    if (!valid)
                    {
                        sourceDirection = rayDirection;
                    }

                    // 補正強度を適用
                    float3 finalDirection = normalize(
                        lerp(
                            rayDirection,
                            sourceDirection,
                            _ParallaxStrength
                        )
                    );

                    // 補正した方向からカラーCubemapを取得
                    half4 color = SAMPLE_TEXTURECUBE_LOD(
                        _Cube,
                        sampler_Cube,
                        finalDirection,
                        0
                    );

                    return color;
                }

                ENDHLSL
            }
        }
}
