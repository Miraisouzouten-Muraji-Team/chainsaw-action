using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

/// <summary>
/// ステージセレクトのカード移動、入力、シーン遷移を管理する。
/// </summary>
public class StageSelectController : MonoBehaviour
{
    /// <summary>
    /// ステージ1つ分の表示情報と遷移先。
    /// </summary>
    [Serializable]
    public class StageAreaData
    {
        [Header("カード表示")]
        [Tooltip("カードに表示するステージ名。改行して複数行で表示できます。")]
        [TextArea(1, 4)]
        public string displayName;

        [Tooltip("カードに表示するステージのスクリーンショット。")]
        public Sprite screenshot;

        [Header("シーン遷移")]
        [Tooltip("ステージ決定時に読み込む遷移先のシーン名。")]
        public string sceneName;
    }

    [Header("ステージデータ")]
    [Tooltip("ステージごとの表示情報と遷移先を設定します。配列の要素を追加するとステージ数を増やせます。")]
    [SerializeField] private StageAreaData[] areas;

    [Header("シーントランジション")]
    [Tooltip("ステージ決定時のシーントランジションを管理するTransitionManagerを設定します。")]
    [SerializeField] private TransitionManager transitionManager;

    [Tooltip("使用するトランジションの種類を選択します。")]
    [SerializeField] private TransitionManager.TransitionType transitionType;

    [Tooltip("最初のステージは0")]
    [SerializeField] private int currentIndex = 0;

    [Header("カード")]
    [Tooltip("現在選択しているステージより1つ前のステージを表示するカードです。")]
    [SerializeField] private StageCardView previousCard;

    [Tooltip("現在選択しているステージを表示するカードです。")]
    [SerializeField] private StageCardView currentCard;

    [Tooltip("現在選択しているステージより1つ後のステージを表示するカードです。")]
    [SerializeField] private StageCardView nextCard;

    [Header("矢印")]
    [SerializeField] private Transform leftArrow;
    [SerializeField] private Transform rightArrow;

    [Header("カード配置")]
    [Tooltip("中央のカードと左右のカードの横方向の間隔です。")]
    [SerializeField] private float cardSpacing = 600f;

    [Tooltip("中央に表示するカードの倍率です。")]
    [SerializeField] private float cardScale = 1f;

    [Tooltip("左右に表示するカードの倍率です。")]
    [SerializeField] private float sideCardScale = 0.65f;

    [Header("アニメーション")]
    [Tooltip("ステージ切り替え時にカードがスライドする時間です。")]
    [SerializeField, Min(0.05f)]
    private float slideDuration = 0.3f;

    [Tooltip("矢印アニメーション時の最大拡大倍率です。")]
    [SerializeField, Range(1f, 2f)]
    private float arrowScaleMultiplier = 1.3f;

    [Tooltip("矢印が拡大・縮小するアニメーションの時間です。")]
    [SerializeField, Min(0.01f)]
    private float arrowAnimationDuration = 0.12f;

    [Header("入力")]
    [Tooltip("スティックを倒したと判定する入力値のしきい値です。")]
    [SerializeField, Range(0.1f, 1f)]
    private float stickThreshold = 0.5f;

    [Tooltip("スティックをニュートラルに戻したと判定する入力値のしきい値です。")]
    [SerializeField, Range(0f, 0.5f)]
    private float resetThreshold = 0.2f;

    private Vector3 leftArrowBaseScale;
    private Vector3 rightArrowBaseScale;

    private bool canReadStick = true;
    private bool isAnimating;

    private RectTransform previousRect;
    private RectTransform currentRect;
    private RectTransform nextRect;

    // カードを少し内側から定位置まで滑らかに移動させる。
    private void AnimateSideCard(
        RectTransform rect,
        StageCardView card,
        float targetX,
        float targetAlpha)
    {
        Sequence sequence = DOTween.Sequence();

        sequence.Join(rect.DOAnchorPosX(targetX, slideDuration));
        sequence.Join(rect.DOScale(sideCardScale, slideDuration));
        sequence.Join(card.Group.DOFade(targetAlpha, slideDuration));

        sequence.SetEase(Ease.OutCubic);
    }

    // 選択可能なステージがある方向だけ矢印を表示する。
    private void UpdateArrowVisibility()
    {
        leftArrow.gameObject.SetActive(currentIndex > 0);
        rightArrow.gameObject.SetActive(currentIndex < areas.Length - 1);
    }

    private void Start()
    {
        if (areas == null || areas.Length == 0)
        {
            Debug.LogError("ステージデータが登録されていません。", this);
            enabled = false;
            return;
        }

        if (previousCard == null ||
            currentCard == null ||
            nextCard == null ||
            leftArrow == null ||
            rightArrow == null)
        {
            Debug.LogError(
                "PreviousCard、CurrentCard、NextCard、左右の矢印を設定してください。",
                this);
            enabled = false;
            return;
        }

        previousRect = previousCard.GetComponent<RectTransform>();
        currentRect = currentCard.GetComponent<RectTransform>();
        nextRect = nextCard.GetComponent<RectTransform>();

        if (previousRect == null ||
            currentRect == null ||
            nextRect == null)
        {
            Debug.LogError("すべてのカードにRectTransformが必要です。", this);
            enabled = false;
            return;
        }

        currentIndex = Mathf.Clamp(currentIndex, 0, areas.Length - 1);

        leftArrowBaseScale = leftArrow.localScale;
        rightArrowBaseScale = rightArrow.localScale;

        UpdateCardData();
        SetInitialPositions();
    }

    private void Update()
    {
        if (isAnimating)
        {
            return;
        }

        ReadDirectionInput();
        ReadConfirmInput();
    }

    private void OnDestroy()
    {
        // オブジェクト破棄時にアニメーションを停止する。
        if (previousRect != null) previousRect.DOKill();
        if (currentRect != null) currentRect.DOKill();
        if (nextRect != null) nextRect.DOKill();

        if (leftArrow != null) leftArrow.DOKill();
        if (rightArrow != null) rightArrow.DOKill();
    }

    // スティックとキーボードの左右入力を読み取る。
    private void ReadDirectionInput()
    {
        Gamepad gamepad = Gamepad.current;
        Keyboard keyboard = Keyboard.current;

        float horizontal = gamepad != null
            ? gamepad.leftStick.ReadValue().x
            : 0f;

        bool leftPressed = keyboard != null &&
            (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed);

        bool rightPressed = keyboard != null &&
            (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed);

        if (leftPressed)
        {
            horizontal = -1f;
        }
        else if (rightPressed)
        {
            horizontal = 1f;
        }

        if (Mathf.Abs(horizontal) <= resetThreshold)
        {
            canReadStick = true;
            return;
        }

        if (!canReadStick || Mathf.Abs(horizontal) < stickThreshold)
        {
            return;
        }

        canReadStick = false;

        if (horizontal < 0f)
        {
            SelectPrevious();
        }
        else
        {
            SelectNext();
        }
    }

    // 決定ボタンを読み取る。
    private void ReadConfirmInput()
    {
        bool gamepadConfirm =
            Gamepad.current != null &&
            Gamepad.current.buttonSouth.wasPressedThisFrame;

        bool keyboardConfirm =
            Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame ||
             Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
             Keyboard.current.spaceKey.wasPressedThisFrame);

        if (gamepadConfirm || keyboardConfirm)
        {
            ConfirmSelection();
        }
    }

    /// <summary>
    /// 前のステージへカードをスライドさせる。
    /// </summary>
    public void SelectPrevious()
    {
        if (isAnimating || currentIndex <= 0)
        {
            return;
        }

        isAnimating = true;
        PlayArrowAnimation(leftArrow, leftArrowBaseScale);

        Sequence sequence = DOTween.Sequence();

        // 左のカードを中央へ移動する。
        sequence.Join(previousRect.DOAnchorPosX(
            0f, slideDuration));

        // 中央のカードを右へ移動する。
        sequence.Join(currentRect.DOAnchorPosX(
            cardSpacing, slideDuration));

        // 右のカードを画面外へ移動する。
        sequence.Join(nextRect.DOAnchorPosX(
            cardSpacing * 2f, slideDuration));

        // カードの大きさを変更する。
        sequence.Join(previousRect.DOScale(
            cardScale, slideDuration));

        sequence.Join(currentRect.DOScale(
            sideCardScale, slideDuration));

        sequence.Join(nextRect.DOScale(
            sideCardScale, slideDuration));

        // 画面外へ移動するカードをフェードアウトする。
        sequence.Join(previousCard.Group.DOFade(
            1f, slideDuration));

        sequence.Join(currentCard.Group.DOFade(
            0.5f, slideDuration));

        sequence.Join(nextCard.Group.DOFade(
            0f, slideDuration));

        sequence.SetEase(Ease.OutCubic);
        sequence.OnComplete(FinishPreviousTransition);
    }

    /// <summary>
    /// 次のステージへカードをスライドさせる。
    /// </summary>
    public void SelectNext()
    {
        // 最後のステージでは右に移動しない。
        if (isAnimating || currentIndex >= areas.Length - 1)
        {
            return;
        }

        isAnimating = true;
        PlayArrowAnimation(rightArrow, rightArrowBaseScale);

        Sequence sequence = DOTween.Sequence();

        // カードを左方向へスライドさせる。
        sequence.Join(previousRect.DOAnchorPosX(
            -cardSpacing * 2f, slideDuration));

        sequence.Join(currentRect.DOAnchorPosX(
            -cardSpacing, slideDuration));

        sequence.Join(nextRect.DOAnchorPosX(
            0f, slideDuration));

        sequence.Join(previousRect.DOScale(
            sideCardScale, slideDuration));

        sequence.Join(currentRect.DOScale(
            sideCardScale, slideDuration));

        sequence.Join(nextRect.DOScale(
            cardScale, slideDuration));

        sequence.Join(previousCard.Group.DOFade(
            0f, slideDuration));

        sequence.Join(currentCard.Group.DOFade(
            0.5f, slideDuration));

        sequence.Join(nextCard.Group.DOFade(
            1f, slideDuration));

        sequence.SetEase(Ease.OutCubic);
        sequence.OnComplete(FinishNextTransition);
    }

    /// <summary>
    /// 前のステージへの移動完了後、カードの役割を入れ替える。
    /// </summary>

    // 前のステージへの移動完了後、カードを並べ直す。
    private void FinishPreviousTransition()
    {
        StageCardView oldPrevious = previousCard;
        StageCardView oldCurrent = currentCard;
        StageCardView oldNext = nextCard;

        previousCard = oldNext;
        currentCard = oldPrevious;
        nextCard = oldCurrent;

        currentIndex--;

        RefreshRectReferences();
        UpdateCardData();

        // 選択中のカードと右側のカードを定位置に置く。
        SetCardPosition(currentRect, 0f, cardScale, 1f);
        SetCardPosition(
            nextRect,
            cardSpacing,
            sideCardScale,
            currentIndex < areas.Length - 1 ? 0.5f : 0f);

        // 新しく左側に登場するカードは、透明な状態から移動させる。
        if (currentIndex > 0)
        {
            SetCardPosition(
                previousRect,
                -cardSpacing * 0.55f,
                sideCardScale * 0.75f,
                0f);

            AnimateSideCard(
                previousRect,
                previousCard,
                -cardSpacing,
                0.5f);
        }
        else
        {
            SetCardPosition(previousRect, -cardSpacing, sideCardScale, 0f);
        }

        UpdateArrowVisibility();
        isAnimating = false;
    }

    // 次のステージへの移動完了後、カードを並べ直す。
    private void FinishNextTransition()
    {
        StageCardView oldPrevious = previousCard;
        StageCardView oldCurrent = currentCard;
        StageCardView oldNext = nextCard;

        previousCard = oldCurrent;
        currentCard = oldNext;
        nextCard = oldPrevious;

        currentIndex++;

        RefreshRectReferences();
        UpdateCardData();

        // 選択中のカードと左側のカードを定位置に置く。
        SetCardPosition(
            previousRect,
            -cardSpacing,
            sideCardScale,
            currentIndex > 0 ? 0.5f : 0f);

        SetCardPosition(currentRect, 0f, cardScale, 1f);

        // 新しく右側に登場するカードは、透明な状態から移動させる。
        if (currentIndex < areas.Length - 1)
        {
            SetCardPosition(
                nextRect,
                cardSpacing * 0.15f,
                sideCardScale * 0.5f,
                0f);

            AnimateSideCard(
                nextRect,
                nextCard,
                cardSpacing,
                0.5f);
        }
        else
        {
            SetCardPosition(nextRect, cardSpacing, sideCardScale, 0f);
        }

        UpdateArrowVisibility();
        isAnimating = false;
    }

    // カードの役割を入れ替えた後、RectTransformを取得し直す。
    private void RefreshRectReferences()
    {
        previousRect = previousCard.GetComponent<RectTransform>();
        currentRect = currentCard.GetComponent<RectTransform>();
        nextRect = nextCard.GetComponent<RectTransform>();
    }

    /// <summary>
    /// 現在の選択位置に合わせて3枚のカードを更新する。
    /// 範囲外のステージは空欄にする。
    /// </summary>
    private void UpdateCardData()
    {
        // 左側のカード。
        if (currentIndex > 0)
        {
            StageAreaData area = areas[currentIndex - 1];

            previousCard.SetData(
                area.displayName,
                area.screenshot);
        }
        else
        {
            previousCard.SetData("", null);
        }

        // 中央のカード。
        StageAreaData currentArea = areas[currentIndex];

        currentCard.SetData(
            currentArea.displayName,
            currentArea.screenshot);

        // 右側のカード。
        if (currentIndex < areas.Length - 1)
        {
            StageAreaData area = areas[currentIndex + 1];

            nextCard.SetData(
                area.displayName,
                area.screenshot);
        }
        else
        {
            nextCard.SetData("", null);
        }
    }


    /// <summary>
    /// 各カードの位置、大きさ、透明度と矢印の表示を設定する。
    /// </summary>
    private void SetInitialPositions()
    {
        bool hasPrevious = currentIndex > 0;
        bool hasNext = currentIndex < areas.Length - 1;

        // カードを配置する。
        SetCardPosition(
            previousRect,
            -cardSpacing,
            sideCardScale,
            hasPrevious ? 0.5f : 0f);

        SetCardPosition(
            currentRect,
            0f,
            cardScale,
            1f);

        SetCardPosition(
            nextRect,
            cardSpacing,
            sideCardScale,
            hasNext ? 0.5f : 0f);

        // 選択できるステージがない方向の矢印を非表示にする。
        leftArrow.gameObject.SetActive(hasPrevious);
        rightArrow.gameObject.SetActive(hasNext);

        UpdateArrowVisibility();
    }


    // カードの位置、大きさ、透明度を設定する。
    private void SetCardPosition(
        RectTransform rect,
        float x,
        float scale,
        float alpha)
    {
        rect.DOKill();
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.localScale = Vector3.one * scale;

        StageCardView card = rect.GetComponent<StageCardView>();

        if (card != null && card.Group != null)
        {
            card.Group.alpha = alpha;
        }
    }

    // 矢印を一瞬拡大して元のサイズに戻す。
    private void PlayArrowAnimation(Transform arrow, Vector3 baseScale)
    {
        arrow.DOKill();
        arrow.localScale = baseScale;

        arrow.DOScale(
                baseScale * arrowScaleMultiplier,
                arrowAnimationDuration)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true);
    }

    /// <summary>
    /// 中央に表示されているステージのシーンへ遷移する。
    /// </summary>
    public void ConfirmSelection()
    {
        if (areas == null ||
            areas.Length == 0 ||
            isAnimating)
        {
            return;
        }

        // TransitionManagerの設定を確認する。
        if (transitionManager == null)
        {
            Debug.LogError(
                "TransitionManagerが設定されていません。",
                this);
            return;
        }

        // 選択中のステージのシーン名を取得する。
        string sceneName = areas[currentIndex].sceneName;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError(
                "遷移先のシーン名が設定されていません。",
                this);
            return;
        }

        // 遷移中の連続入力を防止する。
        isAnimating = true;

        // 指定されたトランジションでシーンを切り替える。
        transitionManager.TransitionToScene(
            sceneName,
            transitionType);
    }
}
