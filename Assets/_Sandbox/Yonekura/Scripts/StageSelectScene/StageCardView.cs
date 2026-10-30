using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ステージセレクトのカード1枚分の表示を管理する。
/// </summary>
public class StageCardView : MonoBehaviour
{
    [Header("表示設定")]
    [Tooltip("ステージ画像を表示するCardのImageを設定します。なお、CardのImage欄には何も設定しないでください。")]
    [SerializeField] private Image screenshotImage;

    [Tooltip("ステージ名を表示するTextMeshProを設定します。")]
    [SerializeField] private TMP_Text stageNameText;

    [Tooltip("カード全体の透明度を制御するCanvasGroupを設定します。")]
    [SerializeField] private CanvasGroup canvasGroup;

    /// <summary>
    /// カードの透明度を制御するCanvasGroup。
    /// </summary>
    public CanvasGroup Group => canvasGroup;

    /// <summary>
    /// ステージ情報をカードに反映する。
    /// </summary>
    public void SetData(string displayName, Sprite screenshot)
    {
        bool hasStage = !string.IsNullOrEmpty(displayName);

        screenshotImage.sprite = screenshot;
        screenshotImage.enabled = hasStage;
        stageNameText.text = hasStage ? displayName : "";
    }

    /// <summary>
    /// カードの透明度と大きさを設定する。
    /// </summary>
    public void SetAppearance(float alpha, float scale)
    {
        canvasGroup.alpha = alpha;
        transform.localScale = Vector3.one * scale;
    }
}
