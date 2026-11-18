using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RiftPrototypeA
{
    /// <summary>
    /// 方式A専用。2Dステージカードからリアルタイム3Dの亀裂へ移動し、本編へ切り替える。
    /// カード画像やRenderTextureのUVを拡大せず、3Dカメラそのものを移動させる。
    /// </summary>
    public sealed class RiftATransitionController : MonoBehaviour
    {
        private static readonly int PROGRESS_ID = Shader.PropertyToID("_Progress");
        private static readonly int CENTER_ID = Shader.PropertyToID("_Center");
        private static readonly int ASPECT_ID = Shader.PropertyToID("_Aspect");
        private static readonly int READY_ID = Shader.PropertyToID("_Ready");

        [Header("亀裂描画")]
        [SerializeField] private Shader riftShader;
        [SerializeField] private RawImage riftImage;
        [SerializeField] private Image flashImage;
        [SerializeField] private RiftAStageLoader stageLoader;
        [SerializeField] private RiftAPreviewCamera previewCamera;

        [Header("ステージ選択側（遷移時に無効化）")]
        [Tooltip("亀裂Canvasは含めない。")]
        [SerializeField] private Canvas[] menuCanvases = new Canvas[0];
        [SerializeField] private Camera[] menuCameras = new Camera[0];
        [Tooltip("選択画面のDirectional Light等。プレビュー3Dとの照明の重複を防ぐ。")]
        [SerializeField] private Light[] menuLights = new Light[0];
        [SerializeField] private AudioListener[] menuListeners = new AudioListener[0];
        [SerializeField] private EventSystem[] menuEventSystems = new EventSystem[0];

        [Header("時間（秒）")]
        [SerializeField, Min(0.05f)] private float crackDuration = 0.25f;
        [SerializeField, Min(0.1f)] private float enterDuration = 1.6f;
        [SerializeField, Min(0.01f)] private float flashInDuration = 0.12f;
        [SerializeField, Min(0.01f)] private float flashOutDuration = 0.24f;

        private Material materialInstance;
        private Tween activeTween;
        private bool[] menuLightStates;
        private bool isPlaying;

        public bool IsPlaying => isPlaying;

        private void Awake()
        {
            if (riftShader == null || riftImage == null || flashImage == null ||
                stageLoader == null || previewCamera == null)
            {
                Debug.LogError("RiftATransitionControllerの参照が不足しています。", this);
                enabled = false;
                return;
            }

            materialInstance = new Material(riftShader) { name = "RiftA_Material_Runtime" };
            riftImage.material = materialInstance;
            riftImage.texture = Texture2D.blackTexture;
            riftImage.color = Color.white;
            riftImage.raycastTarget = false;
            riftImage.gameObject.SetActive(false);
            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            flashImage.gameObject.SetActive(false);
        }

        /// <summary>失敗時のみonFailedを呼び出す。成功時には元のシーンを解放する。</summary>
        public void PlayTransition(string sceneName, RectTransform selectedImage, Action onFailed = null)
        {
            if (!enabled || isPlaying || selectedImage == null || string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("方式Aの遷移を開始できません。参照とシーン名を確認してください。", this);
                onFailed?.Invoke();
                return;
            }
            PlayTransitionAsync(sceneName, selectedImage, onFailed).Forget();
        }

        private async UniTaskVoid PlayTransitionAsync(
            string sceneName, RectTransform selectedImage, Action onFailed)
        {
            isPlaying = true;
            Scene sourceScene = gameObject.scene;
            CancellationToken token = this.GetCancellationTokenOnDestroy();
            bool committed = false;
            try
            {
                Vector2 center = GetScreenUV(selectedImage);
                materialInstance.SetVector(CENTER_ID, new Vector4(center.x, center.y, 0f, 0f));
                materialInstance.SetFloat(ASPECT_ID, (float)Screen.width / Mathf.Max(1, Screen.height));
                materialInstance.SetFloat(PROGRESS_ID, 0f);
                materialInstance.SetFloat(READY_ID, 0f);
                riftImage.texture = Texture2D.blackTexture;
                riftImage.gameObject.SetActive(true);

                // 先に亀裂だけ発生させ、その裏でステージをAdditiveで読み込む。
                float crackProgress = 0f;
                activeTween = DOTween.To(
                        () => crackProgress,
                        value =>
                        {
                            crackProgress = value;
                            SetProgress(value);
                        },
                        0.035f,
                        crackDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true);

                RiftAStageEntry entry = await stageLoader.LoadAsync(sceneName);
                token.ThrowIfCancellationRequested();
                if (entry.GameplayCamera == null)
                    throw new InvalidOperationException("RiftAStageEntryのGameplayCameraが未設定です。");

                // アクティブシーンのRenderSettings等を選択ステージ側へ合わせる。
                if (!SceneManager.SetActiveScene(stageLoader.LoadedScene))
                    throw new InvalidOperationException("遷移先をアクティブシーンに設定できません。");

                SetMenuLights(false);
                activeTween?.Kill();
                previewCamera.Prepare(entry);
                // 最低1回はカメラのRenderTextureへの描画が行われるまで待つ。
                await UniTask.NextFrame(cancellationToken: token);
                riftImage.texture = previewCamera.RenderTexture;
                materialInstance.SetFloat(READY_ID, 1f);

                float openProgress = materialInstance.GetFloat(PROGRESS_ID);
                float cameraProgress = 0f;
                Sequence sequence = DOTween.Sequence();
                sequence.Join(DOTween.To(
                    () => openProgress,
                    value =>
                    {
                        openProgress = value;
                        SetProgress(value);
                    }, 1f, enterDuration));
                sequence.Join(DOTween.To(
                    () => cameraProgress,
                    value =>
                    {
                        cameraProgress = value;
                        previewCamera.MoveTo(value);
                    }, 1f, enterDuration));
                sequence.SetEase(Ease.InCubic).SetUpdate(true);
                activeTween = sequence;
                await sequence.ToUniTask(cancellationToken: token);

                // 通常のOrthographic表示との投影差はフラッシュ中に切り替える。
                flashImage.gameObject.SetActive(true);
                flashImage.color = new Color(1f, 1f, 1f, 0f);
                activeTween = flashImage.DOFade(1f, flashInDuration).SetUpdate(true);
                await activeTween.ToUniTask(cancellationToken: token);

                previewCamera.StopRendering();
                riftImage.gameObject.SetActive(false);
                foreach (Canvas canvas in menuCanvases)
                    if (canvas != null) canvas.enabled = false;
                foreach (Camera camera in menuCameras)
                    if (camera != null) camera.enabled = false;
                foreach (AudioListener listener in menuListeners)
                    if (listener != null) listener.enabled = false;
                foreach (EventSystem eventSystem in menuEventSystems)
                    if (eventSystem != null) eventSystem.enabled = false;

                entry.ActivateGameplay();
                committed = true;

                activeTween = flashImage.DOFade(0f, flashOutDuration).SetUpdate(true);
                await activeTween.ToUniTask(cancellationToken: token);

                // 遷移完了後に元のステージ選択シーンを破棄する。自分も破棄される。
                AsyncOperation unload = SceneManager.UnloadSceneAsync(sourceScene);
                if (unload == null)
                    Debug.LogWarning("方式Aのステージ選択シーンの解放に失敗しました。", this);
            }
            catch (OperationCanceledException)
            {
                // 元のシーンが破棄された場合。Unityのロードそのものはキャンセルしていない。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (committed)
                {
                    // 切替後の後処理に失敗しても、画面を覆ったままにしない。
                    if (flashImage != null) flashImage.gameObject.SetActive(false);
                    SceneManager.UnloadSceneAsync(sourceScene);
                }
                else
                {
                    activeTween?.Kill();
                    previewCamera.StopRendering();
                    previewCamera.Release();
                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                        SceneManager.SetActiveScene(sourceScene);
                    SetMenuLights(true);
                    try { await stageLoader.UnloadAsync(); }
                    catch (Exception unloadError) { Debug.LogException(unloadError, this); }
                    if (riftImage != null) riftImage.gameObject.SetActive(false);
                    if (flashImage != null) flashImage.gameObject.SetActive(false);
                    onFailed?.Invoke();
                }
            }
            finally
            {
                if (!committed) isPlaying = false;
            }
        }

        private void SetProgress(float value)
        {
            if (materialInstance != null) materialInstance.SetFloat(PROGRESS_ID, Mathf.Clamp01(value));
        }

        private void SetMenuLights(bool restore)
        {
            if (!restore)
            {
                menuLightStates = new bool[menuLights.Length];
                for (int i = 0; i < menuLights.Length; i++)
                {
                    if (menuLights[i] == null) continue;
                    menuLightStates[i] = menuLights[i].enabled;
                    menuLights[i].enabled = false;
                }
                return;
            }
            if (menuLightStates == null) return;
            for (int i = 0; i < menuLights.Length; i++)
                if (menuLights[i] != null) menuLights[i].enabled = menuLightStates[i];
        }

        private static Vector2 GetScreenUV(RectTransform target)
        {
            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null &&
                                 canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.rootCanvas.worldCamera : null;
            Vector3 center = target.TransformPoint(target.rect.center);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, center);
            return new Vector2(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1, Screen.height)));
        }

        private void OnDestroy()
        {
            activeTween?.Kill();
            if (materialInstance != null) Destroy(materialInstance);
        }
    }
}
