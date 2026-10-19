using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Near / Middle / FarのOpaqueから統合DepthとNormalを生成する。
    /// GameビューとSceneビューの出力を個別に保持する。
    /// </summary>
    public sealed class PixelOpaqueCompositeRendererFeature : ScriptableRendererFeature
    {
        private const int VIEW_COUNT = 2;
        private const int INPUT_COUNT = 6;
        private const string PIXEL_EFFECT_ENABLED = "_PixelEffectEnabled";
        private const string SCENE_NORMAL_PREVIEW_ENABLED = "_SceneNormalPreviewEnabled";
        private const string COMPOSITE_DEPTH_NAME = "_PixelCompositeDepthTex";
        private const string COMPOSITE_NORMAL_NAME = "_PixelCompositeNormalTex";

        private static readonly string[] INPUT_NAMES =
        {
            "_PixelNearDepthTex",
            "_PixelMiddleDepthTex",
            "_PixelFarDepthTex",
            "_PixelNearNormalTex",
            "_PixelMiddleNormalTex",
            "_PixelFarNormalTex"
        };

        [Header("Shader")]
        [SerializeField] private Shader _compositeShader;

        [Header("Capture Materials")]
        [SerializeField] private Material _gameCompositeMaterial;
        [SerializeField] private Material _sceneCompositeMaterial;

        // Game / Scene別の統合用Material。
        private readonly Material[] _materials = new Material[VIEW_COUNT];

        // Game / Scene別の統合結果。
        private readonly RTHandle[] _depthTextures = new RTHandle[VIEW_COUNT];
        private readonly RTHandle[] _normalTextures = new RTHandle[VIEW_COUNT];

        // Capture RTをRender Graphへ登録するための参照。
        private readonly RTHandle[,] _inputHandles = new RTHandle[VIEW_COUNT, INPUT_COUNT];
        private readonly RenderTexture[,] _inputTextures = new RenderTexture[VIEW_COUNT, INPUT_COUNT];

        private CompositePass _compositePass;

        /// <summary>
        /// Renderer Featureの初期化を行う。
        /// </summary>
        public override void Create()
        {
            // 再生成時に古いMaterialを解放する。
            for (int i = 0; i < VIEW_COUNT; i++)
            {
                CoreUtils.Destroy(_materials[i]);
                _materials[i] = _compositeShader != null ? CoreUtils.CreateEngineMaterial(_compositeShader) : null;
            }

            _compositePass = new CompositePass(this)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// Pixel描画中のGameまたはScene CameraへPassを追加する。
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_compositePass == null) return;

            Camera camera = renderingData.cameraData.camera;
            if (camera == null) return;

            int viewIndex = GetViewIndex(camera);
            if (viewIndex < 0 || _materials[viewIndex] == null) return;

            Material sourceMaterial = GetSourceMaterial(viewIndex);
            if (sourceMaterial == null) return;

            // Pixel OFFの通常描画では統合処理を実行しない。
            if (sourceMaterial.GetFloat(PIXEL_EFFECT_ENABLED) < 0.5f) return;

            // Sceneビューの通常解像度プレビューでは実行しない。
            if (viewIndex == (int)PixelCaptureView.Scene &&
                sourceMaterial.GetFloat(SCENE_NORMAL_PREVIEW_ENABLED) > 0.5f) return;

            renderer.EnqueuePass(_compositePass);
        }

        /// <summary>
        /// CameraからGame / Sceneのインデックスを決定する。
        /// </summary>
        private static int GetViewIndex(Camera camera)
        {
            return camera.cameraType switch
            {
                CameraType.Game => (int)PixelCaptureView.Game,
                CameraType.SceneView => (int)PixelCaptureView.Scene,
                _ => -1
            };
        }

        /// <summary>
        /// Game / Sceneに対応するCapture Materialを返す。
        /// </summary>
        private Material GetSourceMaterial(int viewIndex)
        {
            return viewIndex == (int)PixelCaptureView.Scene
                ? _sceneCompositeMaterial
                : _gameCompositeMaterial;
        }

        /// <summary>
        /// Capture RTが差し替わった場合にRender Graph用Handleを更新する。
        /// </summary>
        private RTHandle GetOrCreateInputHandle(int viewIndex, int inputIndex, RenderTexture texture)
        {
            if (_inputHandles[viewIndex, inputIndex] != null &&
                _inputTextures[viewIndex, inputIndex] == texture)
            {
                return _inputHandles[viewIndex, inputIndex];
            }

            _inputHandles[viewIndex, inputIndex]?.Release();
            _inputTextures[viewIndex, inputIndex] = texture;
            _inputHandles[viewIndex, inputIndex] = RTHandles.Alloc(texture, $"PixelOpaqueInput_{viewIndex}_{inputIndex}");
            return _inputHandles[viewIndex, inputIndex];
        }

        /// <summary>
        /// 指定フォーマットの統合RTを確保する。
        /// </summary>
        private static void AllocateTexture(ref RTHandle texture, RenderTextureDescriptor descriptor, GraphicsFormat format, string textureName)
        {
            descriptor.graphicsFormat = format;
            descriptor.depthBufferBits = 0;
            descriptor.stencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.sRGB = false;

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref texture,
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: textureName);
        }

        /// <summary>
        /// 統合DepthとNormalの出力RTを確保する。
        /// </summary>
        private void EnsureOutputTextures(int viewIndex, RenderTextureDescriptor descriptor)
        {
            AllocateTexture(ref _depthTextures[viewIndex], descriptor, GraphicsFormat.R32_SFloat, $"PixelCompositeDepth_{viewIndex}");
            AllocateTexture(ref _normalTextures[viewIndex], descriptor, GraphicsFormat.R16G16B16A16_SFloat, $"PixelCompositeNormal_{viewIndex}");
        }

        /// <summary>
        /// Render GraphのMRT描画を実行するPass。
        /// </summary>
        private sealed class CompositePass : ScriptableRenderPass
        {
            private readonly PixelOpaqueCompositeRendererFeature _owner;

            private sealed class PassData
            {
                public Material Material;
            }

            public CompositePass(PixelOpaqueCompositeRendererFeature owner)
            {
                _owner = owner;
            }

            /// <summary>
            /// Capture RTを読み取り、統合DepthとNormalを出力する。
            /// </summary>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                Camera camera = cameraData.camera;
                if (camera == null) return;

                int viewIndex = GetViewIndex(camera);
                if (viewIndex < 0) return;

                Material sourceMaterial = _owner.GetSourceMaterial(viewIndex);
                Material compositeMaterial = _owner._materials[viewIndex];
                if (sourceMaterial == null || compositeMaterial == null) return;

                // Capture側から渡されたDepthとNormalを取得する。
                RenderTexture[] inputTextures = new RenderTexture[INPUT_COUNT];
                for (int i = 0; i < INPUT_COUNT; i++)
                {
                    inputTextures[i] = sourceMaterial.GetTexture(INPUT_NAMES[i]) as RenderTexture;
                    if (inputTextures[i] == null || !inputTextures[i].IsCreated()) return;
                }

                // メイン画面の解像度で統合RTを確保する。
                _owner.EnsureOutputTextures(viewIndex, cameraData.cameraTargetDescriptor);

                // 外部RTをRender Graphへ登録する。
                TextureHandle depthOutput = renderGraph.ImportTexture(_owner._depthTextures[viewIndex]);
                TextureHandle normalOutput = renderGraph.ImportTexture(_owner._normalTextures[viewIndex]);
                TextureHandle[] inputs = new TextureHandle[INPUT_COUNT];


                for (int i = 0; i < INPUT_COUNT; i++)
                {
                    // Capture済みRenderTextureを取得する。
                    RenderTexture inputTexture = inputTextures[i];

                    // 既存のRenderTextureを参照するRTHandleを取得する。
                    RTHandle inputHandle = _owner.GetOrCreateInputHandle(viewIndex, i, inputTexture);

                    // Render Graphへ渡すテクスチャ情報を設定する。
                    RenderTargetInfo inputInfo = new()
                    {
                        width = inputTexture.width,
                        height = inputTexture.height,
                        volumeDepth = inputTexture.volumeDepth,
                        msaaSamples = inputTexture.antiAliasing,
                        format = inputTexture.graphicsFormat,
                        bindMS = inputTexture.bindTextureMS
                    };

                    // RTHandleがRenderTargetIdentifierを保持するため、情報を明示してImportする。
                    inputs[i] = renderGraph.ImportTexture(inputHandle, inputInfo);

                    // ShaderからCapture済みテクスチャを参照できるようにする。
                    compositeMaterial.SetTexture(INPUT_NAMES[i], inputTexture);
                }


                // 統合DepthとNormalを同時に書き出す。
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Pixel Opaque Metadata Composite", out var passData))
                {
                    passData.Material = compositeMaterial;

                    // Capture済みRTを読み取りリソースとして登録する。
                    for (int i = 0; i < INPUT_COUNT; i++)
                    {
                        builder.UseTexture(inputs[i], AccessFlags.Read);
                    }

                    // MRTの0番へDepth、1番へNormalを出力する。
                    builder.SetRenderAttachment(depthOutput, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(normalOutput, 1, AccessFlags.WriteAll);

                    // 後続の描画処理から参照できるようにする。
                    builder.SetGlobalTextureAfterPass(depthOutput, Shader.PropertyToID(COMPOSITE_DEPTH_NAME));
                    builder.SetGlobalTextureAfterPass(normalOutput, Shader.PropertyToID(COMPOSITE_NORMAL_NAME));

                    // 後続のPassが参照するため、不要なPassとして削除しない。
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        // フルスクリーントライアングルをMRTへ描画する。
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }
        }

        /// <summary>
        /// 統合RT・入力Handle・Materialを解放する。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;

            for (int viewIndex = 0; viewIndex < VIEW_COUNT; viewIndex++)
            {
                _depthTextures[viewIndex]?.Release();
                _normalTextures[viewIndex]?.Release();
                _depthTextures[viewIndex] = null;
                _normalTextures[viewIndex] = null;

                for (int inputIndex = 0; inputIndex < INPUT_COUNT; inputIndex++)
                {
                    _inputHandles[viewIndex, inputIndex]?.Release();
                    _inputHandles[viewIndex, inputIndex] = null;
                    _inputTextures[viewIndex, inputIndex] = null;
                }

                CoreUtils.Destroy(_materials[viewIndex]);
                _materials[viewIndex] = null;
            }

            _compositePass = null;
        }
    }
}
