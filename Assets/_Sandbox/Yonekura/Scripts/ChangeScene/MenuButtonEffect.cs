using R3;
using R3.Triggers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// メニューボタン選択時の拡大・縮小と透明度を制御する。
/// </summary>
public class MenuButtonEffect : MonoBehaviour
{
    // ボタンの元の大きさ
    private Vector3 originalScale;

    // ボタンの文字
    private TextMeshProUGUI text;

    // アニメーション管理
    private Tween scaleTween;
    private Tween alphaTween;

    [Header("拡大・縮小設定")]
    [SerializeField, Tooltip("選択時の拡大倍率")]
    private float selectedScale = 1.1f;

    [SerializeField, Tooltip("選択時の拡大時間")]
    private float selectDuration = 0.2f;

    [SerializeField, Tooltip("選択解除時の縮小時間")]
    private float deselectDuration = 0.15f;

    [SerializeField, Tooltip("拡大・縮小の速度変化")]
    private AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 3f, 3f),
        new Keyframe(0.8f, 0.96f, 0.2f, 0.2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [Header("テキスト透明度設定")]
    [Tooltip("非選択時の透明度")]
    [SerializeField]
    private float normalTextAlpha = 0.5f;

    [SerializeField]
    [Tooltip("選択時の透明度")]
    private float selectedTextAlpha = 1.0f;

    [Header("選択マーク")]
    [Tooltip("選択時のマークを設定してください。")]
    [SerializeField]
    private GameObject selectMark;

    // 初期設定
    private void Start()
    {
        originalScale = transform.localScale;

        // 子オブジェクトのTextを自動取得
        text = GetComponentInChildren<TextMeshProUGUI>();

        // 初期状態を設定
        transform.localScale = originalScale;

        if (text != null)
        {
            Color color = text.color;
            color.a = normalTextAlpha;
            text.color = color;
        }

        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }

        // ボタンが選択されたとき
        GetComponent<Selectable>()
            .OnSelectAsObservable()
            .Subscribe(_ => OnSelect())
            .AddTo(this);

        // ボタンの選択が解除されたとき
        GetComponent<Selectable>()
            .OnDeselectAsObservable()
            .Subscribe(_ => OnDeselect())
            .AddTo(this);
    }

    // ボタンが選択されたとき
    private void OnSelect()
    {
        // 再生中のアニメーションを停止
        scaleTween?.Kill();
        alphaTween?.Kill();

        // 拡大アニメーション
        scaleTween = transform.DOScale(
            originalScale * selectedScale,
            selectDuration
        )
        .SetEase(scaleCurve)
        .SetUpdate(true);

        // テキストを明るくする
        if (text != null)
        {
            alphaTween = text.DOFade(
                selectedTextAlpha,
                selectDuration
            )
            .SetUpdate(true);
        }

        // 選択マークを表示
        if (selectMark != null)
        {
            selectMark.SetActive(true);
        }
    }

    // ボタンの選択が解除されたとき
    private void OnDeselect()
    {
        // 再生中のアニメーションを停止
        scaleTween?.Kill();
        alphaTween?.Kill();

        // 通常サイズに戻す
        scaleTween = transform.DOScale(
            originalScale,
            deselectDuration
        )
        .SetEase(scaleCurve)
        .SetUpdate(true);

        // テキストを暗くする
        if (text != null)
        {
            alphaTween = text.DOFade(
                normalTextAlpha,
                deselectDuration
            )
            .SetUpdate(true);
        }

        // 選択マークを非表示
        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }
    }

    // オブジェクト破棄時にアニメーションを停止
    private void OnDestroy()
    {
        scaleTween?.Kill();
        alphaTween?.Kill();
    }
}
