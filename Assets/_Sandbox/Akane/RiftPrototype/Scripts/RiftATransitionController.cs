
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RiftPrototypeA
{
    /// <summary>
    /// ステージ選択画面をガラス状に破壊し、
    /// 破片の隙間から3Dステージへ進入する。
    /// </summary>
    public sealed class RiftATransitionController : MonoBehaviour
    {
        [Header("描画参照")]
        [SerializeField] private RawImage riftImage;
        [SerializeField] private Image flashImage;
        [SerializeField] private RiftAPreviewCamera previewCamera;
        [SerializeField] private RiftAGlassFractureController fractureController;

        [Header("既存シリアライズとの互換")]
        [SerializeField, HideInInspector] private Shader riftShader;
        [SerializeField, HideInInspector] private Texture2D glassCrackMask;
        [SerializeField, HideInInspector] private RiftAStageLoader stageLoader;

        [Header("ステージ選択画面")]
        [SerializeField] private Canvas[] menuCanvases = Array.Empty<Canvas>();
        [SerializeField] private Camera[] menuCameras = Array.Empty<Camera>();
        [SerializeField] private Light[] menuLights = Array.Empty<Light>();
        [SerializeField] private AudioListener[] menuListeners = Array.Empty<AudioListener>();
        [SerializeField] private EventSystem[] menuEventSystems = Array.Empty<EventSystem>();

        [Header("演出時間")]
        [SerializeField, Min(0.05f)] private float crackDuration = 0.25f;
        [SerializeField, Min(0.2f)] private float breakDuration = 1.15f;
        [SerializeField, Min(0.2f)] private float enterDuration = 1.05f;
        [SerializeField, Min(0.01f)] private float flashInDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float flashOutDuration = 0.16f;

        [Header("開始タイミング")]
        [SerializeField, Min(0f)] private float breakStartDelay = 0.06f;
        [SerializeField, Min(0f)] private float cameraStartDelay = 0.14f;

        private RenderTexture menuSnapshot;
        private Tween activeTween;
        private bool isPlaying;

        private bool[] canvasStates;
        private bool[] cameraStates;
        private bool[] lightStates;
        private bool[] listenerStates;
        private bool[] eventSystemStates;

        public bool IsPlaying => isPlaying;

        private void Awake()
        {
            if (riftImage == null ||
                flashImage == null ||
                previewCamera == null ||
                fractureController == null)
            {
                Debug.LogError(
                    "RiftAの参照が不足しています。", this);

                enabled = false;
                return;
            }

            // 破片の下には、3Dステージの映像を全面表示する。
            riftImage.material = null;
            riftImage.color = Color.white;
            riftImage.raycastTarget = false;
            riftImage.gameObject.SetActive(false);

            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            flashImage.gameObject.SetActive(false);

            // 実行時の画面保存先を事前に確保しておく。
            AllocateSnapshot();
        }

        private void AllocateSnapshot()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            if (menuSnapshot != null &&
                menuSnapshot.width == width &&
                menuSnapshot.height == height)
            {
                return;
            }

            if (menuSnapshot != null)
            {
                menuSnapshot.Release();
                Destroy(menuSnapshot);
            }

            menuSnapshot = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = "RiftA_MenuSnapshot"
            };

            menuSnapshot.Create();
        }

        /// <summary>
        /// ステージ確定時に呼び出す。
        /// 既存のStageSelectControllerからの呼び出し形式は維持する。
        /// </summary>
        public void PlayTransition(
            string sceneName,
            RectTransform selectedImage,
            Action onFailed = null)
        {
            if (!enabled ||
                isPlaying ||
                selectedImage == null ||
                string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError(
                    "亀裂遷移を開始できません。", this);

                onFailed?.Invoke();
                return;
            }

            PlayTransitionAsync(
                sceneName, selectedImage, onFailed).Forget();
        }

        /// <summary>
        /// 画面の破壊と、3Dカメラの前進を並行して行う。
        /// </summary>
        private async UniTaskVoid PlayTransitionAsync(
            string sceneName,
            RectTransform selectedImage,
            Action onFailed)
        {
            isPlaying = true;
            bool committed = false;

            Scene sourceScene = gameObject.scene;
            CancellationToken token =
                this.GetCancellationTokenOnDestroy();

            RiftAStagePreloadService preloadService =
                RiftAStagePreloadService.GetOrCreate();

            try
            {
                // 先行読み込み済みのステージを取得する。
                RiftAStageEntry entry =
                    await preloadService.AcquireForTransitionAsync(
                        sceneName, token);

                if (entry == null || entry.GameplayCamera == null)
                {
                    throw new InvalidOperationException(
                        "RiftAStageEntryまたはGameplayCameraが未設定です。");
                }

                if (!SceneManager.SetActiveScene(
                    preloadService.LoadedScene))
                {
                    throw new InvalidOperationException(
                        "遷移先ステージをアクティブにできません。");
                }

                // 描画前にメニュー画面を保存する。
                // この時点では元のメニューを消さない。
                AllocateSnapshot();

                await UniTask.WaitForEndOfFrame(
                    this, cancellationToken: token);

                ScreenCapture.CaptureScreenshotIntoRenderTexture(
                    menuSnapshot);

                // 保存処理の結果が使用できる次のフレームまで進める。
                await UniTask.NextFrame(cancellationToken: token);

                Vector2 centerUV = GetScreenUV(selectedImage);

                // 3Dステージの描画を準備する。
                SetMenuLights(false);

                previewCamera.Prepare(entry);
                previewCamera.MoveTo(0f);

                await UniTask.NextFrame(cancellationToken: token);

                // 画面全体の3D映像を破片の背後に配置する。
                riftImage.texture = previewCamera.RenderTexture;

                // 画面を破片メッシュに分割して表示する。
                fractureController.Prepare(
                    menuSnapshot, centerUV);

                // 破片の初回描画を待ってから、元のメニューを隠す。
                await UniTask.NextFrame(cancellationToken: token);

                SetMenuPresentation(false);
                riftImage.gameObject.SetActive(true);

                float crackProgress = 0f;
                float breakProgress = 0f;
                float travelProgress = 0f;

                Sequence sequence =
                    DOTween.Sequence().SetUpdate(true);

                // ① 中心からガラス状のひびを走らせる。
                sequence.Insert(
                    0f,
                    DOTween.To(
                        () => crackProgress,
                        value =>
                        {
                            crackProgress = value;
                            fractureController.SetProgress(
                                crackProgress, breakProgress);
                        },
                        1f,
                        crackDuration)
                    .SetEase(Ease.OutCubic));

                // ② 中央の破片から実際に分離させる。
                // 破片の間から向こう側の3Dステージが見える。
                sequence.Insert(
                    breakStartDelay,
                    DOTween.To(
                        () => breakProgress,
                        value =>
                        {
                            breakProgress = value;
                            fractureController.SetProgress(
                                crackProgress, breakProgress);
                        },
                        1f,
                        breakDuration)
                    .SetEase(Ease.InOutSine));

                // ③ 破片が広がるのと同時に3Dカメラが前進する。
                sequence.Insert(
                    cameraStartDelay,
                    DOTween.To(
                        () => travelProgress,
                        value =>
                        {
                            travelProgress = value;
                            previewCamera.MoveTo(travelProgress);
                        },
                        1f,
                        enterDuration)
                    .SetEase(Ease.InOutCubic));

                activeTween = sequence;
                await sequence.ToUniTask(cancellationToken: token);

                // 破片が視界から消えたら、3D世界のみを表示する。
                fractureController.StopFracture();

                // 最後に短い閃光で本編カメラへの切り替えを隠す。
                flashImage.gameObject.SetActive(true);
                flashImage.color = new Color(1f, 1f, 1f, 0f);

                activeTween = flashImage
                    .DOFade(1f, flashInDuration)
                    .SetUpdate(true);

                await activeTween.ToUniTask(cancellationToken: token);

                previewCamera.StopRendering();
                riftImage.gameObject.SetActive(false);

                // 平行投影の本編カメラへ切り替える。
                entry.ActivateGameplay();

                committed = true;
                preloadService.CommitLoadedScene();

                // 本編の描画が始まったら閃光を消す。
                activeTween = flashImage
                    .DOFade(0f, flashOutDuration)
                    .SetUpdate(true);

                await activeTween.ToUniTask(cancellationToken: token);

                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(sourceScene);
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄による中断。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);

                if (committed)
                {
                    if (flashImage != null)
                        flashImage.gameObject.SetActive(false);

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                        SceneManager.UnloadSceneAsync(sourceScene);
                }
                else
                {
                    activeTween?.Kill();

                    fractureController.StopFracture();
                    previewCamera.StopRendering();
                    previewCamera.Release();

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                        SceneManager.SetActiveScene(sourceScene);

                    RestoreMenuPresentation();

                    if (riftImage != null)
                        riftImage.gameObject.SetActive(false);

                    if (flashImage != null)
                        flashImage.gameObject.SetActive(false);

                    onFailed?.Invoke();
                }
            }
            finally
            {
                if (!committed)
                {
                    preloadService.ReleaseTransitionLock();
                    isPlaying = false;
                }
            }
        }

        /// <summary>
        /// 画面保存後にメニュー側の表示と入力を停止する。
        /// </summary>
        private void SetMenuPresentation(bool visible)
        {
            if (visible)
            {
                RestoreMenuPresentation();
                return;
            }

            canvasStates = new bool[menuCanvases.Length];
            cameraStates = new bool[menuCameras.Length];
            listenerStates = new bool[menuListeners.Length];
            eventSystemStates = new bool[menuEventSystems.Length];

            for (int i = 0; i < menuCanvases.Length; i++)
            {
                if (menuCanvases[i] == null) continue;
                canvasStates[i] = menuCanvases[i].enabled;
                menuCanvases[i].enabled = false;
            }

            for (int i = 0; i < menuCameras.Length; i++)
            {
                if (menuCameras[i] == null) continue;
                cameraStates[i] = menuCameras[i].enabled;
                menuCameras[i].enabled = false;
            }

            for (int i = 0; i < menuListeners.Length; i++)
            {
                if (menuListeners[i] == null) continue;
                listenerStates[i] = menuListeners[i].enabled;
                menuListeners[i].enabled = false;
            }

            for (int i = 0; i < menuEventSystems.Length; i++)
            {
                if (menuEventSystems[i] == null) continue;
                eventSystemStates[i] = menuEventSystems[i].enabled;
                menuEventSystems[i].enabled = false;
            }
        }

        private void RestoreMenuPresentation()
        {
            for (int i = 0; i < menuCanvases.Length; i++)
            {
                if (menuCanvases[i] != null && canvasStates != null)
                    menuCanvases[i].enabled = canvasStates[i];
            }

            for (int i = 0; i < menuCameras.Length; i++)
            {
                if (menuCameras[i] != null && cameraStates != null)
                    menuCameras[i].enabled = cameraStates[i];
            }

            for (int i = 0; i < menuListeners.Length; i++)
            {
                if (menuListeners[i] != null && listenerStates != null)
                    menuListeners[i].enabled = listenerStates[i];
            }

            for (int i = 0; i < menuEventSystems.Length; i++)
            {
                if (menuEventSystems[i] != null && eventSystemStates != null)
                    menuEventSystems[i].enabled = eventSystemStates[i];
            }

            SetMenuLights(true);
        }

        private void SetMenuLights(bool restore)
        {
            if (!restore)
            {
                lightStates = new bool[menuLights.Length];

                for (int i = 0; i < menuLights.Length; i++)
                {
                    if (menuLights[i] == null) continue;
                    lightStates[i] = menuLights[i].enabled;
                    menuLights[i].enabled = false;
                }

                return;
            }

            if (lightStates == null) return;

            for (int i = 0; i < menuLights.Length; i++)
            {
                if (menuLights[i] != null)
                    menuLights[i].enabled = lightStates[i];
            }
        }

        /// <summary>
        /// 選択画像の中心を画面UV座標に変換する。
        /// </summary>
        private static Vector2 GetScreenUV(RectTransform target)
        {
            Canvas canvas =
                target.GetComponentInParent<Canvas>();

            Camera eventCamera =
                canvas != null &&
                canvas.rootCanvas.renderMode !=
                    RenderMode.ScreenSpaceOverlay
                    ? canvas.rootCanvas.worldCamera
                    : null;

            Vector3 center =
                target.TransformPoint(target.rect.center);

            Vector2 screenPoint =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera, center);

            return new Vector2(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1, Screen.height)));
        }

        private void OnDestroy()
        {
            activeTween?.Kill();

            if (menuSnapshot != null)
            {
                menuSnapshot.Release();
                Destroy(menuSnapshot);
            }
        }
    }
}
