using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// 統合Depth・Normalを使用してアウトラインを描画するRenderer Feature。
    /// </summary>
    public sealed class PixelOutlineRendererFeature : ScriptableRendererFeature
    {
        private const string USE_COMPOSITE_TEXTURES = "_UseCompositeTextures";
        private const string SCENE_NORMAL_PREVIEW_ENABLED = "_SceneNormalPreviewEnabled";
        private static readonly int BLIT_TEXTURE_ID = Shader.PropertyToID("_BlitTexture");
        private static readonly int BLIT_SCALE_BIAS_ID = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BLIT_MIP_LEVEL_ID = Shader.PropertyToID("_BlitMipLevel");
        private static readonly int COMPOSITE_DEPTH_ID = Shader.PropertyToID("_PixelCompositeDepthTex");
        private static readonly int COMPOSITE_NORMAL_ID = Shader.PropertyToID("_PixelCompositeNormalTex");

        [Header("Outline Material")]
        [SerializeField] private Material _outlineMaterial;

        private OutlinePass _outlinePass;

        /// <summary>
        /// アウトライン用Render Passを初期化する。
        /// </summary>
        public override void Create()
        {
            _outlinePass = new OutlinePass(_outlineMaterial)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// Pixel描画が有効なCameraにOutline Passを追加する。
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_outlinePass == null || _outlineMaterial == null) return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView) return;

            // 通常描画用のOutlineは既存Rendererに任せる。
            if (_outlineMaterial.GetFloat(USE_COMPOSITE_TEXTURES) < 0.5f) return;

            // Sceneビューの通常プレビューは既にOutlineが描かれているため処理しない。
            if (_outlineMaterial.GetFloat(SCENE_NORMAL_PREVIEW_ENABLED) > 0.5f) return;

            renderer.EnqueuePass(_outlinePass);
        }

        /// <summary>
        /// 統合Depth・Normalを入力として画面にアウトラインを描画する。
        /// </summary>
        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly Material _material;

            /// <summary>
            /// Render Graphの描画時に使用するデータ。
            /// </summary>
            private sealed class PassData
            {
                public Material Material;
                public TextureHandle Color;
                public TextureHandle Depth;
                public TextureHandle Normal;
            }

            public OutlinePass(Material material)
            {
                _material = material;
                requiresIntermediateTexture = true;
            }

            /// <summary>
            /// Render Graphへ入力・出力リソースと描画処理を登録する。
            /// </summary>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || !frameData.Contains<PixelOpaqueFrameData>()) return;

                // Opaque合成Passで生成したDepth・Normalを取得する。
                PixelOpaqueFrameData opaqueData = frameData.Get<PixelOpaqueFrameData>();
                if (!opaqueData.Depth.IsValid() || !opaqueData.Normal.IsValid()) return;

                // 現在のCamera Colorを取得する。
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle sourceColor = resourceData.activeColorTexture;
                if (!sourceColor.IsValid() || resourceData.isActiveTargetBackBuffer) return;

                // 描画中のColorを直接読み書きしないようにコピー先を作成する。
                TextureDesc colorDescriptor = renderGraph.GetTextureDesc(sourceColor);
                colorDescriptor.name = "PixelOutlineColorCopy";
                colorDescriptor.clearBuffer = false;
                colorDescriptor.msaaSamples = MSAASamples.None;
                TextureHandle copiedColor = renderGraph.CreateTexture(colorDescriptor);

                // 現在の画面色を一時RTへコピーする。
                renderGraph.AddBlitPass(sourceColor, copiedColor, Vector2.one, Vector2.zero, passName: "Pixel Outline Copy Color");

                // 統合Depth・Normalを使って元のCamera ColorへOutlineを描画する。
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Pixel Outline Composite", out var passData))
                {
                    passData.Material = _material;
                    passData.Color = copiedColor;
                    passData.Depth = opaqueData.Depth;
                    passData.Normal = opaqueData.Normal;

                    // Color・Depth・Normalの読み取り依存関係を明示する。
                    builder.UseTexture(passData.Color, AccessFlags.Read);
                    builder.UseTexture(passData.Depth, AccessFlags.Read);
                    builder.UseTexture(passData.Normal, AccessFlags.Read);

                    // Outlineを適用した色をCamera Colorへ書き戻す。
                    builder.SetRenderAttachment(sourceColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        // Shaderで使用するColor・Depth・Normalを設定する。
                        data.Material.SetTexture(BLIT_TEXTURE_ID, data.Color);
                        data.Material.SetTexture(COMPOSITE_DEPTH_ID, data.Depth);
                        data.Material.SetTexture(COMPOSITE_NORMAL_ID, data.Normal);
                        data.Material.SetVector(BLIT_SCALE_BIAS_ID, new Vector4(1f, 1f, 0f, 0f));
                        data.Material.SetFloat(BLIT_MIP_LEVEL_ID, 0f);

                        // 既存のOutline Shaderでフルスクリーントライアングルを描画する。
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }
        }
    }
}
