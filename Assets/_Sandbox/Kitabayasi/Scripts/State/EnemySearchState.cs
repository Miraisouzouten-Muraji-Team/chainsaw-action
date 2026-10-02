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
/// ・プレイヤー発見時にState遷移を要求する。
///
/// 担当しない責務:
/// ・敵固有の巡回、検知、移動処理。
/// ・Collisionを利用した具体的な接触処理。
/// ・実際のState切り替え。
/// ・Enemyの設定値の保持。
/// </remarks>
public sealed class EnemySearchState :
    IEnemyState,
    IEnemyCollisionEnterReceiver
{
    private readonly IEnemySearchStrategy searchStrategy;
    private readonly Action requestAlertState;

    private bool hasRequestedAlertState;

    public EnemySearchState(
        IEnemySearchStrategy searchStrategy,
        Action requestAlertState)
    {
        this.searchStrategy = searchStrategy
            ?? throw new ArgumentNullException(nameof(searchStrategy));

        this.requestAlertState = requestAlertState
            ?? throw new ArgumentNullException(nameof(requestAlertState));
    }

    public void Enter()
    {
        hasRequestedAlertState = false;

        searchStrategy.BeginSearch();
    }

    public void Update()
    {
        if (hasRequestedAlertState)
        {
            return;
        }

        bool hasDetectedPlayer = searchStrategy.UpdateSearch();

        if (!hasDetectedPlayer)
        {
            return;
        }

        hasRequestedAlertState = true;

        // 実際のState変更は行わず、StateMachineへ遷移を要求する。
        requestAlertState.Invoke();
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
        searchStrategy.HandleCollisionEnter(collision);
    }

    public void Exit()
    {
        searchStrategy.EndSearch();
    }
}
