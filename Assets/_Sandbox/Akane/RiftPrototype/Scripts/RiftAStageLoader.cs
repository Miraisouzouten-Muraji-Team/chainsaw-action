using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiftPrototypeA
{
    /// <summary>
    /// 方式A専用。Additiveで遷移先を読み込み、ステージ入口を取得する。
    /// LoadSceneAsyncの開始後はUnityの読み込み処理を直接キャンセルできないため、完了まで待つ。
    /// </summary>
    public sealed class RiftAStageLoader : MonoBehaviour
    {
        private static string previewSceneName;
        private Scene loadedScene;
        private bool isLoading;

        public Scene LoadedScene => loadedScene;

        /// <summary>読み込まれるステージのAwakeから参照する。</summary>
        public static bool IsPreviewLoad(string sceneName)
        {
            return string.Equals(previewSceneName, sceneName, StringComparison.Ordinal);
        }

        public async UniTask<RiftAStageEntry> LoadAsync(string sceneName)
        {
            if (isLoading || (loadedScene.IsValid() && loadedScene.isLoaded))
                throw new InvalidOperationException("方式Aの読み込みが既に進行中です。");
            if (SceneManager.GetSceneByName(sceneName).isLoaded)
                throw new InvalidOperationException($"遷移先は既に読み込まれています: {sceneName}");
            if (!string.IsNullOrEmpty(previewSceneName))
                throw new InvalidOperationException("別のプレビューの読み込みが進行中です。");

            isLoading = true;
            previewSceneName = sceneName;
            try
            {
                // 描画するにはシーンの有効化が必要。allowSceneActivation=falseでは止めない。
                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (operation == null)
                    throw new InvalidOperationException($"Scene Listにシーンがありません: {sceneName}");

                // キャンセルによる読み込み中の取り残しを防ぐため、ここは完了まで待つ。
                await operation.ToUniTask();
                loadedScene = SceneManager.GetSceneByName(sceneName);
            }
            finally
            {
                previewSceneName = null;
                isLoading = false;
            }

            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                throw new InvalidOperationException($"読み込み後のシーンを取得できません: {sceneName}");

            foreach (GameObject root in loadedScene.GetRootGameObjects())
            {
                RiftAStageEntry entry = root.GetComponentInChildren<RiftAStageEntry>(true);
                if (entry != null) return entry;
            }

            throw new InvalidOperationException($"{sceneName}にRiftAStageEntryがありません。");
        }

        /// <summary>本編への引き渡し後、サービスによる所有だけを解除する。</summary>
        public void DetachLoadedScene()
        {
            if (isLoading) throw new InvalidOperationException("読み込み中には引き渡せません。");
            loadedScene = default;
        }

        public async UniTask UnloadAsync()
        {
            if (!loadedScene.IsValid() || !loadedScene.isLoaded) return;
            AsyncOperation operation = SceneManager.UnloadSceneAsync(loadedScene);
            if (operation != null) await operation.ToUniTask();
            loadedScene = default;
        }
    }
}
