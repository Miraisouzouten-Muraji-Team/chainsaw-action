using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class SceneFade : MonoBehaviour
{
    // フェードに使用するPanel
    [SerializeField] private Image fadeImage;

    // フェードにかける時間
    [SerializeField] private float fadeDuration = 0.5f;

    // シーンを切り替える
    public void FadeToScene(string sceneName)
    {
        // Panelを透明から黒にする
        fadeImage.DOFade(1f, fadeDuration)
            .OnComplete(() =>
            {
                // フェードが終わったらシーンを切り替える
                SceneManager.LoadScene(sceneName);
            });
    }
}

