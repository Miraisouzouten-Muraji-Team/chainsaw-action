
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
    /// 画面領域ごとに影響するライトを選別し、GPUで散乱光を計算する。
    /// 現在は平行投影に対応する。
    /// </summary>
    public sealed class PointLightVolumeRendererFeature : ScriptableRendererFeature
    {
        private const int MAX_POINT_LIGHTS = 1024;
        private const int MAX_LIGHTS_PER_TILE = 64;
        private const int LIGHT_STRIDE = sizeof(float) * 16;

        // Compute ShaderのProperty ID。
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

        [SerializeField, Range(8, 64)]
        private int _depthSlices = 32;

        [SerializeField, Min(1f)]
        private float _maxDistance = 100f;

        [SerializeField, Range(-0.9f, 0.9f)]
        private float _anisotropy = 0.5f;

        [Header("Debug")]
        [SerializeField]
        private bool _enableDiagnosticLogs = true;

        // GPUへ渡すPoint / Spot共通のライト情報。
        // Vector4を4個使用するため64バイト。
        [StructLayout(LayoutKind.Sequential)]
        private struct PointLightData
        {
            public Vector4 PositionRange;
            public Vector4 ColorIntensity;
            public Vector4 DirectionType;
            public Vector4 SpotParams;
        }

        private PointLightData[] _staging;

        private GraphicsBuffer _lightBuffer;
        private GraphicsBuffer _tileCounts;
        private GraphicsBuffer _tileIndices;

        private int _tileCapacity;

        private VolumePass _pass;

#if UNITY_EDITOR
        // Editor上のログを2秒間隔に制限する。
        private double _nextEnqueueLogTime;
#endif

        /// <summary>
        /// GPUバッファと描画Passを初期化する。
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
        /// 対象カメラに体積照明Passを登録する。
        /// </summary>
        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

            // Renderer Featureが呼ばれているか確認する。
#if UNITY_EDITOR
            if (_enableDiagnosticLogs &&
                camera != null &&
                (camera.cameraType == CameraType.Game ||
                 camera.cameraType == CameraType.SceneView))
            {
                double currentTime =
                    UnityEditor.EditorApplication.timeSinceStartup;

                if (currentTime >= _nextEnqueueLogTime)
                {
                    _nextEnqueueLogTime = currentTime + 2.0;

                    Debug.Log(
                        $"[Volumetric][AddRenderPasses] " +
                        $"Camera: {camera.name}, " +
                        $"Type: {camera.cameraType}, " +
                        $"Orthographic: {camera.orthographic}, " +
                        $"Compute: {_computeShader != null}, " +
                        $"Pass: {_pass != null}, " +
                        $"SupportsCompute: {SystemInfo.supportsComputeShaders}");
                }
            }
#endif

            if (_computeShader == null ||
                _pass == null ||
                !SystemInfo.supportsComputeShaders)
            {
                return;
            }

            if (camera == null)
            {
                return;
            }

            // Pixel Capture Cameraでは体積照明を重複計算しない。
            if (camera.GetComponent<PixelCaptureViewMarker>() != null)
            {
                return;
            }

            // Game / Sceneビュー以外では実行しない。
            if (camera.cameraType != CameraType.Game &&
                camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }

        /// <summary>
        /// 有効なPoint / Spot Lightを収集し、GPU転送用データを作成する。
        /// URPが割り当てた追加ライトの影番号も取得する。
        /// </summary>
        private int CollectLights(
            UniversalLightData lightData,
            UniversalRenderer universalRenderer)
        {
            // LightとURP内部の影番号を対応付ける。
            Dictionary<Light, int> shadowIndices = new();

            if (universalRenderer != null)
            {
                for (int i = 0; i < lightData.visibleLights.Length; i++)
                {
                    // Main Lightは対象外。
                    if (i == lightData.mainLightIndex)
                    {
                        continue;
                    }

                    VisibleLight visibleLight =
                        lightData.visibleLights[i];

                    Light light = visibleLight.light;

                    if (light == null)
                    {
                        continue;
                    }

                    if (light.type != LightType.Point &&
                        light.type != LightType.Spot)
                    {
                        continue;
                    }

                    int shadowIndex =
                        universalRenderer.GetVolumetricShadowLightIndex(i);

                    shadowIndices[light] = shadowIndex;
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

                bool isSpot =
                    light.type == LightType.Spot;

                // 影がない場合は-1。
                int shadowIndex = -1;

                if (source.CastVolumetricShadows &&
                    light.shadows != LightShadows.None &&
                    shadowIndices.TryGetValue(
                        light,
                        out int foundIndex))
                {
                    shadowIndex = foundIndex;
                }

                // Spot Angleは円錐の全角なので半分にする。
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

                // GPUへ転送するライト情報。
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

                    // 影なしは0としてGPUへ渡す。
                    SpotParams = new Vector4(
                        innerCos,
                        outerCos,
                        shadowIndex + 1,
                        0f)
                };

                count++;
            }

            // 収集したライトのみGPUへ転送する。
            if (count > 0)
            {
                _lightBuffer.SetData(
                    _staging,
                    0,
                    0,
                    count);
            }

            return count;
        }

        /// <summary>
        /// 画面領域ごとのライト情報を保持するGPUバッファを確保する。
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
        /// 領域別のライト選別と3D体積照明の生成を実行するPass。
        /// </summary>
        private sealed class VolumePass : ScriptableRenderPass
        {
            private readonly PointLightVolumeRendererFeature _owner;

#if UNITY_EDITOR
            // RenderGraph側のログを2秒間隔に制限する。
            private double _nextLogTime;
#endif

            /// <summary>
            /// ライト選別用のPassデータ。
            /// </summary>
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

            /// <summary>
            /// 3D体積照明の生成用Passデータ。
            /// </summary>
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
            /// Compute Shaderによる体積照明処理をRender Graphに登録する。
            /// </summary>
            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                Camera camera = cameraData.camera;

                // 現在のカメラ情報をログ出力する。
                // 投影方式によるreturnより前に実行する。
                bool shouldLog = false;

#if UNITY_EDITOR
                if (_owner._enableDiagnosticLogs)
                {
                    double currentTime =
                        UnityEditor.EditorApplication.timeSinceStartup;

                    if (currentTime >= _nextLogTime)
                    {
                        _nextLogTime = currentTime + 2.0;
                        shouldLog = true;

                        Debug.Log(
                            $"[Volumetric][RecordRenderGraph] " +
                            $"Camera: {(camera != null ? camera.name : "null")}, " +
                            $"Type: {(camera != null ? camera.cameraType.ToString() : "null")}, " +
                            $"Orthographic: {(camera != null && camera.orthographic)}");
                    }
                }
#endif

                // 現在の3D格子計算は平行投影専用。
                if (camera == null || !camera.orthographic)
                {
                    return;
                }

                int width =
                    cameraData.cameraTargetDescriptor.width;

                int height =
                    cameraData.cameraTargetDescriptor.height;

                // 画面をPixel Size単位の領域に分割する。
                int gridX = Mathf.CeilToInt(
                    (float)width / _owner._pixelSize);

                int gridY = Mathf.CeilToInt(
                    (float)height / _owner._pixelSize);

                int gridZ = _owner._depthSlices;

                int tileCount = gridX * gridY;

                _owner.EnsureTileBuffers(tileCount);

                // URPの可視ライトを取得する。
                UniversalLightData lightData =
                    frameData.Get<UniversalLightData>();

                // Point / Spot LightをGPU転送用に収集する。
                int lightCount = _owner.CollectLights(
                    lightData,
                    cameraData.renderer as UniversalRenderer);

                // 登録ライト数と可視ライト数を確認する。
#if UNITY_EDITOR
                if (shouldLog)
                {
                    Debug.Log(
                        $"[Volumetric][Lights] " +
                        $"Camera: {camera.name}, " +
                        $"Registered Lights: {lightCount}, " +
                        $"Visible Lights: {lightData.visibleLights.Length}, " +
                        $"Grid: {gridX}x{gridY}x{gridZ}");
                }
#endif

                // GPUバッファをRender Graphへ登録する。
                BufferHandle lights =
                    renderGraph.ImportBuffer(_owner._lightBuffer);

                BufferHandle counts =
                    renderGraph.ImportBuffer(_owner._tileCounts);

                BufferHandle indices =
                    renderGraph.ImportBuffer(_owner._tileIndices);

                // 3Dの照明テクスチャを作成する。
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

                // 平行投影カメラの画面サイズをワールド単位で計算する。
                float aspect =
                    (float)width / Mathf.Max(height, 1);

                float screenHeight =
                    camera.orthographicSize * 2f;

                float screenWidth =
                    screenHeight * aspect;

                Transform cameraTransform =
                    camera.transform;

                Vector3 position =
                    cameraTransform.position;

                Vector3 right =
                    cameraTransform.right;

                Vector3 up =
                    cameraTransform.up;

                Vector3 forward =
                    cameraTransform.forward;

                Vector4 grid = new(
                    gridX,
                    gridY,
                    gridZ,
                    0f);

                Vector4 projection = new(
                    screenWidth,
                    screenHeight,
                    _owner._maxDistance,
                    0f);

                Vector4 cameraPosition = new(
                    position.x,
                    position.y,
                    position.z,
                    0f);

                Vector4 cameraRight = new(
                    right.x,
                    right.y,
                    right.z,
                    0f);

                Vector4 cameraUp = new(
                    up.x,
                    up.y,
                    up.z,
                    0f);

                Vector4 cameraForward = new(
                    forward.x,
                    forward.y,
                    forward.z,
                    0f);

                // ----------------------------------------------------
                // 1. Tile Culling
                // ----------------------------------------------------

                using (var builder =
                    renderGraph.AddComputePass<CullData>(
                        "Point Light Tile Culling",
                        out var passData))
                {
                    // Compute Shader。
                    passData.Compute =
                        _owner._computeShader;

                    // GPUバッファ。
                    passData.Lights = lights;
                    passData.Counts = counts;
                    passData.Indices = indices;

                    passData.LightCount = lightCount;

                    // 3D格子とカメラ情報。
                    passData.Grid = grid;

                    passData.CameraPosition = cameraPosition;
                    passData.CameraRight = cameraRight;
                    passData.CameraUp = cameraUp;
                    passData.CameraForward = cameraForward;
                    passData.Projection = projection;

                    // GPUリソースへのアクセス。
                    builder.UseBuffer(
                        lights,
                        AccessFlags.Read);

                    builder.UseBuffer(
                        counts,
                        AccessFlags.Write);

                    builder.UseBuffer(
                        indices,
                        AccessFlags.Write);

                    // 各画面領域に影響するライトを選別する。
                    builder.SetRenderFunc(static (
                        CullData data,
                        ComputeGraphContext context) =>
                    {
                        int kernel =
                            data.Compute.FindKernel("CullLights");

                        // カメラ情報をCompute Shaderへ設定する。
                        SetCameraParameters(
                            context.cmd,
                            data.Compute,
                            data.Grid,
                            data.CameraPosition,
                            data.CameraRight,
                            data.CameraUp,
                            data.CameraForward,
                            data.Projection);

                        // 登録ライト数。
                        context.cmd.SetComputeIntParam(
                            data.Compute,
                            LIGHT_COUNT_ID,
                            data.LightCount);

                        // ライト情報。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_PointLights",
                            data.Lights);

                        // 各領域のライト登録数。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileCounts",
                            data.Counts);

                        // 各領域に登録したライト番号。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileIndices",
                            data.Indices);

                        // 画面領域ごとに1グループ実行する。
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
                    // Compute Shader。
                    passData.Compute =
                        _owner._computeShader;

                    // GPUバッファ。
                    passData.Lights = lights;
                    passData.Counts = counts;
                    passData.Indices = indices;

                    // 3D照明テクスチャ。
                    passData.Volume = volume;

                    // 格子設定。
                    passData.Grid = grid;
                    passData.DepthSlices = gridZ;

                    passData.Anisotropy =
                        _owner._anisotropy;

                    passData.MaxDistance =
                        _owner._maxDistance;

                    // カメラ情報。
                    passData.CameraPosition = cameraPosition;
                    passData.CameraRight = cameraRight;
                    passData.CameraUp = cameraUp;
                    passData.CameraForward = cameraForward;
                    passData.Projection = projection;

                    // GPUバッファの読み取り依存関係。
                    builder.UseBuffer(
                        passData.Lights,
                        AccessFlags.Read);

                    builder.UseBuffer(
                        passData.Counts,
                        AccessFlags.Read);

                    builder.UseBuffer(
                        passData.Indices,
                        AccessFlags.Read);

                    // 3D照明テクスチャへの書き込み。
                    builder.UseTexture(
                        passData.Volume,
                        AccessFlags.Write);

                    // URPの追加ライトShadow Mapを取得する。
                    UniversalResourceData resourceData =
                        frameData.Get<UniversalResourceData>();

                    TextureHandle additionalShadows =
                        resourceData.additionalShadowsTexture;

                    // Shadow Mapがある場合に読み取り依存関係を登録する。
                    if (additionalShadows.IsValid())
                    {
                        builder.UseTexture(
                            additionalShadows,
                            AccessFlags.Read);
                    }

                    // URPのグローバルテクスチャを使用する。
                    builder.UseAllGlobalTextures(true);

                    // 計算した3D照明テクスチャを後続のFogへ公開する。
                    builder.SetGlobalTextureAfterPass(
                        volume,
                        GLOBAL_VOLUME_ID);

                    builder.AllowGlobalStateModification(true);

                    // 3D格子にPoint / Spot Lightの散乱光を生成する。
                    builder.SetRenderFunc(static (
                        InjectData data,
                        ComputeGraphContext context) =>
                    {
                        int kernel =
                            data.Compute.FindKernel("InjectFroxels");

                        // カメラ情報をCompute Shaderへ設定する。
                        SetCameraParameters(
                            context.cmd,
                            data.Compute,
                            data.Grid,
                            data.CameraPosition,
                            data.CameraRight,
                            data.CameraUp,
                            data.CameraForward,
                            data.Projection);

                        // 散乱方向の設定。
                        context.cmd.SetComputeFloatParam(
                            data.Compute,
                            ANISOTROPY_ID,
                            data.Anisotropy);

                        // ライト情報。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_PointLights",
                            data.Lights);

                        // 領域ごとのライト数。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileCountsRead",
                            data.Counts);

                        // 領域ごとのライト番号。
                        context.cmd.SetComputeBufferParam(
                            data.Compute,
                            kernel,
                            "_TileIndicesRead",
                            data.Indices);

                        // 3D照明テクスチャの出力先。
                        context.cmd.SetComputeTextureParam(
                            data.Compute,
                            kernel,
                            VOLUME_ID,
                            data.Volume);

                        // Fog側の参照距離と一致させる。
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
            /// カメラ情報をCompute Shaderに設定する。
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
                    compute,
                    GRID_ID,
                    grid);

                cmd.SetComputeVectorParam(
                    compute,
                    CAMERA_POSITION_ID,
                    cameraPosition);

                cmd.SetComputeVectorParam(
                    compute,
                    CAMERA_RIGHT_ID,
                    cameraRight);

                cmd.SetComputeVectorParam(
                    compute,
                    CAMERA_UP_ID,
                    cameraUp);

                cmd.SetComputeVectorParam(
                    compute,
                    CAMERA_FORWARD_ID,
                    cameraForward);

                cmd.SetComputeVectorParam(
                    compute,
                    PROJECTION_ID,
                    projection);
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

        /// <summary>
        /// Renderer Featureを破棄する際にリソースを解放する。
        /// </summary>
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
