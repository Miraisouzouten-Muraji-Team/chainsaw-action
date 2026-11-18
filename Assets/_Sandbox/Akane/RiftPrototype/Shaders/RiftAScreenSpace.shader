Shader "Prototype/RiftA/ScreenSpace"
{
    Properties
    {
        [PerRendererData] _MainTex ("Live 3D Preview", 2D) = "black" {}
        _CrackTex ("Glass Crack Mask", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _CrackProgress ("Crack Progress", Range(0,1)) = 0
        _HoleProgress ("Center Hole Progress", Range(0,1)) = 0
        _OpenProgress ("Opening Progress", Range(0,1)) = 0
        _TravelProgress ("Camera Travel", Range(0,1)) = 0

        _Ready ("Preview Ready", Float) = 0
        _Center ("Crack Center", Vector) = (0.5,0.5,0,0)
        _Aspect ("Screen Aspect", Float) = 1.77778

        [Header(Hole)]
        _InitialHoleRadius ("Small Hole Radius", Range(0.005,0.15)) = 0.055
        _HoleIrregularity ("Hole Irregularity", Range(0,0.5)) = 0.2
        _EdgeWidth ("Edge Width", Range(0.0005,0.04)) = 0.002

        [Header(Glass)]
        _GlowWidth ("Glow Width", Range(0.002,0.1)) = 0.025
        _CrackIntensity ("Crack Intensity", Range(0,5)) = 2.5
        _Refraction ("Edge Refraction", Range(0,0.03)) = 0.005

        [HDR] _CrackColor ("Glass Crack Color", Color) = (1.4,1.6,1.9,1)
        [HDR] _EdgeColor ("Portal Edge Color", Color) = (0.45,1.15,2.6,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "RiftA_CenterHole"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_CrackTex);
            SAMPLER(sampler_CrackTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Center;
                float4 _CrackColor;
                float4 _EdgeColor;

                float _CrackProgress;
                float _HoleProgress;
                float _OpenProgress;
                float _TravelProgress;

                float _Ready;
                float _Aspect;

                float _InitialHoleRadius;
                float _HoleIrregularity;
                float _EdgeWidth;

                float _GlowWidth;
                float _CrackIntensity;
                float _Refraction;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            /// <summary>
            /// UIの頂点をクリップ空間へ変換する。
            /// </summary>
            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv = input.uv;
                output.color = input.color;

                return output;
            }

            // 角度ごとに不規則な値を生成する。
            float Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            // ガラスが割れたような角張った穴の輪郭を計算する。
            float GetJaggedRadius(float angle, float irregularity)
            {
                // 周囲を18区画に分け、各区画の半径を変える。
                const float SECTOR_COUNT = 18.0;

                float normalizedAngle = (angle + 3.14159265359) / TWO_PI;
                float sectorPosition = normalizedAngle * SECTOR_COUNT;

                float sectorIndex = floor(sectorPosition);
                float sectorFraction = frac(sectorPosition);

                float radiusA = Hash11(sectorIndex + 1.0);
                float radiusB = Hash11(sectorIndex + 2.0);

                // 隣接する頂点間を直線的につなぐことで角を残す。
                float radialNoise = lerp(
                    radiusA,
                    radiusB,
                    sectorFraction);

                return 1.0 + (radialNoise * 2.0 - 1.0) * irregularity;
            }

            /// <summary>
            /// 画面上のひび割れ、中心穴、3Dステージを合成する。
            /// </summary>
            half4 Frag(Varyings input) : SV_Target
            {
                float crackProgress = saturate(_CrackProgress);
                float holeProgress =
                    saturate(_HoleProgress) * saturate(_Ready);
                float openProgress =
                    saturate(_OpenProgress) * saturate(_Ready);

                if (crackProgress <= 0.00001 &&
                    holeProgress <= 0.00001 &&
                    openProgress <= 0.00001)
                {
                    return half4(0, 0, 0, 0);
                }

                // 画面中央ではなく、選択したステージ画像の中心を使う。
                float2 relative = input.uv - _Center.xy;

                // 縦横比を補正して穴を円形に近い比率で計算する。
                float2 p = float2(
                    relative.x * _Aspect,
                    relative.y);

                float distanceFromCenter = length(p);
                float angle = atan2(p.y, p.x);

                // ガラスのひび割れ画像を中心に配置する。
                float2 crackUV =
                    0.5 + relative * float2(_Aspect / 1.77778, 1.0);

                crackUV = saturate(crackUV);

                float4 crackSample =
                    SAMPLE_TEXTURE2D(
                        _CrackTex,
                        sampler_CrackTex,
                        crackUV);

                // ひびが中心から外側へ広がる。
                float crackRadius = length(crackUV - 0.5);

                float crackSpread = 1.0 - smoothstep(
                    crackProgress * 0.8 - 0.045,
                    crackProgress * 0.8,
                    crackRadius);

                float crackLine =
                    saturate(
                        crackSample.r * 1.4 +
                        crackSample.g * 0.13) * crackSpread;

                float crackGlow =
                    saturate(crackSample.g * 1.8) * crackSpread;

                // 中心に最初に開く小さい穴の半径。
                // HoleProgressが1になっても小さい穴のまま維持する。
                float smallHoleRadius =
                    _InitialHoleRadius *
                    pow(holeProgress, 0.75);

                // 後半では中心穴を全画面へ拡大する。
                // 半径を十分大きくして、最後に画面の四隅まで覆う。
                float expandedRadius =
                    2.6 * pow(openProgress, 1.55);

                float holeRadius =
                    smallHoleRadius + expandedRadius;

                // 放射状に角張った穴を作る。
                float jaggedFactor = GetJaggedRadius(
                    angle,
                    _HoleIrregularity);

                // 輪郭にわずかな凹凸を追加する。
                float fineNoise =
                    sin(angle * 37.0 + 0.7) * 0.018 +
                    sin(angle * 53.0 - 1.1) * 0.012;

                float irregularRadius =
                    holeRadius * (jaggedFactor + fineNoise);

                // 穴の境界。負の値なら穴の内部。
                float holeDistance =
                    distanceFromCenter - irregularRadius;

                // 穴が完全に閉じている状態を判定する。
                float hasHole = step(
                    0.00001,
                    holeProgress + openProgress);

                // 穴の内側だけ3Dステージを表示する。
                float insideHole =
                    (1.0 - smoothstep(
                        -_EdgeWidth,
                        _EdgeWidth,
                        holeDistance)) * hasHole;

                // 穴の周辺に発光を追加する。
                float edgeMask =
                    (1.0 - smoothstep(
                        0.0,
                        _GlowWidth,
                        abs(holeDistance))) * hasHole;

                // 亀裂線は穴の外側だけ表示する。
                float visibleCracks =
                    saturate(crackLine + crackGlow * 0.28) *
                    (1.0 - insideHole);

                // 穴の縁が強く発光する。
                float edgeLight =
                    edgeMask *
                    (1.0 - insideHole) *
                    (0.8 + 0.4 * saturate(_TravelProgress));

                // 穴の縁だけ少し屈折したように見せる。
                float2 outwardDirection =
                    p / max(distanceFromCenter, 0.0001);

                float2 distortion = float2(
                    outwardDirection.x / max(_Aspect, 0.0001),
                    outwardDirection.y);

                float2 destinationUV =
                    input.uv + distortion * _Refraction * edgeMask;

                // RenderTextureのUV自体は拡大しない。
                // 奥行きの変化は3Dカメラの移動で表現する。
                half3 previewColor =
                    SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        saturate(destinationUV)).rgb;

                // ひび割れの色と穴の映像を合成する。
                float fractureAlpha =
                    saturate(visibleCracks * _CrackIntensity);

                float edgeAlpha = saturate(edgeLight);

                float alpha = saturate(
                    insideHole +
                    fractureAlpha +
                    edgeAlpha);

                half3 combinedColor =
                    previewColor * insideHole +
                    _CrackColor.rgb * fractureAlpha +
                    _EdgeColor.rgb * edgeAlpha;

                // 透明度に応じて色を補正する。
                half3 finalColor =
                    combinedColor / max(alpha, 0.0001);

                finalColor *=
                    input.color.rgb * _Color.rgb;

                return half4(
                    finalColor,
                    alpha * input.color.a * _Color.a);
            }

            ENDHLSL
        }
    }
}
