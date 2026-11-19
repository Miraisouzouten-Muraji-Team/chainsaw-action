using System;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// ステージ選択カードを拡大するワープ演出を管理する。
/// </summary>
public class StageWarpController : MonoBehaviour
{
    [Header("アニメーション設定")]
    [SerializeField, Min(0.1f)]
    private float expandDuration = 0.8f;

    [SerializeField, Min(1f)]
    private float zoomScale = 1.15f;

    [SerializeField]
    private Ease expandEase = Ease.InCubic;

    private Tween activeTween;
    private bool isPlaying;

    /// <summary>
    /// 指定したカード全体を画面いっぱいに拡大する。
    /// </summary>
    public void PlayWarp(RectTransform targetCard, Action onComplete = null)
    {
        if (isPlaying) return;

        if (targetCard == null)
        {
            Debug.LogError("拡大対象のカードが設定されていません。");
            return;
        }

        RectTransform parentRect = targetCard.parent as RectTransform;

        if (parentRect == null)
        {
            Debug.LogError("カードの親にRectTransformがありません。");
            return;
        }

        Canvas canvas = targetCard.GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("カードのCanvasが見つかりません。");
            return;
        }

        Camera eventCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.rootCanvas.worldCamera;

        // カードの四隅から画面上の大きさを取得する。
        Vector3[] corners = new Vector3[4];
        targetCard.GetWorldCorners(corners);

        Vector2 min = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        Vector2 max = min;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[i]);

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        Vector2 cardScreenSize = max - min;

        if (cardScreenSize.x <= 0f || cardScreenSize.y <= 0f)
        {
            Debug.LogError("カードのサイズを取得できませんでした。");
            return;
        }

        // 画面中央をカードの親オブジェクトの座標に変換する。
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect,
            screenCenter,
            eventCamera,
            out Vector2 targetPosition
        );

        // カードが画面全体を覆うために必要な拡大率を求める。
        float fitScale = Mathf.Max(
            Screen.width / cardScreenSize.x,
            Screen.height / cardScreenSize.y
        );

        Vector3 targetScale = targetCard.localScale * fitScale * zoomScale;

        isPlaying = true;

        Sequence sequence = DOTween.Sequence();

        sequence.Join(targetCard.DOAnchorPos(targetPosition, expandDuration));
        sequence.Join(targetCard.DOScale(targetScale, expandDuration));

        sequence.SetEase(expandEase);

        activeTween = sequence;

        sequence.OnComplete(() =>
        {
            isPlaying = false;
            activeTween = null;
            onComplete?.Invoke();
        });
    }

    private void OnDestroy()
    {
        activeTween?.Kill();
    }
}
