
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
    /// ガラス状の亀裂を発生させ、中心に開いた穴から
    /// 読み込み済みの3Dステージへカメラを進入させる。
    /// </summary>
    public sealed class RiftATransitionController : MonoBehaviour
    {
        private static readonly int CRACK_PROGRESS_ID = Shader.PropertyToID("_CrackProgress");
        private static readonly int HOLE_PROGRESS_ID = Shader.PropertyToID("_HoleProgress");
        private static readonly int OPEN_PROGRESS_ID = Shader.PropertyToID("_OpenProgress");
        private static readonly int TRAVEL_PROGRESS_ID = Shader.PropertyToID("_TravelProgress");
        private static readonly int CENTER_ID = Shader.PropertyToID("_Center");
        private static readonly int ASPECT_ID = Shader.PropertyToID("_Aspect");
        private static readonly int READY_ID = Shader.PropertyToID("_Ready");
        private static readonly int CRACK_TEXTURE_ID = Shader.PropertyToID("_CrackTex");

        [Header("亀裂描画")]
        [SerializeField] private Shader riftShader;
        [SerializeField] private Texture2D glassCrackMask;
        [SerializeField] private RawImage riftImage;
        [SerializeField] private Image flashImage;
        [SerializeField] private RiftAPreviewCamera previewCamera;

        [Header("既存データとの互換")]
        [SerializeField] private RiftAStageLoader stageLoader;

        [Header("ステージ選択画面の停止対象")]
        [Tooltip("亀裂表示用Canvasは含めない。")]
        [SerializeField] private Canvas[] menuCanvases = Array.Empty<Canvas>();
        [SerializeField] private Camera[] menuCameras = Array.Empty<Camera>();
        [SerializeField] private Light[] menuLights = Array.Empty<Light>();
        [SerializeField] private AudioListener[] menuListeners = Array.Empty<AudioListener>();
        [SerializeField] private EventSystem[] menuEventSystems = Array.Empty<EventSystem>();

        [Header("演出時間")]
        [SerializeField, Min(0.05f)] private float crackDuration = 0.28f;
        [SerializeField, Min(0.05f)] private float holeDuration = 0.22f;
        [SerializeField, Min(0.2f)] private float enterDuration = 1.1f;
        [SerializeField, Min(0.01f)] private float flashInDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float flashOutDuration = 0.16f;

        [Header("演出開始タイミング")]
        [Tooltip("亀裂が入ってから中心穴が開き始めるまでの秒数。")]
        [SerializeField, Min(0f)] private float holeStartDelay = 0.035f;

        [Tooltip("中心穴が見え始めてからカメラを前進させる。")]
        [SerializeField, Min(0f)] private float cameraStartDelay = 0.10f;

        [Tooltip("小さな穴から大きな開口へ変わるタイミング。")]
        [SerializeField, Min(0f)] private float openStartDelay = 0.24f;

        private Material materialInstance;
        private Tween activeTween;
        private bool[] menuLightStates;
        private bool isPlaying;

        public bool IsPlaying => isPlaying;

        private void Awake()
        {
            if (riftShader == null ||
                glassCrackMask == null ||
                riftImage == null ||
                flashImage == null ||
                previewCamera == null)
            {
                Debug.LogError(
                    $"RiftAの参照不足: " +
                    $"Shader={riftShader != null}, " +
                    $"GlassMask={glassCrackMask != null}, " +
                    $"RiftImage={riftImage != null}, " +
                    $"FlashImage={flashImage != null}, " +
                    $"PreviewCamera={previewCamera != null}",
                    this);

                enabled = false;
                return;
            }

            // UI専用のマテリアルを生成する。
            materialInstance = new Material(riftShader)
            {
                name = "RiftA_CenterHole_Runtime"
            };

            materialInstance.SetTexture(CRACK_TEXTURE_ID, glassCrackMask);

            riftImage.material = materialInstance;
            riftImage.texture = Texture2D.blackTexture;
            riftImage.color = Color.white;
            riftImage.raycastTarget = false;
            riftImage.gameObject.SetActive(false);

            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            flashImage.gameObject.SetActive(false);

            ResetShaderProgress();
        }

        /// <summary>
        /// 選択されたステージへの亀裂遷移を開始する。
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
                    "RiftAの遷移を開始できません。参照とシーン名を確認してください。",
                    this);

                onFailed?.Invoke();
                return;
            }

            PlayTransitionAsync(sceneName, selectedImage, onFailed).Forget();
        }

        /// <summary>
        /// 先行読み込み済みのステージを表示し、
        /// 中心穴の拡大と3Dカメラの前進を並行して実行する。
        /// </summary>
        private async UniTaskVoid PlayTransitionAsync(
            string sceneName,
            RectTransform selectedImage,
            Action onFailed)
        {
            isPlaying = true;

            bool committed = false;
            Scene sourceScene = gameObject.scene;
            CancellationToken token = this.GetCancellationTokenOnDestroy();

            RiftAStagePreloadService preloadService =
                RiftAStagePreloadService.GetOrCreate();

            try
            {
                // ステージを取得する。未読み込みの場合は演出開始前に待つ。
                RiftAStageEntry entry =
                    await preloadService.AcquireForTransitionAsync(sceneName, token);

                if (entry == null || entry.GameplayCamera == null)
                {
                    throw new InvalidOperationException(
                        "遷移先のRiftAStageEntryまたはGameplayCameraが未設定です。");
                }

                // ステージ側の描画設定を有効にする。
                if (!SceneManager.SetActiveScene(preloadService.LoadedScene))
                {
                    throw new InvalidOperationException(
                        "遷移先ステージをアクティブにできません。");
                }

                // メニュー側のライトが3D映像へ干渉しないようにする。
                SetMenuLights(false);

                // RenderTextureとカメラを準備する。
                previewCamera.Prepare(entry);
                previewCamera.MoveTo(0f);

                // 最初の3D映像が描画されるフレームを確保する。
                await UniTask.NextFrame(cancellationToken: token);

                // 亀裂の発生位置を選択中カードの画像中央に合わせる。
                Vector2 center = GetScreenUV(selectedImage);

                materialInstance.SetVector(
                    CENTER_ID,
                    new Vector4(center.x, center.y, 0f, 0f));

                materialInstance.SetFloat(
                    ASPECT_ID,
                    (float)Screen.width / Mathf.Max(1, Screen.height));

                ResetShaderProgress();

                riftImage.texture = previewCamera.RenderTexture;
                materialInstance.SetFloat(READY_ID, 1f);

                // 亀裂を描画するUIを有効化する。
                riftImage.gameObject.SetActive(true);

                float crackProgress = 0f;
                float holeProgress = 0f;
                float openProgress = 0f;
                float travelProgress = 0f;

                // 4つの演出を時間差で重ねる。
                Sequence sequence = DOTween.Sequence().SetUpdate(true);

                // ① ガラスに亀裂が走る。
                sequence.Insert(
                    0f,
                    DOTween.To(
                            () => crackProgress,
                            value =>
                            {
                                crackProgress = value;
                                materialInstance.SetFloat(
                                    CRACK_PROGRESS_ID, value);
                            },
                            1f,
                            crackDuration)
                        .SetEase(Ease.OutCubic));

                // ② 亀裂の中心に小さな穴を開ける。
                // この時点から向こう側のステージが見える。
                sequence.Insert(
                    holeStartDelay,
                    DOTween.To(
                            () => holeProgress,
                            value =>
                            {
                                holeProgress = value;
                                materialInstance.SetFloat(
                                    HOLE_PROGRESS_ID, value);
                            },
                            1f,
                            holeDuration)
                        .SetEase(Ease.OutCubic));

                // ③ 小さな穴が見えている間にカメラを前進させる。
                // RenderTextureの拡大は行わない。
                sequence.Insert(
                    cameraStartDelay,
                    DOTween.To(
                            () => travelProgress,
                            value =>
                            {
                                travelProgress = value;

                                materialInstance.SetFloat(
                                    TRAVEL_PROGRESS_ID, value);

                                previewCamera.MoveTo(value);
                            },
                            1f,
                            enterDuration)
                        .SetEase(Ease.InOutCubic));

                // ④ 中心穴をガラスの破片状の輪郭で拡大する。
                sequence.Insert(
                    openStartDelay,
                    DOTween.To(
                            () => openProgress,
                            value =>
                            {
                                openProgress = value;
                                materialInstance.SetFloat(
                                    OPEN_PROGRESS_ID, value);
                            },
                            1f,
                            enterDuration)
                        .SetEase(Ease.InOutCubic));

                activeTween = sequence;
                await sequence.ToUniTask(cancellationToken: token);

                // 穴が画面全体を覆った後に短いフラッシュを入れる。
                flashImage.gameObject.SetActive(true);
                flashImage.color = new Color(1f, 1f, 1f, 0f);

                activeTween = flashImage
                    .DOFade(1f, flashInDuration)
                    .SetUpdate(true);

                await activeTween.ToUniTask(cancellationToken: token);

                // 3Dプレビューの描画を終了する。
                previewCamera.StopRendering();
                riftImage.gameObject.SetActive(false);

                // ステージ選択側の描画と入力を停止する。
                foreach (Canvas canvas in menuCanvases)
                {
                    if (canvas != null)
                    {
                        canvas.enabled = false;
                    }
                }

                foreach (Camera camera in menuCameras)
                {
                    if (camera != null)
                    {
                        camera.enabled = false;
                    }
                }

                foreach (AudioListener listener in menuListeners)
                {
                    if (listener != null)
                    {
                        listener.enabled = false;
                    }
                }

                foreach (EventSystem eventSystem in menuEventSystems)
                {
                    if (eventSystem != null)
                    {
                        eventSystem.enabled = false;
                    }
                }

                // 平行投影の本編カメラを有効化する。
                entry.ActivateGameplay();

                committed = true;
                preloadService.CommitLoadedScene();

                // 本編が描画されてからフラッシュを消す。
                activeTween = flashImage
                    .DOFade(0f, flashOutDuration)
                    .SetUpdate(true);

                await activeTween.ToUniTask(cancellationToken: token);

                // 元のステージ選択シーンを解放する。
                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(sourceScene);
                }
            }
            catch (OperationCanceledException)
            {
                // オブジェクト破棄による中断。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);

                if (committed)
                {
                    // 本編への切り替え後ならメニューを解放する。
                    if (flashImage != null)
                    {
                        flashImage.gameObject.SetActive(false);
                    }

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(sourceScene);
                    }
                }
                else
                {
                    // 遷移開始前の状態へ戻す。
                    activeTween?.Kill();

                    previewCamera.StopRendering();
                    previewCamera.Release();

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(sourceScene);
                    }

                    SetMenuLights(true);

                    if (riftImage != null)
                    {
                        riftImage.gameObject.SetActive(false);
                    }

                    if (flashImage != null)
                    {
                        flashImage.gameObject.SetActive(false);
                    }

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
        /// 亀裂と穴の進行度を初期状態へ戻す。
        /// </summary>
        private void ResetShaderProgress()
        {
            if (materialInstance == null)
            {
                return;
            }

            materialInstance.SetFloat(CRACK_PROGRESS_ID, 0f);
            materialInstance.SetFloat(HOLE_PROGRESS_ID, 0f);
            materialInstance.SetFloat(OPEN_PROGRESS_ID, 0f);
            materialInstance.SetFloat(TRAVEL_PROGRESS_ID, 0f);
            materialInstance.SetFloat(READY_ID, 0f);
        }

        /// <summary>
        /// メニュー側のライトを停止・復元する。
        /// </summary>
        private void SetMenuLights(bool restore)
        {
            if (!restore)
            {
                menuLightStates = new bool[menuLights.Length];

                for (int i = 0; i < menuLights.Length; i++)
                {
                    if (menuLights[i] == null)
                    {
                        continue;
                    }

                    menuLightStates[i] = menuLights[i].enabled;
                    menuLights[i].enabled = false;
                }

                return;
            }

            if (menuLightStates == null)
            {
                return;
            }

            for (int i = 0; i < menuLights.Length; i++)
            {
                if (menuLights[i] != null)
                {
                    menuLights[i].enabled = menuLightStates[i];
                }
            }
        }

        /// <summary>
        /// 選択中のステージ画像の中心を画面UV座標で取得する。
        /// </summary>
        private static Vector2 GetScreenUV(RectTransform target)
        {
            Canvas canvas = target.GetComponentInParent<Canvas>();

            Camera eventCamera =
                canvas != null &&
                canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.rootCanvas.worldCamera
                    : null;

            Vector3 center = target.TransformPoint(target.rect.center);

            Vector2 screenPoint =
                RectTransformUtility.WorldToScreenPoint(eventCamera, center);

            return new Vector2(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1, Screen.height)));
        }

        private void OnDestroy()
        {
            activeTween?.Kill();

            if (materialInstance != null)
            {
                Destroy(materialInstance);
            }
        }
    }
}
