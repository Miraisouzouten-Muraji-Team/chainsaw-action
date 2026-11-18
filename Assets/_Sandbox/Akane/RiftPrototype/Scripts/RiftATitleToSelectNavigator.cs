using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiftPrototypeA
{
    /// <summary>
    /// 方式A専用タイトル→ステージ選択の遷移。Singleで既に読み込んだ3Dを消さないためAdditiveを使用。
    /// タイトルのボタンからOpenStageSelect()を呼び出す。既存のTransitionManagerには触れない。
    /// </summary>
    public sealed class RiftATitleToSelectNavigator : MonoBehaviour
    {
        [SerializeField] private string stageSelectSceneName = "RiftA_StageSelect";
        private bool isChanging;

        public void OpenStageSelect()
        {
            if (isChanging) return;
            OpenStageSelectAsync().Forget();
        }

        private async UniTaskVoid OpenStageSelectAsync()
        {
            isChanging = true;
            Scene titleScene = gameObject.scene;
            try
            {
                if (!SceneManager.GetSceneByName(stageSelectSceneName).isLoaded)
                {
                    AsyncOperation operation = SceneManager.LoadSceneAsync(
                        stageSelectSceneName, LoadSceneMode.Additive);
                    if (operation == null)
                        throw new InvalidOperationException($"Scene Listにありません: {stageSelectSceneName}");
                    await operation.ToUniTask();
                }
                Scene selectScene = SceneManager.GetSceneByName(stageSelectSceneName);
                if (!selectScene.IsValid() || !selectScene.isLoaded ||
                    !SceneManager.SetActiveScene(selectScene))
                    throw new InvalidOperationException("方式A用ステージ選択シーンを有効化できません。");

                // タイトルだけを破棄。DontDestroyOnLoadの先行読み込み管理と追加ステージは残す。
                AsyncOperation unload = SceneManager.UnloadSceneAsync(titleScene);
                if (unload != null) await unload.ToUniTask();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                isChanging = false;
            }
        }
    }
}
