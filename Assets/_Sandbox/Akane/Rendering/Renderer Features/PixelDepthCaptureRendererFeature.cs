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
        private const string NEAR_DEPTH_NAME = "_PixelNearDepthTex";
        private const string MIDDLE_DEPTH_NAME = "_PixelMiddleDepthTex";
        private const string FAR_DEPTH_NAME = "_PixelFarDepthTex";

        [Header("Fog Material")]
        [SerializeField]
        private Material _volumetricFogMaterial;

        [Header("Composite Material")]
        [SerializeField] private Material _compositeMaterial;

        [Header("Scene Fog Material")]
        [SerializeField]
        private Material _sceneVolumetricFogMaterial;

        [Header("Scene Composite Material")]
        [SerializeField]
        private Material _sceneCompositeMaterial;

        // Game / SceneそれぞれNear・Middle・Farの3枚を保持する。
        private readonly RTHandle[][] _depthTextures =
        {
            new RTHandle[3],
            new RTHandle[3]
        };

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
            if ((_volumetricFogMaterial == null && _compositeMaterial == null) || _capturePass == null)
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
        /// Capture Cameraに設定された番号を取得する。
        /// </summary>
        private static int GetCaptureIndex(Camera camera)
        {
            if (camera == null || !camera.TryGetComponent(out PixelCaptureViewMarker marker))
            {
                return -1;
            }

            return marker.CaptureIndex is >= 0 and <= 2
                ? marker.CaptureIndex
                : -1;
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
            PixelCaptureView view,
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

            RTHandle[] depthTextures = _depthTextures[(int)view];

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref depthTextures[index],
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: $"{view}_{GetDepthPropertyName(index)}");

            return depthTextures[index];
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

                Camera camera = cameraData.camera;

                if (!camera.TryGetComponent(
                        out PixelCaptureViewMarker marker))
                {
                    return;
                }

                PixelCaptureView view = marker.View;

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
                RTHandle depthTexture = _owner.GetOrCreateDepthTexture(view, captureIndex, descriptor);

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
                string propertyName = GetDepthPropertyName(captureIndex);

                Material fogMaterial = view == PixelCaptureView.Scene
                    ? _owner._sceneVolumetricFogMaterial
                    : _owner._volumetricFogMaterial;

                Material compositeMaterial = view == PixelCaptureView.Scene
                    ? _owner._sceneCompositeMaterial
                    : _owner._compositeMaterial;

                if (fogMaterial != null)
                {
                    fogMaterial.SetTexture(propertyName, depthTexture.rt);
                }

                if (compositeMaterial != null)
                {
                    compositeMaterial.SetTexture(propertyName, depthTexture.rt);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (RTHandle[] depthTextures in _depthTextures)
            {
                for (int i = 0; i < depthTextures.Length; i++)
                {
                    depthTextures[i]?.Release();
                    depthTextures[i] = null;
                }
            }
        }
    }
}
