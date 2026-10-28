using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Transparent描画前後の色差分だけを指定解像度でドット化するRenderer Feature。
    /// Opaqueの解像度と既存Transparentの描画方式は変更しない。
    /// </summary>
    public sealed class PixelTransparentPixelationRendererFeature : ScriptableRendererFeature
    {
        private static readonly int BEFORE_TEX_ID = Shader.PropertyToID("_PixelEffectBeforeTex");
        private static readonly int AFTER_TEX_ID = Shader.PropertyToID("_PixelEffectAfterTex");
        private static readonly int DELTA_TEX_ID = Shader.PropertyToID("_PixelEffectDeltaTex");
        private static readonly int LOW_RES_TEXEL_SIZE_ID = Shader.PropertyToID("_PixelEffectLowResTexelSize");
        private static readonly int BLIT_SCALE_BIAS_ID = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BLIT_MIP_LEVEL_ID = Shader.PropertyToID("_BlitMipLevel");

        [Header("Shader")]
        [SerializeField] private Shader _pixelationShader;

        [Header("Effect Resolution")]
        [SerializeField, Min(1)] private int _textureWidth = 320;
        [SerializeField, Min(1)] private int _textureHeight = 180;

        private Material _material;
        private BeforeTransparentPass _beforePass;
        private AfterTransparentPass _afterPass;

        /// <summary>
        /// 描画PassとMaterialを初期化する。
        /// </summary>
        public override void Create()
        {
            CoreUtils.Destroy(_material);

            _material = _pixelationShader != null
                ? CoreUtils.CreateEngineMaterial(_pixelationShader)
                : null;

            _beforePass = new BeforeTransparentPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };

            _afterPass = new AfterTransparentPass(
                _material,
                Mathf.Max(1, _textureWidth),
                Mathf.Max(1, _textureHeight))
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
        }

        /// <summary>
        /// Pixel Rendering用Cameraへ描画Passを登録する。
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || _beforePass == null || _afterPass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

            Camera camera = renderingData.cameraData.camera;
            if (camera == null || camera.TryGetComponent<PixelCaptureViewMarker>(out _)) return;

            // Inspectorで変更した解像度を反映する。
            _afterPass.SetResolution(
                Mathf.Max(1, _textureWidth),
                Mathf.Max(1, _textureHeight));

            renderer.EnqueuePass(_beforePass);
            renderer.EnqueuePass(_afterPass);
        }

        /// <summary>
        /// 生成したMaterialを破棄する。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        /// <summary>
        /// Transparent描画前のColorを保存する。
        /// </summary>
        private sealed class BeforeTransparentPass : ScriptableRenderPass
        {
            public BeforeTransparentPass()
            {
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid()) return;

                TextureDesc descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "PixelEffectBeforeTransparent";
                descriptor.clearBuffer = false;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.depthBufferBits = DepthBits.None;

                TextureHandle beforeColor = renderGraph.CreateTexture(descriptor);

                renderGraph.AddBlitPass(
                    source,
                    beforeColor,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Pixel Effect Save Before Transparent");

                PixelTransparentFrameData transparentData =
                    frameData.GetOrCreate<PixelTransparentFrameData>();

                transparentData.BeforeColor = beforeColor;
            }
        }

        /// <summary>
        /// Transparent描画後の色差分を指定解像度でドット化する。
        /// </summary>
        private sealed class AfterTransparentPass : ScriptableRenderPass
        {
            private readonly Material _material;

            private int _textureWidth;
            private int _textureHeight;

            private sealed class ExtractPassData
            {
                public Material Material;
                public TextureHandle BeforeColor;
                public TextureHandle AfterColor;
                public Vector4 LowResTexelSize;
            }

            private sealed class CompositePassData
            {
                public Material Material;
                public TextureHandle BeforeColor;
                public TextureHandle DeltaColor;
            }

            public AfterTransparentPass(Material material, int textureWidth, int textureHeight)
            {
                _material = material;
                _textureWidth = textureWidth;
                _textureHeight = textureHeight;

                requiresIntermediateTexture = true;
            }

            /// <summary>
            /// 差分を保存するRenderTextureの解像度を設定する。
            /// </summary>
            public void SetResolution(int textureWidth, int textureHeight)
            {
                _textureWidth = Mathf.Max(1, textureWidth);
                _textureHeight = Mathf.Max(1, textureHeight);
            }

            /// <summary>
            /// Render Graphへ差分抽出と最終合成を登録する。
            /// </summary>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || !frameData.Contains<PixelTransparentFrameData>()) return;

                PixelTransparentFrameData transparentData =
                    frameData.Get<PixelTransparentFrameData>();

                if (!transparentData.BeforeColor.IsValid()) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle afterColor = resourceData.activeColorTexture;
                if (!afterColor.IsValid()) return;

                // Inspectorで指定された解像度をそのまま使用する。
                int width = _textureWidth;
                int height = _textureHeight;

                // Transparentの差分を保存する低解像度RTを生成する。
                TextureDesc descriptor = renderGraph.GetTextureDesc(afterColor);

                descriptor.name = "PixelEffectLowResolutionDelta";
                descriptor.width = width;
                descriptor.height = height;
                descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.clearBuffer = false;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.filterMode = FilterMode.Point;

                TextureHandle deltaColor = renderGraph.CreateTexture(descriptor);

                // Transparent描画前後の差分を指定解像度RTへ書き出す。
                using (var builder = renderGraph.AddRasterRenderPass<ExtractPassData>(
                    "Pixel Effect Extract Low Resolution Delta",
                    out var passData))
                {
                    passData.Material = _material;
                    passData.BeforeColor = transparentData.BeforeColor;
                    passData.AfterColor = afterColor;

                    passData.LowResTexelSize = new Vector4(
                        1f / width,
                        1f / height,
                        width,
                        height);

                    builder.UseTexture(passData.BeforeColor, AccessFlags.Read);
                    builder.UseTexture(passData.AfterColor, AccessFlags.Read);

                    builder.SetRenderAttachment(deltaColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (ExtractPassData data, RasterGraphContext context) =>
                    {
                        data.Material.SetTexture(BEFORE_TEX_ID, data.BeforeColor);
                        data.Material.SetTexture(AFTER_TEX_ID, data.AfterColor);
                        data.Material.SetVector(LOW_RES_TEXEL_SIZE_ID, data.LowResTexelSize);
                        data.Material.SetVector(BLIT_SCALE_BIAS_ID, new Vector4(1f, 1f, 0f, 0f));
                        data.Material.SetFloat(BLIT_MIP_LEVEL_ID, 0f);

                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.Material,
                            0,
                            MeshTopology.Triangles,
                            3,
                            1);
                    });
                }

                // ドット化した差分を元のOpaque Colorへ合成する。
                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                    "Pixel Effect Composite",
                    out var passData))
                {
                    passData.Material = _material;
                    passData.BeforeColor = transparentData.BeforeColor;
                    passData.DeltaColor = deltaColor;

                    builder.UseTexture(passData.BeforeColor, AccessFlags.Read);
                    builder.UseTexture(passData.DeltaColor, AccessFlags.Read);

                    builder.SetRenderAttachment(afterColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                    {
                        data.Material.SetTexture(BEFORE_TEX_ID, data.BeforeColor);
                        data.Material.SetTexture(DELTA_TEX_ID, data.DeltaColor);
                        data.Material.SetVector(BLIT_SCALE_BIAS_ID, new Vector4(1f, 1f, 0f, 0f));
                        data.Material.SetFloat(BLIT_MIP_LEVEL_ID, 0f);

                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.Material,
                            1,
                            MeshTopology.Triangles,
                            3,
                            1);
                    });
                }
            }
        }
    }
}
