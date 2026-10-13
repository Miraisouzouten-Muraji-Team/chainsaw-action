
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Project.Rendering.Pixel.Editor
{
    /// <summary>
    /// Unity Editor起動時に自動登録する。
    /// </summary>
    [InitializeOnLoad]
    internal static class ScenePixelCaptureBootstrap
    {
        static ScenePixelCaptureBootstrap()
        {
            ScenePixelCaptureService service = ScenePixelCaptureService.instance;

            EditorApplication.update += service.UpdatePreview;
            EditorApplication.hierarchyChanged += service.MarkDirty;
            EditorApplication.projectChanged += service.MarkDirty;
            EditorApplication.playModeStateChanged += service.OnPlayModeStateChanged;

            EditorSceneManager.sceneDirtied += service.OnSceneDirtied;
            SceneView.duringSceneGui += service.OnSceneGui;

            Undo.undoRedoPerformed += service.MarkDirty;
            Undo.postprocessModifications += service.OnPostprocessModifications;

            AssemblyReloadEvents.beforeAssemblyReload += service.Cleanup;
            EditorApplication.quitting += service.Cleanup;
        }
    }

    /// <summary>
    /// Sceneビューの変化を検出し、必要なときだけ3層のRTを更新する。
    /// Gameビュー用のRTやマテリアルは変更しない。
    /// </summary>
    internal sealed class ScenePixelCaptureService : ScriptableSingleton<ScenePixelCaptureService>
    {
        private const int NORMAL_RENDERER_INDEX = 0;
        private const int SCENE_PREVIEW_RENDERER_INDEX = 3;
        private const int SCENE_CAPTURE_RENDERER_INDEX = 1;

        // sceneビュー中の更新頻度(15FPS)
        private const double UPDATE_INTERVAL = 1.0 / 15.0;

        private const string COMPOSITE_MATERIAL_PATH = "Assets/_Sandbox/Akane/Rendering/Materials/M_ScenePixelComposite.mat";

        private const string FOG_MATERIAL_PATH = "Assets/_Sandbox/Akane/Rendering/Materials/M_SceneViewVolumetricFog.mat";

        private const string PIXEL_EFFECT_ENABLED = "_PixelEffectEnabled";

        private readonly string[] _layerNames =
        {
            "PixelNear",
            "PixelMiddle",
            "PixelFar"
        };

        private readonly string[] _textureNames =
        {
            "_PixelNearTex",
            "_PixelMiddleTex",
            "_PixelFarTex"
        };

        private readonly int[] _pixelSizes = { 3, 6, 12 };

        private readonly Camera[] _captureCameras = new Camera[3];
        private readonly RenderTexture[] _captureTextures = new RenderTexture[3];

        private Material _compositeMaterial;
        private Material _fogMaterial;

        private Camera _lastSceneCamera;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private Vector2Int _lastViewSize;

        private float _lastFieldOfView;
        private float _lastOrthographicSize;
        private float _lastNearClip;
        private float _lastFarClip;
        private bool _lastOrthographic;

        private double _lastCaptureTime;
        private bool _dirty = true;
        private bool _rendering;
        private bool _reportedError;

        private const string SCENE_NORMAL_TEXTURE = "_SceneNormalTex";
        private const string SCENE_NORMAL_PREVIEW_ENABLED = "_SceneNormalPreviewEnabled";

        private Camera _normalCaptureCamera;
        private RenderTexture _normalCaptureTexture;

        private bool _lastPixelRenderingEnabled;
        private bool _hasLastPixelRenderingState;

        /// <summary>
        /// シーン内の変更を検出する。
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

        public void OnSceneDirtied(Scene scene)
        {
            MarkDirty();
        }

        public UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            MarkDirty();
            return modifications;
        }

        /// <summary>
        /// Pixel RenderingのON/OFFを取得する。
        /// Edit ModeでもInspectorの設定値を参照する。
        /// </summary>
        private static bool GetPixelRenderingEnabled()
        {
            PixelLayerRenderController controller =
                UnityEngine.Object.FindFirstObjectByType<PixelLayerRenderController>();

            if (controller == null)
            {
                return true;
            }

            return controller.PixelRenderingEnabled;
        }

        /// <summary>
        /// Play中は通常Rendererを使用する。
        /// Edit中はScene専用Rendererを使用する。
        /// </summary>
        public void OnSceneGui(SceneView sceneView)
        {
            if (sceneView == null || sceneView.camera == null ||
                Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            UniversalAdditionalCameraData cameraData = sceneView.camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(SCENE_PREVIEW_RENDERER_INDEX);
        }

        public void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredPlayMode)
            {
                Cleanup();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                MarkDirty();
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Scene Cameraの変更とPixel Renderingの状態に応じて
        /// Sceneビュー用のRenderTextureを更新する。
        /// </summary>
        public void UpdatePreview()
        {
            if (_rendering ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;

            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            Camera sceneCamera = sceneView.camera;

            if (sceneCamera.pixelWidth <= 0 || sceneCamera.pixelHeight <= 0)
            {
                return;
            }

            bool pixelEnabled = GetPixelRenderingEnabled();

            bool modeChanged =
                !_hasLastPixelRenderingState ||
                pixelEnabled != _lastPixelRenderingEnabled;

            bool cameraChanged = HasCameraChanged(sceneCamera);

            if (!_dirty &&
                !modeChanged &&
                !cameraChanged &&
                !EditorApplication.isPlaying)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;

            if (currentTime - _lastCaptureTime < UPDATE_INTERVAL)
            {
                return;
            }

            if (!LoadMaterials())
            {
                return;
            }

            _lastCaptureTime = currentTime;
            _rendering = true;

            bool succeeded = false;

            try
            {
                if (pixelEnabled)
                {
                    // Pixel ON：既存の3層Captureを使用。
                    succeeded = CapturePixelScene(sceneCamera);

                    if (succeeded)
                    {
                        ApplyCaptureTextures();
                    }
                }
                else
                {
                    // Pixel OFF：通常解像度でCapture。
                    succeeded = CaptureNormalScene(sceneCamera);

                    if (succeeded)
                    {
                        ApplyNormalTexture();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                succeeded = false;
            }
            finally
            {
                _rendering = false;
            }

            if (!succeeded)
            {
                return;
            }

            _lastPixelRenderingEnabled = pixelEnabled;
            _hasLastPixelRenderingState = true;

            SaveCameraState(sceneCamera);

            _dirty = false;
            _reportedError = false;

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Scene Camera視点でNear / Middle / Farを描画する。
        /// </summary>
        private bool CapturePixelScene(Camera sceneCamera)
        {
            for (int i = 0; i < _captureCameras.Length; i++)
            {
                if (!PrepareCaptureCamera(i, sceneCamera))
                {
                    return false;
                }

                Camera captureCamera = _captureCameras[i];

                UniversalRenderPipeline.SingleCameraRequest request = new()
                {
                    destination = _captureTextures[i]
                };

                if (!RenderPipeline.SupportsRenderRequest(
                        captureCamera,
                        request))
                {
                    ReportError("Pixel CaptureのRenderRequestに対応していません。");
                    return false;
                }

                RenderPipeline.SubmitRenderRequest(
                    captureCamera,
                    request);
            }

            return true;
        }

        /// <summary>
        /// Pixel OFF時に、Scene Camera視点の通常描画を取得する。
        /// 描画にはGameRendererを使用する。
        /// </summary>
        private bool CaptureNormalScene(Camera sceneCamera)
        {
            if (_normalCaptureCamera == null)
            {
                GameObject cameraObject = new("SceneNormalCapture");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;

                _normalCaptureCamera = cameraObject.AddComponent<Camera>();
                _normalCaptureCamera.enabled = false;
            }

            int width = Mathf.Max(1, sceneCamera.pixelWidth);
            int height = Mathf.Max(1, sceneCamera.pixelHeight);

            EnsureNormalRenderTexture(width, height);

            Camera camera = _normalCaptureCamera;

            // Sceneビューの視点・投影設定をコピーする。
            camera.CopyFrom(sceneCamera);

            camera.transform.SetPositionAndRotation(
                sceneCamera.transform.position,
                sceneCamera.transform.rotation);

            // Capture用Cameraとして必要な設定を復元する。
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.cullingMask = sceneCamera.cullingMask;
            camera.targetTexture = _normalCaptureTexture;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.allowHDR = true;

            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();

            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(NORMAL_RENDERER_INDEX);
            cameraData.renderPostProcessing = false;

            UniversalRenderPipeline.SingleCameraRequest request = new()
            {
                destination = _normalCaptureTexture
            };

            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                ReportError("通常表示用のRenderRequestに対応していません。");
                return false;
            }

            RenderPipeline.SubmitRenderRequest(camera, request);

            return true;
        }

        /// <summary>
        /// Sceneビューと同じ解像度のRTを確保する。
        /// </summary>
        private void EnsureNormalRenderTexture(int width, int height)
        {
            if (_normalCaptureTexture != null &&
                _normalCaptureTexture.width == width &&
                _normalCaptureTexture.height == height)
            {
                return;
            }

            ReleaseNormalRenderTexture();

            _normalCaptureTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGBHalf)
            {
                name = "RT_SceneNormal",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };

            _normalCaptureTexture.Create();
        }

        /// <summary>
        /// 通常表示用RTを解放する。
        /// </summary>
        private void ReleaseNormalRenderTexture()
        {
            if (_normalCaptureTexture == null)
            {
                return;
            }

            _normalCaptureTexture.Release();
            UnityEngine.Object.DestroyImmediate(_normalCaptureTexture);
            _normalCaptureTexture = null;
        }

        private bool HasCameraChanged(Camera camera)
        {
            if (_lastSceneCamera != camera)
            {
                return true;
            }

            if ((camera.transform.position - _lastCameraPosition).sqrMagnitude > 0.000001f)
            {
                return true;
            }

            if (Quaternion.Angle(camera.transform.rotation, _lastCameraRotation) > 0.01f)
            {
                return true;
            }

            if (_lastViewSize.x != camera.pixelWidth || _lastViewSize.y != camera.pixelHeight)
            {
                return true;
            }

            return camera.orthographic != _lastOrthographic ||
                Mathf.Abs(camera.fieldOfView - _lastFieldOfView) > 0.001f ||
                Mathf.Abs(camera.orthographicSize - _lastOrthographicSize) > 0.001f ||
                Mathf.Abs(camera.nearClipPlane - _lastNearClip) > 0.001f ||
                Mathf.Abs(camera.farClipPlane - _lastFarClip) > 0.001f;
        }

        private void SaveCameraState(Camera camera)
        {
            _lastSceneCamera = camera;
            _lastCameraPosition = camera.transform.position;
            _lastCameraRotation = camera.transform.rotation;
            _lastViewSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);

            _lastFieldOfView = camera.fieldOfView;
            _lastOrthographicSize = camera.orthographicSize;
            _lastNearClip = camera.nearClipPlane;
            _lastFarClip = camera.farClipPlane;
            _lastOrthographic = camera.orthographic;
        }

        private bool LoadMaterials()
        {
            if (_compositeMaterial == null)
            {
                _compositeMaterial = AssetDatabase.LoadAssetAtPath<Material>(COMPOSITE_MATERIAL_PATH);
            }

            if (_fogMaterial == null)
            {
                _fogMaterial = AssetDatabase.LoadAssetAtPath<Material>(FOG_MATERIAL_PATH);
            }

            if (_compositeMaterial == null)
            {
                ReportError($"Scene用Compositeマテリアルがありません: {COMPOSITE_MATERIAL_PATH}");
                return false;
            }

            return true;
        }

        private bool PrepareCaptureCamera(int index, Camera sceneCamera)
        {
            int layer = LayerMask.NameToLayer(_layerNames[index]);

            if (layer < 0)
            {
                ReportError($"Layerがありません: {_layerNames[index]}");
                return false;
            }

            if (_captureCameras[index] == null)
            {
                GameObject cameraObject = new($"ScenePixelCapture_{_layerNames[index]}");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;

                Camera captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.enabled = false;

                _captureCameras[index] = captureCamera;
            }

            int width = Mathf.Max(1, Mathf.CeilToInt((float)sceneCamera.pixelWidth / _pixelSizes[index]));
            int height = Mathf.Max(1, Mathf.CeilToInt((float)sceneCamera.pixelHeight / _pixelSizes[index]));

            EnsureRenderTexture(index, width, height);

            Camera camera = _captureCameras[index];

            // Scene用Capture Cameraとして識別する。
            if (!camera.TryGetComponent(out PixelCaptureViewMarker marker))
            {
                marker = camera.gameObject.AddComponent<PixelCaptureViewMarker>();
            }

            marker.SetView(PixelCaptureView.Scene);

            // Scene Cameraの設定をコピーする。
            camera.CopyFrom(sceneCamera);
            camera.transform.SetPositionAndRotation(sceneCamera.transform.position, sceneCamera.transform.rotation);

            // Sceneビューの設定をコピーした後、Capture固有の設定を復元する。
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << layer;
            camera.targetTexture = _captureTextures[index];
            camera.rect = new Rect(0, 0, 1, 1);
            camera.allowHDR = true;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(SCENE_CAPTURE_RENDERER_INDEX);
            cameraData.renderPostProcessing = false;

            return true;
        }

        private void EnsureRenderTexture(int index, int width, int height)
        {
            RenderTexture texture = _captureTextures[index];

            if (texture != null && texture.width == width && texture.height == height)
            {
                return;
            }

            ReleaseRenderTexture(index);

            RenderTexture newTexture = new(width, height, 24, RenderTextureFormat.ARGBHalf)
            {
                name = $"RT_ScenePixel_{_layerNames[index]}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };

            newTexture.Create();
            _captureTextures[index] = newTexture;
        }

        private void ApplyCaptureTextures()
        {
            for (int i = 0; i < _captureTextures.Length; i++)
            {
                _compositeMaterial.SetTexture(_textureNames[i], _captureTextures[i]);

                if (_fogMaterial != null)
                {
                    _fogMaterial.SetTexture(_textureNames[i], _captureTextures[i]);
                }
            }

            _compositeMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 1.0f);

            if (_fogMaterial != null)
            {
                _fogMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 1.0f);
            }

            // Pixel ON：通常表示RTを使わない。
            _compositeMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);

            if (_fogMaterial != null)
            {
                _fogMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
            }
        }

        /// <summary>
        /// Pixel OFF時の通常解像度RTをSceneビューへ渡す。
        /// </summary>
        private void ApplyNormalTexture()
        {
            _compositeMaterial.SetTexture(SCENE_NORMAL_TEXTURE, _normalCaptureTexture);

            _compositeMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 1.0f);

            _compositeMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 0.0f);

            if (_fogMaterial != null)
            {
                _fogMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 1.0f);
                _fogMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 0.0f);
            }
        }

        private void ReleaseRenderTexture(int index)
        {
            if (_captureTextures[index] == null)
            {
                return;
            }

            _captureTextures[index].Release();
            UnityEngine.Object.DestroyImmediate(_captureTextures[index]);
            _captureTextures[index] = null;
        }

        private void ReportError(string message)
        {
            if (_reportedError)
            {
                return;
            }

            Debug.LogError(message);
            _reportedError = true;
        }

        public void Cleanup()
        {
            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 0.0f);
            }

            if (_fogMaterial != null)
            {
                _fogMaterial.SetFloat(PIXEL_EFFECT_ENABLED, 0.0f);
            }

            for (int i = 0; i < _captureCameras.Length; i++)
            {
                if (_compositeMaterial != null)
                {
                    _compositeMaterial.SetTexture(_textureNames[i], null);
                }

                if (_fogMaterial != null)
                {
                    _fogMaterial.SetTexture(_textureNames[i], null);
                }

                if (_captureCameras[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_captureCameras[i].gameObject);
                    _captureCameras[i] = null;
                }

                if (_normalCaptureCamera != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        _normalCaptureCamera.gameObject);

                    _normalCaptureCamera = null;
                }

                ReleaseNormalRenderTexture();

                if (_compositeMaterial != null)
                {
                    _compositeMaterial.SetTexture(SCENE_NORMAL_TEXTURE, null);
                    _compositeMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
                }

                if (_fogMaterial != null)
                {
                    _fogMaterial.SetFloat(SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
                }

                _hasLastPixelRenderingState = false;

                ReleaseRenderTexture(i);
            }

            _lastSceneCamera = null;
            _dirty = true;
        }
    }
}
#endif
