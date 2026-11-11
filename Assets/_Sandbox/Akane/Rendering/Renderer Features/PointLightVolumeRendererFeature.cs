using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Point / Spot Lightの散乱光を3Dテクスチャに事前計算し、既存のVolumetric Fogへ渡す。
    /// URP 17.3 / Render Graphを使用し、Pixel Capture Cameraでは重複計算しない。
    /// </summary>
    public sealed class PointLightVolumeRendererFeature : ScriptableRendererFeature
    {
        private const int MAX_POINT_LIGHTS = 128;
        private const int MAX_LIGHTS_PER_TILE = 64;
        private const int LIGHT_STRIDE = sizeof(float) * 16;

        private static readonly int LIGHT_COUNT_ID = Shader.PropertyToID("_PointLightCount");
        private static readonly int GRID_ID = Shader.PropertyToID("_PointVolumeGrid");
        private static readonly int CAMERA_POSITION_ID = Shader.PropertyToID("_PointCameraPosition");
        private static readonly int CAMERA_RIGHT_ID = Shader.PropertyToID("_PointCameraRight");
        private static readonly int CAMERA_UP_ID = Shader.PropertyToID("_PointCameraUp");
        private static readonly int CAMERA_FORWARD_ID = Shader.PropertyToID("_PointCameraForward");
        private static readonly int PROJECTION_ID = Shader.PropertyToID("_PointProjection");
        private static readonly int ANISOTROPY_ID = Shader.PropertyToID("_PointAnisotropy");
        private static readonly int VOLUME_ID = Shader.PropertyToID("_PointVolumeTex");
        private static readonly int GLOBAL_VOLUME_ID = Shader.PropertyToID("_PointLightVolumeTex");
        private static readonly int GLOBAL_DISTANCE_ID = Shader.PropertyToID("_PointVolumeMaxDistance");

        [Header("Compute")]
        [SerializeField] private ComputeShader _computeShader;

        [Header("Volume")]
        [SerializeField, Range(4, 32)] private int _pixelSize = 8;
        [SerializeField, Range(8, 512)] private int _depthSlices = 64;
        [SerializeField, Min(1f)] private float _maxDistance = 100f;
        [SerializeField, Range(-0.9f, 0.9f)] private float _anisotropy = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool _enableDiagnosticLogs = false;

        /// <summary>GPU上のVolumetricLightDataと同じ64バイトの構造体。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PointLightData
        {
            public Vector4 PositionRange;
            public Vector4 ColorIntensity;
            public Vector4 DirectionType;
            public Vector4 SpotParams;
        }

        private readonly Dictionary<Light, int> _visibleLights = new();
        private readonly List<VolumetricPointLightSource> _orderedSources = new();

        private PointLightData[] _staging;
        private GraphicsBuffer _lightBuffer;
        private GraphicsBuffer _tileCounts;
        private GraphicsBuffer _tileIndices;
        private int _tileCapacity;
        private VolumePass _pass;

#if UNITY_EDITOR
        private double _nextDiagnosticTime;
#endif

        public override void Create()
        {
            ReleaseBuffers();

            _staging = new PointLightData[MAX_POINT_LIGHTS];
            _lightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                MAX_POINT_LIGHTS,
                LIGHT_STRIDE);

            _pass = new VolumePass(this)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>メインGame / SceneViewのみを対象とする。</summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null ||
                _computeShader == null ||
                _lightBuffer == null ||
                _pass == null ||
                !SystemInfo.supportsComputeShaders)
            {
                return;
            }

            if (camera.GetComponent<PixelCaptureViewMarker>() != null)
            {
                return;
            }

            if (camera.cameraType != CameraType.Game &&
                camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }

        /// <summary>
        /// ライトの優先度、InstanceIDを用いて安定した順序でGPUへ転送する。
        /// 影が必要なのにリアルタイムShadow Mapがないライトは登録しない。
        /// </summary>
        private int CollectLights(
            UniversalLightData lightData,
            UniversalRenderer universalRenderer)
        {
            _visibleLights.Clear();
            _orderedSources.Clear();

            // 現在のカメラの可視ライトだけを対象にする。
            for (int i = 0; i < lightData.visibleLights.Length; i++)
            {
                if (i == lightData.mainLightIndex)
                {
                    continue;
                }

                Light light = lightData.visibleLights[i].light;
                if (light == null ||
                    (light.type != LightType.Point && light.type != LightType.Spot))
                {
                    continue;
                }

                _visibleLights[light] = i;
            }

            foreach (VolumetricPointLightSource source in VolumetricPointLightSource.Sources)
            {
                if (source != null &&
                    source.IsValid() &&
                    _visibleLights.ContainsKey(source.SourceLight))
                {
                    _orderedSources.Add(source);
                }
            }

            // 同じ優先度ならInstanceIDを使い、HashSetの列挙順に依存させない。
            _orderedSources.Sort(CompareSources);

            int count = 0;
            foreach (VolumetricPointLightSource source in _orderedSources)
            {
                if (count >= MAX_POINT_LIGHTS)
                {
                    break;
                }

                Light light = source.SourceLight;
                bool requiresShadows =
                    source.CastVolumetricShadows &&
                    light.shadows != LightShadows.None;

                int shadowIndex = -1;
                if (requiresShadows)
                {
                    if (universalRenderer == null ||
                        !_visibleLights.TryGetValue(light, out int visibleIndex))
                    {
                        continue;
                    }

                    // URP側のGetVolumetricShadowLightIndexには、
                    // 実際のリアルタイム影スライスを確認する修正が必要。
                    shadowIndex = universalRenderer.GetVolumetricShadowLightIndex(visibleIndex);
                    if (shadowIndex < 0)
                    {
                        continue;
                    }
                }

                bool isSpot = light.type == LightType.Spot;
                float outerAngle = isSpot ? light.spotAngle * 0.5f : 180f;
                float innerAngle = isSpot
                    ? Mathf.Min(light.innerSpotAngle, light.spotAngle) * 0.5f
                    : 0f;

                Vector3 position = light.transform.position;
                Vector3 direction = light.transform.forward;
                Color color = light.color;
                float intensity = light.intensity * source.VolumetricIntensity;

                _staging[count] = new PointLightData
                {
                    PositionRange = new Vector4(
                        position.x, position.y, position.z, light.range),
                    ColorIntensity = new Vector4(
                        color.r * intensity,
                        color.g * intensity,
                        color.b * intensity,
                        0f),
                    DirectionType = new Vector4(
                        direction.x, direction.y, direction.z,
                        isSpot ? 1f : 0f),
                    SpotParams = new Vector4(
                        Mathf.Cos(innerAngle * Mathf.Deg2Rad),
                        Mathf.Cos(outerAngle * Mathf.Deg2Rad),
                        shadowIndex + 1,
                        0f)
                };
                count++;
            }

            if (count > 0)
            {
                _lightBuffer.SetData(_staging, 0, 0, count);
            }

            return count;
        }

        private static int CompareSources(
            VolumetricPointLightSource left,
            VolumetricPointLightSource right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private void EnsureTileBuffers(int tileCount)
        {
            if (_tileCounts != null &&
                _tileIndices != null &&
                _tileCapacity >= tileCount)
            {
                return;
            }

            _tileCounts?.Release();
            _tileIndices?.Release();

            _tileCapacity = tileCount;
            _tileCounts = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                tileCount,
                sizeof(uint));
            _tileIndices = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                tileCount * MAX_LIGHTS_PER_TILE,
                sizeof(uint));
        }

        /// <summary>ライトを選別して3D格子に散乱光を書き込むパス。</summary>
        private sealed class VolumePass : ScriptableRenderPass
        {
            private readonly PointLightVolumeRendererFeature _owner;

            private sealed class CullData
            {
                public ComputeShader Compute;
                public BufferHandle Lights;
                public BufferHandle Counts;
                public BufferHandle Indices;
                public int LightCount;
                public Vector4 Grid;
                public Vector4 CameraPosition;
                public Vector4 CameraRight;
                public Vector4 CameraUp;
                public Vector4 CameraForward;
                public Vector4 Projection;
            }

            private sealed class EmptyData
            {
                public ComputeShader Compute;
                public TextureHandle Volume;
                public float MaxDistance;
            }

            private sealed class InjectData
            {
                public ComputeShader Compute;
                public BufferHandle Lights;
                public BufferHandle Counts;
                public BufferHandle Indices;
                public TextureHandle Volume;
                public Vector4 Grid;
                public int DepthSlices;
                public float Anisotropy;
                public float MaxDistance;
                public Vector4 CameraPosition;
                public Vector4 CameraRight;
                public Vector4 CameraUp;
                public Vector4 CameraForward;
                public Vector4 Projection;
            }

            public VolumePass(PointLightVolumeRendererFeature owner)
            {
                _owner = owner;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                Camera camera = cameraData.camera;
                if (camera == null)
                {
                    return;
                }

                int width = Mathf.Max(cameraData.cameraTargetDescriptor.width, 1);
                int height = Mathf.Max(cameraData.cameraTargetDescriptor.height, 1);
                int gridX = Mathf.CeilToInt((float)width / _owner._pixelSize);
                int gridY = Mathf.CeilToInt((float)height / _owner._pixelSize);
                int gridZ = _owner._depthSlices;
                int tileCount = gridX * gridY;

                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                int lightCount = _owner.CollectLights(
                    lightData,
                    cameraData.renderer as UniversalRenderer);

                // 有効なライトが0個なら1x1x1の黒テクスチャだけ作る。
                // 通常の大きさの3D格子を全て計算する無駄を避ける。
                if (lightCount == 0)
                {
                    TextureDesc emptyDesc = new(1, 1)
                    {
                        name = "EmptyPointLightVolume",
                        dimension = TextureDimension.Tex3D,
                        slices = 1,
                        colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                        depthBufferBits = DepthBits.None,
                        enableRandomWrite = true,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        clearBuffer = false
                    };
                    TextureHandle emptyVolume = renderGraph.CreateTexture(emptyDesc);

                    using (var builder = renderGraph.AddComputePass<EmptyData>(
                        "Clear Empty Point Light Volume", out var passData))
                    {
                        passData.Compute = _owner._computeShader;
                        passData.Volume = emptyVolume;
                        passData.MaxDistance = _owner._maxDistance;
                        builder.UseTexture(emptyVolume, AccessFlags.Write);
                        builder.SetGlobalTextureAfterPass(emptyVolume, GLOBAL_VOLUME_ID);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc(static (EmptyData data, ComputeGraphContext context) =>
                        {
                            int kernel = data.Compute.FindKernel("ClearVolume");
                            context.cmd.SetComputeTextureParam(
                                data.Compute, kernel, VOLUME_ID, data.Volume);
                            context.cmd.SetGlobalFloat(GLOBAL_DISTANCE_ID, data.MaxDistance);
                            context.cmd.DispatchCompute(data.Compute, kernel, 1, 1, 1);
                        });
                    }

                    return;
                }

                _owner.EnsureTileBuffers(tileCount);

#if UNITY_EDITOR
                if (_owner._enableDiagnosticLogs)
                {
                    double now = UnityEditor.EditorApplication.timeSinceStartup;
                    if (now >= _owner._nextDiagnosticTime)
                    {
                        _owner._nextDiagnosticTime = now + 2.0;
                        Debug.Log(
                            $"[Volumetric] Camera={camera.name}, " +
                            $"Lights={lightCount}, Grid={gridX}x{gridY}x{gridZ}");
                    }
                }
#endif

                BufferHandle lights = renderGraph.ImportBuffer(_owner._lightBuffer);
                BufferHandle counts = renderGraph.ImportBuffer(_owner._tileCounts);
                BufferHandle indices = renderGraph.ImportBuffer(_owner._tileIndices);

                TextureDesc volumeDesc = new(gridX, gridY)
                {
                    name = "PointLightVolume",
                    dimension = TextureDimension.Tex3D,
                    slices = gridZ,
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = DepthBits.None,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false
                };
                TextureHandle volume = renderGraph.CreateTexture(volumeDesc);

                float aspect = (float)width / height;
                float viewHeight = camera.orthographic
                    ? camera.orthographicSize * 2f
                    : 2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float viewWidth = viewHeight * aspect;

                Transform cameraTransform = camera.transform;
                Vector3 position = cameraTransform.position;
                Vector3 right = cameraTransform.right;
                Vector3 up = cameraTransform.up;
                Vector3 forward = cameraTransform.forward;

                Vector4 grid = new(gridX, gridY, gridZ, 0f);
                Vector4 projection = new(
                    viewWidth,
                    viewHeight,
                    _owner._maxDistance,
                    camera.orthographic ? 0f : 1f);
                Vector4 cameraPosition = new(position.x, position.y, position.z, 0f);
                Vector4 cameraRight = new(right.x, right.y, right.z, 0f);
                Vector4 cameraUp = new(up.x, up.y, up.z, 0f);
                Vector4 cameraForward = new(forward.x, forward.y, forward.z, 0f);

                // 1. 画面領域ごとに優先度順のライト一覧を作る。
                using (var builder = renderGraph.AddComputePass<CullData>(
                    "Point Light Tile Culling", out var passData))
                {
                    passData.Compute = _owner._computeShader;
                    passData.Lights = lights;
                    passData.Counts = counts;
                    passData.Indices = indices;
                    passData.LightCount = lightCount;
                    passData.Grid = grid;
                    passData.CameraPosition = cameraPosition;
                    passData.CameraRight = cameraRight;
                    passData.CameraUp = cameraUp;
                    passData.CameraForward = cameraForward;
                    passData.Projection = projection;

                    builder.UseBuffer(lights, AccessFlags.Read);
                    builder.UseBuffer(counts, AccessFlags.Write);
                    builder.UseBuffer(indices, AccessFlags.Write);
                    builder.SetRenderFunc(static (CullData data, ComputeGraphContext context) =>
                    {
                        int kernel = data.Compute.FindKernel("CullLights");
                        SetCameraParameters(
                            context.cmd, data.Compute, data.Grid,
                            data.CameraPosition, data.CameraRight,
                            data.CameraUp, data.CameraForward, data.Projection);
                        context.cmd.SetComputeIntParam(
                            data.Compute, LIGHT_COUNT_ID, data.LightCount);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_PointLights", data.Lights);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_TileCounts", data.Counts);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_TileIndices", data.Indices);

                        // Compute側[numthreads(8,8,1)]と一致させる。
                        context.cmd.DispatchCompute(
                            data.Compute, kernel,
                            Mathf.CeilToInt(data.Grid.x / 8f),
                            Mathf.CeilToInt(data.Grid.y / 8f), 1);
                    });
                }

                // 2. タイルのライト情報から、体積照明を生成する。
                using (var builder = renderGraph.AddComputePass<InjectData>(
                    "Point Light Froxel Injection", out var passData))
                {
                    passData.Compute = _owner._computeShader;
                    passData.Lights = lights;
                    passData.Counts = counts;
                    passData.Indices = indices;
                    passData.Volume = volume;
                    passData.Grid = grid;
                    passData.DepthSlices = gridZ;
                    passData.Anisotropy = _owner._anisotropy;
                    passData.MaxDistance = _owner._maxDistance;
                    passData.CameraPosition = cameraPosition;
                    passData.CameraRight = cameraRight;
                    passData.CameraUp = cameraUp;
                    passData.CameraForward = cameraForward;
                    passData.Projection = projection;

                    builder.UseBuffer(lights, AccessFlags.Read);
                    builder.UseBuffer(counts, AccessFlags.Read);
                    builder.UseBuffer(indices, AccessFlags.Read);
                    builder.UseTexture(volume, AccessFlags.Write);

                    // 追加ライトのShadow Mapを明示的に依存先へ登録する。
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    TextureHandle additionalShadows = resourceData.additionalShadowsTexture;
                    if (additionalShadows.IsValid())
                    {
                        builder.UseTexture(additionalShadows, AccessFlags.Read);
                    }

                    // URPの影用グローバルテクスチャ・行列を使う。
                    builder.UseAllGlobalTextures(true);
                    builder.SetGlobalTextureAfterPass(volume, GLOBAL_VOLUME_ID);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (InjectData data, ComputeGraphContext context) =>
                    {
                        int kernel = data.Compute.FindKernel("InjectFroxels");
                        SetCameraParameters(
                            context.cmd, data.Compute, data.Grid,
                            data.CameraPosition, data.CameraRight,
                            data.CameraUp, data.CameraForward, data.Projection);
                        context.cmd.SetComputeFloatParam(
                            data.Compute, ANISOTROPY_ID, data.Anisotropy);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_PointLights", data.Lights);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_TileCountsRead", data.Counts);
                        context.cmd.SetComputeBufferParam(
                            data.Compute, kernel, "_TileIndicesRead", data.Indices);
                        context.cmd.SetComputeTextureParam(
                            data.Compute, kernel, VOLUME_ID, data.Volume);
                        context.cmd.SetGlobalFloat(GLOBAL_DISTANCE_ID, data.MaxDistance);

                        // 全ての格子を書き換える。ライトが0個のときもゼロを書き込む。
                        context.cmd.DispatchCompute(
                            data.Compute, kernel,
                            Mathf.CeilToInt(data.Grid.x / 8f),
                            Mathf.CeilToInt(data.Grid.y / 8f),
                            data.DepthSlices);
                    });
                }
            }

            private static void SetCameraParameters(
                ComputeCommandBuffer cmd,
                ComputeShader compute,
                Vector4 grid,
                Vector4 cameraPosition,
                Vector4 cameraRight,
                Vector4 cameraUp,
                Vector4 cameraForward,
                Vector4 projection)
            {
                cmd.SetComputeVectorParam(compute, GRID_ID, grid);
                cmd.SetComputeVectorParam(compute, CAMERA_POSITION_ID, cameraPosition);
                cmd.SetComputeVectorParam(compute, CAMERA_RIGHT_ID, cameraRight);
                cmd.SetComputeVectorParam(compute, CAMERA_UP_ID, cameraUp);
                cmd.SetComputeVectorParam(compute, CAMERA_FORWARD_ID, cameraForward);
                cmd.SetComputeVectorParam(compute, PROJECTION_ID, projection);
            }
        }

        private void ReleaseBuffers()
        {
            _lightBuffer?.Release();
            _tileCounts?.Release();
            _tileIndices?.Release();
            _lightBuffer = null;
            _tileCounts = null;
            _tileIndices = null;
            _tileCapacity = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            ReleaseBuffers();
            _pass = null;
        }
    }
}
