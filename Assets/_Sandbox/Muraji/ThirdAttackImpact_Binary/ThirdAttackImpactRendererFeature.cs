using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Add to the Universal Renderer Data used by the game camera.
/// Replaces the old Full Screen Pass feature for this effect.
/// Unity 6 URP: supports Render Graph and Compatibility Mode.
/// </summary>
public sealed class ThirdAttackImpactRendererFeature : ScriptableRendererFeature
{
    [Tooltip("同梱のThirdAttackThreshold.shaderを設定。Materialの作成は不要。")]
    [SerializeField] private Shader thresholdShader;
    [SerializeField] private LayerMask actorLayers;
    private Material material;
    private ImpactPass pass;
    private bool warned;

    public override void Create()
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
        pass = null;
        material = null;
        if (thresholdShader == null)
            thresholdShader = Shader.Find("Project/Impact/ThirdAttackThreshold");
        if (thresholdShader == null || !thresholdShader.isSupported)
            return;
        material = CoreUtils.CreateEngineMaterial(thresholdShader);
        pass = new ImpactPass(material);
        warned = false;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (actorLayers.value == 0 || !PlayerThirdAttackImpact.TryGetRenderSettings(renderingData.cameraData.camera,
                renderingData.cameraData.resolveFinalTarget, out _))
            return;
        if (pass == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.LogError("[ThirdAttackImpact] Threshold Shaderが未設定、またはコンパイルエラーです。Renderer FeatureにThirdAttackThreshold.shaderを設定してください。");
            }
            return;
        }
        pass.ActorLayers = actorLayers.value;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        pass = null;
        CoreUtils.Destroy(material);
        material = null;
    }

    private sealed class ImpactPass : ScriptableRenderPass
    {
        private static readonly int ParamsId = Shader.PropertyToID("_ThirdAttackImpactParams");
        private readonly Material material;
        public int ActorLayers { get; set; }
        private static readonly ShaderTagId[] Tags =
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("UniversalGBuffer"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private RendererListParams Actors(CullingResults culling, Camera camera)
        {
            var drawing = new DrawingSettings(Tags[0],
                new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque })
            {
                overrideMaterial = material,
                overrideMaterialPassIndex = 1
            };
            for (int i = 1; i < Tags.Length; i++)
                drawing.SetShaderPassName(i, Tags[i]);
            return new RendererListParams(culling, drawing,
                new FilteringSettings(RenderQueueRange.all, ActorLayers));
        }

        private static readonly int OutlineId = Shader.PropertyToID("_ImpactOutline");
        private RTHandle compatibilityTexture;
        private sealed class OutlineData
        {
            public TextureHandle source;
            public Material material;
            public Vector4 settings;
        }
        private bool backBufferWarning;

        public ImpactPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
            public Vector4 settings;
            public Camera camera;
            public RendererListHandle actors;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!PlayerThirdAttackImpact.TryGetRenderSettings(cameraData.camera,
                    cameraData.resolveFinalTarget, out Vector4 settings))
                return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
            {
                if (!backBufferWarning)
                {
                    backBufferWarning = true;
                    Debug.LogError("[ThirdAttackImpact] 画面色の読み取り元がBackBufferです。Universal Renderer DataのIntermediate TextureをAlwaysへ変更してください。");
                }
                return;
            }

            TextureHandle source = resources.activeColorTexture;
            var descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, descriptor, "Third Attack Impact Color", false);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Third Attack Impact Threshold", out var passData))
            {
                passData.source = source;
                passData.material = material;
                passData.settings = settings; // Immutable values for this camera/frame.
                passData.camera = cameraData.camera;
                passData.actors = renderGraph.CreateRendererList(Actors(
                    frameData.Get<UniversalRenderingData>().cullResults, cameraData.camera));
                builder.UseRendererList(passData.actors);
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    // Set the material only when this pass executes, not in camera callbacks.
                    data.material.SetVector(ParamsId, data.settings);
                    Blitter.BlitTexture(context.cmd, data.source,
                        new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    context.cmd.DrawRendererList(data.actors);
                    PlayerThirdAttackImpact.NotifyPassExecuted(data.camera);
                });
            }
            // Let subsequent URP passes consume the processed color.
            Vector4 outline = PlayerThirdAttackImpact.GetOutlineSettings(cameraData.camera);
            if (outline.z > 0f && outline.w > 0f)
            {
                TextureHandle composite = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, descriptor, "Third Attack Outline Composite", false);
                using (var builder = renderGraph.AddRasterRenderPass<OutlineData>(
                    "Third Attack Outline Blur", out var data))
                {
                    data.source = destination;
                    data.material = material;
                    data.settings = outline;
                    builder.UseTexture(destination, AccessFlags.Read);
                    builder.SetRenderAttachment(composite, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (OutlineData pass, RasterGraphContext context) =>
                    {
                        pass.material.SetVector(OutlineId, pass.settings);
                        Blitter.BlitTexture(context.cmd, pass.source,
                            new Vector4(1f, 1f, 0f, 0f), pass.material, 2);
                    });
                }
                destination = composite;
            }
            resources.cameraColor = destination;
        }

        // These APIs are only used when the project has Render Graph disabled.
#pragma warning disable CS0618, CS0672
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            RenderingUtils.ReAllocateIfNeeded(ref compatibilityTexture, descriptor,
                FilterMode.Bilinear, TextureWrapMode.Clamp, name: "ThirdAttackImpactTemporary");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!PlayerThirdAttackImpact.TryGetRenderSettings(renderingData.cameraData.camera,
                    renderingData.cameraData.resolveFinalTarget, out Vector4 settings) ||
                compatibilityTexture == null)
                return;
            RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            CommandBuffer cmd = CommandBufferPool.Get("Third Attack Impact Threshold");
            try
            {
                material.SetVector(ParamsId, settings);
                Blitter.BlitCameraTexture(cmd, source, compatibilityTexture, material, 0);
                var actors = Actors(renderingData.cullResults, renderingData.cameraData.camera);
                cmd.DrawRendererList(context.CreateRendererList(ref actors));
                Vector4 outline = PlayerThirdAttackImpact.GetOutlineSettings(renderingData.cameraData.camera);
                material.SetVector(OutlineId, outline);
                Blitter.BlitCameraTexture(cmd, compatibilityTexture, source, material, 2);
                context.ExecuteCommandBuffer(cmd);
                PlayerThirdAttackImpact.NotifyPassExecuted(renderingData.cameraData.camera);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
#pragma warning restore CS0618, CS0672

        public void Dispose()
        {
            compatibilityTexture?.Release();
            compatibilityTexture = null;
        }
    }
}
