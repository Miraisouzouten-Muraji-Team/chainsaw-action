using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// 統合Depthを使用してVolumetric Fogを描画するRenderer Feature。
    /// </summary>
    public sealed class PixelVolumetricFogRendererFeature : ScriptableRendererFeature
    {
        private const string PIXEL_EFFECT_ENABLED = "_PixelEffectEnabled";
        private const string SCENE_NORMAL_PREVIEW_ENABLED = "_SceneNormalPreviewEnabled";

        private static readonly int BLIT_TEXTURE_ID = Shader.PropertyToID("_BlitTexture");
        private static readonly int BLIT_SCALE_BIAS_ID = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BLIT_MIP_LEVEL_ID = Shader.PropertyToID("_BlitMipLevel");
        private static readonly int COMPOSITE_DEPTH_ID = Shader.PropertyToID("_PixelCompositeDepthTex");

        [Header("Fog Material")]
        [SerializeField] private Material _fogMaterial;

        private FogPass _fogPass;

        /// <summary>
        /// Fog用Render Passを初期化する。
        /// </summary>
        public override void Create()
        {
            _fogPass = new FogPass(_fogMaterial)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// Pixel描画が有効なCameraにFog Passを追加する。
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_fogPass == null || _fogMaterial == null) return;

            CameraType cameraType = renderingData.cameraData.cameraType;

            // 通常のGame CameraとScene Cameraのみ対象にする。
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView) return;

            // Pixel OFFの通常描画では既存のFog処理を使用する。
            if (_fogMaterial.GetFloat(PIXEL_EFFECT_ENABLED) < 0.5f) return;

            // Sceneビューの通常解像度プレビューでは統合Fogを実行しない。
            if (_fogMaterial.GetFloat(SCENE_NORMAL_PREVIEW_ENABLED) > 0.5f) return;

            renderer.EnqueuePass(_fogPass);
        }

        /// <summary>
        /// 統合Depthを入力としてFogを適用するRender Pass。
        /// </summary>
        private sealed class FogPass : ScriptableRenderPass
        {
            private readonly Material _material;

            /// <summary>
            /// Render Graph実行時に使用するリソースを保持する。
            /// </summary>
            private sealed class PassData
            {
                public Material Material;
                public TextureHandle Color;
                public TextureHandle Depth;
            }

            /// <summary>
            /// Fog描画用Passを生成する。
            /// </summary>
            public FogPass(Material material)
            {
                _material = material;

                // Camera Colorのコピーを使うため中間テクスチャを要求する。
                requiresIntermediateTexture = true;
            }

            /// <summary>
            /// Render GraphへFogの描画処理を登録する。
            /// </summary>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                // Opaque合成で生成したDepthがない場合はFogを描画しない。
                if (!frameData.Contains<PixelOpaqueFrameData>()) return;

                PixelOpaqueFrameData opaqueData = frameData.Get<PixelOpaqueFrameData>();

                if (!opaqueData.Depth.IsValid()) return;

                // Cameraの現在の描画先を取得する。
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Back Bufferへ直接読み書きしないようにする。
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle sourceColor = resourceData.activeColorTexture;

                if (!sourceColor.IsValid()) return;

                // 現在のCamera Colorと同じ形式のコピーRTを作成する。
                TextureDesc colorDescriptor = renderGraph.GetTextureDesc(sourceColor);
                colorDescriptor.name = "PixelFogColorCopy";
                colorDescriptor.clearBuffer = false;
                colorDescriptor.msaaSamples = MSAASamples.None;
                colorDescriptor.depthBufferBits = DepthBits.None;

                TextureHandle copiedColor = renderGraph.CreateTexture(colorDescriptor);

                // Fog適用前の画面色を保存する。
                renderGraph.AddBlitPass(
                    sourceColor,
                    copiedColor,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Pixel Fog Copy Color");

                // 保存した画面色と統合Depthを参照してFogを描画する。
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Pixel Volumetric Fog",
                    out var passData))
                {
                    passData.Material = _material;
                    passData.Color = copiedColor;
                    passData.Depth = opaqueData.Depth;

                    // 画面色の読み取り依存関係を登録する。
                    builder.UseTexture(passData.Color, AccessFlags.Read);

                    // 統合Depthの読み取り依存関係を登録する。
                    builder.UseTexture(passData.Depth, AccessFlags.Read);

                    // メインライトのShadow Mapなど、URPのGlobal Textureも参照可能にする。
                    // 将来的には必要なテクスチャのみを明示的に登録する。
                    builder.UseAllGlobalTextures(true);

                    // Fog適用結果をCamera Colorへ書き込む。
                    builder.SetRenderAttachment(sourceColor, 0, AccessFlags.Write);

                    // フルスクリーントライアングルでFogを描画する。
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        // Fog適用前の画面色をShaderへ渡す。
                        data.Material.SetTexture(BLIT_TEXTURE_ID, data.Color);

                        // Opaque合成で生成した統合DepthをShaderへ渡す。
                        data.Material.SetTexture(COMPOSITE_DEPTH_ID, data.Depth);

                        // URP Blit.hlslが使用するパラメータを設定する。
                        data.Material.SetVector(BLIT_SCALE_BIAS_ID, new Vector4(1f, 1f, 0f, 0f));
                        data.Material.SetFloat(BLIT_MIP_LEVEL_ID, 0f);

                        // 既存のVolumetricFog Shaderで画面全体を描画する。
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
