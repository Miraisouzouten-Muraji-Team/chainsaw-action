Shader "Project/Impact/ThirdAttackImpactCanvas"
{
    Properties
    {
        [PerRendererData] _MainTex ("Actor Capture", 2D) = "black" {}
        _CaptureFlipY ("Capture Flip Y", Float) = 0
        _ImpactZoom ("Zoom", Vector) = (0.5, 0.5, 1, 0)
        _ThresholdSilhouette ("Silhouette", Float) = 1
        _ThresholdBackground ("Background", Color) = (1,1,1,1)
        [HideInInspector] _ImpactCanvasVersion ("Version", Float) = 2
        _UseThreshold ("Use Threshold", Float) = 1
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _ActorWhite ("Actor White", Float) = 0
        _ImpactOutline ("Outline", Vector) = (0.5, 0.5, 14, 0.7)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _ImpactOutline;
            float _ActorWhite;
            float _ThresholdSilhouette;
            float4 _ThresholdBackground;
            float _UseThreshold;
            float _Threshold;
            float4 _ImpactZoom;
            float _CaptureFlipY;

            float2 CaptureUV(float2 uv)
            {
                if (_CaptureFlipY > 0.5) uv.y = 1 - uv.y;
                return saturate(uv);
            }
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Input v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }
            half SampleActor(float2 uv)
            {
                float2 captureUV = _ImpactZoom.xy + (uv - 0.5) * _ImpactZoom.z;
                half3 rgb = tex2D(_MainTex, CaptureUV(captureUV)).rgb;
                half distanceToKey = length(rgb - half3(0, 1, 0));
                return smoothstep(0.04h, 0.25h, distanceToKey);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (_UseThreshold > 0.5)
                {
                    half3 rgb = tex2D(_MainTex, CaptureUV(i.uv)).rgb;
                    half luminance = dot(rgb, half3(0.2126, 0.7152, 0.0722));
                    half bw = step(_Threshold, luminance);
                    half mask = step(0.5h, SampleActor(i.uv));
                    half bg = step(_Threshold, dot(_ThresholdBackground.rgb,
                        half3(0.2126, 0.7152, 0.0722)));
                    half subject = _ThresholdSilhouette > 0.5 ? 1 - bg : bw;
                    bw = lerp(bg, subject, mask);
                    bw = lerp(bw, 1 - bw, _ActorWhite);
                    return half4(bw.xxx, 1);
                }

                half actor = SampleActor(i.uv);
                half tone = lerp(1 - _ActorWhite, _ActorWhite, actor);
                float2 texel = abs(_MainTex_TexelSize.xy);
                float2 ray = (i.uv - _ImpactOutline.xy) / texel;
                float2 offset = ray / max(length(ray), 1) * texel * _ImpactOutline.z;
                half cyan = 0;
                half violet = 0;
                [unroll] for (int n = 1; n <= 6; n++)
                {
                    float t = n / 6.0;
                    float weight = (1 - 0.65 * t) / 4;
                    cyan += SampleActor(saturate(i.uv - offset * t * 0.6)) * weight;
                    violet += SampleActor(saturate(i.uv - offset * t)) * weight;
                }
                half3 tint = lerp(half3(0.55, 0.09, 1), half3(0.03, 0.76, 1),
                    cyan / max(cyan + violet, 0.001));
                half opacity = saturate(max(cyan, violet) * (1 - actor) * _ImpactOutline.w);
                return half4(lerp(tone.xxx, tint, opacity), 1);
            }
            ENDHLSL
        }
    }
}
