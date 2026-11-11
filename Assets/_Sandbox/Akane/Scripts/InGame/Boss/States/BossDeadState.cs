namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの死亡状態。
    /// </summary>
    public sealed class BossDeadState : IBossState
    {
        private readonly BossContext context;

        public BossStateType Type => BossStateType.Dead;

        public BossDeadState(BossContext context)
        {
            this.context = context;
        }

        public void Enter()
        {
            context.Boss.ClearSelectedAttack();
            context.Boss.PlayDeathAnimation();
        }

        public void Tick(float deltaTime)
        {
            // 死亡後の行動は行わない。
        }

        public void Exit()
        {
        }
    }
}
