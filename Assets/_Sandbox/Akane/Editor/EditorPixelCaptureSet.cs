
#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel.Editor
{
    /// <summary>
    /// Editorプレビュー用の3層Pixel Captureを管理する。
    /// GameとSceneで別インスタンスを使用する。
    /// </summary>
    internal sealed class EditorPixelCaptureSet
    {
        private const int CAPTURE_COUNT = 3;

        private readonly string _namePrefix;
        private readonly Camera[] _cameras = new Camera[CAPTURE_COUNT];
        private readonly RenderTexture[] _textures =
            new RenderTexture[CAPTURE_COUNT];

        public EditorPixelCaptureSet(string namePrefix)
        {
            _namePrefix = namePrefix;
        }

        public RenderTexture GetTexture(int index)
        {
            return _textures[index];
        }

        /// <summary>
        /// 指定したCamera視点でNear / Middle / Farを描画する。
        /// </summary>
        public void Capture(
            Camera sourceCamera,
            PixelCaptureView view,
            int rendererIndex,
            Func<int, string> getLayerName,
            Func<int, Vector2Int> getResolution)
        {
            for (int i = 0; i < CAPTURE_COUNT; i++)
            {
                string layerName = getLayerName(i);
                int layer = LayerMask.NameToLayer(layerName);
                Vector2Int resolution = getResolution(i);

                if (layer < 0)
                {
                    throw new InvalidOperationException(
                        $"Capture Layerがありません: {layerName}");
                }

                if (resolution.x <= 0 || resolution.y <= 0)
                {
                    throw new InvalidOperationException(
                        $"Capture解像度が不正です: {layerName}");
                }

                EnsureRenderTexture(i, resolution);

                if (_cameras[i] == null)
                {
                    GameObject cameraObject =
                        new($"{_namePrefix}_{layerName}");

                    cameraObject.hideFlags = HideFlags.HideAndDontSave;

                    _cameras[i] = cameraObject.AddComponent<Camera>();
                    _cameras[i].enabled = false;
                }

                Camera captureCamera = _cameras[i];

                if (!captureCamera.TryGetComponent(
                        out PixelCaptureViewMarker marker))
                {
                    marker = captureCamera.gameObject
                        .AddComponent<PixelCaptureViewMarker>();
                }

                marker.SetView(view);

                // 視点・FOV・投影行列などをコピーする。
                captureCamera.CopyFrom(sourceCamera);

                captureCamera.transform.SetPositionAndRotation(
                    sourceCamera.transform.position,
                    sourceCamera.transform.rotation);

                // CopyFromで上書きされたCapture専用設定を復元する。
                captureCamera.enabled = false;
                captureCamera.cameraType = CameraType.Game;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Color.clear;
                captureCamera.cullingMask = 1 << layer;
                captureCamera.targetTexture = _textures[i];
                captureCamera.rect = new Rect(0, 0, 1, 1);
                captureCamera.allowHDR = true;

                UniversalAdditionalCameraData cameraData =
                    captureCamera.GetUniversalAdditionalCameraData();

                cameraData.renderType = CameraRenderType.Base;
                cameraData.SetRenderer(rendererIndex);
                cameraData.renderPostProcessing = false;

                UniversalRenderPipeline.SingleCameraRequest request = new()
                {
                    destination = _textures[i]
                };

                if (!RenderPipeline.SupportsRenderRequest(
                        captureCamera, request))
                {
                    throw new NotSupportedException(
                        $"RenderRequest非対応: {captureCamera.name}");
                }

                RenderPipeline.SubmitRenderRequest(
                    captureCamera, request);
            }
        }

        private void EnsureRenderTexture(
            int index,
            Vector2Int resolution)
        {
            RenderTexture current = _textures[index];

            if (current != null &&
                current.width == resolution.x &&
                current.height == resolution.y)
            {
                return;
            }

            ReleaseTexture(index);

            RenderTexture texture = new(
                resolution.x,
                resolution.y,
                24,
                RenderTextureFormat.ARGBHalf)
            {
                name = $"RT_{_namePrefix}_{index}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };

            texture.Create();
            _textures[index] = texture;
        }

        private void ReleaseTexture(int index)
        {
            if (_textures[index] == null)
            {
                return;
            }

            if (_cameras[index] != null)
            {
                _cameras[index].targetTexture = null;
            }

            _textures[index].Release();
            UnityEngine.Object.DestroyImmediate(_textures[index]);
            _textures[index] = null;
        }

        /// <summary>
        /// CameraとRTをすべて解放する。
        /// </summary>
        public void Release()
        {
            for (int i = 0; i < CAPTURE_COUNT; i++)
            {
                if (_cameras[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        _cameras[i].gameObject);

                    _cameras[i] = null;
                }

                ReleaseTexture(i);
            }
        }
    }
}
#endif
