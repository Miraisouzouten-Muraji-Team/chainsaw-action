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

    [Header("進行状態の表示")]
    [SerializeField] private GameObject clearMark;
    [SerializeField] private GameObject lockMark;

    [Header("ロック時の画像色")]
    [SerializeField] private Color lockedColor = Color.gray;

    /// <summary>
    /// カードの透明度を制御するCanvasGroup。
    /// </summary>
    public CanvasGroup Group => canvasGroup;

    /// <summary>
    /// ステージ情報と進行状態をカードに反映する。
    /// </summary>
    public void SetData(
        string displayName,
        Sprite screenshot,
        StageAreaStatus status)
    {
        bool hasStage = !string.IsNullOrEmpty(displayName);

        if (screenshotImage != null)
        {
            screenshotImage.sprite = screenshot;
            screenshotImage.enabled = hasStage;

            // ロック中は画像を暗くする。
            screenshotImage.color =
                status == StageAreaStatus.Locked
                    ? lockedColor
                    : Color.white;
        }

        if (stageNameText != null)
            stageNameText.text = hasStage ? displayName : "";

        if (clearMark != null)
            clearMark.SetActive(
                hasStage && status == StageAreaStatus.Cleared);

        if (lockMark != null)
            lockMark.SetActive(
                hasStage && status == StageAreaStatus.Locked);
    }

    /// <summary>
    /// カードの透明度と大きさを設定する。
    /// </summary>
    public void SetAppearance(float alpha, float scale)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = alpha;

        transform.localScale = Vector3.one * scale;
    }
}
