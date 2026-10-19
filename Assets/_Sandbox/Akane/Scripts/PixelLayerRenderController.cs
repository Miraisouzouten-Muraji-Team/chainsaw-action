
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Near / Middle / FarのCapture CameraとRenderTextureを管理する。
    /// Pixel RenderingのON/OFFにも対応する。
    /// </summary>
    public sealed class PixelLayerRenderController : MonoBehaviour
    {
        private const string PIXEL_EFFECT_ENABLED = "_PixelEffectEnabled";

        [Header("Cameras")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private Camera _nearCamera;
        [SerializeField] private Camera _middleCamera;
        [SerializeField] private Camera _farCamera;

        [Header("Materials")]
        [SerializeField] private Material _compositeMaterial;
        [SerializeField] private Material _retroPixelMaterial;
        [SerializeField] private Material _volumetricFogMaterial;

        [Header("Resolution")]
        [SerializeField] private Vector2Int _nearResolution = new(640, 360);
        [SerializeField] private Vector2Int _middleResolution = new(320, 180);
        [SerializeField] private Vector2Int _farResolution = new(160, 90);

        [Header("URP Renderer Indices")]
        [SerializeField] private int _normalRendererIndex = 0;
        [SerializeField] private int _captureRendererIndex = 1;
        [SerializeField] private int _pixelRendererIndex = 2;

        [Header("Pixel Rendering")]
        [SerializeField] private bool _pixelRenderingEnabled = true;

        private RenderTexture _nearTexture;
        private RenderTexture _middleTexture;
        private RenderTexture _farTexture;
        private bool _appliedPixelRenderingEnabled;
        private bool _initialized;

        public bool PixelRenderingEnabled => _pixelRenderingEnabled;
        public Camera MainCamera => _mainCamera;
        public Material CompositeMaterial => _compositeMaterial;
        public Material VolumetricFogMaterial => _volumetricFogMaterial;
        public int NormalRendererIndex => _normalRendererIndex;
        public int CaptureRendererIndex => _captureRendererIndex;
        public int PixelRendererIndex => _pixelRendererIndex;

        /// <summary>
        /// Capture番号に対応する表示名を返す。
        /// 旧Editorコードとの互換性のため、メソッド名は維持する。
        /// </summary>
        public string GetCaptureLayerName(int index)
        {
            return index switch
            {
                0 => "PixelNear",
                1 => "PixelMiddle",
                2 => "PixelFar",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Capture番号に対応する解像度を取得する。
        /// </summary>
        public Vector2Int GetCaptureResolution(int index)
        {
            return index switch
            {
                0 => _nearResolution,
                1 => _middleResolution,
                2 => _farResolution,
                _ => Vector2Int.zero
            };
        }

        private void Awake()
        {
            if (!Initialize())
            {
                enabled = false;
                return;
            }

            CreateRenderTextures();
            ConfigureCaptureCamera(_nearCamera, _nearTexture, 0, -3f);
            ConfigureCaptureCamera(_middleCamera, _middleTexture, 1, -2f);
            ConfigureCaptureCamera(_farCamera, _farTexture, 2, -1f);
            ApplyTexturesToMaterial();

            _initialized = true;
            SetPixelRenderingEnabled(_pixelRenderingEnabled);
        }

        private void LateUpdate()
        {
            // Inspectorや外部スクリプトからの状態変更を反映する。
            if (_pixelRenderingEnabled != _appliedPixelRenderingEnabled)
            {
                SetPixelRenderingEnabled(_pixelRenderingEnabled);
            }

            if (_pixelRenderingEnabled) SyncAllCaptureCameras();
        }

        /// <summary>
        /// Pixel Rendering全体の有効・無効を切り替える。
        /// </summary>
        public void SetPixelRenderingEnabled(bool isEnabled)
        {
            _pixelRenderingEnabled = isEnabled;
            if (!_initialized) return;

            SetCaptureCamerasEnabled(isEnabled);
            SetMainCameraRenderer(isEnabled);
            SetMaterialState(isEnabled);

            if (isEnabled) SyncAllCaptureCameras();
            _appliedPixelRenderingEnabled = isEnabled;
        }

        public void TogglePixelRendering()
        {
            SetPixelRenderingEnabled(!_pixelRenderingEnabled);
        }

        private bool Initialize()
        {
            if (_mainCamera == null || _nearCamera == null ||
                _middleCamera == null || _farCamera == null)
            {
                Debug.LogError("Main / Near / Middle / Far Cameraの参照を確認してください。", this);
                return false;
            }

            if (_normalRendererIndex < 0 || _captureRendererIndex < 0 ||
                _pixelRendererIndex < 0 ||
                _normalRendererIndex == _captureRendererIndex ||
                _normalRendererIndex == _pixelRendererIndex ||
                _captureRendererIndex == _pixelRendererIndex)
            {
                Debug.LogError("URP Renderer Indexが不正です。", this);
                return false;
            }

            if (_nearResolution.x <= 0 || _nearResolution.y <= 0 ||
                _middleResolution.x <= 0 || _middleResolution.y <= 0 ||
                _farResolution.x <= 0 || _farResolution.y <= 0)
            {
                Debug.LogError("Capture解像度は1以上にしてください。", this);
                return false;
            }

            return true;
        }

        private void CreateRenderTextures()
        {
            _nearTexture = CreateRenderTexture("RT_PixelNear", _nearResolution);
            _middleTexture = CreateRenderTexture("RT_PixelMiddle", _middleResolution);
            _farTexture = CreateRenderTexture("RT_PixelFar", _farResolution);
        }

        private static RenderTexture CreateRenderTexture(string textureName, Vector2Int resolution)
        {
            // HDRを保持し、Pointフィルタリングでドットの輪郭を維持する。
            RenderTexture texture = new(resolution.x, resolution.y, 24, RenderTextureFormat.ARGBHalf)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

            texture.Create();
            return texture;
        }

        /// <summary>
        /// Capture Cameraへ描画先のRendering Layerを設定する。
        /// </summary>
        private void ConfigureCaptureCamera(Camera captureCamera, RenderTexture targetTexture, int index, float depthOffset)
        {
            if (!captureCamera.TryGetComponent(out PixelCaptureViewMarker marker))
            {
                marker = captureCamera.gameObject.AddComponent<PixelCaptureViewMarker>();
            }

            marker.SetCapture(PixelCaptureView.Game, index);

            if (!captureCamera.TryGetComponent(out PixelCaptureRenderingLayerFilter filter))
            {
                filter = captureCamera.gameObject.AddComponent<PixelCaptureRenderingLayerFilter>();
            }

            filter.RenderingLayerMask = GetRenderingLayerMask(index);

            UniversalAdditionalCameraData cameraData = captureCamera.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(_captureRendererIndex);
            cameraData.renderPostProcessing = false;

            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;

            // GameObject LayerはMain Cameraから引き継ぐ。
            // Near / Middle / Farの振り分けには使用しない。
            captureCamera.cullingMask = _mainCamera.cullingMask;
            captureCamera.targetTexture = targetTexture;
            captureCamera.allowHDR = true;
            captureCamera.depth = _mainCamera.depth + depthOffset;
        }

        private void SyncAllCaptureCameras()
        {
            SyncCamera(_nearCamera, _nearTexture, 0, -3f);
            SyncCamera(_middleCamera, _middleTexture, 1, -2f);
            SyncCamera(_farCamera, _farTexture, 2, -1f);
        }

        private void SyncCamera(Camera captureCamera, RenderTexture targetTexture, int index, float depthOffset)
        {
            // 投影・描画距離・カメラ位置をMain Cameraと一致させる。
            captureCamera.CopyFrom(_mainCamera);
            captureCamera.transform.SetPositionAndRotation(
                _mainCamera.transform.position,
                _mainCamera.transform.rotation);

            // CopyFrom後にCapture固有の設定を復元する。
            ConfigureCaptureCamera(captureCamera, targetTexture, index, depthOffset);
        }

        private static uint GetRenderingLayerMask(int index)
        {
            return index switch
            {
                0 => PixelRenderingLayerMask.NEAR,
                1 => PixelRenderingLayerMask.MIDDLE,
                2 => PixelRenderingLayerMask.FAR,
                _ => 0u
            };
        }

        private void SetCaptureCamerasEnabled(bool isEnabled)
        {
            _nearCamera.enabled = isEnabled;
            _middleCamera.enabled = isEnabled;
            _farCamera.enabled = isEnabled;
        }

        private void SetMainCameraRenderer(bool isEnabled)
        {
            UniversalAdditionalCameraData cameraData = _mainCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(isEnabled ? _pixelRendererIndex : _normalRendererIndex);
        }

        private void SetMaterialState(bool isEnabled)
        {
            float value = isEnabled ? 1f : 0f;

            if (_compositeMaterial != null) _compositeMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
            if (_retroPixelMaterial != null) _retroPixelMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
            if (_volumetricFogMaterial != null) _volumetricFogMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
        }

        private void ApplyTexturesToMaterial()
        {
            SetColorTextures(_compositeMaterial);
            SetColorTextures(_volumetricFogMaterial);
        }

        private void SetColorTextures(Material material)
        {
            if (material == null) return;

            material.SetTexture("_PixelNearTex", _nearTexture);
            material.SetTexture("_PixelMiddleTex", _middleTexture);
            material.SetTexture("_PixelFarTex", _farTexture);
        }

        private void OnDestroy()
        {
            if (_initialized && _mainCamera != null)
            {
                _mainCamera.GetUniversalAdditionalCameraData().SetRenderer(_normalRendererIndex);
            }

            ReleaseRenderTexture(_nearTexture);
            ReleaseRenderTexture(_middleTexture);
            ReleaseRenderTexture(_farTexture);
        }

        private static void ReleaseRenderTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
        }
    }
}
