using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
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

        [Header("Layers")]
        [SerializeField] private string _nearLayerName = "PixelNear";
        [SerializeField] private string _middleLayerName = "PixelMiddle";
        [SerializeField] private string _farLayerName = "PixelFar";

        [Header("URP Renderer Indices")]
        [Tooltip("通常描画用GameRendererのRenderer List番号")]
        [SerializeField] private int _normalRendererIndex = 0;

        [Tooltip("Near / Middle / Farの描画に使用するPixelCaptureRendererのRenderer List番号")]
        [SerializeField] private int _captureRendererIndex = 1;

        [Tooltip("Pixel Rendering ON時にMain Cameraが使用するPixelGameRendererのRenderer List番号")]
        [SerializeField] private int _pixelRendererIndex = 2;

        [Header("Pixel Rendering")]
        [SerializeField] private bool _pixelRenderingEnabled = true;

        private RenderTexture _nearTexture;
        private RenderTexture _middleTexture;
        private RenderTexture _farTexture;

        private int _nearLayer;
        private int _middleLayer;
        private int _farLayer;
        private bool _appliedPixelRenderingEnabled;
        private bool _initialized;

        public bool PixelRenderingEnabled => _pixelRenderingEnabled;

        private void Awake()
        {
            if (!Initialize())
            {
                enabled = false;
                return;
            }

            _initialized = true;
            CreateRenderTextures();

            ConfigureCaptureCamera(_nearCamera, _nearTexture, _nearLayer, -3.0f);
            ConfigureCaptureCamera(_middleCamera, _middleTexture, _middleLayer, -2.0f);
            ConfigureCaptureCamera(_farCamera, _farTexture, _farLayer, -1.0f);

            ApplyTexturesToMaterial();
            SetPixelRenderingEnabled(_pixelRenderingEnabled);
        }

        private void LateUpdate()
        {
            // Play中にInspectorから切り替えた場合も描画状態へ反映する。
            if (_pixelRenderingEnabled != _appliedPixelRenderingEnabled)
            {
                SetPixelRenderingEnabled(_pixelRenderingEnabled);
            }

            if (_pixelRenderingEnabled)
            {
                SyncAllCaptureCameras();
            }
        }

        /// <summary>
        /// Pixel Rendering全体のON/OFFを設定する。
        /// </summary>
        public void SetPixelRenderingEnabled(bool isEnabled)
        {
            if (!_initialized)
            {
                _pixelRenderingEnabled = isEnabled;
                return;
            }

            _pixelRenderingEnabled = isEnabled;
            SetCaptureCamerasEnabled(isEnabled);
            SetMainCameraRenderer(isEnabled);
            SetMaterialState(isEnabled);

            if (isEnabled)
            {
                SyncAllCaptureCameras();
            }

            _appliedPixelRenderingEnabled = isEnabled;
        }

        /// <summary>
        /// 現在のPixel Rendering状態を反転する。
        /// </summary>
        public void TogglePixelRendering()
        {
            SetPixelRenderingEnabled(!_pixelRenderingEnabled);
        }

        private bool Initialize()
        {
            if (_mainCamera == null || _nearCamera == null || _middleCamera == null || _farCamera == null)
            {
                Debug.LogError("Main / Near / Middle / Far Cameraの参照を確認してください。", this);
                return false;
            }

            if (_normalRendererIndex < 0 || _captureRendererIndex < 0 || _pixelRendererIndex < 0 ||
                _normalRendererIndex == _captureRendererIndex ||
                _normalRendererIndex == _pixelRendererIndex ||
                _captureRendererIndex == _pixelRendererIndex)
            {
                Debug.LogError("URP Renderer Indexが不正です。Renderer Listの番号を確認してください。", this);
                return false;
            }

            if (_nearResolution.x <= 0 || _nearResolution.y <= 0 ||
                _middleResolution.x <= 0 || _middleResolution.y <= 0 ||
                _farResolution.x <= 0 || _farResolution.y <= 0)
            {
                Debug.LogError("Pixel Renderingの解像度には1以上の値を指定してください。", this);
                return false;
            }

            _nearLayer = LayerMask.NameToLayer(_nearLayerName);
            _middleLayer = LayerMask.NameToLayer(_middleLayerName);
            _farLayer = LayerMask.NameToLayer(_farLayerName);

            if (_nearLayer < 0 || _middleLayer < 0 || _farLayer < 0)
            {
                Debug.LogError("PixelNear / PixelMiddle / PixelFar Layerを確認してください。", this);
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
            // HDRを保持し、ポイントサンプリングでドットの形を維持する。
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

        private void ConfigureCaptureCamera(Camera captureCamera, RenderTexture targetTexture, int layer, float depthOffset)
        {
            if (!captureCamera.TryGetComponent(out PixelCaptureViewMarker marker))
            {
                marker = captureCamera.gameObject
                    .AddComponent<PixelCaptureViewMarker>();
            }

            marker.SetView(PixelCaptureView.Game);
            UniversalAdditionalCameraData cameraData = captureCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(_captureRendererIndex);
            cameraData.renderPostProcessing = false;

            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            captureCamera.cullingMask = 1 << layer;
            captureCamera.targetTexture = targetTexture;
            captureCamera.allowHDR = true;
            captureCamera.depth = _mainCamera.depth + depthOffset;
        }

        private void SyncAllCaptureCameras()
        {
            SyncCamera(_nearCamera, _nearTexture, _nearLayer, -3.0f);
            SyncCamera(_middleCamera, _middleTexture, _middleLayer, -2.0f);
            SyncCamera(_farCamera, _farTexture, _farLayer, -1.0f);
        }

        private void SyncCamera(Camera captureCamera, RenderTexture targetTexture, int layer, float depthOffset)
        {
            // FOV・クリップ距離・描画領域などをMain Cameraへ揃える。
            captureCamera.CopyFrom(_mainCamera);
            captureCamera.transform.SetPositionAndRotation(_mainCamera.transform.position, _mainCamera.transform.rotation);

            // CopyFromで上書きされたCapture Camera固有の設定を復元する。
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            captureCamera.cullingMask = 1 << layer;
            captureCamera.targetTexture = targetTexture;
            captureCamera.allowHDR = true;
            captureCamera.depth = _mainCamera.depth + depthOffset;

            UniversalAdditionalCameraData cameraData = captureCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(_captureRendererIndex);
            cameraData.renderPostProcessing = false;
        }

        private void SetCaptureCamerasEnabled(bool isEnabled)
        {
            _nearCamera.enabled = isEnabled;
            _middleCamera.enabled = isEnabled;
            _farCamera.enabled = isEnabled;
        }

        private void SetMainCameraRenderer(bool isEnabled)
        {
            // Culling Maskは変更せず、通常カラー描画の対象をRenderer Data側で切り替える。
            // Main CameraのCulling MaskにPixelLayerを残し、God Ray用の影情報を維持する。
            UniversalAdditionalCameraData cameraData = _mainCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(isEnabled ? _pixelRendererIndex : _normalRendererIndex);
        }

        private void SetMaterialState(bool isEnabled)
        {
            float value = isEnabled ? 1.0f : 0.0f;

            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
            }

            if (_retroPixelMaterial != null)
            {
                _retroPixelMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
            }

            if (_volumetricFogMaterial != null)
            {
                _volumetricFogMaterial.SetFloat(PIXEL_EFFECT_ENABLED, value);
            }
        }

        private void ApplyTexturesToMaterial()
        {
            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetTexture("_PixelNearTex", _nearTexture);
                _compositeMaterial.SetTexture("_PixelMiddleTex", _middleTexture);
                _compositeMaterial.SetTexture("_PixelFarTex", _farTexture);
            }

            // Fogは各RTのAlphaを確認して、対応するDepthTextureを参照する。
            if (_volumetricFogMaterial != null)
            {
                _volumetricFogMaterial.SetTexture("_PixelNearTex", _nearTexture);
                _volumetricFogMaterial.SetTexture("_PixelMiddleTex", _middleTexture);
                _volumetricFogMaterial.SetTexture("_PixelFarTex", _farTexture);
            }
        }

        private void OnDestroy()
        {
            if (_initialized && _mainCamera != null)
            {
                // Pixel専用Rendererを残さないよう、通常描画用に戻す。
                _mainCamera.GetUniversalAdditionalCameraData().SetRenderer(_normalRendererIndex);
            }

            ReleaseRenderTexture(_nearTexture);
            ReleaseRenderTexture(_middleTexture);
            ReleaseRenderTexture(_farTexture);
        }

        private static void ReleaseRenderTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            Destroy(texture);
        }
    }
}
