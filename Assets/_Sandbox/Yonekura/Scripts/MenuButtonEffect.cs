using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// メニューボタン選択時、ボタン拡大や透明度変更
public class MenuButtonEffect : MonoBehaviour,
    ISelectHandler, IDeselectHandler
{
    // ボタンの大きさを保存
    private Vector3 originalScale;
    private Image image;

    //拡大倍率や、透明度の設定
    [SerializeField] private float selectedScale = 1.1f;
    [SerializeField] private float normalAlpha = 0.5f;
    [SerializeField] private float selectedAlpha = 1.0f;
    [SerializeField] private float speed = 10f;
    // 選択中に表示するマーク
    [SerializeField] private GameObject selectMark;

    //選択時の大きさや透明度保存
    private Vector3 targetScale;
    private float targetAlpha;

    // 初期設定
    private void Start()
    {
        originalScale = transform.localScale;
        image = GetComponent<Image>();

        targetScale = originalScale;
        targetAlpha = normalAlpha;

        SetAlpha(normalAlpha);

        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }
    }

    // 大きさと透明度を滑らかに変更
    private void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            Time.unscaledDeltaTime * speed
        );

        if (image != null)
        {
            Color color = image.color;
            color.a = Mathf.Lerp(
                color.a,
                targetAlpha,
                Time.unscaledDeltaTime * speed
            );
            image.color = color;
        }
    }

    // ボタンが選択されたとき
    public void OnSelect(BaseEventData eventData)
    {
        targetScale = originalScale * selectedScale;
        targetAlpha = selectedAlpha;

        if (selectMark != null)
        {
            selectMark.SetActive(true);
        }
    }

    // ボタンの選択が解除されたとき
    public void OnDeselect(BaseEventData eventData)
    {
        targetScale = originalScale;
        targetAlpha = normalAlpha;

        if (selectMark != null)
        {
            selectMark.SetActive(false);
        }
    }

    // 透明度を設定
    private void SetAlpha(float alpha)
    {
        if (image != null)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }
}
