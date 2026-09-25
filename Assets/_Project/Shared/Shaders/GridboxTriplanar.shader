Shader "Project/Prototype/GridboxTriplanar"
{
    Properties
    {
        // 格子模様として使用するテクスチャ。
        // Gridbox Prototype Materials 内の格子テクスチャを設定する。
        _BaseMap ("Grid Texture", 2D) = "white" {}

        // テクスチャ全体に掛ける色。
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        // 格子模様の大きさ。
        // 1.0の場合、1 Unity Unitを基準としてテクスチャを投影する。
        _GridSize ("Grid Size", Float) = 1.0

        // Triplanar投影を切り替える境界の鋭さ。
        // 値が大きいほど、面ごとの投影がはっきり分かれる。
        _ProjectionSharpness ("Projection Sharpness", Range(1.0, 32.0)) = 8.0
    }

    SubShader
    {
        Tags
        {
            // 不透明オブジェクトとして描画する。
            "RenderType" = "Opaque"

            // URP用Shaderであることを指定する。
            "RenderPipeline" = "UniversalPipeline"

            // 通常の不透明オブジェクトと同じ描画順。
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "GridboxTriplanar"

            Tags
            {
                // URPのForward描画で使用するPass。
                "LightMode" = "UniversalForwardOnly"
            }

            // 裏面は描画しない。
            Cull Back

            // 深度バッファへ書き込む。
            ZWrite On

            // 通常の深度比較を行う。
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            // Triplanarやfwidth等を使用するため、
            // Shader Model 3.5を指定する。
            #pragma target 3.5

            // URPで座標変換などを行うための基本ライブラリ。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 頂点シェーダーへ渡されるデータ。
            struct Attributes
            {
                // オブジェクト空間上の頂点位置。
                float4 positionOS : POSITION;

                // オブジェクト空間上の法線。
                float3 normalOS : NORMAL;
            };

            // 頂点シェーダーからピクセルシェーダーへ渡すデータ。
            struct Varyings
            {
                // 画面描画用のクリップ空間座標。
                float4 positionCS : SV_POSITION;

                // ワールド空間上の頂点位置。
                // UVを使用せず、この座標を使ってテクスチャを投影する。
                float3 positionWS : TEXCOORD0;

                // ワールド空間上の法線。
                // どの方向からテクスチャを投影するか判定するために使用する。
                float3 normalWS : TEXCOORD1;
            };

            // 格子テクスチャ。
            TEXTURE2D(_BaseMap);

            // 格子テクスチャ用のSampler。
            SAMPLER(sampler_BaseMap);

            // Materialごとに設定されるパラメータ。
            CBUFFER_START(UnityPerMaterial)

            // Material全体の色。
            float4 _BaseColor;

            // 格子の大きさ。
            float _GridSize;

            // Triplanarの投影切り替えの鋭さ。
            float _ProjectionSharpness;

            CBUFFER_END

            // 頂点シェーダー。
            Varyings Vert(Attributes input)
            {
                Varyings output;

                // オブジェクト空間の頂点位置から、
                // ワールド座標やクリップ座標を取得する。
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                // オブジェクト空間の法線を
                // ワールド空間の法線へ変換する。
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);

                // GPUが画面上のどこへ頂点を描画するか決めるための座標。
                output.positionCS = positionInputs.positionCS;

                // Triplanar投影用にワールド座標を渡す。
                output.positionWS = positionInputs.positionWS;

                // 面の向きを判定するためにワールド法線を渡す。
                output.normalWS = normalInputs.normalWS;

                return output;
            }

            // ピクセルシェーダー。
            half4 Frag(Varyings input) : SV_Target
            {
                // 0除算を防ぐ。
                // Grid Sizeが0に設定されても最低値を保証する。
                float gridSize = max(_GridSize, 0.0001);

                // ワールド座標をGrid Sizeで割ることで、
                // テクスチャの繰り返し間隔を決める。
                //
                // UVではなくワールド座標を使用するため、
                // TransformのScaleを変更しても
                // テクスチャそのものが引き伸ばされない。
                float3 position =
                    input.positionWS / gridSize;

                // 法線を正規化する。
                float3 normal =
                    normalize(input.normalWS);

                // --------------------------------------------------
                // Triplanar Mapping
                //
                // X・Y・Zの3方向から同じテクスチャを投影し、
                // 面の向きに応じてそれらを混ぜ合わせる。
                //
                // これによりUV展開に依存せず、
                // Cube・壁・床などに格子模様を貼ることができる。
                // --------------------------------------------------

                // X軸方向から投影。
                //
                // X軸方向から面を見るため、
                // YZ座標を2DのUVとして使用する。
                float4 sampleX =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        position.yz);

                // Y軸方向から投影。
                //
                // 上下方向の面、
                // 例えば床や天井などで主に使用される。
                float4 sampleY =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        position.xz);

                // Z軸方向から投影。
                //
                // 正面・背面方向の面で主に使用される。
                float4 sampleZ =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        position.xy);

                // --------------------------------------------------
                // 各方向の投影をどの程度使用するか計算する。
                // --------------------------------------------------

                // 法線のX・Y・Z成分を見ることで、
                // その面がどちらを向いているか判定する。
                //
                // absを使うことで、
                // +Xと-Xのような向きの違いを無視している。
                float3 weights =
                    abs(normal);

                // ProjectionSharpnessを使用して、
                // 投影方向の切り替わりを調整する。
                //
                // 値が大きい:
                // 面ごとの切り替えがはっきりする。
                //
                // 値が小さい:
                // 3方向のテクスチャがなめらかに混ざる。
                weights =
                    pow(
                        weights,
                        _ProjectionSharpness);

                // X + Y + Z の合計が1になるように正規化する。
                //
                // 例えば上向きの面なら、
                //
                // X = 0
                // Y = 1
                // Z = 0
                //
                // に近くなる。
                weights /=
                    max(
                        weights.x +
                        weights.y +
                        weights.z,
                        0.0001);

                // 3方向から投影したテクスチャを、
                // 面の向きに応じて混ぜ合わせる。
                float4 textureColor =
                    sampleX * weights.x +
                    sampleY * weights.y +
                    sampleZ * weights.z;

                // 最後にMaterialの色を掛けて出力する。
                return textureColor * _BaseColor;
            }

            ENDHLSL
        }
    }
}
