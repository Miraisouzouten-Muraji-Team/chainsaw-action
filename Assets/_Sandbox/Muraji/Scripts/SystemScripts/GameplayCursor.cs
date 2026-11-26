using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayCursor
{
    private const string Title_PATH   = "Assets/_Project/Scenes/TitleScene.unity";
    private const string STAGE_1_PATH = "Assets/_Project/Scenes/Stage1.unity";
    private const string STAGE_2_PATH = "Assets/_Project/Scenes/Stage2.unity";

    // 最初のシーンに表示状態を適用し、以降のシーン切り替えと終了を監視する。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Application.quitting -= RestoreCursorOnQuit;
        Application.quitting += RestoreCursorOnQuit;

        ApplySceneCursor(SceneManager.GetActiveScene());
    }

    // 切り替え先のシーンに合わせてカーソルの表示状態を更新する。
    private static void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        ApplySceneCursor(nextScene);
    }

    // プレイ用のシーンではカーソルを隠し、タイトルなどでは操作できる状態に戻す。
    private static void ApplySceneCursor(Scene scene)
    {
        bool isGameplay = scene.path == Title_PATH || scene.path== STAGE_1_PATH || scene.path == STAGE_2_PATH;

        if (!isGameplay)
            Cursor.lockState = CursorLockMode.None;

        Cursor.visible = !isGameplay;
    }

    // ゲーム終了時にカーソルを再表示し、登録したイベントを解除する。
    private static void RestoreCursorOnQuit()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Application.quitting -= RestoreCursorOnQuit;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
