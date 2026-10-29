using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
        /// <summary>ステージ名。</summary>
        public string stageName;

        /// <summary>エリア番号。</summary>
        public string areaNumber;

        /// <summary>ステージのスクリーンショット。</summary>
        public Sprite screenshot;

        /// <summary>遷移先のシーン名。</summary>
        public string sceneName;
    }

    [Header("ステージデータ")]
    [SerializeField] private StageAreaData[] areas;

    [Tooltip("最初のステージは0")]
    [SerializeField] private int currentIndex = 0;

    [Header("カード")]
    [SerializeField] private StageCardView previousCard;
    [SerializeField] private StageCardView currentCard;
    [SerializeField] private StageCardView nextCard;

    [Header("矢印")]
    [SerializeField] private Transform leftArrow;
    [SerializeField] private Transform rightArrow;

    [Header("カード配置")]
    [SerializeField] private float cardSpacing = 600f;
    [SerializeField] private float cardScale = 1f;
    [SerializeField] private float sideCardScale = 0.65f;

    [Header("アニメーション")]
    [SerializeField, Min(0.05f)]
    private float slideDuration = 0.3f;

    [SerializeField, Range(1f, 2f)]
    private float arrowScaleMultiplier = 1.3f;

    [SerializeField, Min(0.01f)]
    private float arrowAnimationDuration = 0.12f;

    [Header("入力")]
    [SerializeField, Range(0.1f, 1f)]
    private float stickThreshold = 0.5f;

    [SerializeField, Range(0f, 0.5f)]
    private float resetThreshold = 0.2f;

    private Vector3 leftArrowBaseScale;
    private Vector3 rightArrowBaseScale;

    private bool canReadStick = true;
    private bool isAnimating;

    private RectTransform previousRect;
    private RectTransform currentRect;
    private RectTransform nextRect;

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

    /// <summary>
    /// 前のステージへの移動完了後、カードの役割を入れ替える。
    /// </summary>
    private void FinishPreviousTransition()
    {
        StageCardView oldPrevious = previousCard;
        StageCardView oldCurrent = currentCard;
        StageCardView oldNext = nextCard;

        // 左から中央へ移動したカードを選択中にする。
        previousCard = oldNext;
        currentCard = oldPrevious;
        nextCard = oldCurrent;

        currentIndex--;

        RefreshRectReferences();
        UpdateCardData();

        // 並べ直し時の位置を初期化する。
        SetInitialPositions();

        isAnimating = false;
    }

    // 次のステージへの移動完了後、カードの役割を入れ替える。
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
        SetInitialPositions();

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
                area.stageName,
                area.areaNumber,
                area.screenshot);
        }
        else
        {
            previousCard.SetData("", "", null);
        }

        // 中央のカード。
        StageAreaData currentArea = areas[currentIndex];

        currentCard.SetData(
            currentArea.stageName,
            currentArea.areaNumber,
            currentArea.screenshot);

        // 右側のカード。
        if (currentIndex < areas.Length - 1)
        {
            StageAreaData area = areas[currentIndex + 1];

            nextCard.SetData(
                area.stageName,
                area.areaNumber,
                area.screenshot);
        }
        else
        {
            nextCard.SetData("", "", null);
        }
    }

    // 各カードを定位置に配置する。
    private void SetInitialPositions()
    {
        float previousAlpha = currentIndex > 0 ? 0.5f : 0f;
        float nextAlpha = currentIndex < areas.Length - 1 ? 0.5f : 0f;

        SetCardPosition(
            previousRect, -cardSpacing, sideCardScale, previousAlpha);

        SetCardPosition(
            currentRect, 0f, cardScale, 1f);

        SetCardPosition(
            nextRect, cardSpacing, sideCardScale, nextAlpha);
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

        string sceneName = areas[currentIndex].sceneName;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError(
                "遷移先のシーン名が設定されていません。", this);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
