Shader "Custom/StageSurface"
{
    Properties
    {
        // ============================================================
        // Stage Surface
        // ============================================================

        [MainTexture] _StageSurfaceMap("Surface Texture", 2D) = "white" {}
        [HDR] _StageSurfaceColor("Surface Color", Color) = (1, 1, 1, 1)
        _StageInnerColor("Inner Color", Color) = (0.25, 0.25, 0.25, 1)

        // ゲーム画面の地形外周から淵テクスチャを表示する幅（ワールド単位）。
        _StageEdgeWidth("Edge Width", Range(0, 2)) = 0.5

        // 1 Unity Unitあたりのテクスチャ投影間隔。
        _StageTileSize("Tile Size", Float) = 1.0
        _StageProjectionSharpness("Projection Sharpness", Range(1, 32)) = 8.0

        // ============================================================
        // Lit
        // ============================================================

        _Metallic("Metallic", Range(0, 1)) = 0.0
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)

        // URPの共通Passで使用するパラメータ。
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Surface("Surface Type", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }

        LOD 300

        // 各Passで共有するステージ専用の定義。
        HLSLINCLUDE

        #ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
        #endif

        #include "Assets/_Sandbox/Akane/Rendering/Shaders/Includes/StageSurfaceInput.hlsl"
        #include "Assets/_Sandbox/Akane/Rendering/Shaders/Includes/StageSurfaceCommon.hlsl"

        ENDHLSL

        // ============================================================
        // 1. GBuffer
        // Deferred Rendering用。
        // ============================================================

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            #pragma vertex LitGBufferPassVertex
            #pragma fragment LitGBufferPassFragment

            // 影とReflection Probe。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            // LightmapとGI。
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS

            // Rendering LayersとAPV。
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            // URP LitのSurfaceData生成だけを差し替える。
            #define InitializeStandardLitSurfaceData(uv, data) \
                InitializeStageSurfaceData(uv, input.positionWS, input.normalWS, data)

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"

            ENDHLSL
        }

        // ============================================================
        // 2. Forward
        // Forward Renderer使用時の描画用。
        // ============================================================

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5

            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // Main Light・Additional Lights・Shadow。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX

            // LightmapとGI。
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS

            // Rendering Layers・APV・Fog。
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            // GBufferと同じ表面計算を使用する。
            #define InitializeStandardLitSurfaceData(uv, data) \
                InitializeStageSurfaceData(uv, input.positionWS, input.normalWS, data)

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            ENDHLSL
        }

        // ============================================================
        // 3. ShadowCaster
        // ステージが影を落とすためのPass。
        // ============================================================

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            ENDHLSL
        }

        // ============================================================
        // 4. DepthOnly
        // Depth Capture用。
        // ============================================================

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

            ENDHLSL
        }

        // ============================================================
        // 5. DepthNormals
        // Normal CaptureとSSAO等で使用する。
        // ============================================================

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma multi_compile_instancing

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"

            ENDHLSL
        }

        // ============================================================
        // 6. Meta
        // 静的GIのLightmap Baking用。
        // ============================================================

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex StageMetaVertex
            #pragma fragment StageMetaFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct StageMetaAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct StageMetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            // Baking用の頂点位置と表面情報を渡す。
            StageMetaVaryings StageMetaVertex(StageMetaAttributes input)
            {
                StageMetaVaryings output;

                output.positionCS = UnityMetaVertexPosition(
                    input.positionOS.xyz,
                    input.uv1,
                    input.uv2);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            // 静的GIで使用するAlbedoとEmissionを出力する。
            half4 StageMetaFragment(StageMetaVaryings input) : SV_Target
            {
                SurfaceData surfaceData;

                InitializeStageSurfaceData(
                    float2(0.0, 0.0),
                    input.positionWS,
                    normalize(input.normalWS),
                    surfaceData);

                BRDFData brdfData;

                InitializeBRDFData(
                    surfaceData.albedo,
                    surfaceData.metallic,
                    surfaceData.specular,
                    surfaceData.smoothness,
                    surfaceData.alpha,
                    brdfData);

                UnityMetaInput metaInput = (UnityMetaInput)0;

                metaInput.Albedo =
                    brdfData.diffuse +
                    brdfData.specular * brdfData.roughness * 0.5;

                metaInput.Emission = surfaceData.emission;

                return UnityMetaFragment(metaInput);
            }

            ENDHLSL
        }
    }
}
