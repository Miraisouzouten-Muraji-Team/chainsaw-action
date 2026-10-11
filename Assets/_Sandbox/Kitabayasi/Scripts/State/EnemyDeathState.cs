using System;

/// <summary>
/// Enemy共通の死亡State。
/// 具体的な死亡演出をIEnemyDeathStrategyへ委譲する。
/// </summary>
/// <remarks>
/// 責務:
/// ・死亡StateのEnter / Update / Exitを管理する。
/// ・死亡処理をIEnemyDeathStrategyへ委譲する。
/// ・Strategyの完了を受けてStateMachineへ死亡完了を通知する。
///
/// 担当しない責務:
/// ・HPの保持や死亡判定。
/// ・メッシュ分割や縮小の具体処理。
/// ・Enemy GameObjectの破棄。
/// ・実際のState切り替え。
/// </remarks>
public sealed class EnemyDeathState : IEnemyState
{
    private readonly IEnemyDeathStrategy deathStrategy;
    private readonly Action requestDeathCompletion;

    private bool hasRequestedDeathCompletion;

    public EnemyDeathState(
        IEnemyDeathStrategy deathStrategy,
        Action requestDeathCompletion)
    {
        this.deathStrategy = deathStrategy
            ?? throw new ArgumentNullException(nameof(deathStrategy));

        this.requestDeathCompletion = requestDeathCompletion
            ?? throw new ArgumentNullException(nameof(requestDeathCompletion));
    }

    public void Enter()
    {
        hasRequestedDeathCompletion = false;

        deathStrategy.BeginDeath();
    }

    public void Update()
    {
        if (hasRequestedDeathCompletion ||
            !deathStrategy.UpdateDeath())
        {
            return;
        }

        hasRequestedDeathCompletion = true;

        requestDeathCompletion.Invoke();
    }

    public void Exit()
    {
        deathStrategy.EndDeath();
    }
}
