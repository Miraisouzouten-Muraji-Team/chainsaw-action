using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    Animator animator;

    [SerializeField] ChainsawAttack attackHitBox;

    [Header("Animatorステートのフルパス")]
    [SerializeField] string slash1State = "Base Layer.Slash1";
    [SerializeField] string slash2State = "Base Layer.Slash2";
    [SerializeField] string slash3State = "Base Layer.Slash3";
    [SerializeField] string idleState = "Base Layer.Idle";

    [Header("攻撃トレイル")]
    [SerializeField] SlashTrailEffect slashTrailEffect;

    int activeHash;
    bool attackActive;

    // ===== 食い込みアニメーション =====

    private const string DIGGING_PLAYBACK_SPEED = "DiggingPlaybackSpeed";

    private enum DiggingAnimationPhase
    {
        None,
        Starting,
        Holding,
        Ending
    }

    [Header("食い込みアニメーション")]
    [SerializeField]
    private string diggingState = "Base Layer.Digging";

    private DiggingAnimationPhase diggingAnimationPhase;
    private int diggingStateHash;
    private int diggingStartFrame;

    // 終了モーション中もtrue。
    // 実際に食い込んでいるかどうかとは別の、演出用の状態。
    public bool IsDiggingAnimationActive =>
        diggingAnimationPhase != DiggingAnimationPhase.None;


    // 食い込み開始時にControllerから呼ぶ。
    public bool StartDiggingAnimation()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null || !animator.isActiveAndEnabled)
        {
            return false;
        }

        // 再生中に何度も先頭へ戻さない。
        if (IsDiggingAnimationActive)
        {
            return false;
        }

        diggingStateHash = Animator.StringToHash(diggingState);

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

        animator.Play(diggingStateHash, 0, 0f);

        return true;
    }


    // 止めたい位置のAnimation Eventから呼ぶ。
    public void HoldDiggingAnimation()
    {
        // 解除済みなら停止しない。
        // 停止位置より前に解除した場合にも対応する。
        if (diggingAnimationPhase != DiggingAnimationPhase.Starting)
        {
            return;
        }

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        if (state.fullPathHash != diggingStateHash ||
            animator.IsInTransition(0))
        {
            return;
        }

        diggingAnimationPhase = DiggingAnimationPhase.Holding;

        animator.SetFloat(DIGGING_PLAYBACK_SPEED, 0f);
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
        diggingAnimationPhase = DiggingAnimationPhase.None;

        if (animator != null)
        {
            animator.SetFloat(DIGGING_PLAYBACK_SPEED, 1f);
        }
    }


    // 送ってくれたPlayerAnimatorにはLateUpdateがないため追加できる。
    // 既に追加済みの場合は、この中身を既存のLateUpdateへまとめる。
    private void LateUpdate()
    {
        UpdateDiggingAnimation();
    }


    private void UpdateDiggingAnimation()
    {
        if (!IsDiggingAnimationActive ||
            Time.frameCount == diggingStartFrame)
        {
            return;
        }

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        // 別のモーションへ切り替わった場合は、
        // 食い込み用の停止設定を解除する。
        if (state.fullPathHash != diggingStateHash ||
            animator.IsInTransition(0))
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
        animator.SetFloat("Speed", Mathf.Abs(speed));
    }

    public void PlayJump()
    {
        animator.SetTrigger("Jump");
    }

    public void PlayWedgie()
    {
        animator.SetTrigger("Wedgie");
    }

    // 入力予約時ではなく、実際に次段へ進むときに呼ぶ。
    public bool StartSlash(int step, AttackData data)
    {
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

        string path =
            step == 1 ? slash1State :
            step == 2 ? slash2State :
            slash3State;

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

        // 前段の判定を閉じ、次段のヒットストップ時間を固定。
        attackHitBox.BeginAttack(data);

        activeHash = hash;
        attackActive = true;

        // 古いTriggerを残さない。
        animator.ResetTrigger("Slash");
        animator.ResetTrigger("Slash2");
        animator.ResetTrigger("Slash3");
        animator.ResetTrigger("Jump");
        animator.ResetTrigger("Wedgie");

        // 遷移条件を待たず、指定ステートを先頭から再生。
        animator.Play(hash, 0, 0f);

        return true;
    }

    public bool TryGetAttackProgress(out float progress)
    {
        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        progress = state.normalizedTime;

        return attackActive &&
               !animator.IsInTransition(0) &&
               state.fullPathHash == activeHash;
    }

    public void EndSlash(bool returnToIdle)
    {
        attackActive = false;

        if (slashTrailEffect != null)
        {
            slashTrailEffect.ClearTrail();
        }

        if (attackHitBox != null)
        {
            attackHitBox.EndAttack();
        }

        if (returnToIdle &&
            animator != null &&
            animator.isActiveAndEnabled)
        {
            animator.Play(idleState, 0, 0f);
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

    // Animation Eventから呼ぶ。
    public void EnableAttackHitBox()
    {
        if (attackActive && TryGetAttackProgress(out _))
        {
            attackHitBox.EnableHitBox(); // ヒット判定開始
        }
    }

    // Animation Eventから呼ぶ。
    public void DisableAttackHitBox()
    {
        if (attackHitBox != null)
        {
            attackHitBox.DisableHitBox(); // ヒット判定終了
        }
    }

    void OnDisable()
    {
        CancelDiggingAnimation();
        EndSlash(false);
    }
}