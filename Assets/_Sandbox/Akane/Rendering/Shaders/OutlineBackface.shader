Shader "Custom/OutlineBackface"
{
    Properties
    {
        // 輪郭色。
        _OutlineColor(
            "Outline Color",
            Color) = (0.02, 0.02, 0.02, 1.0)

        // 現在のRenderTarget上での線幅。
        //
        // 1なら低解像度RT上でも1px。
        _OutlineWidthPixels(
            "Outline Width Pixels",
            Range(0.5, 4.0)) = 1.0
    }


    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
        }


        // ============================================================
        // Inverted Hull
        //
        // 表面を捨てて背面だけ描画する。
        // ============================================================

        Cull Front

        // 元モデルのDepthを利用して
        // 本体より手前へ出ないようにする。
        ZTest LEqual

        // Outline自身はDepthを書き換えない。
        ZWrite Off

        Blend SrcAlpha OneMinusSrcAlpha


        Pass
        {
            Name "OutlineBackface"

            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }


            HLSLPROGRAM

            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            CBUFFER_START(UnityPerMaterial)

                float4 _OutlineColor;

                float _OutlineWidthPixels;

            CBUFFER_END


            // ========================================================
            // Vertex Input
            // ========================================================

            struct Attributes
            {
                float4 positionOS : POSITION;

                float3 normalOS : NORMAL;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };


            // ========================================================
            // Vertex Output
            // ========================================================

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };


            // ========================================================
            // Vertex
            // ========================================================

            Varyings Vert(Attributes input)
            {
                Varyings output;


                UNITY_SETUP_INSTANCE_ID(
                    input);

                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(
                    output);


                // ====================================================
                // 1. Position
                // ====================================================

                float3 positionWS =
                    TransformObjectToWorld(
                        input.positionOS.xyz);


                float4 positionCS =
                    TransformWorldToHClip(
                        positionWS);


                // ====================================================
                // 2. Normal
                // ====================================================

                float3 normalWS =
                    TransformObjectToWorldNormal(
                        input.normalOS);


                float3 normalVS =
                    TransformWorldToViewDir(
                        normalWS,
                        true);


                // ====================================================
                // 3. View Space Normalを
                //    Projection Space方向へ変換
                // ====================================================

                float2 projectedNormal =
                    float2(
                        normalVS.x *
                        UNITY_MATRIX_P._m00,

                        normalVS.y *
                        UNITY_MATRIX_P._m11);


                float normalLengthSquared =
                    dot(
                        projectedNormal,
                        projectedNormal);


                float2 outlineDirection =
                    float2(
                        0.0,
                        0.0);


                if (normalLengthSquared > 0.000001)
                {
                    outlineDirection =
                        projectedNormal *
                        rsqrt(
                            normalLengthSquared);
                }


                // ====================================================
                // 4. Pixel → Clip Space
                //
                // NDCは-1～1なので
                // 画面全体の幅は2。
                // ====================================================

                float2 pixelToNdc =
                    2.0 /
                    _ScaledScreenParams.xy;


                float2 clipOffset =
                    outlineDirection *
                    pixelToNdc *
                    _OutlineWidthPixels *
                    positionCS.w;


                // ====================================================
                // 5. Silhouetteを外側へ拡張
                // ====================================================

                positionCS.xy +=
                    clipOffset;


                output.positionCS =
                    positionCS;


                return output;
            }


            // ========================================================
            // Fragment
            // ========================================================

            half4 Frag(Varyings input) : SV_Target
            {
                return
                    half4(
                        _OutlineColor);
            }


            ENDHLSL
        }
    }
}
