
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Pixel ON/OFFの両方でVolumetric Fogを描画する。
    /// Pixel ONでは統合Depth、OFFではカメラDepthを使用する。
    /// </summary>
    public sealed class PixelVolumetricFogRendererFeature : ScriptableRendererFeature
    {
        private const string PIXEL_EFFECT_ENABLED = "_PixelEffectEnabled";
        private const string SCENE_NORMAL_PREVIEW_ENABLED = "_SceneNormalPreviewEnabled";

        private static readonly int BLIT_TEXTURE_ID =
            Shader.PropertyToID("_BlitTexture");
        private static readonly int BLIT_SCALE_BIAS_ID =
            Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BLIT_MIP_LEVEL_ID =
            Shader.PropertyToID("_BlitMipLevel");
        private static readonly int COMPOSITE_DEPTH_ID =
            Shader.PropertyToID("_PixelCompositeDepthTex");

        [Header("Fog Material")]
        [SerializeField] private Material _fogMaterial;

        [Header("Depth Source")]
        [SerializeField] private bool _useCompositeDepth = true;

        private FogPass _fogPass;

        /// <summary>
        /// Fog用Render Passを生成する。
        /// </summary>
        public override void Create()
        {
            _fogPass = new FogPass(_fogMaterial, _useCompositeDepth)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// 対象カメラへFog Passを登録する。
        /// </summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_fogPass == null || _fogMaterial == null)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;
            if (camera == null ||
                (camera.cameraType != CameraType.Game &&
                 camera.cameraType != CameraType.SceneView))
            {
                return;
            }

            // Capture Cameraでは二重適用しない。
            if (camera.TryGetComponent<PixelCaptureViewMarker>(out _))
            {
                return;
            }

            bool pixelEnabled =
                _fogMaterial.GetFloat(PIXEL_EFFECT_ENABLED) > 0.5f;

            // 使用するDepthとMaterialの描画モードを一致させる。
            if (pixelEnabled != _useCompositeDepth)
            {
                return;
            }

            // Sceneビューの通常プレビューには統合Fogを適用しない。
            if (_useCompositeDepth &&
                _fogMaterial.GetFloat(SCENE_NORMAL_PREVIEW_ENABLED) > 0.5f)
            {
                return;
            }

            renderer.EnqueuePass(_fogPass);
        }

        /// <summary>
        /// Render Graphで画面色とDepthを参照してFogを適用する。
        /// </summary>
        private sealed class FogPass : ScriptableRenderPass
        {
            private readonly Material _material;
            private readonly bool _useCompositeDepth;

            private sealed class PassData
            {
                public Material Material;
                public TextureHandle Color;
                public TextureHandle Depth;
                public bool UseCompositeDepth;
            }

            public FogPass(Material material, bool useCompositeDepth)
            {
                _material = material;
                _useCompositeDepth = useCompositeDepth;
                requiresIntermediateTexture = true;

                // 通常描画では_CameraDepthTextureを使用する。
                if (!_useCompositeDepth)
                {
                    ConfigureInput(ScriptableRenderPassInput.Depth);
                }
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_material == null)
                {
                    return;
                }

                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle sourceColor =
                    resourceData.activeColorTexture;

                if (!sourceColor.IsValid())
                {
                    return;
                }

                TextureHandle depth;

                if (_useCompositeDepth)
                {
                    // Pixel ONではNear/Middle/Farの統合Depthを使用。
                    if (!frameData.Contains<PixelOpaqueFrameData>())
                    {
                        return;
                    }

                    PixelOpaqueFrameData opaqueData =
                        frameData.Get<PixelOpaqueFrameData>();

                    depth = opaqueData.Depth;
                }
                else
                {
                    // Pixel OFFではURPのカメラDepthを使用。
                    depth = resourceData.cameraDepthTexture;
                }

                if (!depth.IsValid())
                {
                    return;
                }

                // 描画前の画面色を別テクスチャへコピーする。
                TextureDesc descriptor =
                    renderGraph.GetTextureDesc(sourceColor);

                descriptor.name = "VolumetricFogColorCopy";
                descriptor.clearBuffer = false;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.depthBufferBits = DepthBits.None;

                TextureHandle copiedColor =
                    renderGraph.CreateTexture(descriptor);

                renderGraph.AddBlitPass(
                    sourceColor,
                    copiedColor,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Volumetric Fog Copy Color");

                using (var builder =
                    renderGraph.AddRasterRenderPass<PassData>(
                        "Volumetric Fog",
                        out var passData))
                {
                    passData.Material = _material;
                    passData.Color = copiedColor;
                    passData.Depth = depth;
                    passData.UseCompositeDepth = _useCompositeDepth;

                    builder.UseTexture(
                        copiedColor, AccessFlags.Read);

                    builder.UseTexture(
                        depth, AccessFlags.Read);

                    // Point Lightの3D照明とShadow Mapを参照する。
                    builder.UseAllGlobalTextures(true);

                    builder.SetRenderAttachment(
                        sourceColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (
                        PassData data,
                        RasterGraphContext context) =>
                    {
                        data.Material.SetTexture(
                            BLIT_TEXTURE_ID,
                            data.Color);

                        if (data.UseCompositeDepth)
                        {
                            data.Material.SetTexture(
                                COMPOSITE_DEPTH_ID,
                                data.Depth);
                        }

                        data.Material.SetVector(
                            BLIT_SCALE_BIAS_ID,
                            new Vector4(1f, 1f, 0f, 0f));

                        data.Material.SetFloat(
                            BLIT_MIP_LEVEL_ID, 0f);

                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.Material,
                            0,
                            MeshTopology.Triangles,
                            3,
                            1);
                    });
                }
            }
        }
    }
}
