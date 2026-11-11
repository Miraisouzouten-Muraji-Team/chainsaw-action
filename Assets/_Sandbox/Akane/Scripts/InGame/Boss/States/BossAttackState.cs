namespace Project.InGame.Boss
{
    /// <summary>
    /// 選択された攻撃Strategyを実行する状態。
    /// </summary>
    public sealed class BossAttackState : IBossState
    {
        private readonly BossContext context;
        private IBossAttackStrategy currentAttack;

        public BossStateType Type => BossStateType.Attack;

        public BossAttackState(BossContext context)
        {
            this.context = context;
        }

        public void Enter()
        {
            currentAttack = context.Boss.SelectedAttack;

            if (currentAttack == null)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            currentAttack.Enter(context);
        }

        public void Tick(float deltaTime)
        {
            if (currentAttack == null)
                return;

            currentAttack.Tick(context, deltaTime);

            if (context.Health.IsDead)
                return;

            // 攻撃処理が完了したら待機状態へ戻る。
            if (currentAttack.IsFinished)
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
        }

        public void Exit()
        {
            currentAttack?.Exit(context);
            currentAttack = null;
            context.Boss.ClearSelectedAttack();
        }
    }
}
