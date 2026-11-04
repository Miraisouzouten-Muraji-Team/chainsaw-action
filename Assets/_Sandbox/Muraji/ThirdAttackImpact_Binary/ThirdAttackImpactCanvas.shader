Shader "Project/Impact/ThirdAttackImpactCanvas"
{
    Properties
    {
        [PerRendererData] _MainTex ("Actor Capture", 2D) = "black" {}
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
                half3 rgb = tex2D(_MainTex, uv).rgb;
                half distanceToKey = length(rgb - half3(0, 1, 0));
                return smoothstep(0.04h, 0.25h, distanceToKey);
            }

            half4 Frag(Varyings i) : SV_Target
            {
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
