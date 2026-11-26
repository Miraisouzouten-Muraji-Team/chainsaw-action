using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// 画面色・Depth・Normalから可視面同士の間接光を計算する。
    /// Pixel ON/OFFの両方に対応する画面空間GI。
    /// </summary>
    public sealed class DynamicGiRendererFeature : ScriptableRendererFeature
    {
        private const int GI_PASS_GATHER = 0;
        private const int GI_PASS_BLUR = 1;
        private const int GI_PASS_COMPOSITE = 2;
        private const int MAX_SAMPLES = 32;

        // GI専用テクスチャ。
        private static readonly int SOURCE_ID = Shader.PropertyToID("_GiSourceTexture");
        private static readonly int DEPTH_ID = Shader.PropertyToID("_GiDepthTexture");
        private static readonly int NORMAL_ID = Shader.PropertyToID("_GiNormalTexture");
        private static readonly int GI_RESULT_ID = Shader.PropertyToID("_DynamicGiTexture");

        // GI設定値。
        private static readonly int GI_USE_COMPOSITE_ID = Shader.PropertyToID("_GiUseComposite");
        private static readonly int GI_RADIUS_ID = Shader.PropertyToID("_GiRadius");
        private static readonly int GI_INTENSITY_ID = Shader.PropertyToID("_GiIntensity");
        private static readonly int GI_THRESHOLD_ID = Shader.PropertyToID("_GiSourceThreshold");
        private static readonly int GI_MAX_RADIANCE_ID = Shader.PropertyToID("_GiMaxRadiance");
        private static readonly int GI_NORMAL_BIAS_ID = Shader.PropertyToID("_GiNormalBias");
        private static readonly int GI_OCCLUSION_BIAS_ID = Shader.PropertyToID("_GiOcclusionBias");
        private static readonly int GI_DEPTH_SIGMA_ID = Shader.PropertyToID("_GiDepthSigma");
        private static readonly int GI_NORMAL_POWER_ID = Shader.PropertyToID("_GiNormalPower");
        private static readonly int GI_SAMPLE_COUNT_ID = Shader.PropertyToID("_GiSampleCount");
        private static readonly int GI_BLUR_DIRECTION_ID = Shader.PropertyToID("_GiBlurDirection");
        private static readonly int GI_LOW_TEXEL_ID = Shader.PropertyToID("_GiLowTexelSize");
        private static readonly int GI_DEBUG_ID = Shader.PropertyToID("_GiDebugView");
        private static readonly int GI_PIXELATE_ID = Shader.PropertyToID("_GiPixelate");

        [Header("Shader")]
        [SerializeField] private Shader _giShader;

        [Header("Depth / Normal")]
        [Tooltip("PixelGameRendererではON、GameRendererではOFF")]
        [SerializeField] private bool _useCompositeTextures = true;

        [Header("GI Quality")]
        [SerializeField, Range(2, 8)] private int _downsample = 4;
        [SerializeField, Range(4, MAX_SAMPLES)] private int _sampleCount = 16;
        [SerializeField, Min(0.1f)] private float _radius = 2f;
        [SerializeField, Range(0f, 12f)] private float _intensity = 0.5f;

        [Header("Radiance")]
        [SerializeField, Range(0f, 3f)] private float _sourceThreshold = 0.7f;
        [SerializeField, Range(1f, 32f)] private float _maxRadiance = 2f;
        [SerializeField, Range(0f, 1f)] private float _normalBias = 0.05f;
        [SerializeField, Range(0.01f, 1f)] private float _occlusionBias = 0.1f;

        [Header("Edge-aware Blur")]
        [SerializeField, Range(0.05f, 5f)] private float _depthSigma = 1f;
        [SerializeField, Range(1f, 32f)] private float _normalPower = 8f;

        [Header("Debug")]
        [SerializeField] private bool _showIndirectOnly;
        [SerializeField] private bool _pixelateIndirect = true;

        private Material _material;
        private GiPass _pass;

        /// <summary>
        /// GI用MaterialとRender Passを生成する。
        /// </summary>
        public override void Create()
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;

            if (_giShader == null)
            {
                _giShader = Shader.Find("Project/DynamicScreenSpaceGI");
            }

            if (_giShader == null)
            {
                Debug.LogWarning("Dynamic GI用Shaderが見つかりません。", this);
                return;
            }

            _material = CoreUtils.CreateEngineMaterial(_giShader);

            if (_material.passCount < 3)
            {
                Debug.LogWarning("Dynamic GI用ShaderのPass数が不足しています。", this);
                CoreUtils.Destroy(_material);
                _material = null;
                return;
            }

            _pass = new GiPass(this)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// 対象CameraにGI Passを登録する。
        /// </summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;

            // デバッグ時はIntensityが0でも実行する。
            if (_intensity <= 0f && !_showIndirectOnly) return;

            Camera camera = renderingData.cameraData.camera;
            if (camera == null) return;

            if (camera.cameraType != CameraType.Game &&
                camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            // Near/Middle/Far CaptureではGIを重複適用しない。
            if (camera.GetComponent<PixelCaptureViewMarker>() != null) return;

            // Overlay Cameraには適用しない。
            if (renderingData.cameraData.renderType != CameraRenderType.Base) return;

            _pass.Setup(_useCompositeTextures);
            renderer.EnqueuePass(_pass);
        }

        /// <summary>
        /// 使用していたMaterialを解放する。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;

            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }

        private void OnValidate()
        {
            _downsample = Mathf.Clamp(_downsample, 2, 8);
            _sampleCount = Mathf.Clamp(_sampleCount, 4, MAX_SAMPLES);
            _radius = Mathf.Max(_radius, 0.1f);
            _intensity = Mathf.Max(_intensity, 0f);
            _sourceThreshold = Mathf.Max(_sourceThreshold, 0f);
            _maxRadiance = Mathf.Max(_maxRadiance, 1f);
            _depthSigma = Mathf.Max(_depthSigma, 0.05f);
        }

        /// <summary>
        /// GIの生成・ぼかし・画面合成をRender Graphへ登録する。
        /// </summary>
        private sealed class GiPass : ScriptableRenderPass
        {
            private readonly DynamicGiRendererFeature _owner;

            // 描画ごとにShader設定を渡す。
            private readonly MaterialPropertyBlock _properties = new();

            private bool _useComposite;

            /// <summary>
            /// 各描画Passで使用するリソースと設定値。
            /// </summary>
            private sealed class PassData
            {
                public Material Material;
                public MaterialPropertyBlock Properties;

                public TextureHandle Source;
                public TextureHandle Depth;
                public TextureHandle Normal;
                public TextureHandle Indirect;

                public int ShaderPass;

                public Vector4 BlurDirection;
                public Vector4 LowTexelSize;

                public float Radius;
                public float Intensity;
                public float Threshold;
                public float MaxRadiance;
                public float NormalBias;
                public float OcclusionBias;
                public float DepthSigma;
                public float NormalPower;

                public int SampleCount;

                public bool UseComposite;
                public bool DebugView;
                public bool Pixelate;
            }

            public GiPass(DynamicGiRendererFeature owner)
            {
                _owner = owner;
            }

            /// <summary>
            /// 描画方式に応じたDepth/Normalの使用設定。
            /// </summary>
            public void Setup(bool useComposite)
            {
                _useComposite = useComposite;

                ConfigureInput(
                    useComposite
                        ? ScriptableRenderPassInput.None
                        : ScriptableRenderPassInput.Depth |
                          ScriptableRenderPassInput.Normal);
            }

            /// <summary>
            /// GIの描画順序とリソースの依存関係を登録する。
            /// </summary>
            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                // Back Bufferを直接読み込むことはできない。
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle cameraColor = resourceData.activeColorTexture;

                if (!cameraColor.IsValid()) return;

                TextureHandle depth;
                TextureHandle normal;

                if (_useComposite)
                {
                    // PixelOpaqueCompositeで統合されたDepth/Normalを取得する。
                    if (!frameData.Contains<PixelOpaqueFrameData>()) return;

                    PixelOpaqueFrameData opaqueData =
                        frameData.Get<PixelOpaqueFrameData>();

                    depth = opaqueData.Depth;
                    normal = opaqueData.Normal;
                }
                else
                {
                    // Pixel OFFではURP標準のDepth/Normalを使用する。
                    depth = resourceData.cameraDepthTexture;
                    normal = resourceData.cameraNormalsTexture;
                }

                if (!depth.IsValid() || !normal.IsValid()) return;

                // Camera Colorのコピー用RT。
                TextureDesc fullDesc = renderGraph.GetTextureDesc(cameraColor);
                fullDesc.name = "DynamicGiSourceColor";
                fullDesc.clearBuffer = false;
                fullDesc.depthBufferBits = DepthBits.None;
                fullDesc.msaaSamples = MSAASamples.None;

                TextureHandle sourceCopy =
                    renderGraph.CreateTexture(fullDesc);

                // Camera Colorの読み書き競合を避ける。
                renderGraph.AddBlitPass(
                    cameraColor,
                    sourceCopy,
                    Vector2.one,
                    Vector2.zero,
                    passName: "Dynamic GI Copy Scene Color");

                // 低解像度GI用RTを明示的なサイズで生成する。
                RenderTextureDescriptor cameraDesc =
                    cameraData.cameraTargetDescriptor;

                int downsample = Mathf.Clamp(_owner._downsample, 2, 8);

                int width = Mathf.Max(
                    1, (cameraDesc.width + downsample - 1) / downsample);

                int height = Mathf.Max(
                    1, (cameraDesc.height + downsample - 1) / downsample);

                TextureDesc lowDesc = new(width, height)
                {
                    name = "DynamicGiRaw",
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    dimension = TextureDimension.Tex2D,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = Color.clear
                };

                // GIの計算結果。
                TextureHandle raw =
                    renderGraph.CreateTexture(lowDesc);

                // 水平ぼかしの結果。
                lowDesc.name = "DynamicGiBlurHorizontal";

                TextureHandle horizontal =
                    renderGraph.CreateTexture(lowDesc);

                // 垂直ぼかしの結果。
                lowDesc.name = "DynamicGiBlurVertical";

                TextureHandle filtered =
                    renderGraph.CreateTexture(lowDesc);

                Vector4 lowTexelSize = new(
                    1f / width,
                    1f / height,
                    width,
                    height);

                // 1. 間接光を計算する。
                AddGiPass(
                    renderGraph,
                    "Dynamic GI Gather",
                    sourceCopy,
                    raw,
                    depth,
                    normal,
                    GI_PASS_GATHER,
                    Vector4.zero,
                    lowTexelSize);

                // 2. 水平方向のぼかし。
                AddGiPass(
                    renderGraph,
                    "Dynamic GI Blur Horizontal",
                    raw,
                    horizontal,
                    depth,
                    normal,
                    GI_PASS_BLUR,
                    new Vector4(1f, 0f, 0f, 0f),
                    lowTexelSize);

                // 3. 垂直方向のぼかし。
                AddGiPass(
                    renderGraph,
                    "Dynamic GI Blur Vertical",
                    horizontal,
                    filtered,
                    depth,
                    normal,
                    GI_PASS_BLUR,
                    new Vector4(0f, 1f, 0f, 0f),
                    lowTexelSize);

                // 4. ぼかし済みGIを元の画面へ合成する。
                AddGiPass(
                    renderGraph,
                    "Dynamic GI Composite",
                    sourceCopy,
                    cameraColor,
                    depth,
                    normal,
                    GI_PASS_COMPOSITE,
                    Vector4.zero,
                    lowTexelSize,
                    filtered);
            }

            /// <summary>
            /// GIの各描画Passを登録する。
            /// 全てのテクスチャを明示的な依存関係として扱う。
            /// </summary>
            private void AddGiPass(
                RenderGraph renderGraph,
                string passName,
                TextureHandle source,
                TextureHandle destination,
                TextureHandle depth,
                TextureHandle normal,
                int shaderPass,
                Vector4 blurDirection,
                Vector4 lowTexelSize,
                TextureHandle indirect = default)
            {
                using (var builder =
                    renderGraph.AddRasterRenderPass<PassData>(
                        passName, out var passData))
                {
                    passData.Material = _owner._material;
                    passData.Properties = _properties;

                    passData.Source = source;
                    passData.Depth = depth;
                    passData.Normal = normal;
                    passData.Indirect = indirect;

                    passData.ShaderPass = shaderPass;
                    passData.BlurDirection = blurDirection;
                    passData.LowTexelSize = lowTexelSize;

                    passData.Radius = _owner._radius;
                    passData.Intensity = _owner._intensity;
                    passData.Threshold = _owner._sourceThreshold;
                    passData.MaxRadiance = _owner._maxRadiance;
                    passData.NormalBias = _owner._normalBias;
                    passData.OcclusionBias = _owner._occlusionBias;
                    passData.DepthSigma = _owner._depthSigma;
                    passData.NormalPower = _owner._normalPower;
                    passData.SampleCount = _owner._sampleCount;

                    passData.UseComposite = _useComposite;
                    passData.DebugView = _owner._showIndirectOnly;
                    passData.Pixelate = _owner._pixelateIndirect;

                    // 読み込むリソースをRender Graphへ登録する。
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.UseTexture(normal, AccessFlags.Read);

                    if (indirect.IsValid())
                    {
                        builder.UseTexture(indirect, AccessFlags.Read);
                    }

                    // フルスクリーン描画で全画素を書き換える。
                    builder.SetRenderAttachment(
                        destination, 0, AccessFlags.WriteAll);

                    builder.SetRenderFunc(static (
                        PassData data,
                        RasterGraphContext context) =>
                    {
                        MaterialPropertyBlock properties = data.Properties;

                        // 以前のPassで使用した設定を破棄する。
                        properties.Clear();

                        // 各Pass専用のテクスチャを直接設定する。
                        properties.SetTexture(
                            SOURCE_ID, data.Source);

                        properties.SetTexture(
                            DEPTH_ID, data.Depth);

                        properties.SetTexture(
                            NORMAL_ID, data.Normal);

                        // Compositeでのみ最終GIテクスチャを使用する。
                        if (data.ShaderPass == GI_PASS_COMPOSITE &&
                            data.Indirect.IsValid())
                        {
                            properties.SetTexture(
                                GI_RESULT_ID, data.Indirect);
                        }

                        // 各Passの設定値を個別に渡す。
                        properties.SetFloat(
                            GI_USE_COMPOSITE_ID,
                            data.UseComposite ? 1f : 0f);

                        properties.SetFloat(
                            GI_RADIUS_ID, data.Radius);

                        properties.SetFloat(
                            GI_INTENSITY_ID, data.Intensity);

                        properties.SetFloat(
                            GI_THRESHOLD_ID, data.Threshold);

                        properties.SetFloat(
                            GI_MAX_RADIANCE_ID, data.MaxRadiance);

                        properties.SetFloat(
                            GI_NORMAL_BIAS_ID, data.NormalBias);

                        properties.SetFloat(
                            GI_OCCLUSION_BIAS_ID, data.OcclusionBias);

                        properties.SetFloat(
                            GI_DEPTH_SIGMA_ID, data.DepthSigma);

                        properties.SetFloat(
                            GI_NORMAL_POWER_ID, data.NormalPower);

                        properties.SetFloat(
                            GI_SAMPLE_COUNT_ID, data.SampleCount);

                        properties.SetVector(
                            GI_BLUR_DIRECTION_ID, data.BlurDirection);

                        properties.SetVector(
                            GI_LOW_TEXEL_ID, data.LowTexelSize);

                        properties.SetFloat(
                            GI_DEBUG_ID, data.DebugView ? 1f : 0f);

                        properties.SetFloat(
                            GI_PIXELATE_ID, data.Pixelate ? 1f : 0f);

                        // Blitterのグローバルテクスチャに依存せず描画する。
                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            data.Material,
                            data.ShaderPass,
                            MeshTopology.Triangles,
                            3,
                            1,
                            properties);
                    });
                }
            }
        }
    }
}
