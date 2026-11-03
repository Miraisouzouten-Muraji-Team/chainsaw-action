using System.Collections;
using System.Collections.Generic;
using Unity.VectorGraphics;
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

    LoadScene ls = null;

    [SerializeField]
    List<Behaviour> behaviours = null;

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

            Debug.Log("Player has reached the goal!");

            foreach (var behaviour in behaviours)
            {
                behaviour.enabled = false;
            }
            StartCoroutine(ChangeScene());
            
        }
    }

    private IEnumerator ChangeScene()
    {
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
        ls.LoadSceneNext(nextSceneName);
    }


    private void Start()
    {
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        // Canvasを不透明な状態にする
        //fadeCanvasGroup.gameObject.SetActive(true);
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

        // フェード用Canvasを非表示にする
        //fadeCanvasGroup.gameObject.SetActive(false);
    }
}
