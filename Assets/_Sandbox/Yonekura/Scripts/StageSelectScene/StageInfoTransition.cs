using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// ステージセレクト画面上部のエリア名表示と切り替えを管理する。
/// </summary>
public class StageInfoTransition : MonoBehaviour
{
    [Header("表示範囲")]
    [SerializeField] private RectTransform stageInfoArea;

    [Header("表示テキスト")]
    [SerializeField] private TMP_Text currentText;
    [SerializeField] private TMP_Text nextText;

    [Header("移動設定")]
    [Tooltip("文字が画面端まで移動する時間")]
    [SerializeField, Min(0.01f)] private float moveDuration = 0.4f;

    [Tooltip("次の文字が登場するまでの待機時間")]
    [SerializeField, Min(0f)] private float enterDelay = 0.15f;

    [SerializeField] private Ease moveEase = Ease.OutCubic;

    [Header("透明化設定")]
    [Tooltip("境界付近で文字が透明になる範囲")]
    [SerializeField, Min(0.01f)] private float fadeWidth = 80f;

    private RectTransform currentRect;
    private RectTransform nextRect;

    private Vector2 basePosition;

    private Color32[][] currentBaseColors;
    private Color32[][] nextBaseColors;

    private Tween activeTween;

    /// <summary>
    /// 初期表示するエリア名を設定する。
    /// </summary>
    public void Initialize(string areaName)
    {
        currentText.text = areaName;
        currentText.ForceMeshUpdate();

        currentBaseColors = CaptureBaseColors(currentText);

        currentRect.anchoredPosition = basePosition;
        currentText.gameObject.SetActive(true);
        nextText.gameObject.SetActive(false);
    }

    /// <summary>
    /// エリア名を指定方向へ切り替える。
    /// </summary>
    /// <param name="areaName">切り替え先のエリア名。</param>
    /// <param name="direction">右方向なら1、左方向なら-1。</param>
    public void PlayTransition(
    string areaName,
    int direction,
    float startDelay = 0f)
    {
        if (activeTween != null && activeTween.IsActive())
        {
            activeTween.Kill();
        }

        direction = direction >= 0 ? -1 : 1;

        // 新しい文字を準備する。
        nextText.gameObject.SetActive(true);

        nextText.text = areaName;
        nextText.ForceMeshUpdate();

        nextBaseColors = CaptureBaseColors(nextText);

        // 現在の文字と次の文字の移動先を計算する。
        Rect areaRect = stageInfoArea.rect;

        float exitX = direction > 0
            ? areaRect.xMax + currentRect.rect.width * 0.5f
            : areaRect.xMin - currentRect.rect.width * 0.5f;

        float enterX = direction > 0
            ? areaRect.xMin - nextRect.rect.width * 0.5f
            : areaRect.xMax + nextRect.rect.width * 0.5f;

        // 新しい文字を反対側の画面外に配置する。
        nextRect.anchoredPosition = new Vector2(
            enterX,
            basePosition.y
        );

        Sequence sequence = DOTween.Sequence();

        sequence.AppendInterval(Mathf.Max(0f, startDelay));

        // 現在の文字を画面端まで移動させる。
        sequence.Append(
            currentRect.DOAnchorPosX(exitX, moveDuration)
                .SetEase(moveEase)
        );

        // 次の文字が登場するまで待機する。
        sequence.AppendInterval(enterDelay);

        // 次の文字を反対側から中央へ移動させる。
        sequence.Append(
            nextRect.DOAnchorPos(basePosition, moveDuration)
                .SetEase(moveEase)
        );

        // 移動中、文字の位置に応じて透明度を更新する。
        sequence.OnUpdate(() =>
        {
            UpdateTextFade(currentText, currentBaseColors, direction, false);
            UpdateTextFade(nextText, nextBaseColors, direction, true);
        });

        sequence.OnComplete(() =>
        {
            // 表示中のTextと次のTextを入れ替える。
            TMP_Text tempText = currentText;
            currentText = nextText;
            nextText = tempText;

            RectTransform tempRect = currentRect;
            currentRect = nextRect;
            nextRect = tempRect;

            Color32[][] tempColors = currentBaseColors;
            currentBaseColors = nextBaseColors;
            nextBaseColors = tempColors;

            currentRect.anchoredPosition = basePosition;
            nextRect.anchoredPosition = basePosition;

            // 次の表示に備えて透明度を元に戻す。
            RestoreBaseColors(currentText, currentBaseColors);
            RestoreBaseColors(nextText, nextBaseColors);

            nextText.gameObject.SetActive(false);
        });

        activeTween = sequence;
    }

    private void Awake()
    {
        currentRect = currentText.rectTransform;
        nextRect = nextText.rectTransform;

        basePosition = currentRect.anchoredPosition;

        nextText.gameObject.SetActive(false);
    }

    // TextMeshProの元の頂点カラーを保存する。
    private Color32[][] CaptureBaseColors(TMP_Text text)
    {
        TMP_MeshInfo[] meshInfo = text.textInfo.meshInfo;

        Color32[][] colors = new Color32[meshInfo.Length][];

        for (int i = 0; i < meshInfo.Length; i++)
        {
            colors[i] = new Color32[meshInfo[i].colors32.Length];

            Array.Copy(
                meshInfo[i].colors32,
                colors[i],
                meshInfo[i].colors32.Length
            );
        }

        return colors;
    }

    // 文字の位置に応じて透明度を更新する。
    private void UpdateTextFade(
        TMP_Text text,
        Color32[][] baseColors,
        int direction,
        bool isEntering)
    {
        if (baseColors == null)
        {
            return;
        }

        TMP_TextInfo textInfo = text.textInfo;
        Rect areaRect = stageInfoArea.rect;

        float boundary;
        bool fadeFromLeft;

        if (isEntering)
        {
            fadeFromLeft = direction > 0;
        }
        else
        {
            fadeFromLeft = direction < 0;
        }

        boundary = fadeFromLeft
            ? areaRect.xMin
            : areaRect.xMax;

        float fadeStart = fadeFromLeft
            ? boundary + fadeWidth
            : boundary - fadeWidth;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];

            if (!character.isVisible)
            {
                continue;
            }

            int materialIndex = character.materialReferenceIndex;
            int vertexIndex = character.vertexIndex;

            // 文字の中心位置をStageInfoArea基準に変換する。
            Vector3 center = (
                character.bottomLeft +
                character.topRight
            ) * 0.5f;

            Vector3 worldPosition = text.rectTransform.TransformPoint(center);

            float localX = stageInfoArea.InverseTransformPoint(
                worldPosition
            ).x;

            float alpha;

            if (fadeFromLeft)
            {
                alpha = Mathf.InverseLerp(
                    boundary,
                    fadeStart,
                    localX
                );
            }
            else
            {
                alpha = 1f - Mathf.InverseLerp(
                    fadeStart,
                    boundary,
                    localX
                );
            }

            alpha = Mathf.Clamp01(alpha);

            Color32[] colors = textInfo.meshInfo[materialIndex].colors32;
            Color32[] originalColors = baseColors[materialIndex];

            for (int j = 0; j < 4; j++)
            {
                int index = vertexIndex + j;

                Color32 color = originalColors[index];
                color.a = (byte)(color.a * alpha);

                colors[index] = color;
            }
        }

        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    /// <summary>
    /// 指定したエリア名を表示し、実行中の切り替えをリセットする。
    /// </summary>
    public void ResetTo(string areaName)
    {
        // 実行中のアニメーションを停止する。
        activeTween?.Kill();
        activeTween = null;

        // 現在の表示を確定済みのエリア名に戻す。
        currentText.gameObject.SetActive(true);
        currentText.text = areaName;
        currentText.ForceMeshUpdate();

        currentBaseColors = CaptureBaseColors(currentText);

        // 表示位置を初期状態に戻す。
        currentRect.anchoredPosition = basePosition;
        nextRect.anchoredPosition = basePosition;

        // 次の文字は非表示にする。
        nextText.gameObject.SetActive(false);
    }

    // 保存した頂点カラーに戻す。
    private void RestoreBaseColors(
        TMP_Text text,
        Color32[][] baseColors)
    {
        if (baseColors == null)
        {
            return;
        }

        TMP_MeshInfo[] meshInfo = text.textInfo.meshInfo;

        for (int i = 0; i < meshInfo.Length; i++)
        {
            Array.Copy(
                baseColors[i],
                meshInfo[i].colors32,
                baseColors[i].Length
            );
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    /// <summary>
    /// 実行中のエリア名切り替えを停止する。
    /// </summary>
    public void KillTransition()
    {
        activeTween?.Kill();
        activeTween = null;
    }

    private void OnDestroy()
    {
        activeTween?.Kill();
    }
}
