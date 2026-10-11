using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using R3;

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

    // シーン遷移演出
    [SerializeField] private TransitionManager transitionManager;

    // シーン遷移の種類
    [SerializeField] private TransitionManager.TransitionType transitionType;

    // 終了確認パネル
    [SerializeField] private GameObject confirmPanel;

    // 「いいえ」ボタン
    [SerializeField] private Button noButton;

    // 「はい」ボタン
    [SerializeField] private Button yesButton;

    // 終了ボタン
    [SerializeField] private Button closeButton;

    private Button button;

    // ボタンを取得
    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void Start()
    {
        // このボタンが押されたら処理する
        button
            .OnClickAsObservable()
            .Subscribe(_ => ChangeScene())
            .AddTo(this);

        // 「いいえ」ボタンが押されたら処理する
        if (noButton != null)
        {
            noButton
                .OnClickAsObservable()
                .Subscribe(_ => NoButton())
                .AddTo(this);
        }
        //「はい」ボタンが押されたら処理する
        if (yesButton != null)
        {
            yesButton
                .OnClickAsObservable()
                .Subscribe(_ => QuitGame())
                .AddTo(this);
        }
    }

    // ボタンが押されたときの処理
    private void ChangeScene()
    {
        // シーン変更の場合
        if (action == ButtonAction.ChangeScene)
        {
            ChangeToScene();
        }

        // ゲーム終了の場合
        else if (action == ButtonAction.QuitGame)
        {
            OpenConfirmPanel().Forget();
        }
    }

    // 終了確認パネルを開く
    private async UniTask OpenConfirmPanel()
    {
        if (confirmPanel == null)
        {
            Debug.LogError("ConfirmPanelが設定されていません！");
            return;
        }

        confirmPanel.SetActive(true);

        // 1フレーム待つ
        await UniTask.Yield();

        // 「いいえ」を選択状態にする
        EventSystem.current.SetSelectedGameObject(noButton.gameObject);
    }

    // 「いいえ」を押したとき
    private async void NoButton()
    {
        confirmPanel.SetActive(false);

        // 1フレーム待つ
        await UniTask.Yield();

        // 「閉じる」を選択状態にする
        EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
    }

    // シーン切り替え
    private void ChangeToScene()
    {
        if (targetScene == null)
        {
            Debug.LogError("遷移先のシーンが設定されていません！");
            return;
        }

        if (transitionManager == null)
        {
            Debug.LogError("TransitionManagerが設定されていません！");
            return;
        }

        transitionManager.TransitionToScene(
            targetScene.name,
            transitionType
        );
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
