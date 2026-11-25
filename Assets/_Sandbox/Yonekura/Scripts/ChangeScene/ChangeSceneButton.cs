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

    [Header("ボタン設定")]
    [Tooltip("ボタンを押したときの処理設定。")]
    [SerializeField] private ButtonAction action;

    [Header("シーン遷移設定")]
    [Tooltip("遷移先のシーン名を設定してください。")]
    //[SerializeField] private Object targetScene;
    [SerializeField] private string targetSceneName;

    [Tooltip("シーン遷移時に使用するTransitionManagerを設定してください。")]
    [SerializeField] private TransitionManager transitionManager;

    [Tooltip("シーン遷移時に使用するトランジションを設定してください。")]
    [SerializeField] private TransitionManager.TransitionType transitionType;

    [Header("終了確認パネル設定")]
    [Space(-8)]
    [Header("(ボタン設定がシーン遷移の場合は設定不要)")]
    [Tooltip("終了確認パネルを開くボタンを設定してください。")]
    [SerializeField] private Button closeButton;

    [Tooltip("ゲーム終了時に表示する確認パネルを設定してください。")]
    [SerializeField] private GameObject confirmPanel;

    [Tooltip("終了確認パネルの「いいえ」ボタンを設定してください。")]
    [SerializeField] private Button noButton;

    [Tooltip("終了確認パネルの「はい」ボタンを設定してください。")]
    [SerializeField] private Button yesButton;

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
        Debug.Log(
        $"[ChangeSceneButton] シーン遷移開始: {gameObject.scene.name}"
        );

        if (targetSceneName == null)
        {
            Debug.LogError("遷移先のシーンが設定されていません！");
            return;
        }

        if (transitionManager == null)
        {
            Debug.LogError("TransitionManagerが設定されていません！");
            return;
        }

        Debug.Log(
        $"[ChangeSceneButton] TransitionType: {transitionType}"
        );

        Debug.Log(
            $"[ChangeSceneButton] TransitionManager: " +
            $"{transitionManager.gameObject.name}"
        );

        transitionManager.TransitionToScene(
            targetSceneName,
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
