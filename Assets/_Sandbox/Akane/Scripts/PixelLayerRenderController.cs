using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace Project.Rendering.Pixel
{
    public sealed class PixelLayerRenderController : MonoBehaviour
    {
        private const string PIXEL_EFFECT_ENABLED =
            "_PixelEffectEnabled";

        [Header("Cameras")]
        [SerializeField]
        private Camera _mainCamera;

        [SerializeField]
        private Camera _nearCamera;

        [SerializeField]
        private Camera _middleCamera;

        [SerializeField]
        private Camera _farCamera;

        [Header("Materials")]
        [SerializeField]
        private Material _compositeMaterial;

        [SerializeField]
        private Material _retroPixelMaterial;

        [SerializeField]
        private Material _volumetricFogMaterial;

        [Header("Resolution")]
        [SerializeField]
        private Vector2Int _nearResolution =
            new(640, 360);

        [SerializeField]
        private Vector2Int _middleResolution =
            new(320, 180);

        [SerializeField]
        private Vector2Int _farResolution =
            new(160, 90);

        [Header("Layers")]
        [SerializeField]
        private string _nearLayerName = "PixelNear";

        [SerializeField]
        private string _middleLayerName = "PixelMiddle";

        [SerializeField]
        private string _farLayerName = "PixelFar";

        [Header("URP")]
        [Tooltip(
            "URP Asset の Renderer List における " +
            "PixelCaptureRenderer の番号")]
        [SerializeField]
        private int _captureRendererIndex = 1;

        [Header("Pixel Rendering")]
        [SerializeField]
        private bool _pixelRenderingEnabled = true;

        private RenderTexture _nearTexture;
        private RenderTexture _middleTexture;
        private RenderTexture _farTexture;

        private int _nearLayer;
        private int _middleLayer;
        private int _farLayer;

        private int _pixelLayerMask;
        private int _originalMainCameraMask;

        // 実際に現在適用されている状態。
        // Inspectorから値を直接変えたか判定するために使用する。
        private bool _appliedPixelRenderingEnabled;

        public bool PixelRenderingEnabled =>
            _pixelRenderingEnabled;

        private void Awake()
        {
            if (!Initialize())
            {
                enabled = false;
                return;
            }

            CreateRenderTextures();

            ConfigureCaptureCamera(
                _nearCamera,
                _nearTexture,
                _nearLayer,
                -3.0f);

            ConfigureCaptureCamera(
                _middleCamera,
                _middleTexture,
                _middleLayer,
                -2.0f);

            ConfigureCaptureCamera(
                _farCamera,
                _farTexture,
                _farLayer,
                -1.0f);

            ApplyTexturesToMaterial();

            SetPixelRenderingEnabled(
                _pixelRenderingEnabled);
        }

        private void LateUpdate()
        {
            // ---------------------------------------------------------
            // Play中にInspectorからチェックボックスを変更した場合、
            // SetPixelRenderingEnabledを通して実際の描画状態にも反映する。
            // ---------------------------------------------------------
            if (_pixelRenderingEnabled !=
                _appliedPixelRenderingEnabled)
            {
                SetPixelRenderingEnabled(
                    _pixelRenderingEnabled);
            }

            // Pixel RenderingがOFFなら、
            // Capture Cameraを同期する必要はない。
            if (!_pixelRenderingEnabled)
            {
                return;
            }

            SyncAllCaptureCameras();
        }

        /// <summary>
        /// Pixel Rendering全体のON/OFFを設定する。
        /// </summary>
        public void SetPixelRenderingEnabled(
            bool isEnabled)
        {
            _pixelRenderingEnabled =
                isEnabled;

            SetCaptureCamerasEnabled(
                isEnabled);

            SetMainCameraMask(
                isEnabled);

            SetMaterialState(
                isEnabled);

            // ONへ切り替えた瞬間から
            // Capture CameraをMain Cameraへ合わせる。
            if (isEnabled)
            {
                SyncAllCaptureCameras();
            }

            // 現在実際に適用されている値を保存する。
            _appliedPixelRenderingEnabled =
                isEnabled;
        }

        /// <summary>
        /// 現在のPixel Rendering状態を反転する。
        /// </summary>
        public void TogglePixelRendering()
        {
            SetPixelRenderingEnabled(
                !_pixelRenderingEnabled);
        }

        private bool Initialize()
        {
            if (_mainCamera == null)
            {
                Debug.LogError(
                    "Main Cameraが設定されていません。",
                    this);

                return false;
            }

            _nearLayer =
                LayerMask.NameToLayer(
                    _nearLayerName);

            _middleLayer =
                LayerMask.NameToLayer(
                    _middleLayerName);

            _farLayer =
                LayerMask.NameToLayer(
                    _farLayerName);

            if (_nearLayer < 0 ||
                _middleLayer < 0 ||
                _farLayer < 0)
            {
                Debug.LogError(
                    "PixelNear / PixelMiddle / PixelFar " +
                    "Layerを確認してください。",
                    this);

                return false;
            }

            _pixelLayerMask =
                (1 << _nearLayer) |
                (1 << _middleLayer) |
                (1 << _farLayer);

            // Pixel Rendering OFF時に
            // 元のCulling Maskへ戻すため保存する。
            _originalMainCameraMask =
                _mainCamera.cullingMask;

            return true;
        }

        private void CreateRenderTextures()
        {
            _nearTexture =
                CreateRenderTexture(
                    "RT_PixelNear",
                    _nearResolution);

            _middleTexture =
                CreateRenderTexture(
                    "RT_PixelMiddle",
                    _middleResolution);

            _farTexture =
                CreateRenderTexture(
                    "RT_PixelFar",
                    _farResolution);
        }

        private static RenderTexture CreateRenderTexture(
            string textureName,
            Vector2Int resolution)
        {
            // HDR値を保持するためARGBHalfを使用する。
            RenderTexture texture =
                new(
                    resolution.x,
                    resolution.y,
                    24,
                    RenderTextureFormat.ARGBHalf)
                {
                    name = textureName,

                    // 補間せずピクセルの形を維持する。
                    filterMode =
                        FilterMode.Point,

                    wrapMode =
                        TextureWrapMode.Clamp,

                    useMipMap = false,
                    autoGenerateMips = false
                };

            texture.Create();

            return texture;
        }

        private void ConfigureCaptureCamera(
            Camera captureCamera,
            RenderTexture targetTexture,
            int layer,
            float depthOffset)
        {
            if (captureCamera == null)
            {
                return;
            }

            UniversalAdditionalCameraData cameraData =
                captureCamera
                    .GetUniversalAdditionalCameraData();

            // Full Screen Passを持たない
            // PixelCaptureRendererを使用する。
            cameraData.SetRenderer(
                _captureRendererIndex);

            // BloomやFogなどは
            // 合成後にMain Camera側で処理する。
            cameraData.renderPostProcessing =
                false;

            captureCamera.clearFlags =
                CameraClearFlags.SolidColor;

            // 合成時に背景部分を透過させる。
            captureCamera.backgroundColor =
                new Color(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f);

            captureCamera.cullingMask =
                1 << layer;

            captureCamera.targetTexture =
                targetTexture;

            captureCamera.allowHDR =
                true;

            captureCamera.depth =
                _mainCamera.depth +
                depthOffset;
        }

        private void SyncAllCaptureCameras()
        {
            SyncCamera(
                _nearCamera,
                _nearTexture,
                _nearLayer,
                -3.0f);

            SyncCamera(
                _middleCamera,
                _middleTexture,
                _middleLayer,
                -2.0f);

            SyncCamera(
                _farCamera,
                _farTexture,
                _farLayer,
                -1.0f);
        }

        private void SyncCamera(
            Camera captureCamera,
            RenderTexture targetTexture,
            int layer,
            float depthOffset)
        {
            if (captureCamera == null ||
                _mainCamera == null)
            {
                return;
            }

            // FOV、Near/Far Clipなどを
            // Main Cameraからコピーする。
            captureCamera.CopyFrom(
                _mainCamera);

            captureCamera.transform
                .SetPositionAndRotation(
                    _mainCamera.transform.position,
                    _mainCamera.transform.rotation);

            // CopyFromで上書きされた
            // Capture Camera固有設定を戻す。
            captureCamera.clearFlags =
                CameraClearFlags.SolidColor;

            captureCamera.backgroundColor =
                new Color(
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f);

            captureCamera.cullingMask =
                1 << layer;

            captureCamera.targetTexture =
                targetTexture;

            captureCamera.allowHDR =
                true;

            captureCamera.depth =
                _mainCamera.depth +
                depthOffset;

            UniversalAdditionalCameraData cameraData =
                captureCamera
                    .GetUniversalAdditionalCameraData();

            cameraData.SetRenderer(
                _captureRendererIndex);

            cameraData.renderPostProcessing =
                false;
        }

        private void SetCaptureCamerasEnabled(
            bool isEnabled)
        {
            if (_nearCamera != null)
            {
                _nearCamera.enabled =
                    isEnabled;
            }

            if (_middleCamera != null)
            {
                _middleCamera.enabled =
                    isEnabled;
            }

            if (_farCamera != null)
            {
                _farCamera.enabled =
                    isEnabled;
            }
        }

        private void SetMainCameraMask(
            bool isEnabled)
        {
            if (_mainCamera == null)
            {
                return;
            }

            if (isEnabled)
            {
                // Pixel ON
                //
                // Near / Middle / Farは
                // Main Cameraでは直接描画せず、
                // 各Capture CameraからRTへ描画する。
                //_mainCamera.cullingMask =
                //    _originalMainCameraMask &
                //    ~_pixelLayerMask;
                _mainCamera.cullingMask =
    _originalMainCameraMask;

                return;
            }

            // Pixel OFF
            //
            // Pixel用LayerもMain Cameraから
            // 通常解像度で直接描画する。
            _mainCamera.cullingMask =
                _originalMainCameraMask;
        }

        private void SetMaterialState(
            bool isEnabled)
        {
            float value =
                isEnabled
                    ? 1.0f
                    : 0.0f;

            // Near / Middle / Farの合成。
            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED,
                    value);
            }

            // 色数削減・ディザリング。
            if (_retroPixelMaterial != null)
            {
                _retroPixelMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED,
                    value);
            }

            if (_volumetricFogMaterial != null)
            {
                _volumetricFogMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED,
                    value);
            }
        }

        private void ApplyTexturesToMaterial()
        {
            // 従来の色合成
            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetTexture(
                    "_PixelNearTex", _nearTexture);

                _compositeMaterial.SetTexture(
                    "_PixelMiddleTex", _middleTexture);

                _compositeMaterial.SetTexture(
                    "_PixelFarTex", _farTexture);
            }

            if (_volumetricFogMaterial == null)
            {
                return;
            }

            // Fog側でも各レイヤーの色を参照する
            // 主に描画されている画素の判定に使用
            _volumetricFogMaterial.SetTexture(
                "_PixelNearTex", _nearTexture);

            _volumetricFogMaterial.SetTexture(
                "_PixelMiddleTex", _middleTexture);

            _volumetricFogMaterial.SetTexture(
                "_PixelFarTex", _farTexture);
        }

        private void OnDestroy()
        {
            // Main Cameraを元の状態へ戻す。
            if (_mainCamera != null)
            {
                _mainCamera.cullingMask =
                    _originalMainCameraMask;
            }

            ReleaseRenderTexture(
                _nearTexture);

            ReleaseRenderTexture(
                _middleTexture);

            ReleaseRenderTexture(
                _farTexture);
        }

        private static void ReleaseRenderTexture(
            RenderTexture texture)
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
