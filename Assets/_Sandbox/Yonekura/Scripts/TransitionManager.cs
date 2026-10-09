using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;
using Cysharp.Threading.Tasks;

public class TransitionManager : MonoBehaviour
{
    // 次のシーンで使用するトランジション
    private static TransitionType nextTransitionType;
    private static bool hasNextTransition;

    public enum TransitionType
    {
        Fade,               //フェードアウト
        DiagonalWipe,       //斜めワイプ
        CircleWipe,         //円形ワイプ
        ZigzagWipe          //ジグザグワイプ
    }

    // トランジション用のUI
    [SerializeField] private Image fadeImage;
    [SerializeField] private RectTransform diagonalWipePanel;
    [SerializeField] private RectTransform circleWipePanel;
    [SerializeField] private ZigzagWipeGraphic zigzagWipePanel;

    // トランジションの設定
    [SerializeField] private float moveDistance = 3500f;
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private float circleWipeDuration = 1f;
    [SerializeField] private float zigzagWipeDuration = 0.5f;

    private void Start()
    {
        Debug.Log($"[TransitionManager] Start: {gameObject.scene.name}, InstanceID={GetInstanceID()}");

        // 最初は全部非表示
        HideAllTransitions();

        // 遷移前で選択されているトランジションのみ再生
        if (hasNextTransition)
        {
            hasNextTransition = false;
            PlayStartTransition().Forget();
        }
    }

    // すべてのトランジションを非表示
    private void HideAllTransitions()
    {
        if (fadeImage != null)
            fadeImage.gameObject.SetActive(false);

        if (diagonalWipePanel != null)
            diagonalWipePanel.gameObject.SetActive(false);

        if (circleWipePanel != null)
            circleWipePanel.gameObject.SetActive(false);

        if (zigzagWipePanel != null)
            zigzagWipePanel.gameObject.SetActive(false);
    }

    // シーン開始時のトランジション
    private async UniTask PlayStartTransition()
    {
        switch (nextTransitionType)
        {
            // フェード
            case TransitionType.Fade:
                if (fadeImage != null)
                {
                    // フェード用のImageを表示
                    fadeImage.gameObject.SetActive(true);

                    // 画面を完全に黒くした状態から開始
                    // アルファ値を1にすることで、黒画面へ
                    SetImageAlpha(fadeImage, 1f);

                    // 黒い画面から透明になるまでフェード
                    Tween tween = fadeImage
                        .DOFade(0f, duration)
                        .SetEase(Ease.InOutCubic);

                    // DOTweenの処理が終了するまで待機
                    await tween.ToUniTask();

                    // フェード終了時、Imageを非表示(普段の画面にフェード用Imageが表示されない)
                    fadeImage.gameObject.SetActive(false);
                }
                break;

            // 斜めワイプ
            case TransitionType.DiagonalWipe:
                if (diagonalWipePanel != null)
                {
                    // 斜めワイプ用のPanelを表示
                    diagonalWipePanel.gameObject.SetActive(true);

                    // Panelを画面中央の位置に移動させ、その後左方向へ移動
                    diagonalWipePanel.anchoredPosition = new Vector2(
                        0f,
                        diagonalWipePanel.anchoredPosition.y
                    );

                    // PanelをX方向に-moveDistanceまで移動
                    // Panelが画面外へ移動することで、遷移後の画面が見えるように
                    Tween tween = diagonalWipePanel
                        .DOAnchorPosX(-moveDistance, duration)
                        .SetEase(Ease.InOutCubic);

                    // Panelの移動が終了するまで待機
                    await tween.ToUniTask();

                    // ワイプ終了時、Panelを非表示
                    diagonalWipePanel.gameObject.SetActive(false);
                }
                break;

            // 円形ワイプ
            case TransitionType.CircleWipe:
                if (circleWipePanel != null)
                {
                    // 円形ワイプ用のPanelを表示
                    circleWipePanel.gameObject.SetActive(true);

                    // 画面全体を覆える大きさから開始し、徐々に縮小させる
                    circleWipePanel.localScale = Vector3.one * 30f;

                    // 大きな円を0.01まで縮小
                    // 円が小さくなることで、遷移後の画面が見えるように
                    Tween tween = circleWipePanel
                        .DOScale(Vector3.one * 0.01f, duration)
                        .SetEase(Ease.InOutCubic);

                    // 円の縮小が終了するまで待機
                    await tween.ToUniTask();

                    // ワイプ終了時、Panelを非表示
                    circleWipePanel.gameObject.SetActive(false);
                }
                break;

            // ジグザグワイプ
            case TransitionType.ZigzagWipe:
                if (zigzagWipePanel != null)
                {
                    zigzagWipePanel.gameObject.SetActive(true);

                    // 1から2まで動かして画面を開く
                    await AnimateZigzag(1f, 2f);

                    zigzagWipePanel.gameObject.SetActive(false);
                }
                break;
        }
    }

    // 指定したトランジションでシーンを切り替える
    public void TransitionToScene(
        string sceneName,
        TransitionType transitionType)
    {
        // 遷移後で使用するトランジションを保存
        // 遷移後、PlayStartTransition()で使用
        nextTransitionType = transitionType;

        // 遷移後、トランジションを再生することを記録
        // (遷移後のTransitionManagerは、遷移前で使用していたトランジションを自動では覚えていないため必要)
        hasNextTransition = true;

        // 選択されたトランジションによって処理を分ける
        switch (transitionType)
        {
            // フェード
            case TransitionType.Fade:
                Fade(sceneName).Forget();
                break;

            // 斜めワイプ
            case TransitionType.DiagonalWipe:
                DiagonalWipe(sceneName).Forget();
                break;

            // 円形ワイプ
            case TransitionType.CircleWipe:
                CircleWipe(sceneName).Forget();
                break;

            // ジグザグワイプ
            case TransitionType.ZigzagWipe:
                ZigzagWipe(sceneName).Forget();
                break;
        }
    }

    // ジグザグワイプの遷移前後の処理
    // 4本の帯を同じ時間・間隔で動かす
    private async UniTask AnimateZigzag(float startProgress, float endProgress)
    {
        // 帯ごとの開始間隔
        float[] startIntervals = { 0.02f, 0.06f, 0.06f };

        // すべての帯を開始位置に揃える
        zigzagWipePanel.SetAllProgress(startProgress);

        // 4本の完了を待つ処理
        UniTask[] tasks = new UniTask[4];

        for (int i = 0; i < 4; i++)
        {
            int index = i;

            Debug.Log(
                $"[Zigzag] 帯{index + 1} 開始: " +
                $"{Time.realtimeSinceStartup:F2}秒"
            );

            Tween tween = DOTween.To(
                () => zigzagWipePanel.GetBarProgress(index),
                value => zigzagWipePanel.SetBarProgress(index, value),
                endProgress,
                zigzagWipeDuration
            ).SetEase(Ease.Linear);

            tasks[index] = tween.ToUniTask();

            if (i < 3)
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(startIntervals[i]),
                    ignoreTimeScale: true
                );
            }
        }

        // 4本すべてのアニメーションが終わるまで待つ
        await UniTask.WhenAll(tasks);
    }

    // Imageのアルファ値を設定
    private void SetImageAlpha(Image image, float alpha)
    {
        image.color = new Color(
            image.color.r,
            image.color.g,
            image.color.b,
            alpha
        );
    }

    // フェードでシーン遷移
    private async UniTask Fade(string sceneName)
    {
        // フェード用のImageを表示
        fadeImage.gameObject.SetActive(true);

        // 最初は透明状態
        SetImageAlpha(fadeImage, 0f);

        // Imageを透明から不透明に
        Tween tween = fadeImage
            .DOFade(1f, duration)
            .SetEase(Ease.InOutCubic);

        await tween.ToUniTask();

        // 次のシーンを読み込む
        SceneManager.LoadScene(sceneName);
    }

    // 斜めワイプでシーン遷移
    private async UniTask DiagonalWipe(string sceneName)
    {
        // 斜めワイプ用のPanelを表示
        diagonalWipePanel.gameObject.SetActive(true);

        // Panelを画面外の左側に配置
        diagonalWipePanel.anchoredPosition = new Vector2(
            -moveDistance,
            diagonalWipePanel.anchoredPosition.y
        );

        // Panelを画面中央まで移動
        Tween tween = diagonalWipePanel
            .DOAnchorPosX(0f, duration)
            .SetEase(Ease.InOutCubic);

        await tween.ToUniTask();

        // 次のシーンを読み込む
        SceneManager.LoadScene(sceneName);
    }

    // 円形ワイプでシーン遷移
    private async UniTask CircleWipe(string sceneName)
    {
        // 円形ワイプ用のPanelを表示
        circleWipePanel.gameObject.SetActive(true);

        // 円を見えない大きさに
        circleWipePanel.localScale = Vector3.one * 0.01f;

        // 円を0.01から30まで拡大
        Tween tween = circleWipePanel
            .DOScale(Vector3.one * 30f, circleWipeDuration)
            .SetEase(Ease.InOutCubic);

        await tween.ToUniTask();

        // 次のシーンを読み込む
        SceneManager.LoadScene(sceneName);
    }

    // ジグザグワイプでシーン遷移
    private async UniTask ZigzagWipe(string sceneName)
    {
        if (zigzagWipePanel == null)
        {
            Debug.LogError("ZigzagWipePanelが設定されていません！");
            return;
        }

        zigzagWipePanel.gameObject.SetActive(true);

        // 0から1まで動かして画面を覆う
        await AnimateZigzag(0f, 1f);

        // 画面を覆った状態を維持してからシーンを切り替える
        await UniTask.NextFrame();

        SceneManager.LoadScene(sceneName);
    }
}
