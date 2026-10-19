
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
    [InitializeOnLoad]
    internal static class EditorPixelPreviewBootstrap
    {
        static EditorPixelPreviewBootstrap()
        {
            ScenePixelCaptureService service =
                ScenePixelCaptureService.instance;

            EditorApplication.update += service.UpdatePreview;
            EditorApplication.hierarchyChanged += service.MarkDirty;
            EditorApplication.projectChanged += service.MarkDirty;
            EditorApplication.playModeStateChanged +=
                service.OnPlayModeStateChanged;

            EditorSceneManager.sceneDirtied += service.OnSceneDirtied;
            EditorSceneManager.sceneSaving += service.OnSceneSaving;
            EditorSceneManager.sceneSaved += service.OnSceneSaved;

            SceneView.duringSceneGui += service.OnSceneGui;

            Undo.undoRedoPerformed += service.MarkDirty;
            Undo.postprocessModifications +=
                service.OnPostprocessModifications;

            AssemblyReloadEvents.beforeAssemblyReload += service.Cleanup;
            EditorApplication.quitting += service.Cleanup;
        }
    }

    /// <summary>
    /// Game / SceneのEditorプレビューを制御する。
    /// Gameは停止中のみ、Sceneは停止中とPlay中に更新する。
    /// </summary>
    internal sealed class ScenePixelCaptureService
        : ScriptableSingleton<ScenePixelCaptureService>
    {
        private const int NORMAL_RENDERER_INDEX = 0;
        private const int CAPTURE_RENDERER_INDEX = 1;
        private const int SCENE_PREVIEW_RENDERER_INDEX = 3;

        private const int CAPTURE_COUNT = 3;
        private const double UPDATE_INTERVAL = 1.0 / 15.0;

        private const string PIXEL_EFFECT_ENABLED =
            "_PixelEffectEnabled";

        private const string SCENE_NORMAL_TEXTURE =
            "_SceneNormalTex";

        private const string SCENE_NORMAL_PREVIEW_ENABLED =
            "_SceneNormalPreviewEnabled";

        private const string SCENE_COMPOSITE_MATERIAL_PATH =
            "Assets/_Sandbox/Akane/Rendering/Materials/M_ScenePixelComposite.mat";

        private const string SCENE_FOG_MATERIAL_PATH =
            "Assets/_Sandbox/Akane/Rendering/Materials/M_SceneViewVolumetricFog.mat";

        private readonly string[] _layerNames =
        {
            "PixelNear",
            "PixelMiddle",
            "PixelFar"
        };

        private readonly string[] _colorNames =
        {
            "_PixelNearTex",
            "_PixelMiddleTex",
            "_PixelFarTex"
        };

        private readonly string[] _depthNames =
        {
            "_PixelNearDepthTex",
            "_PixelMiddleDepthTex",
            "_PixelFarDepthTex"
        };

        private readonly int[] _pixelSizes = { 3, 6, 12 };

        // 内部ロジックは共有し、RTとCameraは分離する。
        private readonly EditorPixelCaptureSet _gameCaptures =
            new("EditorGamePixel");

        private readonly EditorPixelCaptureSet _sceneCaptures =
            new("EditorScenePixel");

        private PixelLayerRenderController _gameController;
        private Camera _gameCamera;

        private Camera _sceneCamera;
        private Camera _sceneNormalCamera;
        private RenderTexture _sceneNormalTexture;

        private Material _sceneCompositeMaterial;
        private Material _sceneFogMaterial;

        private CameraState _gameState;
        private CameraState _sceneState;

        private double _lastGameCaptureTime;
        private double _lastSceneCaptureTime;

        private bool _gameDirty = true;
        private bool _sceneDirty = true;
        private bool _gamePreviewActive;
        private bool _sceneModeKnown;
        private bool _lastScenePixelEnabled;
        private bool _gameRendererRestoredForSave;
        private bool _rendering;
        private bool _reportedError;

        public void MarkDirty()
        {
            _gameDirty = true;
            _sceneDirty = true;
        }

        public void OnSceneDirtied(Scene scene)
        {
            MarkDirty();
        }

        public UndoPropertyModification[] OnPostprocessModifications(
            UndoPropertyModification[] modifications)
        {
            MarkDirty();
            return modifications;
        }

        public void OnSceneGui(SceneView sceneView)
        {
            if (sceneView == null ||
                sceneView.camera == null ||
                Event.current == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            sceneView.camera.GetUniversalAdditionalCameraData()
                .SetRenderer(SCENE_PREVIEW_RENDERER_INDEX);
        }

        public void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // RuntimeのAwakeに先立ってEditor用Game RTを解放。
                StopGamePreview();
                StopScenePreview();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _sceneDirty = true;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopScenePreview();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                MarkDirty();
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Scene保存時はMain CameraのRendererを通常値へ戻す。
        /// </summary>
        public void OnSceneSaving(Scene scene, string path)
        {
            if (!_gamePreviewActive ||
                _gameController == null ||
                _gameCamera == null ||
                _gameCamera.gameObject.scene != scene)
            {
                return;
            }

            _gameCamera.GetUniversalAdditionalCameraData()
                .SetRenderer(_gameController.NormalRendererIndex);

            _gameRendererRestoredForSave = true;
        }

        /// <summary>
        /// 保存後、Editor表示用のRendererを再適用する。
        /// </summary>
        public void OnSceneSaved(Scene scene)
        {
            if (!_gameRendererRestoredForSave)
            {
                return;
            }

            _gameRendererRestoredForSave = false;

            if (_gamePreviewActive &&
                _gameCamera != null &&
                _gameController != null &&
                _gameController.PixelRenderingEnabled &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _gameCamera.GetUniversalAdditionalCameraData().SetRenderer(_gameController.PixelRendererIndex);

                RepaintGameViews();
            }
        }

        /// <summary>
        /// Editor更新処理。
        /// </summary>
        public void UpdatePreview()
        {
            if (_rendering || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            _rendering = true;

            try
            {
                PixelLayerRenderController controller = UnityEngine.Object.FindFirstObjectByType<PixelLayerRenderController>();

                bool pixelEnabled = controller == null || controller.PixelRenderingEnabled;

                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    UpdateGamePreview(controller);
                }

                // SceneビューはPlay中もEditor側で管理する。
                UpdateScenePreview(pixelEnabled);

                _reportedError = false;
            }
            catch (Exception exception)
            {
                if (!_reportedError)
                {
                    Debug.LogException(exception);
                    _reportedError = true;
                }
            }
            finally
            {
                // Sceneビュー用の振り分けをゲームCamera基準へ戻す。
                PixelLayerRenderController controller = UnityEngine.Object.FindFirstObjectByType<PixelLayerRenderController>();

                PixelRenderLayerManager manager = UnityEngine.Object.FindFirstObjectByType<PixelRenderLayerManager>();

                if (controller != null && manager != null && controller.MainCamera != null)
                {
                    manager.RefreshForCamera(controller.MainCamera);
                }

                _rendering = false;
            }
        }

        /// <summary>
        /// 停止中のGameビューを更新する。
        /// </summary>
        private void UpdateGamePreview(
            PixelLayerRenderController controller)
        {
            if (controller == null ||
                !controller.isActiveAndEnabled ||
                controller.MainCamera == null ||
                !controller.PixelRenderingEnabled)
            {
                if (_gamePreviewActive || _gameController != null)
                {
                    StopGamePreview();
                }

                return;
            }

            Camera mainCamera = controller.MainCamera;

            if (_gameController != controller ||
                _gameCamera != mainCamera)
            {
                StopGamePreview();

                _gameController = controller;
                _gameCamera = mainCamera;
                _gameDirty = true;
            }

            if (controller.CompositeMaterial == null)
            {
                throw new InvalidOperationException(
                    "Game用Composite Materialが未設定です。");
            }

            // Cameraやシーンが変わっていなければ再Captureしない。
            if (_gamePreviewActive &&
                !_gameDirty &&
                _gameState.Matches(mainCamera))
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;

            if (currentTime - _lastGameCaptureTime < UPDATE_INTERVAL)
            {
                return;
            }

            _lastGameCaptureTime = currentTime;

            _gameCaptures.Capture(
                mainCamera,
                PixelCaptureView.Game,
                controller.CaptureRendererIndex,
                controller.GetCaptureLayerName,
                controller.GetCaptureResolution);

            ApplyColors(
                _gameCaptures,
                controller.CompositeMaterial,
                controller.VolumetricFogMaterial);

            controller.CompositeMaterial.SetFloat(
                PIXEL_EFFECT_ENABLED, 1.0f);

            controller.CompositeMaterial.SetFloat(
                SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);

            if (controller.VolumetricFogMaterial != null)
            {
                controller.VolumetricFogMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED, 1.0f);

                controller.VolumetricFogMaterial.SetFloat(
                    SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
            }

            // Capture完成後、Main CameraのみPixel Rendererへ。
            if (!_gamePreviewActive)
            {
                mainCamera.GetUniversalAdditionalCameraData()
                    .SetRenderer(controller.PixelRendererIndex);
            }

            _gamePreviewActive = true;
            _gameDirty = false;
            _gameState.Save(mainCamera);

            RepaintGameViews();
        }

        /// <summary>
        /// GameビューのEditor用Captureを終了する。
        /// </summary>
        private void StopGamePreview()
        {
            if (_gameController != null)
            {
                if (_gameCamera != null && _gamePreviewActive)
                {
                    _gameCamera.GetUniversalAdditionalCameraData()
                        .SetRenderer(_gameController.NormalRendererIndex);
                }

                Material composite = _gameController.CompositeMaterial;
                Material fog = _gameController.VolumetricFogMaterial;

                ClearMaterial(composite);
                ClearMaterial(fog);
            }

            _gameCaptures.Release();

            _gameController = null;
            _gameCamera = null;
            _gamePreviewActive = false;
            _gameRendererRestoredForSave = false;
            _gameDirty = true;
            _gameState.Reset();

            RepaintGameViews();
        }

        /// <summary>
        /// SceneビューのPixel / Normalプレビューを更新する。
        /// </summary>
        private void UpdateScenePreview(bool pixelEnabled)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;

            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            Camera camera = sceneView.camera;

            if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0)
            {
                return;
            }

            bool modeChanged =
                !_sceneModeKnown ||
                pixelEnabled != _lastScenePixelEnabled;

            if (!_sceneDirty &&
                !modeChanged &&
                _sceneState.Matches(camera) &&
                !EditorApplication.isPlaying)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;

            if (currentTime - _lastSceneCaptureTime < UPDATE_INTERVAL)
            {
                return;
            }

            LoadSceneMaterials();

            _lastSceneCaptureTime = currentTime;
            _sceneCamera = camera;

            if (pixelEnabled)
            {
                _sceneCaptures.Capture(
                    camera,
                    PixelCaptureView.Scene,
                    CAPTURE_RENDERER_INDEX,
                    index => _layerNames[index],
                    index => GetSceneResolution(camera, index));

                ApplyColors(
                    _sceneCaptures,
                    _sceneCompositeMaterial,
                    _sceneFogMaterial);

                _sceneCompositeMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED, 1.0f);

                _sceneCompositeMaterial.SetFloat(
                    SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);

                if (_sceneFogMaterial != null)
                {
                    _sceneFogMaterial.SetFloat(
                        PIXEL_EFFECT_ENABLED, 1.0f);

                    _sceneFogMaterial.SetFloat(
                        SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
                }
            }
            else
            {
                CaptureNormalScene(camera);

                _sceneCompositeMaterial.SetTexture(
                    SCENE_NORMAL_TEXTURE,
                    _sceneNormalTexture);

                _sceneCompositeMaterial.SetFloat(
                    SCENE_NORMAL_PREVIEW_ENABLED, 1.0f);

                _sceneCompositeMaterial.SetFloat(
                    PIXEL_EFFECT_ENABLED, 0.0f);

                if (_sceneFogMaterial != null)
                {
                    _sceneFogMaterial.SetFloat(
                        SCENE_NORMAL_PREVIEW_ENABLED, 1.0f);

                    _sceneFogMaterial.SetFloat(
                        PIXEL_EFFECT_ENABLED, 0.0f);
                }
            }

            _lastScenePixelEnabled = pixelEnabled;
            _sceneModeKnown = true;
            _sceneDirty = false;
            _sceneState.Save(camera);

            SceneView.RepaintAll();
        }

        private Vector2Int GetSceneResolution(
            Camera sceneCamera,
            int index)
        {
            int pixelSize = _pixelSizes[index];

            return new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(
                    (float)sceneCamera.pixelWidth / pixelSize)),
                Mathf.Max(1, Mathf.CeilToInt(
                    (float)sceneCamera.pixelHeight / pixelSize)));
        }

        /// <summary>
        /// Sceneビューの通常解像度Capture。
        /// </summary>
        private void CaptureNormalScene(Camera sceneCamera)
        {
            if (_sceneNormalCamera == null)
            {
                GameObject cameraObject = new("EditorSceneNormal");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;

                _sceneNormalCamera =
                    cameraObject.AddComponent<Camera>();

                _sceneNormalCamera.enabled = false;
            }

            int width = Mathf.Max(1, sceneCamera.pixelWidth);
            int height = Mathf.Max(1, sceneCamera.pixelHeight);

            if (_sceneNormalTexture == null ||
                _sceneNormalTexture.width != width ||
                _sceneNormalTexture.height != height)
            {
                ReleaseNormalTexture();

                _sceneNormalTexture = new RenderTexture(
                    width, height, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "RT_EditorSceneNormal",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };

                _sceneNormalTexture.Create();
            }

            Camera camera = _sceneNormalCamera;

            camera.CopyFrom(sceneCamera);

            camera.transform.SetPositionAndRotation(
                sceneCamera.transform.position,
                sceneCamera.transform.rotation);

            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.cullingMask = sceneCamera.cullingMask;
            camera.targetTexture = _sceneNormalTexture;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.allowHDR = true;

            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();

            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(NORMAL_RENDERER_INDEX);
            cameraData.renderPostProcessing = false;

            UniversalRenderPipeline.SingleCameraRequest request = new()
            {
                destination = _sceneNormalTexture
            };

            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                throw new NotSupportedException(
                    "Scene Normal CaptureのRenderRequest非対応");
            }

            RenderPipeline.SubmitRenderRequest(camera, request);
        }

        private void LoadSceneMaterials()
        {
            if (_sceneCompositeMaterial == null)
            {
                _sceneCompositeMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SCENE_COMPOSITE_MATERIAL_PATH);
            }

            if (_sceneFogMaterial == null)
            {
                _sceneFogMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SCENE_FOG_MATERIAL_PATH);
            }

            if (_sceneCompositeMaterial == null)
            {
                throw new InvalidOperationException(
                    "M_ScenePixelCompositeが見つかりません。");
            }
        }

        /// <summary>
        /// CaptureしたColor RTを指定したMaterialへ設定する。
        /// Depth RTはPixelDepthCaptureRendererFeature側で設定。
        /// </summary>
        private void ApplyColors(
            EditorPixelCaptureSet captureSet,
            Material composite,
            Material fog)
        {
            for (int i = 0; i < CAPTURE_COUNT; i++)
            {
                RenderTexture texture = captureSet.GetTexture(i);

                if (composite != null)
                {
                    composite.SetTexture(_colorNames[i], texture);
                }

                if (fog != null)
                {
                    fog.SetTexture(_colorNames[i], texture);
                }
            }
        }

        private void ClearMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            for (int i = 0; i < CAPTURE_COUNT; i++)
            {
                material.SetTexture(_colorNames[i], null);
                material.SetTexture(_depthNames[i], null);
            }

            material.SetFloat(PIXEL_EFFECT_ENABLED, 0.0f);
        }

        private void ReleaseNormalTexture()
        {
            if (_sceneNormalTexture == null)
            {
                return;
            }

            if (_sceneNormalCamera != null)
            {
                _sceneNormalCamera.targetTexture = null;
            }

            _sceneNormalTexture.Release();
            UnityEngine.Object.DestroyImmediate(_sceneNormalTexture);
            _sceneNormalTexture = null;
        }

        private void StopScenePreview()
        {
            ClearMaterial(_sceneCompositeMaterial);
            ClearMaterial(_sceneFogMaterial);

            if (_sceneCompositeMaterial != null)
            {
                _sceneCompositeMaterial.SetTexture(
                    SCENE_NORMAL_TEXTURE, null);

                _sceneCompositeMaterial.SetFloat(
                    SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
            }

            if (_sceneFogMaterial != null)
            {
                _sceneFogMaterial.SetFloat(
                    SCENE_NORMAL_PREVIEW_ENABLED, 0.0f);
            }

            _sceneCaptures.Release();

            if (_sceneNormalCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    _sceneNormalCamera.gameObject);

                _sceneNormalCamera = null;
            }

            ReleaseNormalTexture();

            _sceneCamera = null;
            _sceneDirty = true;
            _sceneModeKnown = false;
            _sceneState.Reset();
        }

        private static void RepaintGameViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();

            foreach (EditorWindow window in
                     Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window.GetType().FullName == "UnityEditor.GameView")
                {
                    window.Repaint();
                }
            }
        }

        public void Cleanup()
        {
            StopGamePreview();
            StopScenePreview();
        }

        /// <summary>
        /// Capture元Cameraの視点変更を検出する。
        /// </summary>
        private struct CameraState
        {
            private Camera _camera;
            private Vector3 _position;
            private Quaternion _rotation;
            private Vector2Int _size;
            private float _fieldOfView;
            private float _orthographicSize;
            private float _aspect;
            private float _near;
            private float _far;
            private bool _orthographic;
            private bool _valid;

            public bool Matches(Camera camera)
            {
                if (!_valid || _camera != camera)
                {
                    return false;
                }

                return
                    (camera.transform.position - _position).sqrMagnitude
                        <= 0.000001f &&
                    Quaternion.Angle(
                        camera.transform.rotation, _rotation) <= 0.01f &&
                    camera.pixelWidth == _size.x &&
                    camera.pixelHeight == _size.y &&
                    Mathf.Abs(camera.fieldOfView - _fieldOfView) <= 0.001f &&
                    Mathf.Abs(camera.orthographicSize - _orthographicSize)
                        <= 0.001f &&
                    Mathf.Abs(camera.aspect - _aspect) <= 0.001f &&
                    Mathf.Abs(camera.nearClipPlane - _near) <= 0.001f &&
                    Mathf.Abs(camera.farClipPlane - _far) <= 0.001f &&
                    camera.orthographic == _orthographic;
            }

            public void Save(Camera camera)
            {
                _camera = camera;
                _position = camera.transform.position;
                _rotation = camera.transform.rotation;
                _size = new Vector2Int(
                    camera.pixelWidth, camera.pixelHeight);
                _fieldOfView = camera.fieldOfView;
                _orthographicSize = camera.orthographicSize;
                _aspect = camera.aspect;
                _near = camera.nearClipPlane;
                _far = camera.farClipPlane;
                _orthographic = camera.orthographic;
                _valid = true;
            }

            public void Reset()
            {
                _valid = false;
                _camera = null;
            }
        }
    }
}
#endif
