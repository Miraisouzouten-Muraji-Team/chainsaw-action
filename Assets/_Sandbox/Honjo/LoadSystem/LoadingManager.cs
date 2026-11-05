using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.InputSystem;

public class LoadingManager : MonoBehaviour
{
    [Header("ロードバー")]
    [SerializeField]
    Slider slider;

    [Header("表示テキスト")]
    [SerializeField]
    TextMeshProUGUI loadingText;

    [Tooltip("ロード中に表示される文字")]
    [SerializeField]
    string nowLoading = "NOW LOADING...";

    [Tooltip("ロード完了後に表示される文字")]
    [SerializeField]
    string loadingCompleted = "PRESS SPACE KEY";

    [Header("最低ロード時間")]
    [SerializeField]
    float minimumLoadingTime = 1f;

    [Header("ゲージ補間速度")]
    [SerializeField]
    float gaugeLerpSpeed = 8f;

    bool canTransition = false;

    IEnumerator Start()
    {
        // 初期表示
        if (loadingText) { loadingText.text = nowLoading; }

        if (slider)
        {
            // 念のため0開始
            slider.value = 0f;
        }

        yield return new WaitForSeconds(0.01f);

        // 非同期ロード開始
        AsyncOperation op =
            SceneManager.LoadSceneAsync(
                SceneLoader.NextSceneName
            );

        // 自動遷移停止
        op.allowSceneActivation = false;

        float timer = 0f;

        while (op.progress < 0.9f ||
               timer < minimumLoadingTime)
        {
            timer += Time.deltaTime;

            // 実際のロード進捗
            float realProgress =
                Mathf.Clamp01(op.progress / 0.9f);

            // 最低ロード時間ベース進捗
            float fakeProgress =
                timer / minimumLoadingTime;

            // 遅い方に合わせる
            float targetProgress =
                Mathf.Min(realProgress, fakeProgress);
            if (slider)
            {
                // なめらか補間
                slider.value =
                    Mathf.Lerp(
                        slider.value,
                        targetProgress,
                        gaugeLerpSpeed *
                        Time.deltaTime
                    );
            }


            yield return null;
        }

        if (slider)
        {
            // 最後まで埋める
            while (slider.value < 0.999f)
            {
                slider.value =
                    Mathf.Lerp(
                        slider.value,
                        1f,
                        gaugeLerpSpeed *
                        Time.deltaTime
                    );

                yield return null;
            }
            // 100%
            slider.value = 1f;
        }


        // テキスト変更
        if (loadingText) { loadingText.text = loadingCompleted; }


        canTransition = true;


        // シーン切り替え
        op.allowSceneActivation = true;
    }

    void Update()
    {
        if (!canTransition)
        {
            return;
        }
        if (!loadingText) { return; }

        // 点滅
        Color color = loadingText.color;

        color.a =
            Mathf.Abs(
                Mathf.Sin(Time.time * 3f)
            );

        loadingText.color = color;
    }
}
