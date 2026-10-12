
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

            SceneView.duringSceneGui += service.OnSceneGui;
            EditorSceneManager.sceneDirtied += service.OnSceneDirtied;

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
        private const int SCENE_CAPTURE_RENDERER_INDEX = 4;

        // sceneビュー中の更新頻度(15FPS)
        private const double UPDATE_INTERVAL = 1.0 / 15.0;

        private const string COMPOSITE_MATERIAL_PATH =
            "Assets/_Sandbox/Akane/Rendering/Materials/M_ScenePixelComposite.mat";

        private const string FOG_MATERIAL_PATH =
            "Assets/_Sandbox/Akane/Rendering/Materials/M_SceneViewVolumetricFog.mat";

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
        /// Editorが更新されるたびに状態だけ確認する。
        /// 実際のCaptureは必要なときのみ実行する。
        /// </summary>
        public void UpdatePreview()
        {
            if (_rendering || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (SceneView.sceneViews.Count == 0)
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

            if (!_dirty && !HasCameraChanged(sceneCamera) && !EditorApplication.isPlaying)
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

            bool succeeded = true;

            try
            {
                for (int i = 0; i < _captureCameras.Length; i++)
                {
                    if (!PrepareCaptureCamera(i, sceneCamera))
                    {
                        succeeded = false;
                        break;
                    }

                    Camera captureCamera = _captureCameras[i];

                    UniversalRenderPipeline.SingleCameraRequest request = new()
                    {
                        destination = _captureTextures[i]
                    };

                    if (!RenderPipeline.SupportsRenderRequest(captureCamera, request))
                    {
                        ReportError("URPがSingleCameraRequestに対応していません。");
                        succeeded = false;
                        break;
                    }

                    RenderPipeline.SubmitRenderRequest(captureCamera, request);
                }

                if (succeeded)
                {
                    ApplyCaptureTextures();
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

            SaveCameraState(sceneCamera);
            _dirty = false;
            _reportedError = false;

            // Captureを更新したときだけSceneビューを再描画する。
            SceneView.RepaintAll();
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

            camera.CopyFrom(sceneCamera);
            camera.transform.SetPositionAndRotation(
                sceneCamera.transform.position,
                sceneCamera.transform.rotation);

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

                ReleaseRenderTexture(i);
            }

            _lastSceneCamera = null;
            _dirty = true;
        }
    }
}
#endif
