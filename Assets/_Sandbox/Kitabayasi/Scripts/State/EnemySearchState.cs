using System;
using UnityEngine;

/// <summary>
/// Enemy共通の索敵State。
/// </summary>
/// <remarks>
/// 責務:
/// ・索敵StateのEnter / Update / Exitを管理する。
/// ・敵固有の索敵処理をIEnemySearchStrategyへ委譲する。
/// ・索敵中に受け取ったCollision開始通知をStrategyへ委譲する。
/// ・プレイヤー発見時に、発見方向を渡してState遷移を要求する。
/// ・攻撃後の再索敵に失敗した場合のみ、帰還処理の開始を遅らせ、待機中のアニメーションを停止する。
///
/// 担当しない責務:
/// ・敵固有の巡回、検知、移動処理。
/// ・Collisionを利用した具体的な接触処理。
/// ・実際のState切り替え。
/// ・Enemyの設定値の保持。
/// </remarks>
public sealed class EnemySearchState :
    IEnemyState,
    IEnemyCollisionEnterReceiver,
    IEnemyCollisionStayReceiver
{
    private readonly IEnemySearchStrategy searchStrategy;
    private readonly Animator animator;
    private readonly Action<float> requestAlertState;
    private readonly float postAttackSearchFailureWaitDuration;

    private bool hasRequestedAlertState;
    private bool hasBegunSearch;
    private float remainingWaitDuration;
    private float animatorSpeedBeforeWait;
    private bool hasPausedAnimatorForWait;

    public EnemySearchState(
        IEnemySearchStrategy searchStrategy,
        Action<float> requestAlertState,
        float postAttackSearchFailureWaitDuration,
        Animator animator)
    {
        this.searchStrategy = searchStrategy
            ?? throw new ArgumentNullException(nameof(searchStrategy));

        this.requestAlertState = requestAlertState
            ?? throw new ArgumentNullException(nameof(requestAlertState));

        this.animator = animator;

        this.postAttackSearchFailureWaitDuration =
            Mathf.Max(0f, postAttackSearchFailureWaitDuration);
    }

    /// <summary>
    /// 攻撃完了直後の再索敵に失敗した場合だけ、次のSearch開始を遅らせる。
    /// 通常のSearch開始やDamage復帰では呼ばない。
    /// </summary>
    public void WaitBeforeReturningAfterFailedAttackSearch()
    {
        remainingWaitDuration =
            postAttackSearchFailureWaitDuration;
    }

    public void Enter()
    {
        hasRequestedAlertState = false;
        hasBegunSearch = false;

        // StrategyのBeginSearchで帰還が始まるため、待機中は呼ばない。
        if (remainingWaitDuration <= 0f)
        {
            BeginSearch();
            return;
        }

        PauseAnimatorForWait();
    }

    public void Update()
    {
        if (hasRequestedAlertState)
        {
            return;
        }

        if (!hasBegunSearch)
        {
            remainingWaitDuration -= Time.fixedDeltaTime;

            if (remainingWaitDuration > 0f)
            {
                return;
            }

            BeginSearch();
        }

        bool hasDetectedPlayer =
            searchStrategy.UpdateSearch(
                out float detectedPlayerDirectionSign);

        if (!hasDetectedPlayer)
        {
            return;
        }

        hasRequestedAlertState = true;

        // 発見した瞬間の方向をAttack Stateまで保持するため、
        // StateMachineへ遷移要求と一緒に渡す。
        requestAlertState.Invoke(
            detectedPlayerDirectionSign);
    }

    /// <summary>
    /// 索敵State中に発生したCollision開始通知を
    /// 現在使用している索敵Strategyへ委譲する。
    /// </summary>
    /// <param name="collision">
    /// StateMachineから渡されたCollision情報。
    /// </param>
    public void HandleCollisionEnter(Collision collision)
    {
        // 待機中はSearch Strategyをまだ開始していない。
        if (hasBegunSearch)
        {
            searchStrategy.HandleCollisionEnter(collision);
        }
    }

    /// <summary>
    /// Search Strategyが継続接触を必要とする場合だけ、
    /// Collision Stayを委譲する。
    /// </summary>
    public void HandleCollisionStay(Collision collision)
    {
        if (hasBegunSearch &&
            searchStrategy is IEnemyCollisionStayReceiver receiver)
        {
            receiver.HandleCollisionStay(collision);
        }
    }

    public void Exit()
    {
        ResumeAnimatorAfterWait();

        if (hasBegunSearch)
        {
            searchStrategy.EndSearch();
        }

        hasBegunSearch = false;
        remainingWaitDuration = 0f;
    }

    private void BeginSearch()
    {
        // Walkなどの帰還モーションはAnimatorの再生を復元してから開始する。
        ResumeAnimatorAfterWait();
        searchStrategy.BeginSearch();
        hasBegunSearch = true;
    }

    private void PauseAnimatorForWait()
    {
        if (animator == null || hasPausedAnimatorForWait)
        {
            return;
        }

        // 攻撃終了直後のポーズを保ったまま、待機中だけ再生を止める。
        // Animatorを無効化せず速度を記録しておくことで、遷移・被弾でも復元できる。
        animatorSpeedBeforeWait = animator.speed;
        animator.speed = 0f;
        hasPausedAnimatorForWait = true;
    }

    private void ResumeAnimatorAfterWait()
    {
        if (!hasPausedAnimatorForWait)
        {
            return;
        }

        if (animator != null)
        {
            animator.speed = animatorSpeedBeforeWait;
        }

        hasPausedAnimatorForWait = false;
    }
}
