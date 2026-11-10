
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Transparentを背景から分離して描画し、指定解像度でドット化する。
    /// GameObject LayerやRendering Layerには依存しない。
    /// </summary>
    public sealed class PixelTransparentPixelationRendererFeature : ScriptableRendererFeature
    {
        [Header("Shader")]
        [SerializeField] private Shader _pixelationShader;

        [Header("Effect Resolution")]
        [SerializeField, Min(1)] private int _textureWidth = 640;
        [SerializeField, Min(1)] private int _textureHeight = 360;

        private Material _material;
        private TransparentPixelationPass _pass;

        /// <summary>
        /// Materialと描画Passを初期化する。
        /// Unity APIを使用するID生成はフィールド初期化時に行わない。
        /// </summary>
        public override void Create()
        {
            CoreUtils.Destroy(_material);

            _material = _pixelationShader != null
                ? CoreUtils.CreateEngineMaterial(_pixelationShader)
                : null;

            // ScriptableObjectの型初期化中にShaderTagIdを生成しない。
            List<ShaderTagId> shaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("LightweightForward")
            };

            // Shader Property IDもCreate内で生成する。
            int blackTextureId =
                Shader.PropertyToID("_PixelEffectBlackTex");

            int whiteTextureId =
                Shader.PropertyToID("_PixelEffectWhiteTex");

            int effectTextureId =
                Shader.PropertyToID("_PixelEffectDataTex");

            int texelSizeId =
                Shader.PropertyToID("_PixelEffectTexelSize");

            _pass = new TransparentPixelationPass(
                _material,
                shaderTags,
                blackTextureId,
                whiteTextureId,
                effectTextureId,
                texelSizeId)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        /// <summary>
        /// Pixel Rendering用Game Cameraへ描画Passを登録する。
        /// </summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_material == null || _pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

            Camera camera = renderingData.cameraData.camera;
            if (camera == null) return;

            // Near / Middle / FarのCapture Cameraには適用しない。
            if (camera.TryGetComponent<PixelCaptureViewMarker>(out _))
            {
                return;
            }

            _pass.SetResolution(
                Mathf.Max(1, _textureWidth),
                Mathf.Max(1, _textureHeight));

            renderer.EnqueuePass(_pass);
        }

        /// <summary>
        /// Renderer Featureが生成したMaterialを解放する。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }

        /// <summary>
        /// Transparent描画、低解像度化、最終合成を管理する。
        /// </summary>
        private sealed class TransparentPixelationPass : ScriptableRenderPass
        {
            private readonly Material _material;
            private readonly List<ShaderTagId> _shaderTags;

            private readonly int _blackTextureId;
            private readonly int _whiteTextureId;
            private readonly int _effectTextureId;
            private readonly int _texelSizeId;

            private int _textureWidth;
            private int _textureHeight;

            private sealed class DrawPassData
            {
                public RendererListHandle RendererList;
                public Color ClearColor;
            }

            private sealed class ExtractPassData
            {
                public Material Material;
                public Vector4 TexelSize;
                public int TexelSizeId;
            }

            private sealed class CompositePassData
            {
                public Material Material;
                public TextureHandle OpaqueColor;
            }

            public TransparentPixelationPass(
                Material material,
                List<ShaderTagId> shaderTags,
                int blackTextureId,
                int whiteTextureId,
                int effectTextureId,
                int texelSizeId)
            {
                _material = material;
                _shaderTags = shaderTags;

                _blackTextureId = blackTextureId;
                _whiteTextureId = whiteTextureId;
                _effectTextureId = effectTextureId;
                _texelSizeId = texelSizeId;

                requiresIntermediateTexture = true;
            }

            /// <summary>
            /// 透明エフェクトのドット化解像度を設定する。
            /// </summary>
            public void SetResolution(int width, int height)
            {
                _textureWidth = Mathf.Max(1, width);
                _textureHeight = Mathf.Max(1, height);
            }

            /// <summary>
            /// Render Graphに各描画Passを登録する。
            /// </summary>
            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_material == null) return;

                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle opaqueColor = resourceData.activeColorTexture;
                TextureHandle opaqueDepth = resourceData.activeDepthTexture;

                if (!opaqueColor.IsValid() || !opaqueDepth.IsValid()) return;

                UniversalRenderingData renderingData =
                    frameData.Get<UniversalRenderingData>();

                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                UniversalLightData lightData =
                    frameData.Get<UniversalLightData>();

                // GameObject LayerやRendering Layerによる絞り込みは行わない。
                // Render QueueがTransparentのオブジェクトを対象にする。
                FilteringSettings filteringSettings = new FilteringSettings(
                    RenderQueueRange.transparent,
                    -1);

                DrawingSettings drawingSettings =
                    RenderingUtils.CreateDrawingSettings(
                        _shaderTags,
                        renderingData,
                        cameraData,
                        lightData,
                        SortingCriteria.CommonTransparent);

                RendererListParams rendererListParams = new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings);

                // 黒背景用と白背景用に独立したRenderer Listを生成する。
                // 同一RendererListHandleを複数のPassで実行してはいけない。
                RendererListHandle blackRendererList =
                    renderGraph.CreateRendererList(rendererListParams);

                RendererListHandle whiteRendererList =
                    renderGraph.CreateRendererList(rendererListParams);

                if (!blackRendererList.IsValid() || !whiteRendererList.IsValid())
                {
                    return;
                }

                // Depthと同じ解像度で透明物を描画する。
                TextureDesc fullDescriptor =
                    renderGraph.GetTextureDesc(opaqueColor);

                fullDescriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                fullDescriptor.msaaSamples = MSAASamples.None;
                fullDescriptor.depthBufferBits = DepthBits.None;
                fullDescriptor.clearBuffer = false;

                // 黒背景への透明描画先。
                fullDescriptor.name = "PixelEffectBlack";

                TextureHandle blackColor =
                    renderGraph.CreateTexture(fullDescriptor);

                // 白背景への透明描画先。
                fullDescriptor.name = "PixelEffectWhite";

                TextureHandle whiteColor =
                    renderGraph.CreateTexture(fullDescriptor);

                // 黒背景へTransparentを描画する。
                AddDrawPass(
                    renderGraph,
                    blackRendererList,
                    opaqueDepth,
                    blackColor,
                    Color.black,
                    _blackTextureId,
                    "Pixel Effect Draw Black");

                // 白背景へTransparentを描画する。
                AddDrawPass(
                    renderGraph,
                    whiteRendererList,
                    opaqueDepth,
                    whiteColor,
                    Color.white,
                    _whiteTextureId,
                    "Pixel Effect Draw White");

                // Color ContributionとTransmittanceの保存先を作成する。
                TextureDesc effectDescriptor = fullDescriptor;

                effectDescriptor.name = "PixelEffectLowResolution";
                effectDescriptor.width = _textureWidth;
                effectDescriptor.height = _textureHeight;
                effectDescriptor.filterMode = FilterMode.Point;

                TextureHandle effectColor =
                    renderGraph.CreateTexture(effectDescriptor);

                // 黒背景・白背景の結果を指定解像度へ縮小する。
                using (var builder = renderGraph.AddRasterRenderPass<ExtractPassData>(
                    "Pixel Effect Extract",
                    out var passData))
                {
                    passData.Material = _material;
                    passData.TexelSize = new Vector4(
                        1f / _textureWidth,
                        1f / _textureHeight,
                        _textureWidth,
                        _textureHeight);
                    passData.TexelSizeId = _texelSizeId;

                    builder.UseTexture(blackColor, AccessFlags.Read);
                    builder.UseTexture(whiteColor, AccessFlags.Read);

                    builder.SetRenderAttachment(
                        effectColor,
                        0,
                        AccessFlags.Write);

                    // 後続の合成Passで参照できるようにする。
                    builder.SetGlobalTextureAfterPass(
                        effectColor,
                        _effectTextureId);

                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (
                        ExtractPassData data,
                        RasterGraphContext context) =>
                    {
                        // エフェクトの低解像度RTに対応したサンプリング間隔を設定する。
                        data.Material.SetVector(
                            data.TexelSizeId,
                            data.TexelSize);

                        // Blit.hlslのVert関数が使用するUV変換を設定する。
                        data.Material.SetVector(
                            "_BlitScaleBias",
                            new Vector4(1f, 1f, 0f, 0f));

                        data.Material.SetFloat("_BlitMipLevel", 0f);

                        // 入力テクスチャを必要としないフルスクリーン描画。
                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.Material,
                            0,
                            MeshTopology.Triangles,
                            3,
                            1);
                    });
                }

                // Opaqueと同じ解像度の最終Colorを作成する。
                TextureDesc outputDescriptor =
                    renderGraph.GetTextureDesc(opaqueColor);

                outputDescriptor.name = "PixelEffectComposite";
                outputDescriptor.clearBuffer = false;
                outputDescriptor.msaaSamples = MSAASamples.None;
                outputDescriptor.depthBufferBits = DepthBits.None;

                TextureHandle outputColor =
                    renderGraph.CreateTexture(outputDescriptor);

                // 元のOpaqueへ低解像度の透明エフェクトを合成する。
                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                    "Pixel Effect Composite",
                    out var passData))
                {
                    passData.Material = _material;
                    passData.OpaqueColor = opaqueColor;

                    builder.UseTexture(
                        opaqueColor,
                        AccessFlags.Read);

                    builder.UseTexture(
                        effectColor,
                        AccessFlags.Read);

                    builder.SetRenderAttachment(
                        outputColor,
                        0,
                        AccessFlags.Write);

                    builder.SetRenderFunc(static (
                        CompositePassData data,
                        RasterGraphContext context) =>
                    {
                        // _BlitTextureには元のOpaque Colorを設定する。
                        Blitter.BlitTexture(
                            context.cmd,
                            data.OpaqueColor,
                            new Vector4(1f, 1f, 0f, 0f),
                            data.Material,
                            1);
                    });
                }

                // 後続のURP描画処理が合成後のColorを使用する。
                resourceData.cameraColor = outputColor;
            }

            /// <summary>
            /// 元のMaterialのBlend設定で透明オブジェクトを描画する。
            /// </summary>
            private static void AddDrawPass(
                RenderGraph renderGraph,
                RendererListHandle rendererList,
                TextureHandle depth,
                TextureHandle color,
                Color clearColor,
                int globalTextureId,
                string passName)
            {
                using (var builder = renderGraph.AddRasterRenderPass<DrawPassData>(
                    passName,
                    out var passData))
                {
                    passData.RendererList = rendererList;
                    passData.ClearColor = clearColor;

                    builder.UseRendererList(rendererList);

                    builder.SetRenderAttachment(
                        color,
                        0,
                        AccessFlags.Write);

                    // 統合後のOpaque Depthを変更せずに参照する。
                    builder.SetRenderAttachmentDepth(
                        depth,
                        AccessFlags.Read);

                    // ライト・影などのグローバルテクスチャを利用可能にする。
                    builder.UseAllGlobalTextures(true);

                    // 描画したRTを後続のPassから参照できるようにする。
                    builder.SetGlobalTextureAfterPass(
                        color,
                        globalTextureId);

                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (
                        DrawPassData data,
                        RasterGraphContext context) =>
                    {
                        // Colorのみクリアする。Depthはクリアしない。
                        context.cmd.ClearRenderTarget(
                            RTClearFlags.Color,
                            data.ClearColor,
                            1f,
                            0);

                        context.cmd.DrawRendererList(data.RendererList);
                    });
                }
            }
        }
    }
}
