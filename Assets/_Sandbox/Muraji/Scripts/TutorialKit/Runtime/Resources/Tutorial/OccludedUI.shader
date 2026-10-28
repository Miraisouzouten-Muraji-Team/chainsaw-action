Shader "Tutorial/OccludedUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PlayerMask ("Player Mask", 2D) = "black" {}
        _FlipMaskY ("Flip Mask Y", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _PlayerMask;
            half4 _Color, _TextureSampleAdd;
            float _FlipMaskY;
            struct Attributes { float4 vertex : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screen = output.position;
                output.screen.xy = (output.position.xy + output.position.w) * 0.5;
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screen.xy / input.screen.w;
                screenUV.y = lerp(screenUV.y, 1 - screenUV.y, _FlipMaskY);
                half4 color = (tex2D(_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                color.a *= 1 - tex2D(_PlayerMask, screenUV).r;
                return color;
            }
            ENDHLSL
        }
    }
}
