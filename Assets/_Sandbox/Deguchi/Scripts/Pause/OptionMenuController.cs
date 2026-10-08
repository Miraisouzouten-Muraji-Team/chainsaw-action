using UnityEngine;
using UnityEngine.EventSystems;

public class OptionsMenuController : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField]
    private GameObject menuVisualRoot;

    [Header("Pages")]
    [SerializeField]
    private GameObject gamePage;

    [SerializeField]
    private GameObject controllerPage;

    [Header("Tab Indicators")]
    [SerializeField]
    private GameObject gameTabIndicator;

    [SerializeField]
    private GameObject controllerTabIndicator;

    [Header("First Selected")]
    [SerializeField]
    private GameObject gameFirstSelected;

    [SerializeField]
    private GameObject controllerFirstSelected;

    private int currentTab = 0;

    public int CurrentTab => currentTab;

    private void Start()
    {
        if (menuVisualRoot != null)
        {
            menuVisualRoot.SetActive(false);
        }
    }

    // ポーズ画面から設定を開く
    public void OpenFromPause()
    {
        if (menuVisualRoot == null)
            return;

        menuVisualRoot.SetActive(true);

        ShowGameTab();
    }

    // 設定を閉じてポーズ画面へ戻るとき
    public void CloseFromPause()
    {
        SettingsManager.Instance?.Save();

        if (menuVisualRoot != null)
        {
            menuVisualRoot.SetActive(false);
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void ShowGameTab()
    {
        currentTab = 0;

        if (gamePage != null)
            gamePage.SetActive(true);

        if (controllerPage != null)
            controllerPage.SetActive(false);

        if (gameTabIndicator != null)
            gameTabIndicator.SetActive(true);

        if (controllerTabIndicator != null)
            controllerTabIndicator.SetActive(false);

        SelectObject(gameFirstSelected);
    }

    public void ShowControllerTab()
    {
        currentTab = 1;

        if (gamePage != null)
            gamePage.SetActive(false);

        if (controllerPage != null)
            controllerPage.SetActive(true);

        if (gameTabIndicator != null)
            gameTabIndicator.SetActive(false);

        if (controllerTabIndicator != null)
            controllerTabIndicator.SetActive(true);

        SelectObject(controllerFirstSelected);
    }

    // R1
    public void ShowNextTab()
    {
        if (currentTab == 0)
        {
            ShowControllerTab();
        }
        else
        {
            ShowGameTab();
        }
    }

    // L1
    public void ShowPreviousTab()
    {
        // 今は2タブなのでNextと同じ切り替えでOK
        if (currentTab == 0)
        {
            ShowControllerTab();
        }
        else
        {
            ShowGameTab();
        }
    }

    public void SelectCurrentTabFirstItem()
    {
        if (currentTab == 0)
        {
            SelectObject(gameFirstSelected);
        }
        else
        {
            SelectObject(controllerFirstSelected);
        }
    }

    private void SelectObject(GameObject target)
    {
        if (EventSystem.current == null ||
            target == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(target);
    }
}