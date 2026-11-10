Shader "Tutorial/PlayerSilhouette"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4x4 _TutorialMaskVP;
            struct Attributes { float4 vertex : POSITION; };
            struct Varyings { float4 position : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = mul(_TutorialMaskVP, mul(unity_ObjectToWorld, input.vertex));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
    }
}
