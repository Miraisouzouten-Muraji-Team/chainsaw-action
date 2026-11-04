using DG.Tweening;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ステージセレクトのカード移動、入力、シーン遷移を管理する。
/// </summary>
public class StageSelectController : MonoBehaviour
{
    [Header("ステージデータ")]
    [Tooltip("ステージ情報を管理するアセットです。\nステージの追加や変更は、このアセットから行います。")]
    [SerializeField] private StageDatabase stageDatabase;

    [Header("シーントランジション")]
    [Tooltip("ステージ決定時のシーントランジションを管理するTransitionManagerを設定します。")]
    [SerializeField] private TransitionManager transitionManager;

    [Tooltip("使用するトランジションの種類を選択します。")]
    [SerializeField] private TransitionManager.TransitionType transitionType;

    [Header("カード")]
    [Tooltip("現在選択しているステージより1つ前のステージを表示するカードです。")]
    [SerializeField] private StageCardView previousCard;

    [Tooltip("現在選択しているステージを表示するカードです。")]
    [SerializeField] private StageCardView currentCard;

    [Tooltip("現在選択しているステージより1つ後のステージを表示するカードです。")]
    [SerializeField] private StageCardView nextCard;

    [Tooltip("初期化後に中央に表示するエリアの配列番号です。\n最初のステージは0です。")]
    [SerializeField] private int currentIndex = 0;

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

    [Header("ロックメッセージ")]
    [Tooltip("ロック中のエリアを決定したときに理由を表示するTextMeshProを設定します。")]
    [SerializeField] private TMP_Text lockMessageText;

    [Header("ページインジケーター")]
    [Tooltip("エリアの選択位置を表示するUI")]
    [SerializeField] private AreaPageIndicator pageIndicator;

    private Vector3 leftArrowBaseScale;
    private Vector3 rightArrowBaseScale;

    private bool canReadStick = true;
    private bool isAnimating;

    private RectTransform previousRect;
    private RectTransform currentRect;
    private RectTransform nextRect;

    // 各エリアのシーン名を取得する。
    private string[] GetSceneNames()
    {
        string[] sceneNames = new string[stageDatabase.Areas.Length];

        for (int i = 0; i < stageDatabase.Areas.Length; i++)
            sceneNames[i] = stageDatabase.Areas[i].sceneName;

        return sceneNames;
    }

    /// <summary>
    /// 進行状況をカード表示に反映する。
    /// </summary>
    public void RefreshProgressDisplay()
    {
        UpdateCardData();
    }

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

    // 3枚のカードの移動・拡大縮小・透明度変更をまとめて実行する。
    private void PlayCardSlide(
        float previousX,
        float currentX,
        float nextX,
        float previousScale,
        float currentScale,
        float nextScale,
        float previousAlpha,
        float currentAlpha,
        float nextAlpha,
        TweenCallback onComplete)
    {
        Sequence sequence = DOTween.Sequence();

        // カードの位置を変更する。
        sequence.Join(previousRect.DOAnchorPosX(previousX, slideDuration));
        sequence.Join(currentRect.DOAnchorPosX(currentX, slideDuration));
        sequence.Join(nextRect.DOAnchorPosX(nextX, slideDuration));

        // カードの大きさを変更する。
        sequence.Join(previousRect.DOScale(previousScale, slideDuration));
        sequence.Join(currentRect.DOScale(currentScale, slideDuration));
        sequence.Join(nextRect.DOScale(nextScale, slideDuration));

        // カードの透明度を変更する。
        sequence.Join(previousCard.Group.DOFade(previousAlpha, slideDuration));
        sequence.Join(currentCard.Group.DOFade(currentAlpha, slideDuration));
        sequence.Join(nextCard.Group.DOFade(nextAlpha, slideDuration));

        sequence.SetEase(Ease.OutCubic);
        sequence.OnComplete(onComplete);
    }

    // カードの入れ替え後に表示情報を更新する。
    private void RefreshCardsAfterTransition()
    {
        pageIndicator.SetCurrentIndex(currentIndex);

        RefreshRectReferences();
        UpdateCardData();
    }

    // カード移動の完了処理を行う。
    private void CompleteCardTransition()
    {
        UpdateArrowVisibility();
        isAnimating = false;
    }

    // 選択可能なステージがある方向だけ矢印を表示する。
    private void UpdateArrowVisibility()
    {
        leftArrow.gameObject.SetActive(currentIndex > 0);
        rightArrow.gameObject.SetActive(currentIndex < stageDatabase.Areas.Length - 1);
    }

    //初期化
    private void Start()
    {
        if (stageDatabase == null ||
            stageDatabase.Areas == null ||
            stageDatabase.Areas.Length == 0)
        {
            Debug.LogError("StageDatabaseが設定されていないか、ステージデータが登録されていません。", this);
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

        currentIndex = Mathf.Clamp(currentIndex, 0, stageDatabase.Areas.Length - 1);

        leftArrowBaseScale = leftArrow.localScale;
        rightArrowBaseScale = rightArrow.localScale;

        UpdateCardData();
        SetInitialPositions();

        // ページインジケーターを初期化する。
        pageIndicator.Initialize(stageDatabase.Areas.Length);
        pageIndicator.SetCurrentIndex(currentIndex, false);

        if (lockMessageText != null)
        {
            lockMessageText.gameObject.SetActive(false);
        }
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
        if (lockMessageText != null)
            lockMessageText.gameObject.SetActive(false);

        if (isAnimating || currentIndex <= 0)
            return;

        isAnimating = true;
        PlayArrowAnimation(leftArrow, leftArrowBaseScale);

        PlayCardSlide(
            0f,
            cardSpacing,
            cardSpacing * 2f,
            cardScale,
            sideCardScale,
            sideCardScale,
            1f,
            0.5f,
            0f,
            FinishPreviousTransition);
    }

    /// <summary>
    /// 次のステージへカードをスライドさせる。
    /// </summary>
    public void SelectNext()
    {
        if (lockMessageText != null)
            lockMessageText.gameObject.SetActive(false);

        if (isAnimating || currentIndex >= stageDatabase.Areas.Length - 1)
            return;

        isAnimating = true;
        PlayArrowAnimation(rightArrow, rightArrowBaseScale);

        PlayCardSlide(
            -cardSpacing * 2f,
            -cardSpacing,
            0f,
            sideCardScale,
            sideCardScale,
            cardScale,
            0f,
            0.5f,
            1f,
            FinishNextTransition);
    }

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

        RefreshCardsAfterTransition();

        // 選択中のカードと右側のカードを定位置に置く。
        SetCardPosition(currentRect, 0f, cardScale, 1f);

        SetCardPosition(
            nextRect,
            cardSpacing,
            sideCardScale,
            currentIndex < stageDatabase.Areas.Length - 1 ? 0.5f : 0f);

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

        CompleteCardTransition();
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

        RefreshCardsAfterTransition();

        // 選択中のカードと左側のカードを定位置に置く。
        SetCardPosition(
            previousRect,
            -cardSpacing,
            sideCardScale,
            currentIndex > 0 ? 0.5f : 0f);

        SetCardPosition(currentRect, 0f, cardScale, 1f);

        // 新しく右側に登場するカードは、透明な状態から移動させる。
        if (currentIndex < stageDatabase.Areas.Length - 1)
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

        CompleteCardTransition();
    }

    // カードの役割を入れ替えた後、RectTransformを取得し直す。
    private void RefreshRectReferences()
    {
        previousRect = previousCard.GetComponent<RectTransform>();
        currentRect = currentCard.GetComponent<RectTransform>();
        nextRect = nextCard.GetComponent<RectTransform>();
    }

    // 現在の選択位置に合わせて3枚のカードを更新する。
    // 範囲外のステージは空欄にする。
    private void UpdateCardData()
    {
        string[] sceneNames = GetSceneNames();

        // 左側のカード。
        if (currentIndex > 0)
        {
            int index = currentIndex - 1;
            StageAreaData area = stageDatabase.Areas[index];

            StageAreaStatus status =
                StageProgressManager.GetAreaStatus(index, sceneNames);

            previousCard.SetData(
                area.displayName,
                area.screenshot,
                status);
        }
        else
        {
            previousCard.SetData("", null, StageAreaStatus.Unplayed);
        }

        // 中央のカード。
        StageAreaData currentArea = stageDatabase.Areas[currentIndex];

        StageAreaStatus currentStatus =
            StageProgressManager.GetAreaStatus(currentIndex, sceneNames);

        currentCard.SetData(
            currentArea.displayName,
            currentArea.screenshot,
            currentStatus);

        // 右側のカード。
        if (currentIndex < stageDatabase.Areas.Length - 1)
        {
            int index = currentIndex + 1;
            StageAreaData area = stageDatabase.Areas[index];

            StageAreaStatus status =
                StageProgressManager.GetAreaStatus(index, sceneNames);

            nextCard.SetData(
                area.displayName,
                area.screenshot,
                status);
        }
        else
        {
            nextCard.SetData("", null, StageAreaStatus.Unplayed);
        }
    }

    // 各カードの位置、大きさ、透明度と矢印の表示を設定する。
    private void SetInitialPositions()
    {
        bool hasPrevious = currentIndex > 0;
        bool hasNext = currentIndex < stageDatabase.Areas.Length - 1;

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
    /// 中央に表示されているエリアのシーンへ遷移する。
    /// </summary>
    public void ConfirmSelection()
    {
        if (stageDatabase.Areas == null ||
            stageDatabase.Areas.Length == 0 ||
            isAnimating)
        {
            return;
        }

        // ロック中のエリアは決定できない。
        if (!StageProgressManager.IsAreaUnlocked(
            currentIndex,
            GetSceneNames()))
        {
            if (lockMessageText != null)
            {
                lockMessageText.text =
                    "前のエリアをクリアすると開放されます";

                lockMessageText.gameObject.SetActive(true);
            }

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

        string sceneName = stageDatabase.Areas[currentIndex].sceneName;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError(
                "遷移先のシーン名が設定されていません。",
                this);
            return;
        }

        if (lockMessageText != null)
        {
            lockMessageText.gameObject.SetActive(false);
        }

        isAnimating = true;

        transitionManager.TransitionToScene(
            sceneName,
            transitionType);
    }

}
