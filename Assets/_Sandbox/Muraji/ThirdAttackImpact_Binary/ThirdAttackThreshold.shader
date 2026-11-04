Shader "Project/Impact/ThirdAttackThreshold"
{
    Properties
    {
        [HideInInspector] _ImpactOutline ("Outline", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _ThirdAttackImpactParams ("Impact Params", Vector) = (0, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ThirdAttackThreshold"
            ZTest Always ZWrite Off Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Per-pass material data. No camera callbacks or global shader values.
            CBUFFER_START(UnityPerMaterial)
                float4 _ThirdAttackImpactParams;
                float4 _ImpactOutline;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half bg = 1.0h - _ThirdAttackImpactParams.z;
                return half4(bg, bg, bg, 1.0h);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ImpactActorSilhouette"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex ActorVertex
            #pragma fragment ActorFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ThirdAttackImpactParams;
                float4 _ImpactOutline;
            CBUFFER_END
            float4 ActorVertex(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }
            half4 ActorFragment() : SV_Target
            {
                half fg = _ThirdAttackImpactParams.z;
                return half4(fg, fg, fg, 1.0h);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ImpactOutlineBlur"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ThirdAttackImpactParams;
                float4 _ImpactOutline;
            CBUFFER_END

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_ImpactOutline.z <= 0 || _ImpactOutline.w <= 0)
                    return original;

                float2 center = _ImpactOutline.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    center.y = 1.0 - center.y;
                #endif
                center = center * _BlitScaleBias.xy + _BlitScaleBias.zw;
                float2 texel = abs(_BlitTexture_TexelSize.xy);
                float2 ray = (uv - center) / texel;
                float2 dir = ray / max(length(ray), 1.0);
                float2 offset = dir * texel * _ImpactOutline.z;
                half bg = 1.0h - _ThirdAttackImpactParams.z;
                half outside = 1.0h - abs(original.r - bg);
                half cyan = 0.0h;
                half violet = 0.0h;
                [unroll] for (int i = 1; i <= 6; i++)
                {
                    float t = i / 6.0;
                    half weight = (1.0h - 0.65h * t) / 4.0h;
                    cyan += abs(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                        uv - offset * t * 0.6).r - bg) * weight;
                    violet += abs(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                        uv - offset * t).r - bg) * weight;
                }
                half3 tint = lerp(half3(0.55, 0.09, 1.0), half3(0.03, 0.76, 1.0),
                    cyan / max(cyan + violet, 0.001h));
                half opacity = saturate(max(cyan, violet) * outside * outside * _ImpactOutline.w);
                return half4(lerp(original.rgb, tint, opacity), original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
