
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
    /// 進入中にドット化を強め、最後は本編カメラへ接続する。
    /// </summary>
    public sealed class RiftATransitionController : MonoBehaviour
    {
        private static readonly int PIXEL_BLEND_ID =
            Shader.PropertyToID("_PixelBlend");

        [Header("描画参照")]
        [SerializeField] private RawImage riftImage;
        [SerializeField] private Image flashImage;
        [SerializeField] private RiftAPreviewCamera previewCamera;
        [SerializeField] private RiftAGlassFractureController fractureController;

        [Header("プレビュー描画")]
        [Tooltip("Prototype/RiftA/PreviewPixelBlendを設定する。")]
        [SerializeField] private Shader previewPixelBlendShader;

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
        [SerializeField, Min(0.2f)] private float enterDuration = 1.6f;

        [Header("本編カメラへの接続")]
        [Tooltip("プレビュー映像から本編映像へ切り替える時間。")]
        [SerializeField, Min(0.05f)]
        private float gameplayBlendDuration = 0.3f;

        [Header("ドット化の開始タイミング")]
        [Tooltip("カメラ進入の何割からドット化を開始するか。")]
        [SerializeField, Range(0f, 1f)]
        private float pixelBlendStart = 0.25f;

        [Tooltip("カメラ進入の何割でドット化を完了するか。")]
        [SerializeField, Range(0f, 1f)]
        private float pixelBlendEnd = 0.95f;

        [Header("開始タイミング")]
        [SerializeField, Min(0f)] private float breakStartDelay = 0.06f;
        [SerializeField, Min(0f)] private float cameraStartDelay = 0.14f;

        private RenderTexture menuSnapshot;
        private Material previewPixelMaterial;
        private Tween activeTween;
        private bool isPlaying;

        private bool[] canvasStates;
        private bool[] cameraStates;
        private bool[] lightStates;
        private bool[] listenerStates;
        private bool[] eventSystemStates;

        public bool IsPlaying => isPlaying;

        /// <summary>
        /// 必要な参照を検証し、プレビュー用マテリアルを作成する。
        /// </summary>
        private void Awake()
        {
            if (riftImage == null ||
                previewCamera == null ||
                fractureController == null)
            {
                Debug.LogError(
                    "RiftAの描画参照が不足しています。",
                    this);

                enabled = false;
                return;
            }

            if (previewPixelBlendShader == null)
            {
                Debug.LogError(
                    "PreviewPixelBlend Shaderが未設定です。",
                    this);

                enabled = false;
                return;
            }

            if (!previewPixelBlendShader.isSupported)
            {
                Debug.LogError(
                    "PreviewPixelBlend Shaderが現在の環境に対応していません。",
                    this);

                enabled = false;
                return;
            }

            // プレビュー映像専用のマテリアルを生成する。
            previewPixelMaterial = new Material(previewPixelBlendShader)
            {
                name = "RiftA_PreviewPixelBlend_Runtime"
            };

            previewPixelMaterial.SetFloat(PIXEL_BLEND_ID, 0f);

            // 3Dステージ映像の表示先を初期化する。
            riftImage.material = previewPixelMaterial;
            riftImage.color = Color.white;
            riftImage.raycastTarget = false;
            riftImage.gameObject.SetActive(false);

            // 旧フラッシュ演出は使用しない。
            // 既存シーンとの参照互換性のため保持する。
            if (flashImage != null)
            {
                flashImage.color = new Color(1f, 1f, 1f, 0f);
                flashImage.raycastTarget = false;
                flashImage.gameObject.SetActive(false);
            }

            AllocateSnapshot();
        }

        /// <summary>
        /// ステージ選択画面の保存用RenderTextureを確保する。
        /// </summary>
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
                menuSnapshot = null;
            }

            menuSnapshot = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = "RiftA_MenuSnapshot",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
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
                    "亀裂遷移を開始できません。",
                    this);

                onFailed?.Invoke();
                return;
            }

            PlayTransitionAsync(
                sceneName,
                selectedImage,
                onFailed).Forget();
        }

        /// <summary>
        /// 亀裂・破片・カメラ進入・ドット化を並行して実行する。
        /// 最後は本編カメラの映像へ滑らかに切り替える。
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
                // ① 遷移先ステージの先行読み込みが完了するまで待つ。
                RiftAStageEntry entry =
                    await preloadService.AcquireForTransitionAsync(
                        sceneName,
                        token);

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

                // ② 元の選択画面をRenderTextureへ保存する。
                AllocateSnapshot();

                await UniTask.WaitForEndOfFrame(
                    this,
                    cancellationToken: token);

                ScreenCapture.CaptureScreenshotIntoRenderTexture(
                    menuSnapshot);

                await UniTask.NextFrame(
                    cancellationToken: token);

                Vector2 centerUV = GetScreenUV(selectedImage);

                // ③ 透視投影のプレビューカメラを準備する。
                SetMenuLights(false);

                previewCamera.Prepare(entry);
                previewCamera.MoveTo(0f);

                // ドット化を初期状態へ戻す。
                previewPixelMaterial.SetFloat(
                    PIXEL_BLEND_ID,
                    0f);

                await UniTask.NextFrame(
                    cancellationToken: token);

                // ④ ステージ映像と破片の表示を準備する。
                riftImage.texture = previewCamera.RenderTexture;
                riftImage.color = Color.white;

                fractureController.Prepare(
                    menuSnapshot,
                    centerUV);

                // 初回の破片描画を待つ。
                await UniTask.NextFrame(
                    cancellationToken: token);

                // 元のメニューを非表示にする。
                SetMenuPresentation(false);

                // 破片の背後に3Dステージ映像を表示する。
                riftImage.gameObject.SetActive(true);

                float crackProgress = 0f;
                float breakProgress = 0f;
                float travelProgress = 0f;

                Sequence sequence =
                    DOTween.Sequence().SetUpdate(true);

                // ⑤ 中心からひび割れを発生させる。
                sequence.Insert(
                    0f,
                    DOTween.To(
                        () => crackProgress,
                        value =>
                        {
                            crackProgress = value;

                            fractureController.SetProgress(
                                crackProgress,
                                breakProgress);
                        },
                        1f,
                        crackDuration)
                    .SetEase(Ease.OutCubic));

                // ⑥ ガラスの破片を分離・飛散させる。
                sequence.Insert(
                    breakStartDelay,
                    DOTween.To(
                        () => breakProgress,
                        value =>
                        {
                            breakProgress = value;

                            fractureController.SetProgress(
                                crackProgress,
                                breakProgress);
                        },
                        1f,
                        breakDuration)
                    .SetEase(Ease.InOutSine));

                // ⑦ 3Dステージへ進入しながらドット化を強める。
                sequence.Insert(
                    cameraStartDelay,
                    DOTween.To(
                        () => travelProgress,
                        value =>
                        {
                            travelProgress = value;

                            // 開始地点から本編カメラの位置へ直接進む。
                            // MoveTo側で透視投影から平行投影へ変化させる。
                            previewCamera.MoveTo(travelProgress);

                            // カメラの進入量に応じてドット化を適用する。
                            UpdatePreviewPixelBlend(travelProgress);
                        },
                        1f,
                        enterDuration)
                    .SetEase(Ease.InOutCubic));

                activeTween = sequence;

                await sequence.ToUniTask(
                    cancellationToken: token);

                // ⑧ 最終状態を明示的に設定する。
                previewCamera.MoveTo(1f);
                UpdatePreviewPixelBlend(1f);

                fractureController.SetProgress(1f, 1f);

                // 破片が消えたらステージ映像のみ表示する。
                fractureController.StopFracture();

                // 最後に描画したプレビュー映像を保持する。
                previewCamera.StopRendering();

                // ⑨ プレビュー映像の背後で本編カメラを有効にする。
                entry.ActivateGameplay();

                committed = true;
                preloadService.CommitLoadedScene();

                // 本編カメラの描画結果が用意されるまで待つ。
                await UniTask.NextFrame(
                    cancellationToken: token);

                // ⑩ 手前のプレビュー映像を透明にする。
                // 奥には本編の平行投影画面が表示されている。
                riftImage.color = Color.white;

                activeTween = riftImage
                    .DOFade(0f, gameplayBlendDuration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true);

                await activeTween.ToUniTask(
                    cancellationToken: token);

                // ⑪ プレビュー用の描画資源を解放する。
                riftImage.gameObject.SetActive(false);
                riftImage.texture = null;

                previewCamera.Release();

                // 元の選択シーンを破棄する。
                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(sourceScene);
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄に伴うキャンセル。
                // OnDestroy側で生成済みの描画資源を解放する。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                activeTween?.Kill();

                if (committed)
                {
                    // 本編移行後は選択画面へ戻さない。
                    fractureController.StopFracture();

                    riftImage.gameObject.SetActive(false);
                    riftImage.texture = null;

                    previewCamera.Release();

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(sourceScene);
                    }
                }
                else
                {
                    // 本編移行前の失敗なら選択画面を復元する。
                    fractureController.StopFracture();
                    previewCamera.StopRendering();
                    previewCamera.Release();

                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(sourceScene);
                    }

                    RestoreMenuPresentation();
                    ResetPreviewPresentation();

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
        /// カメラの進入度に応じてドット化の強さを更新する。
        /// </summary>
        private void UpdatePreviewPixelBlend(float progress)
        {
            if (previewPixelMaterial == null)
            {
                return;
            }

            float start = Mathf.Clamp01(pixelBlendStart);

            float end = Mathf.Max(
                start + 0.001f,
                pixelBlendEnd);

            // 設定した区間で0から1へ変化させる。
            float blend = Mathf.Clamp01(
                (progress - start) / (end - start));

            blend = Mathf.SmoothStep(0f, 1f, blend);

            previewPixelMaterial.SetFloat(
                PIXEL_BLEND_ID,
                blend);
        }

        /// <summary>
        /// プレビュー画面の表示状態を初期化する。
        /// </summary>
        private void ResetPreviewPresentation()
        {
            if (riftImage != null)
            {
                riftImage.DOKill();
                riftImage.gameObject.SetActive(false);
                riftImage.texture = null;
                riftImage.color = Color.white;
            }

            if (previewPixelMaterial != null)
            {
                previewPixelMaterial.SetFloat(
                    PIXEL_BLEND_ID,
                    0f);
            }

            if (flashImage != null)
            {
                flashImage.gameObject.SetActive(false);
                flashImage.color = new Color(1f, 1f, 1f, 0f);
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

        /// <summary>
        /// 遷移失敗時に元の選択画面を復元する。
        /// </summary>
        private void RestoreMenuPresentation()
        {
            for (int i = 0; i < menuCanvases.Length; i++)
            {
                if (menuCanvases[i] != null && canvasStates != null)
                {
                    menuCanvases[i].enabled = canvasStates[i];
                }
            }

            for (int i = 0; i < menuCameras.Length; i++)
            {
                if (menuCameras[i] != null && cameraStates != null)
                {
                    menuCameras[i].enabled = cameraStates[i];
                }
            }

            for (int i = 0; i < menuListeners.Length; i++)
            {
                if (menuListeners[i] != null && listenerStates != null)
                {
                    menuListeners[i].enabled = listenerStates[i];
                }
            }

            for (int i = 0; i < menuEventSystems.Length; i++)
            {
                if (menuEventSystems[i] != null &&
                    eventSystemStates != null)
                {
                    menuEventSystems[i].enabled = eventSystemStates[i];
                }
            }

            SetMenuLights(true);
        }

        /// <summary>
        /// ステージ選択画面のライトを停止・復元する。
        /// </summary>
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
                {
                    menuLights[i].enabled = lightStates[i];
                }
            }
        }

        /// <summary>
        /// 選択したカードの中心を画面UV座標へ変換する。
        /// </summary>
        private static Vector2 GetScreenUV(RectTransform target)
        {
            Canvas canvas =
                target.GetComponentInParent<Canvas>();

            Camera eventCamera =
                canvas != null &&
                canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.rootCanvas.worldCamera
                    : null;

            Vector3 center =
                target.TransformPoint(target.rect.center);

            Vector2 screenPoint =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    center);

            return new Vector2(
                Mathf.Clamp01(
                    screenPoint.x / Mathf.Max(1, Screen.width)),
                Mathf.Clamp01(
                    screenPoint.y / Mathf.Max(1, Screen.height)));
        }

        /// <summary>
        /// 生成したTween・RenderTexture・Materialを解放する。
        /// </summary>
        private void OnDestroy()
        {
            activeTween?.Kill();

            if (riftImage != null)
            {
                riftImage.DOKill();
                riftImage.texture = null;
            }

            if (previewCamera != null)
            {
                previewCamera.Release();
            }

            if (menuSnapshot != null)
            {
                menuSnapshot.Release();
                Destroy(menuSnapshot);
                menuSnapshot = null;
            }

            if (previewPixelMaterial != null)
            {
                Destroy(previewPixelMaterial);
                previewPixelMaterial = null;
            }
        }
    }
}
