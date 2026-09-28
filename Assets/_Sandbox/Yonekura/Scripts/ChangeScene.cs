using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class ChangeScene:MonoBehaviour
{
    // 確認画面
    public GameObject ConfirmPanel;
    public GameObject NoButton;
    public GameObject CloseButton;

    // はじめから
    public void Startbutton()
    {
        SceneManager.LoadScene("Stage1Scene");
    }

    // ステージセレクト
    public void Selectbutton()
    {
        SceneManager.LoadScene("StageSelectScene");
    }

    // 設定
    public void Settingbutton()
    {
        SceneManager.LoadScene("SettingScene");
    }

    //終了
    public void OpenConfirmPanel()
    {
        ConfirmPanel.SetActive(true);

        // 「いいえ」を選択状態にする
        StartCoroutine(SelectNoButton());
    }

    // 「いいえ」を選択
    private System.Collections.IEnumerator SelectNoButton()
    {
        yield return null;

        EventSystem.current.SetSelectedGameObject(NoButton);
    }

    // いいえを押したとき
    public void Nobutton()
    {
        ConfirmPanel.SetActive(false);
        EventSystem.current.SetSelectedGameObject(CloseButton);
    }

    //終了を選択
    private System.Collections.IEnumerator SelectCloseButton()
    {
        yield return null;

        EventSystem.current.SetSelectedGameObject(CloseButton);
    }

    // はいを押したとき
    public void Yesbutton()
    {
        Application.Quit();
    }

}


