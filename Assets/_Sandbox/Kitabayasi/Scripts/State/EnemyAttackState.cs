using System;
using UnityEngine;

/// <summary>
/// Enemy共通の攻撃State。
/// 具体的な攻撃方法はIEnemyAttackStrategyへ委譲する。
/// </summary>
/// <remarks>
/// 責務:
/// ・攻撃StateのEnter / Update / Exitを管理する。
/// ・敵固有の攻撃処理をIEnemyAttackStrategyへ委譲する。
/// ・攻撃中に受け取ったCollision開始・継続通知をStrategyへ委譲する。
///
/// 担当しない責務:
/// ・敵固有の移動、ダメージ、崖判定、Effect生成。
/// ・実際のState切り替え。
/// </remarks>
public sealed class EnemyAttackState :
    IEnemyState,
    IEnemyCollisionEnterReceiver,
    IEnemyCollisionStayReceiver
{
    private readonly IEnemyAttackStrategy attackStrategy;

    private float initialAttackDirectionSign = 1f;

    public EnemyAttackState(
        IEnemyAttackStrategy attackStrategy)
    {
        this.attackStrategy = attackStrategy
            ?? throw new ArgumentNullException(nameof(attackStrategy));
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
        attackStrategy.BeginAttack(
            initialAttackDirectionSign);
    }

    public void Update()
    {
        attackStrategy.UpdateAttack();
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
