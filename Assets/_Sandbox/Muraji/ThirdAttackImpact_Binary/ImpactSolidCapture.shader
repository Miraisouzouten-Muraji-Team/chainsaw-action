Shader "Project/Impact/SolidCapture"
{
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4x4 _ImpactCaptureVP;
            float _ImpactCaptureY;
            float4 Vert(float4 vertex : POSITION) : SV_POSITION
            {
                float4 clip = mul(_ImpactCaptureVP, mul(unity_ObjectToWorld, vertex));
                clip.y *= _ImpactCaptureY;
                return clip;
            }
            half4 Frag() : SV_Target
            {
                return half4(0,0,0,1);
            }
            ENDHLSL
        }
    }
}
