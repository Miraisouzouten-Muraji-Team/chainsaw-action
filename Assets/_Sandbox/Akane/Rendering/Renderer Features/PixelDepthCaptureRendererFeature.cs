using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Capture Cameraの深度を、
    /// Main Cameraから参照可能なRTへ保存する。
    /// </summary>
    public sealed class PixelDepthCaptureRendererFeature
        : ScriptableRendererFeature
    {
        private const string NEAR_LAYER_NAME = "PixelNear";
        private const string MIDDLE_LAYER_NAME = "PixelMiddle";
        private const string FAR_LAYER_NAME = "PixelFar";

        private const string NEAR_DEPTH_NAME = "_PixelNearDepthTex";
        private const string MIDDLE_DEPTH_NAME = "_PixelMiddleDepthTex";
        private const string FAR_DEPTH_NAME = "_PixelFarDepthTex";

        [Header("Fog Material")]
        [SerializeField]
        private Material _volumetricFogMaterial;

        // Render Graphの外側で保持するRT。
        // カメラ間・フレーム間で利用する。
        private readonly RTHandle[] _depthTextures =
            new RTHandle[3];

        private DepthCapturePass _capturePass;

        public override void Create()
        {
            _capturePass = new DepthCapturePass(this)
            {
                renderPassEvent =
                    RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_volumetricFogMaterial == null ||
                _capturePass == null)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;

            if (camera.cameraType != CameraType.Game)
            {
                return;
            }

            if (GetCaptureIndex(camera) < 0)
            {
                return;
            }

            renderer.EnqueuePass(_capturePass);
        }

        /// <summary>
        /// Capture Cameraの描画レイヤーから
        /// Near / Middle / Farを識別する。
        /// </summary>
        private static int GetCaptureIndex(Camera camera)
        {
            int nearLayer = LayerMask.NameToLayer(NEAR_LAYER_NAME);
            int middleLayer = LayerMask.NameToLayer(MIDDLE_LAYER_NAME);
            int farLayer = LayerMask.NameToLayer(FAR_LAYER_NAME);

            if (nearLayer >= 0 &&
                camera.cullingMask == (1 << nearLayer))
            {
                return 0;
            }

            if (middleLayer >= 0 &&
                camera.cullingMask == (1 << middleLayer))
            {
                return 1;
            }

            if (farLayer >= 0 &&
                camera.cullingMask == (1 << farLayer))
            {
                return 2;
            }

            return -1;
        }

        private static string GetDepthPropertyName(int index)
        {
            return index switch
            {
                0 => NEAR_DEPTH_NAME,
                1 => MIDDLE_DEPTH_NAME,
                2 => FAR_DEPTH_NAME,
                _ => string.Empty
            };
        }

        /// <summary>
        /// 指定レイヤーの深度保存用RTを確保する。
        /// 解像度が同じなら再確保しない。
        /// </summary>
        private RTHandle GetOrCreateDepthTexture(
            int index,
            RenderTextureDescriptor descriptor)
        {
            descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            descriptor.depthBufferBits = 0;
            descriptor.stencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.sRGB = false;

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _depthTextures[index],
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: GetDepthPropertyName(index));

            return _depthTextures[index];
        }

        /// <summary>
        /// URPのDepth Textureを保存するパス。
        /// </summary>
        private sealed class DepthCapturePass : ScriptableRenderPass
        {
            private readonly PixelDepthCaptureRendererFeature _owner;

            public DepthCapturePass(
                PixelDepthCaptureRendererFeature owner)
            {
                _owner = owner;

                // URPに深度テクスチャが必要なことを伝える。
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                int captureIndex =
                    GetCaptureIndex(cameraData.camera);

                if (captureIndex < 0)
                {
                    return;
                }

                // URPのCopyDepthで生成された深度。
                TextureHandle source =
                    resourceData.cameraDepthTexture;

                if (!source.IsValid())
                {
                    return;
                }

                // 実際の深度テクスチャの解像度を取得。
                TextureDesc sourceDesc =
                    renderGraph.GetTextureDesc(source);

                RenderTextureDescriptor descriptor =
                    cameraData.cameraTargetDescriptor;

                descriptor.width = sourceDesc.width;
                descriptor.height = sourceDesc.height;

                // 別カメラから使えるRTを確保。
                RTHandle depthTexture =
                    _owner.GetOrCreateDepthTexture(
                        captureIndex,
                        descriptor);

                // Render Graphへ外部RTを登録。
                TextureHandle destination =
                    renderGraph.ImportTexture(depthTexture);

                // 既存の深度情報をコピー。
                // 深度の再描画はしない。
                renderGraph.AddBlitPass(
                    source,
                    destination,
                    Vector2.one,
                    Vector2.zero,
                    filterMode:
                        RenderGraphUtils.BlitFilterMode.ClampNearest,
                    passName:
                        "Pixel Depth Copy " +
                        GetDepthPropertyName(captureIndex));

                // Main Cameraで使用するFogマテリアルへ渡す。
                _owner._volumetricFogMaterial.SetTexture(
                    GetDepthPropertyName(captureIndex),
                    depthTexture.rt);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            for (int i = 0; i < _depthTextures.Length; i++)
            {
                _depthTextures[i]?.Release();
                _depthTextures[i] = null;
            }
        }
    }
}
