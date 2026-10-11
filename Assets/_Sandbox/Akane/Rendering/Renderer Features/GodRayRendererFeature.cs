using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering
{
    public sealed class GodRayRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Header("Material")]
            public Material Material;

            [Header("Sun")]
            public Light SunLight;

            [Header("God Ray")]
            [Range(0.0f, 5.0f)]
            public float Intensity = 1.0f;

            [Range(0.0f, 1.0f)]
            public float Decay = 0.95f;

            [Range(0.1f, 2.0f)]
            public float Density = 1.0f;

            [Range(0.01f, 1.0f)]
            public float Weight = 0.15f;

            [Range(0.01f, 1.0f)]
            public float SunRadius = 0.15f;

            [Range(4, 64)]
            public int SampleCount = 32;

            [Header("Rendering")]
            public RenderPassEvent InjectionPoint =
                RenderPassEvent.BeforeRenderingPostProcessing;

            public bool ApplySceneView = true;
        }

        [SerializeField]
        private Settings _settings = new();

        private GodRayRenderPass _renderPass;

        public override void Create()
        {
            _renderPass = new GodRayRenderPass(_settings)
            {
                renderPassEvent = _settings.InjectionPoint
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_settings.Material == null ||
                _settings.SunLight == null)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;

            if (!_settings.ApplySceneView &&
                camera.cameraType == CameraType.SceneView)
            {
                return;
            }

            renderer.EnqueuePass(_renderPass);
        }

        private sealed class GodRayRenderPass : ScriptableRenderPass
        {
            private const string PASS_NAME = "God Ray";

            private static readonly int SUN_SCREEN_POSITION_ID =
                Shader.PropertyToID("_SunScreenPosition");

            private static readonly int RAY_COLOR_ID =
                Shader.PropertyToID("_RayColor");

            private static readonly int INTENSITY_ID =
                Shader.PropertyToID("_Intensity");

            private static readonly int DECAY_ID =
                Shader.PropertyToID("_Decay");

            private static readonly int DENSITY_ID =
                Shader.PropertyToID("_Density");

            private static readonly int WEIGHT_ID =
                Shader.PropertyToID("_Weight");

            private static readonly int SUN_RADIUS_ID =
                Shader.PropertyToID("_SunRadius");

            private static readonly int SAMPLE_COUNT_ID =
                Shader.PropertyToID("_SampleCount");

            private readonly Settings _settings;

            public GodRayRenderPass(Settings settings)
            {
                _settings = settings;

                // God Rayでは現在のColorとDepthを使う。
                ConfigureInput(
                    ScriptableRenderPassInput.Color |
                    ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                Camera camera = cameraData.camera;

                UpdateMaterial(camera);

                TextureHandle source =
                    resourceData.activeColorTexture;

                TextureDesc destinationDescriptor =
                    renderGraph.GetTextureDesc(source);

                destinationDescriptor.name = "GodRayColor";
                destinationDescriptor.clearBuffer = false;
                destinationDescriptor.depthBufferBits = 0;

                TextureHandle destination =
                    renderGraph.CreateTexture(
                        destinationDescriptor);

                RenderGraphUtils.BlitMaterialParameters parameters =
                    new(
                        source,
                        destination,
                        _settings.Material,
                        0);

                renderGraph.AddBlitPass(
                    parameters,
                    PASS_NAME);

                // God Ray適用後のTextureを
                // 以降のCamera Colorとして使用する。
                resourceData.cameraColor = destination;
            }

            private void UpdateMaterial(Camera camera)
            {
                Light sunLight = _settings.SunLight;

                // Directional Lightは位置ではなく方向を持つため、
                // カメラから十分遠い位置に仮想的な太陽を置く。
                Vector3 sunWorldPosition =
                    camera.transform.position -
                    sunLight.transform.forward * 1000.0f;

                Vector3 viewportPosition =
                    camera.WorldToViewportPoint(
                        sunWorldPosition);

                bool isSunInFront =
                    viewportPosition.z > 0.0f;

                float intensity =
                    isSunInFront
                        ? _settings.Intensity
                        : 0.0f;

                _settings.Material.SetVector(
                    SUN_SCREEN_POSITION_ID,
                    new Vector4(
                        viewportPosition.x,
                        viewportPosition.y,
                        0.0f,
                        0.0f));

                _settings.Material.SetColor(
                    RAY_COLOR_ID,
                    sunLight.color);

                _settings.Material.SetFloat(
                    INTENSITY_ID,
                    intensity);

                _settings.Material.SetFloat(
                    DECAY_ID,
                    _settings.Decay);

                _settings.Material.SetFloat(
                    DENSITY_ID,
                    _settings.Density);

                _settings.Material.SetFloat(
                    WEIGHT_ID,
                    _settings.Weight);

                _settings.Material.SetFloat(
                    SUN_RADIUS_ID,
                    _settings.SunRadius);

                _settings.Material.SetInt(
                    SAMPLE_COUNT_ID,
                    _settings.SampleCount);
            }
        }
    }
}