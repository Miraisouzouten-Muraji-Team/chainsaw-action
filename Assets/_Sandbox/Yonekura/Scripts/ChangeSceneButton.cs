using UnityEngine;
using UnityEngine.EventSystems;

public class ChangeSceneButton : MonoBehaviour
{
    // ボタンの動作
    public enum ButtonAction
    {
        ChangeScene,
        QuitGame
    }

    // ボタンの動作を設定
    [SerializeField] private ButtonAction action;

    // 遷移先のシーン
    [SerializeField] private Object targetScene;

    // フェード演出
    [SerializeField] private SceneFade sceneFade;

    // 終了確認パネル
    [SerializeField] private GameObject confirmPanel;

    // 「いいえ」ボタン
    [SerializeField] private GameObject noButton;

    // 終了ボタン
    [SerializeField] private GameObject closeButton;

    // ボタンが押されたときの処理
    public void ChangeScene()
    {
        // シーン変更の場合
        if (action == ButtonAction.ChangeScene)
        {
            ChangeToScene();
        }

        // ゲーム終了の場合
        else if (action == ButtonAction.QuitGame)
        {
            OpenConfirmPanel();
        }
    }

    // 終了確認パネルを開く
    private void OpenConfirmPanel()
    {
        if (confirmPanel == null)
        {
            Debug.LogError("ConfirmPanelが設定されていません！");
            return;
        }

        confirmPanel.SetActive(true);
        // 「いいえ」を選択状態にする
        StartCoroutine(SelectNoButton());
    }

    // 「いいえ」を選択
    private System.Collections.IEnumerator SelectNoButton()
    {
        yield return null;

        EventSystem.current.SetSelectedGameObject(noButton);
    }

    // 「いいえ」を押したとき
    public void NoButton()
    {
        confirmPanel.SetActive(false);
        EventSystem.current.SetSelectedGameObject(closeButton);
    }

    // シーン切り替え
    private void ChangeToScene()
    {
        if (targetScene == null)
        {
            Debug.LogError("遷移先のシーンが設定されていません！");
            return;
        }

        if (sceneFade == null)
        {
            Debug.LogError("SceneFadeが設定されていません！");
            return;
        }

        sceneFade.FadeToScene(targetScene.name);
    }

    // ゲーム終了
    public void QuitGame()
    {
        Debug.Log("ゲームを終了します");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
