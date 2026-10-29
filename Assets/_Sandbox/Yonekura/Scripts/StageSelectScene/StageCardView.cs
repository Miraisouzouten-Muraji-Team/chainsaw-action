using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ステージセレクトのカード1枚分の表示を管理する。
/// </summary>
public class StageCardView : MonoBehaviour
{
    [Header("表示設定")]
    [SerializeField] private Image screenshotImage;
    [SerializeField] private TMP_Text stageNameText;
    [SerializeField] private CanvasGroup canvasGroup;

    /// <summary>
    /// カードの透明度を制御するCanvasGroup。
    /// </summary>
    public CanvasGroup Group => canvasGroup;

    /// <summary>
    /// ステージ情報をカードに反映する。
    /// </summary>
    public void SetData(
        string stageName,
        string areaNumber,
        Sprite screenshot)
    {
        bool hasStage = !string.IsNullOrEmpty(stageName);

        screenshotImage.sprite = screenshot;
        screenshotImage.enabled = hasStage;
        stageNameText.text = hasStage ? $"{stageName}\n{areaNumber}" : "";
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
