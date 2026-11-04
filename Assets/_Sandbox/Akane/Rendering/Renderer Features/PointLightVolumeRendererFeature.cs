
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
    /// Point / Spot Lightの体積照明を3Dテクスチャへ生成する。
    /// 平行投影と透視投影の両方に対応する。
    /// </summary>
    public sealed class PointLightVolumeRendererFeature : ScriptableRendererFeature
    {
        private const int MAX_POINT_LIGHTS = 1024;
        private const int MAX_LIGHTS_PER_TILE = 64;
        private const int LIGHT_STRIDE = sizeof(float) * 16;

        private static readonly int LIGHT_COUNT_ID =
            Shader.PropertyToID("_PointLightCount");

        private static readonly int GRID_ID =
            Shader.PropertyToID("_PointVolumeGrid");

        private static readonly int CAMERA_POSITION_ID =
            Shader.PropertyToID("_PointCameraPosition");

        private static readonly int CAMERA_RIGHT_ID =
            Shader.PropertyToID("_PointCameraRight");

        private static readonly int CAMERA_UP_ID =
            Shader.PropertyToID("_PointCameraUp");

        private static readonly int CAMERA_FORWARD_ID =
            Shader.PropertyToID("_PointCameraForward");

        private static readonly int PROJECTION_ID =
            Shader.PropertyToID("_PointProjection");

        private static readonly int ANISOTROPY_ID =
            Shader.PropertyToID("_PointAnisotropy");

        private static readonly int VOLUME_ID =
            Shader.PropertyToID("_PointVolumeTex");

        private static readonly int GLOBAL_VOLUME_ID =
            Shader.PropertyToID("_PointLightVolumeTex");

        private static readonly int GLOBAL_DISTANCE_ID =
            Shader.PropertyToID("_PointVolumeMaxDistance");

        [Header("Compute")]
        [SerializeField]
        private ComputeShader _computeShader;

        [Header("Volume")]
        [SerializeField, Range(4, 32)]
        private int _pixelSize = 8;

        [SerializeField, Range(8, 512)]
        private int _depthSlices = 32;

        [SerializeField, Min(1f)]
        private float _maxDistance = 100f;

        [SerializeField, Range(-0.9f, 0.9f)]
        private float _anisotropy = 0.5f;

        [Header("Debug")]
        [SerializeField]
        private bool _enableDiagnosticLogs = false;

        /// <summary>
        /// GPUへ転送するライト情報。
        /// HLSL側のVolumetricLightDataと配置を合わせる。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PointLightData
        {
            public Vector4 PositionRange;
            public Vector4 ColorIntensity;
            public Vector4 DirectionType;
            public Vector4 SpotParams;
        }

        private PointLightData[] _staging;

        private readonly Dictionary<Light, int> _shadowIndices = new();

        private GraphicsBuffer _lightBuffer;
        private GraphicsBuffer _tileCounts;
        private GraphicsBuffer _tileIndices;

        private int _tileCapacity;
        private VolumePass _pass;

#if UNITY_EDITOR
        private double _nextEnqueueLogTime;
#endif

        /// <summary>
        /// 必要なバッファとPassを作成する。
        /// </summary>
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
                renderPassEvent =
                    RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        /// <summary>
        /// Game / Sceneカメラに描画Passを登録する。
        /// </summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

#if UNITY_EDITOR
            if (_enableDiagnosticLogs && camera != null)
            {
                double currentTime =
                    UnityEditor.EditorApplication.timeSinceStartup;

                if (currentTime >= _nextEnqueueLogTime)
                {
                    _nextEnqueueLogTime = currentTime + 2.0;

                    Debug.Log(
                        $"[Volumetric][AddRenderPasses] " +
                        $"Camera={camera.name}, " +
                        $"Type={camera.cameraType}, " +
                        $"Orthographic={camera.orthographic}");
                }
            }
#endif

            if (camera == null ||
                _computeShader == null ||
                _pass == null ||
                !SystemInfo.supportsComputeShaders)
            {
                return;
            }

            // Pixel Capture Cameraでは重複計算しない。
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
        /// Point / Spot Lightを収集してGPUへ転送する。
        /// </summary>
        private int CollectLights(
            UniversalLightData lightData,
            UniversalRenderer universalRenderer)
        {
            _shadowIndices.Clear();

            // URPのVisibleLight番号と影番号を対応付ける。
            if (universalRenderer != null)
            {
                for (int i = 0; i < lightData.visibleLights.Length; i++)
                {
                    if (i == lightData.mainLightIndex)
                    {
                        continue;
                    }

                    VisibleLight visibleLight =
                        lightData.visibleLights[i];

                    Light light = visibleLight.light;

                    if (light == null ||
                        (light.type != LightType.Point &&
                         light.type != LightType.Spot))
                    {
                        continue;
                    }

                    int shadowIndex =
                        universalRenderer.GetVolumetricShadowLightIndex(i);

                    _shadowIndices[light] = shadowIndex;
                }
            }

            int count = 0;

            foreach (VolumetricPointLightSource source
                     in VolumetricPointLightSource.Sources)
            {
                if (source == null || !source.IsValid())
                {
                    continue;
                }

                if (count >= MAX_POINT_LIGHTS)
                {
                    break;
                }

                Light light = source.SourceLight;

                Vector3 position = light.transform.position;
                Vector3 direction = light.transform.forward;

                Color color = light.color;

                float intensity =
                    light.intensity * source.VolumetricIntensity;

                bool isSpot = light.type == LightType.Spot;

                // 有効なShadow Mapがなければ-1。
                int shadowIndex = -1;

                if (source.CastVolumetricShadows &&
                    light.shadows != LightShadows.None &&
                    _shadowIndices.TryGetValue(
                        light, out int foundIndex))
                {
                    shadowIndex = foundIndex;
                }

                // Spot Angleは円錐全体の角度。
                float outerAngle = isSpot
                    ? light.spotAngle * 0.5f
                    : 180f;

                float innerAngle = isSpot
                    ? Mathf.Min(
                        light.innerSpotAngle,
                        light.spotAngle) * 0.5f
                    : 0f;

                float outerCos =
                    Mathf.Cos(outerAngle * Mathf.Deg2Rad);

                float innerCos =
                    Mathf.Cos(innerAngle * Mathf.Deg2Rad);

                _staging[count] = new PointLightData
                {
                    PositionRange = new Vector4(
                        position.x,
                        position.y,
                        position.z,
                        light.range),

                    ColorIntensity = new Vector4(
                        color.r * intensity,
                        color.g * intensity,
                        color.b * intensity,
                        0f),

                    DirectionType = new Vector4(
                        direction.x,
                        direction.y,
                        direction.z,
                        isSpot ? 1f : 0f),

                    SpotParams = new Vector4(
                        innerCos,
                        outerCos,
                        shadowIndex + 1,
                        0f)
                };

                count++;
            }

            if (count > 0)
            {
                _lightBuffer.SetData(
                    _staging, 0, 0, count);
            }

            return count;
        }

        /// <summary>
        /// 画面領域ごとのライト一覧用バッファを確保する。
        /// </summary>
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

        /// <summary>
        /// 領域別ライト選別と3D体積照明の生成を行う。
        /// </summary>
        private sealed class VolumePass : ScriptableRenderPass
        {
            private readonly PointLightVolumeRendererFeature _owner;

#if UNITY_EDITOR
            private double _nextLogTime;
#endif

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

            public VolumePass(
                PointLightVolumeRendererFeature owner)
            {
                _owner = owner;
            }

            /// <summary>
            /// Render GraphへCompute Passを登録する。
            /// </summary>
            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                Camera camera = cameraData.camera;

                // 平行投影・透視投影の両方を処理する。
                if (camera == null)
                {
                    return;
                }

                int width = Mathf.Max(
                    cameraData.cameraTargetDescriptor.width, 1);

                int height = Mathf.Max(
                    cameraData.cameraTargetDescriptor.height, 1);

                int gridX = Mathf.CeilToInt(
                    (float)width / _owner._pixelSize);

                int gridY = Mathf.CeilToInt(
                    (float)height / _owner._pixelSize);

                int gridZ = _owner._depthSlices;
                int tileCount = gridX * gridY;

                _owner.EnsureTileBuffers(tileCount);

                UniversalLightData lightData =
                    frameData.Get<UniversalLightData>();

                int lightCount = _owner.CollectLights(
                    lightData,
                    cameraData.renderer as UniversalRenderer);

#if UNITY_EDITOR
                if (_owner._enableDiagnosticLogs)
                {
                    double currentTime =
                        UnityEditor.EditorApplication.timeSinceStartup;

                    if (currentTime >= _nextLogTime)
                    {
                        _nextLogTime = currentTime + 2.0;

                        Debug.Log(
                            $"[Volumetric] Camera={camera.name}, " +
                            $"Orthographic={camera.orthographic}, " +
                            $"Lights={lightCount}, " +
                            $"Visible={lightData.visibleLights.Length}, " +
                            $"Grid={gridX}x{gridY}x{gridZ}");

                        for (int i = 0; i < lightCount; i++)
                        {
                            PointLightData light = _owner._staging[i];

                            int shadowIndex =
                                Mathf.RoundToInt(light.SpotParams.z) - 1;

                            Debug.Log(
                                $"[Volumetric] Light={i}, " +
                                $"Type={(light.DirectionType.w > 0.5f ? "Spot" : "Point")}, " +
                                $"ShadowIndex={shadowIndex}");
                        }
                    }
                }
#endif

                // GPUバッファをRender Graphへ登録する。
                BufferHandle lights =
                    renderGraph.ImportBuffer(_owner._lightBuffer);

                BufferHandle counts =
                    renderGraph.ImportBuffer(_owner._tileCounts);

                BufferHandle indices =
                    renderGraph.ImportBuffer(_owner._tileIndices);

                // 体積照明の3Dテクスチャを作成する。
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

                TextureHandle volume =
                    renderGraph.CreateTexture(volumeDesc);

                // ----------------------------------------------------
                // Camera Projection
                // ----------------------------------------------------

                float aspect = (float)width / height;
                float viewHeight;

                if (camera.orthographic)
                {
                    // 平行投影は奥行きによらず画面幅が一定。
                    viewHeight =
                        camera.orthographicSize * 2f;
                }
                else
                {
                    // 透視投影は奥行き1の視野幅を求める。
                    float halfFovRadians =
                        camera.fieldOfView *
                        Mathf.Deg2Rad * 0.5f;

                    viewHeight =
                        2f * Mathf.Tan(halfFovRadians);
                }

                float viewWidth = viewHeight * aspect;

                Transform cameraTransform = camera.transform;

                Vector3 position = cameraTransform.position;
                Vector3 right = cameraTransform.right;
                Vector3 up = cameraTransform.up;
                Vector3 forward = cameraTransform.forward;

                Vector4 grid = new(
                    gridX, gridY, gridZ, 0f);

                // x: 視野幅、y: 視野高さ。
                // z: 最大計算距離。
                // w: 0=平行投影、1=透視投影。
                Vector4 projection = new(
                    viewWidth,
                    viewHeight,
                    _owner._maxDistance,
                    camera.orthographic ? 0f : 1f);

                Vector4 cameraPosition = new(
                    position.x, position.y, position.z, 0f);

                Vector4 cameraRight = new(
                    right.x, right.y, right.z, 0f);

                Vector4 cameraUp = new(
                    up.x, up.y, up.z, 0f);

                Vector4 cameraForward = new(
                    forward.x, forward.y, forward.z, 0f);

                // ----------------------------------------------------
                // 1. Tile Culling
                // ----------------------------------------------------

                using (var builder =
                    renderGraph.AddComputePass<CullData>(
                        "Point Light Tile Culling",
                        out var passData))
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

                    builder.SetRenderFunc(static (
                        CullData data,
                        ComputeGraphContext context) =>
                    {
                        int kernel =
                            data.Compute.FindKernel("CullLights");

                        SetCameraParameters(
                            context.cmd,
                            data.Compute,
                            data.Grid,
                            data.CameraPosition,
                            data.CameraRight,
                            data.CameraUp,
                            data.CameraForward,
                            data.Projection);

                        context.cmd.SetComputeIntParam(
                            data.Compute,
                            LIGHT_COUNT_ID,
                            data.LightCount);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_PointLights",
                            data.Lights);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileCounts",
                            data.Counts);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileIndices",
                            data.Indices);

                        // 1領域につき1スレッドグループを実行。
                        context.cmd.DispatchCompute(
                            data.Compute,
                            kernel,
                            (int)data.Grid.x,
                            (int)data.Grid.y,
                            1);
                    });
                }

                // ----------------------------------------------------
                // 2. Froxel Injection
                // ----------------------------------------------------

                using (var builder =
                    renderGraph.AddComputePass<InjectData>(
                        "Point Light Froxel Injection",
                        out var passData))
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

                    builder.UseBuffer(
                        passData.Lights,
                        AccessFlags.Read);

                    builder.UseBuffer(
                        passData.Counts,
                        AccessFlags.Read);

                    builder.UseBuffer(
                        passData.Indices,
                        AccessFlags.Read);

                    builder.UseTexture(
                        passData.Volume,
                        AccessFlags.Write);

                    // URPの追加ライトShadow Mapを参照する。
                    UniversalResourceData resourceData =
                        frameData.Get<UniversalResourceData>();

                    TextureHandle additionalShadows =
                        resourceData.additionalShadowsTexture;

                    if (additionalShadows.IsValid())
                    {
                        builder.UseTexture(
                            additionalShadows,
                            AccessFlags.Read);
                    }

                    // URPの影用グローバルテクスチャを利用する。
                    builder.UseAllGlobalTextures(true);

                    // 計算結果を後続のVolumetric Fogへ渡す。
                    builder.SetGlobalTextureAfterPass(
                        volume,
                        GLOBAL_VOLUME_ID);

                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (
                        InjectData data,
                        ComputeGraphContext context) =>
                    {
                        int kernel =
                            data.Compute.FindKernel("InjectFroxels");

                        SetCameraParameters(
                            context.cmd,
                            data.Compute,
                            data.Grid,
                            data.CameraPosition,
                            data.CameraRight,
                            data.CameraUp,
                            data.CameraForward,
                            data.Projection);

                        context.cmd.SetComputeFloatParam(
                            data.Compute,
                            ANISOTROPY_ID,
                            data.Anisotropy);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_PointLights",
                            data.Lights);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileCountsRead",
                            data.Counts);

                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileIndicesRead",
                            data.Indices);

                        context.cmd.SetComputeTextureParam(
                            data.Compute,
                            kernel,
                            VOLUME_ID,
                            data.Volume);

                        context.cmd.SetGlobalFloat(
                            GLOBAL_DISTANCE_ID,
                            data.MaxDistance);

                        // 8×8×1スレッドで3D格子を計算する。
                        context.cmd.DispatchCompute(
                            data.Compute,
                            kernel,
                            Mathf.CeilToInt(data.Grid.x / 8f),
                            Mathf.CeilToInt(data.Grid.y / 8f),
                            data.DepthSlices);
                    });
                }
            }

            /// <summary>
            /// Compute Shaderへカメラ情報を設定する。
            /// </summary>
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
                cmd.SetComputeVectorParam(
                    compute, GRID_ID, grid);

                cmd.SetComputeVectorParam(
                    compute, CAMERA_POSITION_ID, cameraPosition);

                cmd.SetComputeVectorParam(
                    compute, CAMERA_RIGHT_ID, cameraRight);

                cmd.SetComputeVectorParam(
                    compute, CAMERA_UP_ID, cameraUp);

                cmd.SetComputeVectorParam(
                    compute, CAMERA_FORWARD_ID, cameraForward);

                cmd.SetComputeVectorParam(
                    compute, PROJECTION_ID, projection);
            }
        }

        /// <summary>
        /// GPUバッファを解放する。
        /// </summary>
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
