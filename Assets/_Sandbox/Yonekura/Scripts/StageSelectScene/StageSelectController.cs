using DG.Tweening;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine.SceneManagement;

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

    [Tooltip("新しく登場するステージを表示するカードです。")]
    [SerializeField] private StageCardView additionalCard;

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

    [Tooltip("中央に到着したときの最大拡大倍率です。")]
    [SerializeField, Min(1f)]
    private float centerPeakScaleMultiplier = 1.12f;

    [Tooltip("最大サイズから通常の中央サイズに戻るまでの時間です。")]
    [SerializeField, Min(0f)]
    private float centerScaleDownDuration = 0.15f;

    [Tooltip("矢印アニメーション時の最大拡大倍率です。")]
    [SerializeField, Range(1f, 2f)]
    private float arrowScaleMultiplier = 1.3f;

    [Tooltip("矢印が拡大・縮小するアニメーションの時間です。")]
    [SerializeField, Min(0.01f)]
    private float arrowAnimationDuration = 0.12f;

    [Header("入力")]
    [Tooltip("スティックを倒したと判定する入力値のしきい値です。")]
    [SerializeField, Range(0.1f, 1f)]
    private float stickThreshold = 0.7f;

    [Tooltip("スティックをニュートラルに戻したと判定する入力値のしきい値です。")]
    [SerializeField, Range(0f, 0.5f)]
    private float resetThreshold = 0.3f;

    [Header("連続入力")]
    [Tooltip("長押ししてから連続移動を開始するまでの時間です。")]
    [SerializeField, Min(0f)]
    private float holdStartDelay = 0.35f;

    [Tooltip("連続移動するときの入力間隔です。")]
    [SerializeField, Min(0.01f)]
    private float repeatInterval = 0.15f;

    [Header("長押し加速")]
    [Tooltip("長押し時に使用するスライド速度の最大倍率です。")]
    [SerializeField, Range(1f, 5f)]
    private float holdMaxSpeedMultiplier = 2.5f;

    [Header("ロックメッセージ")]
    [Tooltip("ロック中のエリアを決定したときに理由を表示するTextMeshProを設定します。")]
    [SerializeField] private TMP_Text lockMessageText;

    [Header("ページインジケーター")]
    [Tooltip("エリアの選択位置を表示するUI")]
    [SerializeField] private AreaPageIndicator pageIndicator;

    [Header("インジケーター開始タイミング")]
    [Tooltip("エリア名インジケーターの開始遅延")]
    [SerializeField] private float infoStartDelay = 0f;

    [Tooltip("ページインジケーターの開始遅延")]
    [SerializeField] private float pageStartDelay = 0f;

    [Header("ワープ演出")]
    [Tooltip("ステージ決定後のワープ演出を管理する。")]
    [SerializeField] private StageWarpController stageWarpController;

    [Header("エリア・シーン名表示")]
    [SerializeField] private StageInfoTransition stageInfoTransition;

    private Vector3 leftArrowBaseScale;
    private Vector3 rightArrowBaseScale;

    // 現在押されている方向。左は-1、右は1、未入力は0。
    private int heldDirection;

    // 長押しと連続入力の時間管理。
    private float repeatTimer;

    // スティックが入力状態に入っているか。
    private bool isStickEngaged;

    // 長押し判定後に移動した回数。
    private int holdSlideCount;

    // 長押しによる連続入力中かどうか。
    private bool isHoldRepeating;

    // 現在のカード移動に使用する時間。
    private float currentSlideDuration;

    // 現在実行中のカード移動アニメーション。
    private Sequence cardSlideSequence;

    // 現在実行中の移動方向。左は-1、右は1。
    private int activeSlideDirection;

    // 4枚のカードをまとめて管理する。
    private StageCardView[] cards;

    // 各カードに現在割り当てているステージ番号。
    private int[] cardStageIndices;

    // 現在の移動先。アニメーション完了前でも更新する。
    private int targetIndex;

    //カード移動中やワープ演出中の入力制御
    private bool isAnimating;

    // ステージのシーンを読み込み中かどうか。
    private bool isLoadingScene;

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
        for (int i = 0; i < cards.Length; i++)
        {
            UpdateCardData(cards[i], cardStageIndices[i]);
        }
    }

    // 指定方向のステージ配置へ4枚のカードを移動させる。
    private void PlayCardSlide(int destinationIndex, int direction)
    {
        cardSlideSequence?.Kill();

        // 右移動なら選択位置の左側から右側まで、
        // 左移動なら左側に新しいカードが登場する配置にする。
        int[] desiredIndices = direction > 0
            ? new[]
            {
            destinationIndex - 1,
            destinationIndex,
            destinationIndex + 1,
            destinationIndex + 2
            }
            : new[]
            {
            destinationIndex - 2,
            destinationIndex - 1,
            destinationIndex,
            destinationIndex + 1
            };

        bool[] usedCards = new bool[cards.Length];
        int[] assignedCards = new int[cards.Length];

        // すでに表示しているステージのカードは、そのまま再利用する。
        for (int i = 0; i < desiredIndices.Length; i++)
        {
            assignedCards[i] = -1;

            for (int j = 0; j < cards.Length; j++)
            {
                if (!usedCards[j] &&
                    cardStageIndices[j] == desiredIndices[i])
                {
                    assignedCards[i] = j;
                    usedCards[j] = true;
                    break;
                }
            }
        }

        // 表示されていないステージは、余っているカードに割り当てる。
        for (int i = 0; i < desiredIndices.Length; i++)
        {
            if (assignedCards[i] >= 0)
                continue;

            for (int j = 0; j < cards.Length; j++)
            {
                if (!usedCards[j])
                {
                    assignedCards[i] = j;
                    usedCards[j] = true;
                    break;
                }
            }
        }

        cardSlideSequence = DOTween.Sequence();

        RectTransform centerCardRect = null;
        float centerTargetScale = 1f;

        // 各カードの移動先を設定する。
        for (int i = 0; i < desiredIndices.Length; i++)
        {
            int cardSlot = assignedCards[i];
            int stageIndex = desiredIndices[i];

            StageCardView card = cards[cardSlot];
            RectTransform rect = card.GetComponent<RectTransform>();

            bool isNewCard = cardStageIndices[cardSlot] != stageIndex;

            // 新しいステージを割り当てる場合は,透明にしてから表示情報を変更する。
            if (isNewCard)
            {
                card.Group.alpha = 0f;

                cardStageIndices[cardSlot] = stageIndex;
                UpdateCardData(card, stageIndex);

                // 画面外から登場する位置に配置する。
                float startX = direction > 0
                    ? (stageIndex - currentIndex) * cardSpacing
                    : (stageIndex - currentIndex - 1) * cardSpacing;

                rect.anchoredPosition = new Vector2(startX, 0f);
                rect.localScale = Vector3.one * sideCardScale;
            }

            float targetX =
                (stageIndex - destinationIndex) * cardSpacing;

            int distance = Mathf.Abs(stageIndex - destinationIndex);

            float targetScale = distance == 0
                ? cardScale
                : sideCardScale;

            float targetAlpha =
                GetCardAlpha(stageIndex, destinationIndex);

            cardSlideSequence.Join(
                rect.DOAnchorPosX(targetX, currentSlideDuration)
                    .SetEase(Ease.OutCubic));

            if (distance == 0)
            {
                float peakScale = targetScale * centerPeakScaleMultiplier;

                centerCardRect = rect;
                centerTargetScale = targetScale;

                cardSlideSequence.Join(
                    rect.DOScale(peakScale, currentSlideDuration)
                        .SetEase(Ease.OutCubic)
                            );
            }
            else
            {
                cardSlideSequence.Join(
                    rect.DOScale(targetScale, currentSlideDuration)
                );
            }
            cardSlideSequence.Join(
                card.Group.DOFade(targetAlpha, currentSlideDuration)
                .SetEase(Ease.OutCubic));
        }

        // 中央到着後、通常サイズに縮小する。
        if (centerCardRect != null)
        {
            cardSlideSequence.Append(
                centerCardRect.DOScale(cardScale, centerScaleDownDuration)
                    .SetEase(Ease.OutCubic)
            );
        }

        cardSlideSequence
            .OnComplete(() => CompleteCardTransition(destinationIndex));
    }

    // カード移動の完了処理を行う。
    private void CompleteCardTransition(int destinationIndex)
    {
        currentIndex = destinationIndex;
        targetIndex = destinationIndex;

        RefreshCardRoles();
        UpdateArrowVisibility();

        isAnimating = false;
        activeSlideDirection = 0;
        cardSlideSequence = null;

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
            additionalCard == null ||
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

        cards = new StageCardView[]
        {
            previousCard,
            currentCard,
            nextCard,
            additionalCard
　　　　　};

        cardStageIndices = new int[cards.Length];

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null || cards[i].Group == null)
            {
                Debug.LogError(
                    "4枚すべてのカードとCanvasGroupを設定してください。",
                    this);

                enabled = false;
                return;
            }
        }

        targetIndex = currentIndex;

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

        InitializeCards();

        StageAreaData initialArea = stageDatabase.Areas[currentIndex];
        stageInfoTransition.Initialize(initialArea.displayName);

        //SetInitialPositions();

        // ページインジケーターを初期化する。
        pageIndicator.Initialize(stageDatabase.Areas.Length);
        pageIndicator.SetCurrentIndex(currentIndex, false);

        if (lockMessageText != null)
        {
            lockMessageText.gameObject.SetActive(false);
        }
    }

    // 長押しによる移動回数に応じてスライド時間を計算する。
    private float GetSlideDuration()
    {
        // 長押し中でなければ通常速度にする。
        if (!isHoldRepeating)
        {
            return slideDuration;
        }

        // 長押しによる移動回数を加算する。
        holdSlideCount = Mathf.Min(holdSlideCount + 1, 4);

        // 1枚目から4枚目にかけて速度を段階的に上げる。
        float progress = (holdSlideCount - 1) / 3f;

        float speedMultiplier = Mathf.Lerp(
            1f,
            holdMaxSpeedMultiplier,
            progress
        );

        // 速度倍率に応じて移動時間を短くする。
        return slideDuration / speedMultiplier;
    }

    private void Update()
    {
        // カード移動中も左右入力は読み取る。
        ReadDirectionInput();

        // 決定入力はカード移動中には受け付けない。
        if (!isAnimating)
        {
            ReadConfirmInput();
        }
    }

    private void OnDestroy()
    {
        cardSlideSequence?.Kill();

        if (cards != null)
        {
            foreach (StageCardView card in cards)
            {
                if (card == null)
                    continue;

                card.transform.DOKill();

                if (card.Group != null)
                    card.Group.DOKill();
            }
        }

        if (leftArrow != null)
            leftArrow.DOKill();

        if (rightArrow != null)
            rightArrow.DOKill();

        stageInfoTransition?.KillTransition();
    }

    // スティックとキーボードの左右入力を読み取る。
    private void ReadDirectionInput()
    {
        Gamepad gamepad = Gamepad.current;
        Keyboard keyboard = Keyboard.current;

        int direction = 0;

        // キーボード入力を優先する。
        bool leftPressed = keyboard != null &&
            (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed);

        bool rightPressed = keyboard != null &&
            (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed);

        if (leftPressed != rightPressed)
        {
            direction = leftPressed ? -1 : 1;
        }
        else if (!leftPressed && !rightPressed && gamepad != null)
        {
            float horizontal = gamepad.leftStick.ReadValue().x;
            float absHorizontal = Mathf.Abs(horizontal);

            if (isStickEngaged)
            {
                // 入力解除のしきい値を下回るまで入力状態を維持する。
                if (absHorizontal <= resetThreshold)
                {
                    isStickEngaged = false;
                }
                else
                {
                    direction = horizontal < 0f ? -1 : 1;
                }
            }
            else if (absHorizontal >= stickThreshold)
            {
                // 入力開始のしきい値を超えたら入力状態にする。
                isStickEngaged = true;
                direction = horizontal < 0f ? -1 : 1;
            }
        }

        // 移動できない方向への入力は無効にする。
        if ((direction < 0 && targetIndex <= 0) ||
            (direction > 0 && targetIndex >= stageDatabase.Areas.Length - 1))
        {
            heldDirection = 0;
            repeatTimer = 0f;
            //bufferedDirection = 0;

            isHoldRepeating = false;
            holdSlideCount = 0;

            return;
        }

        // 入力が解除されたら長押し時間をリセットする。
        if (direction == 0)
        {
            heldDirection = 0;
            repeatTimer = 0f;

            isHoldRepeating = false;
            holdSlideCount = 0;

            return;
        }

        // 入力方向が変わった瞬間に1回移動する。
        if (heldDirection != direction)
        {
            heldDirection = direction;
            repeatTimer = holdStartDelay;

            // 新しい方向への入力なので加速状態をリセットする。
            isHoldRepeating = false;
            holdSlideCount = 0;

            RequestDirection(direction);
            return;
        }

        // 同じ方向の入力が続いている場合、連続入力の時間を計測する。
        repeatTimer -= Time.deltaTime;

        if (repeatTimer <= 0f)
        {
            repeatTimer = repeatInterval;

            // 長押し判定を開始する。
            isHoldRepeating = true;

            RequestDirection(direction);
        }
    }

    // 指定された方向への移動を要求する。
    private void RequestDirection(int direction)
    {
        // 現在の移動先を基準に、次の移動先を決める
        int destination = targetIndex + direction;

        // ステージの範囲外なら移動しない
        if (destination < 0 || destination >= stageDatabase.Areas.Length)
            return;

        StartCardTransition(destination, direction);
    }

    // カード、エリア名、ページインジケーターの移動を開始する。
    private void StartCardTransition(int destinationIndex, int direction)
    {
        // 現在のカードTweenを停止する。
        cardSlideSequence?.Kill();

        isAnimating = true;
        activeSlideDirection = direction;
        targetIndex = destinationIndex;

        currentSlideDuration = GetSlideDuration();

        // ロックメッセージを非表示にする。
        if (lockMessageText != null)
            lockMessageText.gameObject.SetActive(false);

        // 確定済みのエリア名に戻してから、最新の移動先へ切り替える。
        string currentAreaName =
            stageDatabase.Areas[currentIndex].displayName;

        stageInfoTransition.ResetTo(currentAreaName);

        if (destinationIndex != currentIndex)
        {
            stageInfoTransition.PlayTransition(
                stageDatabase.Areas[destinationIndex].displayName,
                direction,
                infoStartDelay);
        }

        // ページインジケーターを最新の移動先へ切り替える。
        pageIndicator.SetCurrentIndex(
            destinationIndex,
            true,
            pageStartDelay);

        // 移動方向の矢印をアニメーションさせる。
        if (direction < 0)
            PlayArrowAnimation(leftArrow, leftArrowBaseScale);
        else
            PlayArrowAnimation(rightArrow, rightArrowBaseScale);

        PlayCardSlide(destinationIndex, direction);
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
        RequestDirection(-1);
    }

    /// <summary>
    /// 次のステージへカードをスライドさせる。
    /// </summary>
    public void SelectNext()
    {
        RequestDirection(1);
    }

    // カードの役割を入れ替えた後、RectTransformを取得し直す。
    private void RefreshRectReferences()
    {
        previousRect = previousCard.GetComponent<RectTransform>();
        currentRect = currentCard.GetComponent<RectTransform>();
        nextRect = nextCard.GetComponent<RectTransform>();
    }

    // 4枚のカードにステージ情報を割り当て、初期配置する。
    private void InitializeCards()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            int stageIndex = currentIndex + i - 1;

            cardStageIndices[i] = stageIndex;

            UpdateCardData(cards[i], stageIndex);

            RectTransform rect = cards[i].GetComponent<RectTransform>();

            float x = (stageIndex - currentIndex) * cardSpacing;
            float alpha = GetCardAlpha(stageIndex, currentIndex);

            SetCardPosition(
                rect,
                x,
                Mathf.Abs(stageIndex - currentIndex) == 0
                    ? cardScale
                    : sideCardScale,
                alpha);
        }

        RefreshCardRoles();
        UpdateArrowVisibility();
    }

    // 指定したステージの情報をカードに設定する。
    private void UpdateCardData(StageCardView card, int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= stageDatabase.Areas.Length)
        {
            card.SetData("", null, StageAreaStatus.Unplayed);
            return;
        }

        StageAreaData area = stageDatabase.Areas[stageIndex];

        StageAreaStatus status =
            StageProgressManager.GetAreaStatus(stageIndex, GetSceneNames());

        card.SetData(
            area.displayName,
            area.screenshot,
            status);
    }

    // 中央は不透明、左右は半透明、画面外の予備カードは透明にする。
    private float GetCardAlpha(int stageIndex, int centerIndex)
    {
        if (stageIndex < 0 || stageIndex >= stageDatabase.Areas.Length)
            return 0f;

        int distance = Mathf.Abs(stageIndex - centerIndex);

        if (distance == 0)
            return 1f;

        if (distance == 1)
            return 0.5f;

        return 0f;
    }

    // 現在の選択位置に合わせてカードの役割を更新する。
    private void RefreshCardRoles()
    {
        previousCard = FindCard(currentIndex - 1);
        currentCard = FindCard(currentIndex);
        nextCard = FindCard(currentIndex + 1);

        RefreshRectReferences();
    }

    // 指定ステージを表示しているカードを取得する。
    private StageCardView FindCard(int stageIndex)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            if (cardStageIndices[i] == stageIndex)
                return cards[i];
        }

        return null;
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
    /// ステージのシーンを事前読み込みし、読み込み完了後に切り替える。
    /// </summary>
    private IEnumerator LoadStageScene(string sceneName)
    {
        isLoadingScene = true;

        // 遷移先のシーンを非同期で読み込む。
        AsyncOperation operation =
            SceneManager.LoadSceneAsync(sceneName);

        if (operation == null)
        {
            Debug.LogError(
                $"シーンの読み込みを開始できませんでした: {sceneName}",
                this);

            isLoadingScene = false;
            isAnimating = false;
            yield break;
        }

        // 読み込み完了後もシーンの切り替えを一時停止する。
        operation.allowSceneActivation = false;

        // 読み込みが90%に達するまで待機する。
        while (operation.progress < 0.9f)
        {
            yield return null;
        }

        // 読み込み完了後、シーンを有効化する。
        operation.allowSceneActivation = true;

        // シーンの切り替えが完了するまで待機する。
        yield return new WaitUntil(() => operation.isDone);
    }

    /// <summary>
    /// 中央に表示されているエリアのシーンへ遷移する。
    /// </summary>
    /// <summary>
    /// 中央に表示されているエリアのシーンへ遷移する。
    /// </summary>
    public void ConfirmSelection()
    {
        if (stageDatabase == null ||
            stageDatabase.Areas == null ||
            stageDatabase.Areas.Length == 0 ||
            isAnimating ||
            isLoadingScene)
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

        // ワープ演出の設定を確認する。
        if (stageWarpController == null)
        {
            Debug.LogError(
                "StageWarpControllerが設定されていません。",
                this);
            return;
        }

        string sceneName =
            stageDatabase.Areas[currentIndex].sceneName;

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

        // 決定後の入力を受け付けない。
        isAnimating = true;

        // CurrentCardのワープ演出を開始する。
        stageWarpController.PlayWarp(
            currentCard.GetComponent<RectTransform>(),
            () =>
            {
                // 拡大完了後にステージの読み込みを開始する。
                StartCoroutine(LoadStageScene(sceneName));
            });
    }
}
