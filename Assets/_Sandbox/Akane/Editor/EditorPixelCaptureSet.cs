
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        private readonly RenderTexture[] _textures = new RenderTexture[CAPTURE_COUNT];

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
            if (sourceCamera == null)
            {
                throw new ArgumentNullException(nameof(sourceCamera));
            }

            if (getLayerName == null)
            {
                throw new ArgumentNullException(nameof(getLayerName));
            }

            if (getResolution == null)
            {
                throw new ArgumentNullException(nameof(getResolution));
            }

            PixelRenderLayerManager manager =
                UnityEngine.Object.FindFirstObjectByType<PixelRenderLayerManager>();

            // Editor Capture前のRendering Layerを保存する。
            // Capture時にRendererの値が書き換わるため、終了後に復元する。
            Dictionary<Renderer, uint> originalMasks =
                manager != null ? CaptureRenderingLayerState() : null;

            try
            {
                // GameまたはSceneの視点を基準にAutoを振り分ける。
                if (manager != null)
                {
                    manager.DiscoverAndRefresh(sourceCamera);
                }

                for (int i = 0; i < CAPTURE_COUNT; i++)
                {
                    string layerName = getLayerName(i);
                    Vector2Int resolution = getResolution(i);

                    if (resolution.x <= 0 || resolution.y <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Capture解像度が不正です: {layerName}");
                    }

                    EnsureRenderTexture(i, resolution);

                    if (_cameras[i] == null)
                    {
                        GameObject cameraObject = new($"{_namePrefix}_{layerName}");
                        cameraObject.hideFlags = HideFlags.HideAndDontSave;

                        _cameras[i] = cameraObject.AddComponent<Camera>();
                        _cameras[i].enabled = false;
                    }

                    Camera captureCamera = _cameras[i];

                    // Cameraの基本設定を元Cameraからコピーする。
                    captureCamera.CopyFrom(sourceCamera);
                    captureCamera.transform.SetPositionAndRotation(
                        sourceCamera.transform.position,
                        sourceCamera.transform.rotation);

                    // Capture専用のCamera設定はCopyFromの後に適用する。
                    captureCamera.enabled = false;
                    captureCamera.cameraType = CameraType.Game;
                    captureCamera.clearFlags = CameraClearFlags.SolidColor;
                    captureCamera.backgroundColor = Color.clear;
                    captureCamera.cullingMask = sourceCamera.cullingMask;
                    captureCamera.targetTexture = _textures[i];
                    captureCamera.rect = new Rect(0f, 0f, 1f, 1f);
                    captureCamera.allowHDR = true;

                    // Capture CameraがGame用かScene用か、
                    // およびNear / Middle / Farのどれかを記録する。
                    if (!captureCamera.TryGetComponent(out PixelCaptureViewMarker marker))
                    {
                        marker = captureCamera.gameObject.AddComponent<PixelCaptureViewMarker>();
                    }

                    marker.SetCapture(view, i);

                    // URP側の描画フィルターに対象Rendering Layerを指定する。
                    if (!captureCamera.TryGetComponent(out PixelCaptureRenderingLayerFilter filter))
                    {
                        filter = captureCamera.gameObject.AddComponent<PixelCaptureRenderingLayerFilter>();
                    }

                    filter.RenderingLayerMask = i switch
                    {
                        0 => PixelRenderingLayerMask.NEAR,
                        1 => PixelRenderingLayerMask.MIDDLE,
                        2 => PixelRenderingLayerMask.FAR,
                        _ => 0u
                    };

                    UniversalAdditionalCameraData cameraData =
                        captureCamera.GetUniversalAdditionalCameraData();

                    cameraData.renderType = CameraRenderType.Base;
                    cameraData.SetRenderer(rendererIndex);
                    cameraData.renderPostProcessing = false;

                    // 指定グループを対応するRTへ描画する。
                    UniversalRenderPipeline.SingleCameraRequest request = new()
                    {
                        destination = _textures[i]
                    };

                    if (!RenderPipeline.SupportsRenderRequest(captureCamera, request))
                    {
                        throw new NotSupportedException(
                            $"RenderRequest非対応: {captureCamera.name}");
                    }

                    RenderPipeline.SubmitRenderRequest(captureCamera, request);
                }
            }
            finally
            {
                // Captureが成功・失敗した場合のどちらでも
                // 元のRendering Layerを復元する。
                RestoreRenderingLayerState(originalMasks);
            }
        }

        /// <summary>
        /// Capture前のRendererのRendering Layerを保存する。
        /// </summary>
        private static Dictionary<Renderer, uint> CaptureRenderingLayerState()
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            Dictionary<Renderer, uint> originalMasks = new(renderers.Length);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                originalMasks.Add(renderer, renderer.renderingLayerMask);
            }

            return originalMasks;
        }

        /// <summary>
        /// Capture後のRendererを元のRendering Layerへ戻す。
        /// </summary>
        private static void RestoreRenderingLayerState(
            Dictionary<Renderer, uint> originalMasks)
        {
            if (originalMasks == null) return;

            foreach (KeyValuePair<Renderer, uint> entry in originalMasks)
            {
                if (entry.Key == null) continue;

                if (entry.Key.renderingLayerMask != entry.Value)
                {
                    entry.Key.renderingLayerMask = entry.Value;
                }
            }
        }

        /// <summary>
        /// 指定した解像度のRTを確保する。
        /// </summary>
        private void EnsureRenderTexture(int index, Vector2Int resolution)
        {
            RenderTexture current = _textures[index];

            if (current != null &&
                current.width == resolution.x &&
                current.height == resolution.y)
            {
                return;
            }

            ReleaseTexture(index);

            RenderTexture texture = new(resolution.x, resolution.y, 24, RenderTextureFormat.ARGBHalf)
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

        /// <summary>
        /// 指定したRTを解放する。
        /// </summary>
        private void ReleaseTexture(int index)
        {
            if (_textures[index] == null) return;

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
                ReleaseTexture(i);

                if (_cameras[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_cameras[i].gameObject);
                    _cameras[i] = null;
                }
            }
        }
    }
}
#endif
