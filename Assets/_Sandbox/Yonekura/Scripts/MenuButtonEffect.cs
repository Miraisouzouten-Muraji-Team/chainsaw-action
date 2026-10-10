using R3;
using R3.Triggers;
using UnityEngine;
using UnityEngine.UI;

// メニューボタン選択時、ボタン拡大や透明度変更
public class MenuButtonEffect : MonoBehaviour
{
    // ボタンの大きさを保存
    private Vector3 originalScale;

    // ボタンの文字
    private Text text;

    // 拡大倍率や、透明度の設定
    [SerializeField] private float selectedScale = 1.1f;
    [SerializeField] private float speed = 10f;
    [SerializeField] private float normalTextAlpha = 0.5f;
    [SerializeField] private float selectedTextAlpha = 1.0f;

    // 選択中に表示するマーク
    [SerializeField] private GameObject selectMark;

    // 選択時の大きさや透明度
    private Vector3 targetScale;
    private float targetTextAlpha;

    // 初期設定
    private void Start()
    {
        originalScale = transform.localScale;

        // 子オブジェクトのTextを自動取得
        text = GetComponentInChildren<Text>();

        targetScale = originalScale;
        targetTextAlpha = normalTextAlpha;

        // ゲーム開始時、選択マークを非表示
        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }

        // ボタンが選択されたとき
        gameObject
            .GetComponent<Selectable>()
            .OnSelectAsObservable()
            .Subscribe(_ => OnSelect())
            .AddTo(this);

        // ボタンの選択が解除されたとき
        gameObject
            .GetComponent<Selectable>()
            .OnDeselectAsObservable()
            .Subscribe(_ => OnDeselect())
            .AddTo(this);
    }

    // 大きさと透明度を滑らかに変更
    private void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            Time.unscaledDeltaTime * speed
        );

        if (text != null)
        {
            Color color = text.color;
            color.a = targetTextAlpha;
            text.color = color;
        }
    }

    // ボタンが選択されたとき
    private void OnSelect()
    {
        targetScale = originalScale * selectedScale;
        targetTextAlpha = selectedTextAlpha;

        if (selectMark != null)
        {
            selectMark.SetActive(true);
        }
    }

    // ボタンの選択が解除されたとき
    private void OnDeselect()
    {
        targetScale = originalScale;
        targetTextAlpha = normalTextAlpha;

        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }
    }
}
