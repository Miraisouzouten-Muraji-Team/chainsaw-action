using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiftPrototypeA
{
    /// <summary>
    /// タイトルからステージ選択まで生存する方式A専用の先行読み込み管理。
    /// 読み込みは一つずつ行い、選択変更時は最後に要求されたステージを優先する。
    /// </summary>
    [RequireComponent(typeof(RiftAStageLoader))]
    public sealed class RiftAStagePreloadService : MonoBehaviour
    {
        private const int PRELOAD_DEBOUNCE_MS = 200;
        private static RiftAStagePreloadService instance;

        private RiftAStageLoader stageLoader;
        private string requestedSceneName;
        private string lockedSceneName;
        private string readySceneName;
        private string failedSceneName;
        private Exception lastFailure;
        private RiftAStageEntry readyEntry;
        private bool isProcessing;
        private bool isBusy;
        private bool isCommitted;

        public static RiftAStagePreloadService Instance => instance;
        public bool IsBusy => isBusy;
        public string ReadySceneName => readySceneName;
        public Scene LoadedScene => stageLoader != null ? stageLoader.LoadedScene : default;

        /// <summary>タイトルから起動し、シーン遷移をまたいで保持する。</summary>
        public static RiftAStagePreloadService GetOrCreate()
        {
            if (instance != null) return instance;
            GameObject root = new GameObject("RiftA_PreloadService");
            root.AddComponent<RiftAStageLoader>();
            RiftAStagePreloadService service = root.AddComponent<RiftAStagePreloadService>();
            DontDestroyOnLoad(root);
            return service;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            stageLoader = GetComponent<RiftAStageLoader>();
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>選択中のステージを先行読み込みする。何度呼んでも同じシーンは再読込しない。</summary>
        public void RequestPreload(string sceneName)
        {
            if (isCommitted || string.IsNullOrWhiteSpace(sceneName)) return;
            if (!string.IsNullOrEmpty(lockedSceneName) && lockedSceneName != sceneName) return;
            if (readySceneName == sceneName && readyEntry != null && !isBusy)
            {
                // 他のカードへの変更予約が残っていても、確定ステージを優先する。
                requestedSceneName = null;
                return;
            }

            requestedSceneName = sceneName;
            if (failedSceneName == sceneName)
            {
                failedSceneName = null;
                lastFailure = null;
            }
            if (!isProcessing) ProcessRequestsAsync().Forget();
        }

        /// <summary>遷移が確定したシーンを固定し、先行読み込み完了まで待つ。</summary>
        public async UniTask<RiftAStageEntry> AcquireForTransitionAsync(
            string sceneName, CancellationToken cancellationToken)
        {
            if (isCommitted) throw new InvalidOperationException("既にゲーム画面へ引き渡しています。");
            if (!string.IsNullOrEmpty(lockedSceneName) && lockedSceneName != sceneName)
                throw new InvalidOperationException("別ステージへの遷移が進行中です。");

            lockedSceneName = sceneName;
            RequestPreload(sceneName);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!isBusy && readySceneName == sceneName && readyEntry != null &&
                    LoadedScene.IsValid() && LoadedScene.isLoaded)
                    return readyEntry;
                if (!isBusy && !isProcessing && failedSceneName == sceneName)
                    throw new InvalidOperationException($"先行読み込みに失敗しました: {sceneName}", lastFailure);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        /// <summary>選択画面に戻すときの固定解除。</summary>
        public void ReleaseTransitionLock()
        {
            if (!isCommitted) lockedSceneName = null;
        }

        /// <summary>本編へ引き渡す。シーン自体は破棄せず、保持サービスだけを破棄する。</summary>
        public void CommitLoadedScene()
        {
            if (isCommitted || readyEntry == null) return;
            isCommitted = true;
            stageLoader.DetachLoadedScene();
            readyEntry = null;
            readySceneName = null;
            Destroy(gameObject);
        }

        private async UniTaskVoid ProcessRequestsAsync()
        {
            isProcessing = true;
            try
            {
                while (!string.IsNullOrEmpty(requestedSceneName) && !isCommitted)
                {
                    string sceneName = requestedSceneName;
                    // 連続してカードを移動しているときの無駄な読み込みを抑える。
                    await UniTask.Delay(PRELOAD_DEBOUNCE_MS, ignoreTimeScale: true,
                        cancellationToken: this.GetCancellationTokenOnDestroy());
                    if (sceneName != requestedSceneName) continue;
                    requestedSceneName = null;
                    if (readySceneName == sceneName && readyEntry != null) continue;

                    isBusy = true;
                    try
                    {
                        readyEntry = null;
                        readySceneName = null;
                        await stageLoader.UnloadAsync();
                        readyEntry = await stageLoader.LoadAsync(sceneName);
                        readySceneName = sceneName;
                        failedSceneName = null;
                        lastFailure = null;
                        Debug.Log($"[RiftA] 先行読み込み完了: {sceneName}", this);
                    }
                    catch (Exception exception)
                    {
                        failedSceneName = sceneName;
                        lastFailure = exception;
                        Debug.LogException(exception, this);
                        try { await stageLoader.UnloadAsync(); }
                        catch (Exception unloadException) { Debug.LogException(unloadException, this); }
                    }
                    finally
                    {
                        isBusy = false;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                isProcessing = false;
                if (!isCommitted && !string.IsNullOrEmpty(requestedSceneName) && this != null)
                    ProcessRequestsAsync().Forget();
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
