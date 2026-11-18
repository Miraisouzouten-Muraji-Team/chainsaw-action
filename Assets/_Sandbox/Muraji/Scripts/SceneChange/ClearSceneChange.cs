
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ClearSceneChange : MonoBehaviour
{
    [Header("シーン設定")]
    [SerializeField]
    private string nextSceneName;

    [Header("フェード設定")]
    [SerializeField]
    private CanvasGroup fadeCanvasGroup;

    [SerializeField, Min(0f)]
    private float fadeDuration = 3f;

    private bool isChangingScene;

    private LoadScene ls = null;

    [Header("無効化するBehaviour")]
    [SerializeField]
    private List<Behaviour> behaviours = new List<Behaviour>();

    static bool startPlayFead = false;

    private void Awake()
    {
        ls = GetComponent<LoadScene>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isChangingScene)
            return;

        if (other.CompareTag("Player"))
        {
            isChangingScene = true;

            // 登録されたBehaviourを無効化
            DisableBehaviours();

            StartCoroutine(ChangeScene());
        }
    }

    /// <summary>
    /// 登録されたBehaviourを無効化する
    /// </summary>
    private void DisableBehaviours()
    {
        if (behaviours == null)
            return;

        foreach (var behaviour in behaviours)
        {
            // 未設定の要素はスキップ
            if (behaviour == null)
                continue;

            behaviour.enabled = false;
        }
    }

    /// <summary>
    /// シーン遷移とフェードアウト
    /// </summary>
    private IEnumerator ChangeScene()
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogError("FadeCanvasGroupが設定されていません。");
            yield break;
        }

        // Canvasを表示状態にする
        fadeCanvasGroup.gameObject.SetActive(true);
        fadeCanvasGroup.alpha = 0f;

        // フェードアウト
        float elapsedTime = 0f;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;

            fadeCanvasGroup.alpha =
                Mathf.Clamp01(elapsedTime / fadeDuration);

            yield return null;
        }

        // フェード完了
        fadeCanvasGroup.alpha = 1f;

        startPlayFead = true;

        // 次のシーンへ遷移
        if (ls != null)
        {
            ls.LoadSceneNext(nextSceneName);
        }
        else
        {
            Debug.LogError("LoadSceneコンポーネントが見つかりません。");
        }
    }

    private void Start()
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogError("FadeCanvasGroupが設定されていません。");
            return;
        }

        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// シーン開始時のフェードイン
    /// </summary>
    private IEnumerator FadeIn()
    {
        // Canvasを不透明な状態にする
        fadeCanvasGroup.alpha = 1f;

        float elapsedTime = 0f;

        // フェードイン
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;

            fadeCanvasGroup.alpha =
                1f - Mathf.Clamp01(elapsedTime / fadeDuration);

            yield return null;
        }

        // フェード完了
        fadeCanvasGroup.alpha = 0f;
    }
}
