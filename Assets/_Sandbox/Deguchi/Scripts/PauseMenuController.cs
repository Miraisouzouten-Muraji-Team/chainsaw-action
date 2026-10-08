using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum PauseMenuState
{
    Closed,
    Main,
    Settings,
    Confirm
}

public class PauseMenuController : MonoBehaviour
{
    [Header("Pause UI")]
    [SerializeField]
    private GameObject pauseVisualRoot;

    [SerializeField]
    private GameObject commandPanel;

    [SerializeField]
    private GameObject confirmPanel;

    [Header("Confirmation")]
    [SerializeField]
    private TMP_Text confirmMessageText;

    [SerializeField]
    private Button yesButton;

    [SerializeField]
    private Button noButton;

    [Header("Main Menu Buttons")]
    [SerializeField]
    private Button retryButton;

    [SerializeField]
    private Button settingsButton;

    [SerializeField]
    private Button selectButton;

    [SerializeField]
    private Button titleButton;

    [SerializeField]
    private Button closeButton;

    [Header("Options Menu")]
    [SerializeField]
    private OptionsMenuController optionsMenu;

    [Header("Scene Destinations")]
    [SerializeField]
    private string retrySceneName;

    [SerializeField]
    private string selectSceneName;

    [SerializeField]
    private string titleSceneName;

    public PauseMenuState State { get; private set; }
        = PauseMenuState.Closed;

    public bool IsPaused =>
        State != PauseMenuState.Closed;

    private string pendingSceneName;

    private GameObject lastSelectedMainButton;

    private void Start()
    {
        pauseVisualRoot.SetActive(false);
        confirmPanel.SetActive(false);
    }

    // -------------------------
    // ポーズ画面
    // -------------------------

    public void OpenPauseMenu()
    {
        if (IsPaused)
            return;

        PauseManager.Instance?.RequestPause(this);

        State = PauseMenuState.Main;

        pauseVisualRoot.SetActive(true);
        commandPanel.SetActive(true);
        confirmPanel.SetActive(false);

        Select(retryButton.gameObject);
    }

    public void ClosePauseMenu()
    {
        if (!IsPaused)
            return;

        if (optionsMenu != null)
        {
            optionsMenu.CloseFromPause();
        }

        State = PauseMenuState.Closed;

        pauseVisualRoot.SetActive(false);

        PauseManager.Instance?.ReleasePause(this);

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ESC / OPTIONS 用
    public void HandleMenuButton()
    {
        switch (State)
        {
            case PauseMenuState.Closed:
                OpenPauseMenu();
                break;

            case PauseMenuState.Main:
                ClosePauseMenu();
                break;

            case PauseMenuState.Settings:
                ReturnFromSettings();
                break;

            case PauseMenuState.Confirm:
                CancelConfirmation();
                break;
        }
    }

    // ○ボタン用
    public void HandleBackButton()
    {
        switch (State)
        {
            case PauseMenuState.Main:
                ClosePauseMenu();
                break;

            case PauseMenuState.Settings:
                ReturnFromSettings();
                break;

            case PauseMenuState.Confirm:
                CancelConfirmation();
                break;
        }
    }

    // -------------------------
    // 設定
    // -------------------------

    public void OpenSettings()
    {
        Debug.Log(
            $"設定ボタン押下 State={State}"
        );

        if (State != PauseMenuState.Main)
        {
            Debug.LogWarning(
                "StateがMainではないので設定画面を開けません"
            );

            return;
        }

        Debug.Log("設定画面を開きます");

        lastSelectedMainButton =
            settingsButton.gameObject;

        commandPanel.SetActive(false);
        pauseVisualRoot.SetActive(false);

        State = PauseMenuState.Settings;

        optionsMenu.OpenFromPause();
    }

    public void ReturnFromSettings()
    {
        if (State != PauseMenuState.Settings)
            return;

        optionsMenu.CloseFromPause();

        State = PauseMenuState.Main;

        pauseVisualRoot.SetActive(true);
        commandPanel.SetActive(true);
        confirmPanel.SetActive(false);

        Select(settingsButton.gameObject);
    }

    public void ShowPreviousSettingsTab()
    {
        if (State != PauseMenuState.Settings)
            return;

        if (optionsMenu == null)
            return;

        optionsMenu.ShowPreviousTab();
    }

    public void ShowNextSettingsTab()
    {
        if (State != PauseMenuState.Settings)
            return;

        if (optionsMenu == null)
            return;

        optionsMenu.ShowNextTab();
    }

    // -------------------------
    // リトライ
    // -------------------------

    public void OnRetryPressed()
    {
        OpenConfirmation(
            retrySceneName,
            "本当にリトライしますか？",
            retryButton.gameObject
        );
    }

    // -------------------------
    // セレクト
    // -------------------------

    public void OnSelectPressed()
    {
        OpenConfirmation(
            selectSceneName,
            "セレクト画面に戻りますか？",
            selectButton.gameObject
        );
    }

    // -------------------------
    // タイトル
    // -------------------------

    public void OnTitlePressed()
    {
        OpenConfirmation(
            titleSceneName,
            "タイトル画面に戻りますか？",
            titleButton.gameObject
        );
    }

    // -------------------------
    // とじる
    // -------------------------

    public void OnClosePressed()
    {
        ClosePauseMenu();
    }

    // -------------------------
    // 確認画面
    // -------------------------

    private void OpenConfirmation(
        string sceneName,
        string message,
        GameObject returnButton)
    {
        pendingSceneName = sceneName;

        lastSelectedMainButton =
            returnButton;

        State = PauseMenuState.Confirm;

        commandPanel.SetActive(false);
        confirmPanel.SetActive(true);

        confirmMessageText.text = message;

        // ★ 最初は「いいえ」を選択
        Select(noButton.gameObject);
    }

    public void ConfirmYes()
    {
        if (string.IsNullOrWhiteSpace(
                pendingSceneName))
        {
            Debug.LogError(
                "遷移先のScene名が設定されていません。"
            );

            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(
                pendingSceneName))
        {
            Debug.LogError(
                $"Scene '{pendingSceneName}' を読み込めません。" +
                "Build Settings / Build Profile に追加されているか確認してください。"
            );

            return;
        }

        SettingsManager.Instance?.Save();

        PauseManager.Instance?.ReleasePause(this);

        State = PauseMenuState.Closed;

        SceneManager.LoadScene(
            pendingSceneName
        );
    }

    public void CancelConfirmation()
    {
        if (State != PauseMenuState.Confirm)
            return;

        State = PauseMenuState.Main;

        confirmPanel.SetActive(false);
        commandPanel.SetActive(true);

        if (lastSelectedMainButton != null)
        {
            Select(lastSelectedMainButton);
        }
    }

    private void Select(GameObject target)
    {
        if (EventSystem.current == null ||
            target == null)
        {
            return;
        }

        EventSystem.current
            .SetSelectedGameObject(null);

        EventSystem.current
            .SetSelectedGameObject(target);
    }

    private void OnDisable()
    {
        if (!IsPaused)
            return;

        PauseManager.Instance
            ?.ReleasePause(this);

        State = PauseMenuState.Closed;
    }
}
