using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    // 次に読み込むシーン
    public static string NextSceneName;

    /// <summary>
    /// ロード画面経由でシーン遷移
    /// </summary>
    public static void Load(string sceneName)
    {
        NextSceneName = sceneName;

        // 遷移先の存在確認
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"遷移先シーン '{sceneName}' がBuild Profileに登録されていません。");
            return;
        }

        // ロード画面の存在確認
        if (Application.CanStreamedLevelBeLoaded("LoadScene"))
        {
            SceneManager.LoadScene("LoadScene");
        }
        else
        {
            Debug.LogWarning(
                "LoadSceneがBuild Profileに登録されていないため直接遷移します。");

            SceneManager.LoadScene(sceneName);
        }
    }
}
