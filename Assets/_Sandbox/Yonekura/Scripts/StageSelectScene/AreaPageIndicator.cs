using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// ステージエリアの現在位置を示すページインジケーターを管理する。
/// </summary>
public class AreaPageIndicator : MonoBehaviour
{
    [Header("ドット設定")]
    [Tooltip("ドット1個分のPrefab")]
    [SerializeField] private Image dotPrefab;

    [Tooltip("未選択時に使用する画像")]
    [SerializeField] private Sprite unselectedSprite;

    [Tooltip("選択時に使用する画像")]
    [SerializeField] private Sprite selectedSprite;

    [Header("サイズ設定")]
    [Tooltip("未選択時のサイズ")]
    [SerializeField] private float unselectedScale = 0.5f;

    [Tooltip("選択時の最大サイズ")]
    [SerializeField] private float peakScale = 1.0f;

    [Tooltip("アニメーション終了時のサイズ")]
    [SerializeField] private float selectedScale = 0.65f;

    [Header("アニメーション設定")]
    [Tooltip("拡大にかかる時間")]
    [SerializeField] private float expandDuration = 0.2f;

    [Tooltip("縮小にかかる時間")]
    [SerializeField] private float shrinkDuration = 0.25f;

    [Tooltip("アニメーションの滑らかさ")]
    [SerializeField] private Ease animationEase = Ease.InOutSine;

    private readonly List<Image> dots = new List<Image>();
    private readonly List<Tween> activeTweens = new List<Tween>();

    private int currentIndex = -1;

    /// <summary>
    /// エリア数に合わせてドットを生成する。
    /// </summary>
    public void Initialize(int areaCount)
    {
        ClearDots();

        for (int i = 0; i < areaCount; i++)
        {
            Image dot = Instantiate(dotPrefab, transform);

            dot.sprite = unselectedSprite;
            dot.rectTransform.localScale =
                Vector3.one * unselectedScale;

            dots.Add(dot);
            activeTweens.Add(null);
        }

        currentIndex = -1;
    }

    /// <summary>
    /// 選択中のエリア番号を設定する。
    /// </summary>
    /// <param name="index">選択するエリア番号。</param>
    /// <param name="animate">アニメーションを再生するか。</param>
    /// <param name="startDelay">アニメーション開始までの待機時間。</param>
    public void SetCurrentIndex(
        int index,
        bool animate = true,
        float startDelay = 0f)
    {
        if (index < 0 || index >= dots.Count)
            return;

        if (currentIndex == index)
            return;

        currentIndex = index;

        for (int i = 0; i < dots.Count; i++)
        {
            Image dot = dots[i];

            if (activeTweens[i] != null)
            {
                activeTweens[i].Kill();
                activeTweens[i] = null;
            }

            if (i == currentIndex)
            {
                dot.sprite = selectedSprite;

                if (animate)
                {
                    Sequence sequence = DOTween.Sequence();

                    sequence.AppendInterval(startDelay);

                    sequence.Append(
                        dot.rectTransform.DOScale(
                            peakScale, expandDuration
                        ).SetEase(animationEase)
                    );

                    sequence.Append(
                        dot.rectTransform.DOScale(
                            selectedScale, shrinkDuration
                        ).SetEase(animationEase)
                    );

                    activeTweens[i] = sequence;
                }
                else
                {
                    dot.rectTransform.localScale =
                        Vector3.one * selectedScale;
                }
            }
            else
            {
                dot.sprite = unselectedSprite;

                if (animate)
                {
                    Sequence sequence = DOTween.Sequence();

                    sequence.AppendInterval(startDelay);

                    sequence.Append(
                        dot.rectTransform.DOScale(
                            unselectedScale, shrinkDuration
                        ).SetEase(animationEase)
                    );

                    activeTweens[i] = sequence;
                }
                else
                {
                    dot.rectTransform.localScale =
                        Vector3.one * unselectedScale;
                }
            }
        }
    }

    private void ClearDots()
    {
        foreach (Tween tween in activeTweens)
        {
            tween?.Kill();
        }

        activeTweens.Clear();

        foreach (Image dot in dots)
        {
            if (dot != null)
                Destroy(dot.gameObject);
        }

        dots.Clear();
    }

    private void Awake()
    {
        // Tween管理用のリストを初期化する。
        activeTweens.Clear();
    }

    private void OnDestroy()
    {
        foreach (Tween tween in activeTweens)
        {
            tween?.Kill();
        }
    }
}
