namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃処理を定義するインターフェース。
    /// </summary>
    public interface IBossAttackStrategy
    {
        BossAttackDefinition Definition { get; }
        bool IsFinished { get; }

        void Enter(BossContext context);
        void Tick(BossContext context, float deltaTime);
        void Exit(BossContext context);
    }
}
