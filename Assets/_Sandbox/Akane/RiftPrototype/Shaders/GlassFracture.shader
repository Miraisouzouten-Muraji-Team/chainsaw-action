
Shader "Prototype/RiftA/GlassFracture"
{
    Properties
    {
        _MainTex ("Captured Menu", 2D) = "white" {}
        _SideColor ("Broken Edge Color", Color) = (0.12, 0.18, 0.25, 1)
        [HDR] _CrackColor ("Crack Glow", Color) = (1.5, 2.0, 3.0, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "RiftA_GlassPieces"

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SideColor;
                float4 _CrackColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    TransformObjectToHClip(input.positionOS);

                output.uv = input.uv;
                output.color = input.color;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UVのZ成分に頂点の種類を入れている。
                // 0 = 画面画像、1 = 破片側面、2 = 亀裂線。
                float vertexType = input.uv.z;

                if (vertexType > 1.5)
                {
                    // 発光する亀裂線。
                    half alpha =
                        saturate(input.color.a * _CrackColor.a);

                    clip(alpha - 0.001);

                    return half4(
                        _CrackColor.rgb * input.color.rgb,
                        alpha);
                }

                if (vertexType > 0.5)
                {
                    // ガラス片の側面。元画像より暗く表示する。
                    return half4(
                        _SideColor.rgb * input.color.rgb,
                        1.0);
                }

                // ガラス片の表面には元の選択画面を表示する。
                half4 menuColor = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    input.uv.xy);

                return half4(
                    menuColor.rgb * input.color.rgb,
                    1.0);
            }

            ENDHLSL
        }
    }
}
