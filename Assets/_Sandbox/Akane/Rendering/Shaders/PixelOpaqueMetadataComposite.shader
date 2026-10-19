Shader "Custom/PixelOpaqueMetadataComposite"
{
    Properties
    {
        [HideInInspector] _PixelNearDepthTex("Near Depth", 2D) = "white" {}
        [HideInInspector] _PixelMiddleDepthTex("Middle Depth", 2D) = "white" {}
        [HideInInspector] _PixelFarDepthTex("Far Depth", 2D) = "white" {}
        [HideInInspector] _PixelNearNormalTex("Near Normal", 2D) = "black" {}
        [HideInInspector] _PixelMiddleNormalTex("Middle Normal", 2D) = "black" {}
        [HideInInspector] _PixelFarNormalTex("Far Normal", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelOpaqueMetadataComposite"

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "Assets/_Sandbox/Akane/Rendering/Shaders/PixelOpaqueSelection.hlsl"

            TEXTURE2D(_PixelNearDepthTex);
            TEXTURE2D(_PixelMiddleDepthTex);
            TEXTURE2D(_PixelFarDepthTex);
            TEXTURE2D(_PixelNearNormalTex);
            TEXTURE2D(_PixelMiddleNormalTex);
            TEXTURE2D(_PixelFarNormalTex);

            // フルスクリーン描画用の頂点出力。
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // DepthとNormalをMRTへ出力する。
            struct CompositeOutput
            {
                float Depth : SV_Target0;
                float4 Normal : SV_Target1;
            };

            // フルスクリーントライアングルを生成する。
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);

                return output;
            }

            // URPのNormal保存形式をワールド空間の法線に変換する。
            float3 DecodePixelNormal(float3 encodedNormal)
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 packedNormal = Unpack888ToFloat2(encodedNormal);
                    float2 octNormal = packedNormal * 2.0 - 1.0;
                    return normalize(UnpackNormalOctQuadEncode(octNormal));
                #else
                    return normalize(encodedNormal);
                #endif
            }

            // 指定した描画層のNormalを取得する。
            float3 SampleSelectedNormal(float2 uv, int selectedLayer)
            {
                float3 encodedNormal;

                if (selectedLayer == 0)
                {
                    encodedNormal = SAMPLE_TEXTURE2D(_PixelNearNormalTex, sampler_PointClamp, uv).xyz;
                }
                else if (selectedLayer == 1)
                {
                    encodedNormal = SAMPLE_TEXTURE2D(_PixelMiddleNormalTex, sampler_PointClamp, uv).xyz;
                }
                else
                {
                    encodedNormal = SAMPLE_TEXTURE2D(_PixelFarNormalTex, sampler_PointClamp, uv).xyz;
                }

                return DecodePixelNormal(encodedNormal);
            }

            // 最前面のOpaqueからDepthとNormalを出力する。
            CompositeOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;

                // 各描画層のDepthを取得する。
                float nearDepth = SAMPLE_TEXTURE2D(_PixelNearDepthTex, sampler_PointClamp, uv).r;
                float middleDepth = SAMPLE_TEXTURE2D(_PixelMiddleDepthTex, sampler_PointClamp, uv).r;
                float farDepth = SAMPLE_TEXTURE2D(_PixelFarDepthTex, sampler_PointClamp, uv).r;

                // 最も手前の描画層を決定する。
                float selectedDepth;
                int selectedLayer = SelectOpaqueLayer(nearDepth, middleDepth, farDepth, selectedDepth);

                CompositeOutput output;
                output.Depth = selectedDepth;
                output.Normal = float4(0.0, 0.0, 0.0, 0.0);

                // 描画対象がなければSky Depthと無効なNormalを返す。
                if (selectedLayer < 0) return output;

                // 選択した描画層のWorld Normalを取得する。
                float3 normalWS = SampleSelectedNormal(uv, selectedLayer);
                output.Normal = float4(normalWS, 1.0);

                return output;
            }

            ENDHLSL
        }
    }
}
