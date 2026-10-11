using System;
using UnityEngine;

/// <summary>
/// Enemy共通の攻撃予告State。
/// 具体的な演出・時間・ダメージ処理はStrategyへ委譲する。
/// </summary>
/// <remarks>
/// 責務:
/// ・攻撃予告StateのEnter / Update / Exitを管理する。
/// ・敵固有の攻撃予告処理をIEnemyAlertStrategyへ委譲する。
/// ・Alert中に受け取ったCollision開始・継続通知をStrategyへ委譲する。
/// ・Strategyの完了を受けてStateMachineへ遷移を要求する。
///
/// 担当しない責務:
/// ・Stateの切り替え。
/// ・Collisionを利用した具体的な接触処理。
/// ・PlayerのHP変更。
/// </remarks>
public sealed class EnemyAlertState :
    IEnemyState,
    IEnemyCollisionEnterReceiver,
    IEnemyCollisionStayReceiver
{
    private readonly IEnemyAlertStrategy alertStrategy;
    private readonly Action requestAttackState;

    private bool hasRequestedAttackState;

    public EnemyAlertState(
        IEnemyAlertStrategy alertStrategy,
        Action requestAttackState)
    {
        this.alertStrategy = alertStrategy
            ?? throw new ArgumentNullException(nameof(alertStrategy));

        this.requestAttackState = requestAttackState
            ?? throw new ArgumentNullException(nameof(requestAttackState));
    }

    public void Enter()
    {
        hasRequestedAttackState = false;

        alertStrategy.BeginAlert();
    }

    public void Update()
    {
        if (hasRequestedAttackState ||
            !alertStrategy.UpdateAlert())
        {
            return;
        }

        hasRequestedAttackState = true;

        requestAttackState.Invoke();
    }

    /// <summary>
    /// Alert State中に発生したCollision開始通知を
    /// 現在使用しているAlert Strategyへ委譲する。
    /// </summary>
    /// <param name="collision">
    /// StateMachineから渡されたCollision情報。
    /// </param>
    public void HandleCollisionEnter(Collision collision)
    {
        alertStrategy.HandleCollision(collision);
    }

    /// <summary>
    /// Alert State中に発生したCollision継続通知を
    /// 現在使用しているAlert Strategyへ委譲する。
    /// </summary>
    /// <param name="collision">
    /// StateMachineから渡されたCollision情報。
    /// </param>
    public void HandleCollisionStay(Collision collision)
    {
        alertStrategy.HandleCollision(collision);
    }

    public void Exit()
    {
        alertStrategy.EndAlert();
    }
}
