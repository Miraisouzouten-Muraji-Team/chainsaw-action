using System;
using UnityEngine;

/// <summary>
/// Enemy共通の攻撃State。
/// 具体的な予備動作と攻撃方法はIEnemyAttackStrategyへ委譲する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Attack Stateへ入るたび、Preparation完了後にだけ攻撃本体を開始する順序を保証する。
/// ・1回分の攻撃完了後、StateMachineへ制御を返して再索敵を要求する。
/// ・攻撃中に受け取ったCollision開始・継続通知をStrategyへ委譲する。
///
/// 担当しない責務:
/// ・敵固有の移動、ダメージ、崖判定、Effect生成。
/// ・次の攻撃を継続するかどうかの判定。
/// ・実際のState切り替え。
/// </remarks>
public sealed class EnemyAttackState :
    IEnemyState,
    IEnemyCollisionEnterReceiver,
    IEnemyCollisionStayReceiver
{
    private readonly IEnemyAttackStrategy attackStrategy;
    private readonly Action requestAttackCycleCompletion;

    private float initialAttackDirectionSign = 1f;
    private bool hasBegunAttack;
    private bool hasRequestedAttackCycleCompletion;

    public EnemyAttackState(
        IEnemyAttackStrategy attackStrategy,
        Action requestAttackCycleCompletion)
    {
        this.attackStrategy = attackStrategy
            ?? throw new ArgumentNullException(nameof(attackStrategy));

        this.requestAttackCycleCompletion = requestAttackCycleCompletion
            ?? throw new ArgumentNullException(nameof(requestAttackCycleCompletion));
    }

    /// <summary>
    /// 次にAttack Stateへ入る際に使用する初回攻撃方向を設定する。
    /// </summary>
    public void SetInitialAttackDirection(
        float directionSign)
    {
        initialAttackDirectionSign =
            directionSign < 0f
                ? -1f
                : 1f;
    }

    public void Enter()
    {
        hasBegunAttack = false;
        hasRequestedAttackCycleCompletion = false;

        // どの遷移経路からAttackへ入っても、攻撃本体より先に必ずPreparationを開始する。
        attackStrategy.BeginPreparation(
            initialAttackDirectionSign);
    }

    public void Update()
    {
        if (hasRequestedAttackCycleCompletion)
        {
            return;
        }

        if (!hasBegunAttack)
        {
            if (!attackStrategy.UpdatePreparation())
            {
                return;
            }

            hasBegunAttack = true;
            attackStrategy.BeginAttack();
            return;
        }

        if (!attackStrategy.UpdateAttack())
        {
            return;
        }

        // Strategy内部で次の攻撃を始めず、必ずStateMachineの再索敵へ制御を返す。
        hasRequestedAttackCycleCompletion = true;
        requestAttackCycleCompletion.Invoke();
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        attackStrategy.HandleCollisionEnter(
            collision);
    }

    public void HandleCollisionStay(
        Collision collision)
    {
        attackStrategy.HandleCollisionStay(
            collision);
    }

    public void Exit()
    {
        attackStrategy.EndAttack();
    }
}
