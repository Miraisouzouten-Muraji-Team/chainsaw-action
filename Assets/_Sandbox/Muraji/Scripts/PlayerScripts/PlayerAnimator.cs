using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    [Header("落下アニメーション")]
    [Tooltip("落下モーションのステートのフルパスです。空欄なら落下モーションを使用しません。")]
    [SerializeField] private string fallState = "";
    [Tooltip("空中で最も高かった位置から、この距離だけ落下すると落下モーションを開始します。")]
    [SerializeField, Min(0f)] private float minimumFallDistance = 0.5f;

    private SensorPlayerController fallController;
    private Rigidbody fallBody;
    private bool jumpLeftGround;
    private bool combinedJumpActive;
    private bool jumpHeld;
    private float speedBeforeJumpHold;
    private int combinedJumpHash;
    private bool fallActive;
    private bool fallHeld;
    private bool fallLanding;
    private bool fallAirborne;
    private float fallPeakHeight;
    private float speedBeforeFallHold;
    private int fallHash;

    // 操作と物理更新の結果を確認し、落下開始・着地・再生終了を処理する。
    private void LateUpdate()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        UpdateRequestedActionTransition();
        if (fallController == null) fallController = GetComponentInParent<SensorPlayerController>();
        if (fallBody == null && fallController != null) fallBody = fallController.GetComponent<Rigidbody>();
        if (fallController == null || fallBody == null) return;

        bool grounded = fallController.TryGetGroundSupport(out _);
        // 入力更新後にも空中の走り条件を解除する。
        if (!grounded)
            animator.SetFloat("Speed", 0f);
        float height = fallBody.position.y;
        if (grounded || !fallAirborne) fallPeakHeight = height;
        else fallPeakHeight = Mathf.Max(fallPeakHeight, height);
        fallAirborne = !grounded;

        SynchronizeAnimatorWeakAttackExit();
        UpdateDiggingAnimation();

        if (attackActive || IsDiggingAnimationActive || isHurtAnimationActive || requestedActionStateHash != 0 ||
            IsWeakAttackHeld)
        {
            CancelFallAnimation();
            return;
        }

        // 衝突硬直で上昇が打ち切られたときは、上昇姿勢の固定だけを解除する。
        if (fallController.IsImpactStunned) ResumeJumpPlayback();

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (combinedJumpActive)
        {
            if (state.fullPathHash != combinedJumpHash)
            {
                // ジャンプへの遷移中は接続先のステートを待つ。
                if (animator.IsInTransition(0) &&
                    animator.GetNextAnimatorStateInfo(0).fullPathHash == combinedJumpHash) return;
                CancelFallAnimation();
                return;
            }
            if (!grounded)
                jumpLeftGround = true;
            if (grounded && jumpLeftGround)
            {
                ResumeJumpPlayback();
                fallLanding = true;
                ResumeFallPlayback();
                if (state.normalizedTime >= 1f)
                {
                    CancelFallAnimation();
                    animator.Play(idleState, 0, 0f);
                }
            }
            else if (fallBody.linearVelocity.y <= 0f) ResumeJumpPlayback();
            return;
        }
        if (fallActive)
        {
            if (state.fullPathHash != fallHash || animator.IsInTransition(0))
            {
                CancelFallAnimation();
                return;
            }
            if (grounded)
            {
                fallLanding = true;
                ResumeFallPlayback();
            }
            else if (fallLanding)
            {
                CancelFallAnimation();
                return;
            }
            if (fallLanding && state.normalizedTime >= 1f)
            {
                CancelFallAnimation();
                animator.Play(idleState, 0, 0f);
            }
            return;
        }

        // 二段ジャンプは最後まで再生してから落下モーションへ移る。
        if (state.fullPathHash == Animator.StringToHash(doubleJumpState) && state.normalizedTime < 1f)
            return;
        if (grounded || fallBody.linearVelocity.y >= -0.01f ||
            fallPeakHeight - height < minimumFallDistance || animator.IsInTransition(0)) return;
        if (string.IsNullOrEmpty(fallState)) return;
        fallHash = Animator.StringToHash(fallState);
        if (!animator.HasState(0, fallHash)) return;
        fallActive = true;
        fallLanding = false;
        animator.Play(fallHash, 0, 0f);
    }

    // 上昇中のイベントで姿勢を固定し、下降開始時に続きを再生する。
    public void OnJumpHoldFrame()
    {
        if (!combinedJumpActive || animator == null || fallBody == null ||
            fallLanding || jumpHeld || fallHeld ||
            animator.GetCurrentAnimatorStateInfo(0).fullPathHash != combinedJumpHash) return;

        // イベントまでに下降へ切り替わった場合は、そのまま落下側へ再生を進める。
        if (fallBody.linearVelocity.y <= 0f) return;
        speedBeforeJumpHold = animator.speed;
        jumpHeld = true;
        animator.speed = 0f;
    }

    // 上昇中の固定を解除して停止前の再生速度へ戻す。
    private void ResumeJumpPlayback()
    {
        if (!jumpHeld) return;
        jumpHeld = false;
        if (animator != null) animator.speed = speedBeforeJumpHold;
    }

    // 落下クリップの停止したいフレームに置くAnimation Eventから呼び出す。
    public void OnFallHoldFrame()
    {
        if (!fallActive || fallLanding || fallHeld || animator == null ||
            fallController == null ||
            (fallController.TryGetGroundSupport(out _)) ||
            animator.GetCurrentAnimatorStateInfo(0).fullPathHash != fallHash) return;
        speedBeforeFallHold = animator.speed;
        fallHeld = true;
        animator.speed = 0f;
    }

    // 落下モーションの停止前の再生速度へ戻す。
    private void ResumeFallPlayback()
    {
        if (!fallHeld) return;
        fallHeld = false;
        if (animator != null) animator.speed = speedBeforeFallHold;
    }

    // 別の行動の開始前に落下モーションの停止を解除する。
    private void CancelFallAnimation()
    {
        ResumeJumpPlayback();
        ResumeFallPlayback();
        combinedJumpActive = false;
        fallActive = false;
        fallLanding = false;
    }

    Animator animator;

    [SerializeField] ChainsawAttack attackHitBox;

    [Header("Animatorステートのフルパス")]
    [SerializeField] string slash1State = "Base Layer.Slash1";
    [Tooltip("空中で弱攻撃を開始したときに再生するステートのフルパスです。例：Base Layer.AirSlash。空欄なら地上用のステートを使用します。")]
    [SerializeField] private string airSlashState = "";
    [SerializeField] string slash2State = "Base Layer.Slash2";
    [SerializeField] string slash3State = "Base Layer.Slash3";
    [SerializeField] string idleState = "Base Layer.Idle";

    private const string WEAK_ACTION_TARGET_PARAMETER = "WeakActionTarget";
    private const string WEAK_ACTION_REQUEST_TRIGGER = "WeakActionRequested";
    private const string WEAK_ATTACK_EXIT_PARAMETER = "WeakAttackExit";
    private const int WEAK_ATTACK_EXIT_BLOCKED = -1;
    private const int WEAK_ATTACK_EXIT_IDLE = 0;
    private const int WEAK_ATTACK_EXIT_DIGGING = 1;
    private const float ACTION_FALLBACK_BLEND_SECONDS = 0.05f;

    // AnimatorのInt条件で、トリガー受信時の遷移先を選ぶ。
    private enum WeakActionTarget
    {
        Idle = 0,
        GroundAttack = 1,
        AirAttack = 2,
        FloorDigging = 3,
        WallDigging = 4,
        CeilingDigging = 5,
        EnemyDigging = 6
    }

    public bool UsesAnimatorWeakAttackExit { get; private set; }

    private int requestedActionStateHash;
    private int requestedActionFrame;
    private double requestedActionFixedTime;
    private bool actionTransitionWarningLogged;

    [Header("攻撃トレイル")]
    [SerializeField] SlashTrailEffect slashTrailEffect;

    [Header("被弾アニメーション")]
    [Tooltip("やられモーションを設定したAnimatorステートのフルパスです。例：Base Layer.Hurt")]
    [SerializeField] private string hurtState = "Base Layer.Hurt";

    private const string HURT_PARAMETER = "IsHurt";
    private bool isHurtAnimationActive;
    private bool rootMotionBeforeHurt;
    public bool IsHurtAnimationActive => isHurtAnimationActive;

    public bool IsWeakAttackHeld { get; private set; }
    private float weakAttackPreviousSpeed;
    private bool weakAttackPreviousRootMotion;
    private SensorPlayerController weakAttackController;

    // 弱攻撃が最初に命中した姿勢を維持し、プレイヤーの移動も止める。
    public bool HoldWeakAttackPose()
    {
        if (IsWeakAttackHeld || !attackActive || animator == null ||
            !animator.isActiveAndEnabled || !TryGetAttackProgress(out _)) return false;
        weakAttackPreviousSpeed = animator.speed;
        weakAttackPreviousRootMotion = animator.applyRootMotion;
        IsWeakAttackHeld = true;
        SetWeakAttackExit(false, false);
        animator.speed = 0f;
        animator.applyRootMotion = false;
        if (weakAttackController == null) weakAttackController = GetComponent<SensorPlayerController>();
        if (weakAttackController != null) weakAttackController.BeginWeakAttackHold();
        return true;
    }

    // 攻撃の停止前の再生速度とルートモーションを戻し、同じ位置から再生する。
    public void ResumeWeakAttackPose()
    {
        if (!IsWeakAttackHeld) return;
        IsWeakAttackHeld = false;
        if (animator != null)
        {
            animator.speed = weakAttackPreviousSpeed;
            animator.applyRootMotion = weakAttackPreviousRootMotion;
        }
        if (weakAttackController != null) weakAttackController.EndWeakAttackHold();
    }

    int activeHash;
    bool attackActive;

    // ===== 食い込みアニメーション =====

    private const string DIGGING_PLAYBACK_SPEED = "DiggingPlaybackSpeed";

    private enum DiggingAnimationPhase
    {
        None,
        Starting,
        Holding,
        Playing,
        Ending
    }

    [Header("食い込みアニメーション")]
    [SerializeField]
    private string diggingState = "Base Layer.Digging";
    [Tooltip("空欄または未作成ならDiggingを使用")]
    [SerializeField] private string wallDiggingState = "";
    [SerializeField] private string ceilingDiggingState = "";
    [SerializeField] private string enemyDiggingState = "";
    [SerializeField] private string jumpState = "Base Layer.Jump";

    [Header("追加ジャンプアニメーション")]
    [Tooltip("二段ジャンプ時に再生するAnimatorステートのフルパス")]
    [SerializeField] private string doubleJumpState = "Base Layer.DoubleJump";

    [Tooltip("壁ジャンプ時に再生するAnimatorステートのフルパス")]
    [SerializeField] private string wallJumpState = "Base Layer.WallJump";

    [Tooltip("敵食い込みから飛び出すときに全方向で使うAnimatorステートのフルパスです。例：Base Layer.EnemyDiggingJump。空欄または無効な場合は従来のモーションを使用します。")]
    [SerializeField] private string enemyDiggingJumpState = "";

    private DiggingAnimationPhase diggingAnimationPhase;
    private int diggingStateHash;
    private int diggingStartFrame;
    private bool blendingFromAttackToDigging;

    // 終了モーション中もtrue。
    // 実際に食い込んでいるかどうかとは別の、演出用の状態。
    public bool IsDiggingAnimationActive =>
        diggingAnimationPhase != DiggingAnimationPhase.None;


    // 食い込み開始時にControllerから呼ぶ。
    public bool StartDiggingAnimation()
    {
        return StartDiggingAnimation(ChainsawSurface.Floor);
    }

    public bool StartDiggingAnimation(ChainsawSurface surface)
    {
        return StartDiggingAnimation(surface, false);
    }

    // 既存の呼び出しからは、通常どおり食い込みモーションを開始する。
    public bool StartDiggingAnimation(ChainsawSurface surface, bool blendFromAttack)
    {
        return StartDiggingAnimation(surface, blendFromAttack, false);
    }

    // 食い込みモーションを開始するか、Animatorが始めた遷移を引き継ぐ。
    public bool StartDiggingAnimation(ChainsawSurface surface, bool blendFromAttack, bool adoptAnimatorTransition)
    {
        CancelFallAnimation();
        if (isHurtAnimationActive) return false;
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null || !animator.isActiveAndEnabled)
        {
            return false;
        }

        // 食い込みのブレンド中に接触先が確定した場合も、姿勢の切り替えを滑らかにする。
        bool blend = blendFromAttack || blendingFromAttackToDigging;
        if (IsDiggingAnimationActive)
        {
            CancelDiggingAnimation();
        }

        bool hasPlaybackSpeed = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == DIGGING_PLAYBACK_SPEED && parameter.type == AnimatorControllerParameterType.Float)
                hasPlaybackSpeed = true;
        }
        if (!hasPlaybackSpeed)
        {
            Debug.LogError("AnimatorにFloatのDiggingPlaybackSpeedを追加し、DiggingステートのSpeed Multiplierに設定してください。", this);
            return false;
        }
        string path = surface == ChainsawSurface.Wall ? wallDiggingState :
            surface == ChainsawSurface.Ceiling ? ceilingDiggingState :
            surface == ChainsawSurface.Enemy ? enemyDiggingState : diggingState;
        if (string.IsNullOrEmpty(path) || !animator.HasState(0, Animator.StringToHash(path)))
            path = diggingState;
        diggingStateHash = Animator.StringToHash(path);

        if (!animator.HasState(0, diggingStateHash) ||
            !animator.HasState(0, Animator.StringToHash(idleState)))
        {
            Debug.LogError(
                "食い込みまたはIdleのステート名を確認してください。",
                this
            );

            return false;
        }

        // 以前の食い込みTriggerを残さない。
        animator.ResetTrigger("Wedgie");

        animator.SetFloat(DIGGING_PLAYBACK_SPEED, 1f);

        diggingAnimationPhase = DiggingAnimationPhase.Starting;
        diggingStartFrame = Time.frameCount;
        blendingFromAttackToDigging = blend;

        animator.SetBool("IsDigging", true);
        // Animatorがすでに開始した遷移を引き継ぎ、再生位置とブレンドを保つ。
        if (adoptAnimatorTransition && TryGetActionState(diggingStateHash, out _))
        {
            return true;
        }

        if (blend)
        {
            // 対象別のステートがなければ、通常の食い込みステートへ遷移する。
            WeakActionTarget target = path == diggingState ? WeakActionTarget.FloorDigging : surface switch
            {
                ChainsawSurface.Wall => WeakActionTarget.WallDigging,
                ChainsawSurface.Ceiling => WeakActionTarget.CeilingDigging,
                ChainsawSurface.Enemy => WeakActionTarget.EnemyDigging,
                _ => WeakActionTarget.FloorDigging
            };
            RequestActionTransition(diggingStateHash, target);
        }
        else
        {
            ClearRequestedActionTransition();
            animator.Play(diggingStateHash, 0, 0f);
        }
        return true;
    }


    // 止めたい位置のAnimation Eventから呼ぶ。
    public void HoldDiggingAnimation(AnimationEvent animationEvent = null)
    {
        SynchronizeAnimatorWeakAttackExit();
        // 解除済みなら停止しない。
        // 停止位置より前に解除した場合にも対応する。
        if (diggingAnimationPhase != DiggingAnimationPhase.Starting)
        {
            return;
        }

        TryHoldDiggingAnimationAtEvent(animationEvent);
    }

    // 食い込み状態を維持したまま、停止中のモーションを現在位置から再開する。
    public void ResumeDiggingAnimation()
    {
        if (animator == null ||
            !animator.isActiveAndEnabled ||
            !IsDiggingAnimationActive)
        {
            return;
        }

        // Endingにしないことで、再生終了を理由にIdleへ戻さない。
        // Startingにもしないため、停止用Animation Eventで再停止しない。
        diggingAnimationPhase = DiggingAnimationPhase.Playing;

        animator.SetFloat(DIGGING_PLAYBACK_SPEED, 1f);
    }

    // 通常の食い込み解除時にControllerから呼ぶ。
    public void ReleaseDiggingAnimation()
    {
        if (!IsDiggingAnimationActive)
        {
            return;
        }

        diggingAnimationPhase = DiggingAnimationPhase.Ending;

        // Playし直さず、現在位置から続きを再生する。
        animator.SetFloat(DIGGING_PLAYBACK_SPEED, 1f);
    }


    // 被弾・死亡・ジャンプなどで、終了モーションを省略するときに呼ぶ。
    public void CancelDiggingAnimation()
    {
        ClearRequestedActionTransition();
        diggingAnimationPhase = DiggingAnimationPhase.None;
        blendingFromAttackToDigging = false;

        if (animator != null)
        {
            animator.SetFloat(DIGGING_PLAYBACK_SPEED, 1f);
            animator.SetBool("IsDigging", false);
        }
    }


    // 食い込みへ向かうブレンドを維持し、別のモーションへ出たら食い込み再生を解除する。
    private void UpdateDiggingAnimation()
    {
        if (!IsDiggingAnimationActive ||
            Time.frameCount == diggingStartFrame || requestedActionStateHash == diggingStateHash)
        {
            return;
        }

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        if (animator.IsInTransition(0) &&
            animator.GetNextAnimatorStateInfo(0).fullPathHash == diggingStateHash)
        {
            return;
        }

        blendingFromAttackToDigging = false;
        if (state.fullPathHash != diggingStateHash || animator.IsInTransition(0))
        {
            CancelDiggingAnimation();
            return;
        }

        // 終了部分を最後まで再生したらIdleへ戻す。
        if (diggingAnimationPhase == DiggingAnimationPhase.Ending &&
            state.normalizedTime >= 1f)
        {
            CancelDiggingAnimation();
            animator.Play(idleState, 0, 0f);
        }
    }


    // 攻撃・食い込みの再生状態を解除し、やられステートを先頭から再生する。
    public bool PlayHurt()
    {
        CancelFallAnimation();
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null || !animator.isActiveAndEnabled) return false;

        EndSlash(false);
        CancelDiggingAnimation();
        // 硬直中にモーションの移動量で本体が動かないよう、一時的に無効化する。
        if (!isHurtAnimationActive) rootMotionBeforeHurt = animator.applyRootMotion;
        animator.applyRootMotion = false;
        isHurtAnimationActive = true;
        animator.SetFloat("Speed", 0f);
        SetHurtParameter(true);

        // 被弾前に予約された攻撃・ジャンプの遷移を残さない。
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                animator.ResetTrigger(parameter.nameHash);
        }

        int hash = Animator.StringToHash(hurtState);
        if (string.IsNullOrEmpty(hurtState) || !animator.HasState(0, hash))
        {
            Debug.LogError("やられ：Hurt Stateに指定したステートがありません。Animatorにモーションとステートを設定してください。", this);
            return false;
        }
        animator.Play(hash, 0, 0f);
        return true;
    }

    // 硬直終了時にやられ状態を解除し、指定された場合は待機を再生する。
    public void EndHurt(bool returnToIdle)
    {
        bool wasHurt = isHurtAnimationActive;
        isHurtAnimationActive = false;
        if (wasHurt && animator != null) animator.applyRootMotion = rootMotionBeforeHurt;
        SetHurtParameter(false);
        if (!wasHurt || !returnToIdle || animator == null || !animator.isActiveAndEnabled)
            return;

        int hash = Animator.StringToHash(idleState);
        if (!animator.HasState(0, hash))
        {
            Debug.LogError("やられ：Idle Stateのフルパスを確認してください。", this);
            return;
        }
        animator.SetFloat("Speed", 0f);
        animator.Play(hash, 0, 0f);
    }

    // IsHurtのBoolが存在する場合、Animator側の遷移条件にも被弾状態を通知する。
    private void SetHurtParameter(bool value)
    {
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == HURT_PARAMETER && parameter.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(HURT_PARAMETER, value);
                return;
            }
        }
    }

    // Inspectorからの動作確認用。
    // 実行中にコンポーネントのメニューから呼び出す。
    [ContextMenu("Test/Start Digging Animation")]
    private void TestStartDiggingAnimation()
    {
        if (Application.isPlaying)
        {
            StartDiggingAnimation();
        }
    }

    [ContextMenu("Test/Release Digging Animation")]
    private void TestReleaseDiggingAnimation()
    {
        if (Application.isPlaying)
        {
            ReleaseDiggingAnimation();
        }
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public void SetSpeed(float speed)
    {
        // 移動速度を通知。
        if (fallController == null)
            fallController = GetComponentInParent<SensorPlayerController>();
        if (fallBody == null && fallController != null)
            fallBody = fallController.GetComponent<Rigidbody>();

        // 空中と離陸直後は走り用の速度をゼロにし、着地後に通常の値を渡す。
        bool airborne = fallController != null &&
            !fallController.TryGetGroundSupport(out _);
        animator.SetFloat("Speed", isHurtAnimationActive || airborne ? 0f : Mathf.Abs(speed));

        // 食い込みアニメーション中かを通知。
        // 終了モーション中もtrueを維持する。
        animator.SetBool("IsDigging", IsDiggingAnimationActive);
    }

    public void PlayJump()
    {
        // 接地判定が切り替わる前の離陸フレームから走り条件を解除する。
        if (animator != null) animator.SetFloat("Speed", 0f);
        CancelFallAnimation();
        if (isHurtAnimationActive) return;
        CancelDiggingAnimation();
        int jumpHash = Animator.StringToHash(jumpState);
        if (animator.HasState(0, jumpHash))
        {
            // イベントの成否に依存せず、ジャンプ開始時から落下側の停止も受け付ける。
            if (fallController == null)
                fallController = GetComponentInParent<SensorPlayerController>();
            if (fallBody == null && fallController != null)
                fallBody = fallController.GetComponent<Rigidbody>();
            combinedJumpActive = true;
            combinedJumpHash = jumpHash;
            fallHash = jumpHash;
            fallActive = true;
            fallLanding = false;
            jumpLeftGround = false;
            animator.Play(jumpHash, 0, 0f);
        }
        else animator.SetTrigger("Jump");
    }

    // 二段ジャンプのアニメーションを再生する。
    // ステートが未作成の場合は既存の通常ジャンプを使用する。
    public void PlayDoubleJump()
    {
        // 接地判定が切り替わる前の離陸フレームから走り条件を解除する。
        if (animator != null) animator.SetFloat("Speed", 0f);
        CancelFallAnimation();
        PlayAdditionalJumpState(doubleJumpState);
    }

    // 壁ジャンプのアニメーションを再生する。
    // ステートが未作成の場合は既存の通常ジャンプを使用する。
    public void PlayWallJump()
    {
        // 接地判定が切り替わる前の離陸フレームから走り条件を解除する。
        if (animator != null) animator.SetFloat("Speed", 0f);
        CancelFallAnimation();
        PlayAdditionalJumpState(wallJumpState);
    }

    // 成立した敵食い込みジャンプに、飛び出す方向に共通のモーションを再生する。
    public void PlayEnemyDiggingJump(bool useNormalJumpFallback)
    {
        if (isHurtAnimationActive || animator == null || !animator.isActiveAndEnabled) return;

        int stateHash = string.IsNullOrWhiteSpace(enemyDiggingJumpState)
            ? 0 : Animator.StringToHash(enemyDiggingJumpState);
        if (stateHash == 0 || !animator.HasState(0, stateHash))
        {
            if (useNormalJumpFallback) PlayJump();
            return;
        }

        // 食い込み・落下の固定姿勢を解除し、専用ステートの先頭から再生する。
        animator.SetFloat("Speed", 0f);
        CancelFallAnimation();
        CancelDiggingAnimation();
        animator.Play(stateHash, 0, 0f);
    }

    [Tooltip("低アクセルで敵から跳ね返った時に再生するステートのフルパスです。例：Base Layer.EnemyRebound")]
    [SerializeField] private string enemyReboundState;

    // 通常敵食い込みの強制解除が成立した瞬間に、跳ね返りモーションを先頭から再生する。
    public void PlayEnemyRebound()
    {
        if (isHurtAnimationActive || animator == null || !animator.isActiveAndEnabled ||
            string.IsNullOrWhiteSpace(enemyReboundState)) return;

        int stateHash = Animator.StringToHash(enemyReboundState);
        if (!animator.HasState(0, stateHash)) return;
        CancelFallAnimation();
        CancelDiggingAnimation();
        animator.Play(stateHash, 0, 0f);
    }

    // 指定された追加ジャンプステートを再生する。
    // 既存のPlayJumpには手を加えず、未設定時のみそこへ戻す。
    private void PlayAdditionalJumpState(string statePath)
    {
        if (isHurtAnimationActive) return;
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(statePath) ||
            !animator.HasState(0, Animator.StringToHash(statePath)))
        {
            PlayJump();
            return;
        }

        CancelDiggingAnimation();
        animator.Play(Animator.StringToHash(statePath), 0, 0f);
    }

    public void ExitDiggingImmediately()
    {
        bool wasDigging = IsDiggingAnimationActive;
        CancelDiggingAnimation();
        if (!isHurtAnimationActive && wasDigging && animator != null && animator.isActiveAndEnabled &&
            animator.GetCurrentAnimatorStateInfo(0).fullPathHash == diggingStateHash)
            animator.Play(idleState, 0, 0f);
    }

    public void PlayWedgie()
    {
        CancelFallAnimation();
        if (isHurtAnimationActive) return;
        animator.SetTrigger("Wedgie");
    }

    public void HoldDiggingAnimationImmediately()
    {
        if (animator == null || !IsDiggingAnimationActive)
        {
            return;
        }

        diggingAnimationPhase = DiggingAnimationPhase.Holding;
        animator.SetFloat(DIGGING_PLAYBACK_SPEED, 0f);
    }

    // ブレンドで残っている別ステートのイベントを除外し、今回の食い込み姿勢だけを固定する。
    public bool TryHoldDiggingAnimationAtEvent(AnimationEvent animationEvent)
    {
        if (!IsDiggingAnimationActive || diggingAnimationPhase == DiggingAnimationPhase.Ending ||
            !IsActionAnimationEvent(animationEvent, diggingStateHash))
        {
            return false;
        }

        HoldDiggingAnimationImmediately();
        return true;
    }

    // 既存の呼び出しでは、地上用の攻撃ステートを再生する。
    public bool StartSlash(int step, AttackData data)
    {
        return StartSlash(step, data, false);
    }

    // 攻撃開始時の接地状態から、地上用または空中用のステートを選んで再生する。
    public bool StartSlash(int step, AttackData data, bool isAirAttack)
    {
        CancelFallAnimation();
        if (isHurtAnimationActive) return false;
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (attackHitBox == null ||
            data == null ||
            step < 1 ||
            step > 3)
        {
            Debug.LogError(
                "攻撃判定またはAttackDataの参照を確認してください。",
                this
            );

            return false;
        }

        bool useAirState = isAirAttack && !string.IsNullOrWhiteSpace(airSlashState);
        string path = useAirState
            ? airSlashState
            : step == 1 ? slash1State
            : step == 2 ? slash2State
            : slash3State;

        int hash = Animator.StringToHash(path);
        int idleHash = Animator.StringToHash(idleState);

        if (!animator.HasState(0, hash) ||
            !animator.HasState(0, idleHash))
        {
            Debug.LogError(
                "攻撃またはIdleのステート名・レイヤー名が一致しません：" + path,
                this
            );

            return false;
        }

        if (slashTrailEffect != null)
        {
            slashTrailEffect.ClearTrail();
        }

        CancelDiggingAnimation();
        // 前段の判定を閉じ、次段のヒットストップ時間を固定。
        attackHitBox.ConfigureWeakAttack(this, isAirAttack);
        attackHitBox.BeginAttack(data);

        activeHash = hash;
        attackActive = true;
        UsesAnimatorWeakAttackExit = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == WEAK_ATTACK_EXIT_PARAMETER && parameter.type == AnimatorControllerParameterType.Int)
            {
                UsesAnimatorWeakAttackExit = true;
                SetWeakAttackExit(false, false);
                break;
            }
        }

        // 古いTriggerを残さない。
        animator.ResetTrigger("Slash");
        animator.ResetTrigger("Slash2");
        animator.ResetTrigger("Slash3");
        animator.ResetTrigger("Jump");
        animator.ResetTrigger("Wedgie");

        // 地上・空中弱攻撃のブレンドは、Animatorの遷移設定で行う。
        if (step == 1 || useAirState)
            RequestActionTransition(hash, useAirState ? WeakActionTarget.AirAttack : WeakActionTarget.GroundAttack);
        else
            animator.Play(hash, 0, 0f);

        return true;
    }

    // Animatorへ遷移先を通知する。設定がない場合も、既存のステートで行動を続けられるようにする。
    private void RequestActionTransition(int stateHash, WeakActionTarget target)
    {
        ClearRequestedActionTransition();
        if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHash)
        {
            // 同じステートを連続で指定された場合も、先頭から再生する。
            animator.Play(stateHash, 0, 0f);
            return;
        }

        bool hasTarget = false;
        bool hasTrigger = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            hasTarget |= parameter.name == WEAK_ACTION_TARGET_PARAMETER &&
                parameter.type == AnimatorControllerParameterType.Int;
            hasTrigger |= parameter.name == WEAK_ACTION_REQUEST_TRIGGER &&
                parameter.type == AnimatorControllerParameterType.Trigger;
        }

        if (!hasTarget || !hasTrigger)
        {
            PlayActionTransitionFallback(stateHash);
            return;
        }

        requestedActionStateHash = stateHash;
        requestedActionFrame = Time.frameCount;
        requestedActionFixedTime = Time.fixedTimeAsDouble;
        animator.SetInteger(WEAK_ACTION_TARGET_PARAMETER, (int)target);
        animator.SetTrigger(WEAK_ACTION_REQUEST_TRIGGER);
    }

    // Animatorに遷移開始の機会を一度だけ渡し、未開始なら短いブレンドで再生する。
    private void UpdateRequestedActionTransition()
    {
        if (requestedActionStateHash == 0) return;
        if (TryGetActionState(requestedActionStateHash, out _))
        {
            ClearRequestedActionTransition();
            return;
        }

        if (Time.timeScale <= 0f) return;
        bool evaluatedAfterRequest = animator.updateMode == AnimatorUpdateMode.Fixed
            ? Time.fixedTimeAsDouble > requestedActionFixedTime
            : Time.frameCount > requestedActionFrame;
        if (!evaluatedAfterRequest) return;

        int stateHash = requestedActionStateHash;
        ClearRequestedActionTransition();
        if (IsDiggingAnimationActive && stateHash == diggingStateHash)
            diggingStartFrame = Time.frameCount;
        PlayActionTransitionFallback(stateHash);
    }

    // 終了・中断した行動のTriggerを次の行動へ持ち越さない。
    private void ClearRequestedActionTransition()
    {
        if (requestedActionStateHash == 0) return;
        requestedActionStateHash = 0;
        if (animator != null && animator.isActiveAndEnabled)
            animator.ResetTrigger(WEAK_ACTION_REQUEST_TRIGGER);
    }

    // 遷移が設定されていない場合も、現在の姿勢から短くブレンドして行動を開始する。
    private void PlayActionTransitionFallback(int stateHash)
    {
        animator.CrossFadeInFixedTime(stateHash, ACTION_FALLBACK_BLEND_SECONDS, 0, 0f);
        if (actionTransitionWarningLogged) return;
        actionTransitionWarningLogged = true;
        Debug.LogWarning("Animatorの弱攻撃・食い込み遷移が開始されなかったため、短いブレンドで再生しました。" +
            "対象の遷移の Has Exit Time と、WeakActionRequested / WeakActionTarget の条件を確認してください。", this);
    }

    // 食い込みを受理できるかだけを通知する。遷移の開始位置はAnimatorのExit Timeで決まる。
    public void SetWeakAttackExit(bool canExit, bool canDig)
    {
        if (!UsesAnimatorWeakAttackExit || animator == null || !animator.isActiveAndEnabled) return;

        int target = WEAK_ATTACK_EXIT_BLOCKED;
        if (canExit && !IsWeakAttackHeld)
            target = canDig ? WEAK_ATTACK_EXIT_DIGGING : WEAK_ATTACK_EXIT_IDLE;
        animator.SetInteger(WEAK_ATTACK_EXIT_PARAMETER, target);
    }

    // 攻撃から別ステートへ出たとき、Animatorが選んだ遷移先を読み取る。
    public bool TryGetWeakAttackExit(bool observedAttack, out bool startsDigging)
    {
        startsDigging = false;
        if (!UsesAnimatorWeakAttackExit || !attackActive || IsWeakAttackHeld || isHurtAnimationActive ||
            animator == null || !animator.isActiveAndEnabled) return false;

        int currentHash = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        bool inTransition = animator.IsInTransition(0);
        int destination = inTransition ? animator.GetNextAnimatorStateInfo(0).fullPathHash : currentHash;
        if (destination == 0 || destination == activeHash || (!observedAttack && (!inTransition || currentHash != activeHash)))
            return false;

        startsDigging = destination == Animator.StringToHash(diggingState) ||
            (!string.IsNullOrEmpty(wallDiggingState) && destination == Animator.StringToHash(wallDiggingState)) ||
            (!string.IsNullOrEmpty(ceilingDiggingState) && destination == Animator.StringToHash(ceilingDiggingState)) ||
            (!string.IsNullOrEmpty(enemyDiggingState) && destination == Animator.StringToHash(enemyDiggingState));
        return true;
    }

    // Animatorの評価直後や、食い込み先のAnimation Eventより先にゲーム側の状態を合わせる。
    public void SynchronizeAnimatorWeakAttackExit()
    {
        if (!UsesAnimatorWeakAttackExit) return;
        if (fallController == null) fallController = GetComponentInParent<SensorPlayerController>();
        if (fallController != null) fallController.SynchronizeAnimatorWeakAttackExit();
    }

    // 弱攻撃が現在または次のステートで再生中かを調べる。
    public bool TryGetAttackProgress(out float progress)
    {
        return TryGetAttackPlayback(out progress, out _);
    }

    // 攻撃へ入る途中も進行度を取得し、攻撃ステートから出たことを判定する。
    public bool TryGetAttackPlayback(out float progress, out bool exitedAttackState)
    {
        return TryGetAttackPlayback(out progress, out exitedAttackState, out _);
    }

    // 攻撃の最中にIdleへの自動遷移が始まった場合も、待機へ着く前に終了先を選べるようにする。
    public bool TryGetAttackPlayback(out float progress, out bool exitedAttackState, out bool transitioningToIdle)
    {
        progress = 0f;
        exitedAttackState = false;
        transitioningToIdle = false;
        if (!attackActive || isHurtAnimationActive || animator == null || !animator.isActiveAndEnabled)
            return false;

        if (TryGetActionState(activeHash, out AnimatorStateInfo state))
        {
            progress = state.normalizedTime;
            transitioningToIdle = animator.IsInTransition(0) &&
                animator.GetNextAnimatorStateInfo(0).fullPathHash == Animator.StringToHash(idleState);
            return true;
        }

        exitedAttackState = true;
        return false;
    }

    // 同じレイヤーのブレンド先を先に見て、対象ステートの再生情報を取得する。
    private bool TryGetActionState(int stateHash, out AnimatorStateInfo state)
    {
        if (animator.IsInTransition(0))
        {
            state = animator.GetNextAnimatorStateInfo(0);
            if (state.fullPathHash == stateHash) return true;
        }

        state = animator.GetCurrentAnimatorStateInfo(0);
        return state.fullPathHash == stateHash;
    }

    // Animation Eventを発火したステートを確認し、古い姿勢側のイベントを無視する。
    private bool IsActionAnimationEvent(AnimationEvent animationEvent, int stateHash)
    {
        if (animator == null || !animator.isActiveAndEnabled) return false;
        return animationEvent != null
            ? animationEvent.isFiredByAnimator && animationEvent.animatorStateInfo.fullPathHash == stateHash
            : TryGetActionState(stateHash, out _);
    }

    // 弱攻撃の当たり判定と演出を閉じる。必要な場合は待機モーションへ戻す。
    public void EndSlash(bool returnToIdle)
    {
        ClearRequestedActionTransition();
        ResumeWeakAttackPose();
        SetWeakAttackExit(false, false);
        UsesAnimatorWeakAttackExit = false;
        attackActive = false;

        if (slashTrailEffect != null)
        {
            slashTrailEffect.ClearTrail();
        }

        if (attackHitBox != null)
        {
            attackHitBox.EndAttack();
        }

        if (returnToIdle && !isHurtAnimationActive && animator != null && animator.isActiveAndEnabled)
            animator.Play(idleState, 0, 0f);
    }

    // 食い込みへつながらなかった弱攻撃が終わったら、待機モーションへ戻す。
    public void ReturnToIdleAfterSlash()
    {
        if (!isHurtAnimationActive && animator != null && animator.isActiveAndEnabled)
        {
            RequestActionTransition(Animator.StringToHash(idleState), WeakActionTarget.Idle);
        }
    }

    // プレイヤーの攻撃Animation Eventから呼ぶ。
    // int引数で1・2・3段目を指定する。
    public void PlayAttackTrail(int step)
    {
        if (!attackActive || slashTrailEffect == null)
        {
            return;
        }

        slashTrailEffect.PlayEffect(step);
    }

    // プレイヤーの攻撃Animation Eventから呼ぶ。
    public void StopAttackTrail()
    {
        if (slashTrailEffect != null)
        {
            slashTrailEffect.StopTrail();
        }
    }

    // 斬撃エフェクトを出したいフレームの Animation Event から呼ぶ。
    public void OnWeakAttackEffect(AnimationEvent animationEvent = null)
    {
        if (!attackActive || !IsActionAnimationEvent(animationEvent, activeHash))
        {
            return;
        }

        if (weakAttackController == null)
        {
            weakAttackController = GetComponent<SensorPlayerController>();
        }

        if (weakAttackController != null)
        {
            weakAttackController.PlayWeakSwingEffect();
        }
    }

    // 弱攻撃の振り抜きフレームでフィニッシャーの判定を開く。
    public void OnWeakAttackSwing(AnimationEvent animationEvent = null)
    {
        if (attackActive && attackHitBox != null && IsActionAnimationEvent(animationEvent, activeHash))
        {
            attackHitBox.EnableWeakAttackFinisher();
        }
    }

    // Animation Eventから呼ぶ。
    public void EnableAttackHitBox(AnimationEvent animationEvent = null)
    {
        if (attackActive && IsActionAnimationEvent(animationEvent, activeHash))
        {
            attackHitBox.EnableHitBox(); // ヒット判定開始
        }
    }

    // Animation Eventから呼ぶ。
    public void DisableAttackHitBox(AnimationEvent animationEvent = null)
    {
        if (attackHitBox != null && (animationEvent == null ||
            (attackActive && IsActionAnimationEvent(animationEvent, activeHash))))
        {
            attackHitBox.DisableHitBox(); // ヒット判定終了
        }
    }

    void OnDisable()
    {
        CancelFallAnimation();
        fallAirborne = false;
        EndHurt(false);
        CancelDiggingAnimation();
        EndSlash(false);
    }
}
