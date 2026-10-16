Shader "Custom/Player Evade Afterimage"
{
    Properties
    {
        _AfterimageColor ("残像の色（実行中はスクリプトから指定）", Color) = (1, 0.45, 0, 0.65)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            // LightMode省略時、URPの通常3D描画ではSRPDefaultUnlitとして扱われる。
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _AfterimageColor;

            // 保存済みのMesh頂点をカメラ描画用の座標へ変換する。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            // テクスチャや照明を使わず、シルエットを指定色と透明度で描画する。
            half4 Frag(Varyings input) : SV_Target
            {
                return _AfterimageColor;
            }
            ENDCG
        }
    }
    Fallback Off
}
