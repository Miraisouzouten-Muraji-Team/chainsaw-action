using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    public sealed class PixelLayerRenderController : MonoBehaviour
    {
        [Header("Cameras")]
        [SerializeField]
        private Camera _mainCamera;

        [SerializeField]
        private Camera _nearCamera;

        [SerializeField]
        private Camera _middleCamera;

        [SerializeField]
        private Camera _farCamera;

        [Header("Composite")]
        [SerializeField]
        private Material _compositeMaterial;

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
        [Tooltip("URP Asset の Renderer List における PixelCaptureRenderer の番号")]
        [SerializeField]
        private int _captureRendererIndex = 1;

        private RenderTexture _nearTexture;
        private RenderTexture _middleTexture;
        private RenderTexture _farTexture;

        private int _nearLayer;
        private int _middleLayer;
        private int _farLayer;

        private void Awake()
        {
            _nearLayer =
                LayerMask.NameToLayer(_nearLayerName);

            _middleLayer =
                LayerMask.NameToLayer(_middleLayerName);

            _farLayer =
                LayerMask.NameToLayer(_farLayerName);

            if (_nearLayer < 0 ||
                _middleLayer < 0 ||
                _farLayer < 0)
            {
                Debug.LogError(
                    "PixelNear / PixelMiddle / PixelFar Layerを確認してください。",
                    this);

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

            ConfigureMainCamera();

            ApplyTexturesToMaterial();
        }

        private void LateUpdate()
        {
            // Main Cameraが動くので、
            // 描画直前に3台を同期する。
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
            // HDR値を保持したいのでARGBHalf。
            // Bloomなどで1.0を超える明るさを残せる。
            RenderTexture texture =
                new(
                    resolution.x,
                    resolution.y,
                    24,
                    RenderTextureFormat.ARGBHalf)
                {
                    name = textureName,

                    // ピクセル補間しない。
                    filterMode = FilterMode.Point,

                    wrapMode = TextureWrapMode.Clamp,

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
                captureCamera.GetUniversalAdditionalCameraData();

            // Full Screen Passを持っていない
            // PixelCaptureRendererを使用する。
            cameraData.SetRenderer(_captureRendererIndex);

            // Bloomなどは合成後にMain Cameraで行う。
            cameraData.renderPostProcessing = false;

            captureCamera.clearFlags =
                CameraClearFlags.SolidColor;

            // オブジェクトのない部分を透明にする。
            captureCamera.backgroundColor =
                new Color(0, 0, 0, 0);

            captureCamera.cullingMask =
                1 << layer;

            captureCamera.targetTexture =
                targetTexture;

            captureCamera.allowHDR = true;

            captureCamera.depth =
                _mainCamera.depth + depthOffset;
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

            // Main Cameraと画角などを合わせる。
            captureCamera.CopyFrom(_mainCamera);

            captureCamera.transform.SetPositionAndRotation(
                _mainCamera.transform.position,
                _mainCamera.transform.rotation);

            // CopyFromで上書きされた
            // Capture用設定を戻す。
            captureCamera.clearFlags =
                CameraClearFlags.SolidColor;

            captureCamera.backgroundColor =
                new Color(0, 0, 0, 0);

            captureCamera.cullingMask =
                1 << layer;

            captureCamera.targetTexture =
                targetTexture;

            captureCamera.allowHDR = true;

            captureCamera.depth =
                _mainCamera.depth + depthOffset;

            UniversalAdditionalCameraData cameraData =
                captureCamera.GetUniversalAdditionalCameraData();

            cameraData.SetRenderer(_captureRendererIndex);
            cameraData.renderPostProcessing = false;
        }

        private void ConfigureMainCamera()
        {
            if (_mainCamera == null)
            {
                return;
            }

            int pixelLayers =
                (1 << _nearLayer) |
                (1 << _middleLayer) |
                (1 << _farLayer);

            // Main Cameraでは3グループを直接描かない。
            // RenderTextureから後で合成する。
            _mainCamera.cullingMask &= ~pixelLayers;
        }

        private void ApplyTexturesToMaterial()
        {
            if (_compositeMaterial == null)
            {
                return;
            }

            _compositeMaterial.SetTexture(
                "_PixelNearTex",
                _nearTexture);

            _compositeMaterial.SetTexture(
                "_PixelMiddleTex",
                _middleTexture);

            _compositeMaterial.SetTexture(
                "_PixelFarTex",
                _farTexture);
        }

        private void OnDestroy()
        {
            ReleaseRenderTexture(_nearTexture);
            ReleaseRenderTexture(_middleTexture);
            ReleaseRenderTexture(_farTexture);
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