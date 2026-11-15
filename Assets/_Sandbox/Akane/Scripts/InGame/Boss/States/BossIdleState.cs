namespace Project.InGame.Boss
{
    /// <summary>
    /// 行動の区切りと盾の再生成を管理する待機状態。
    /// </summary>
    public sealed class BossIdleState : IBossState
    {
        private readonly BossContext context;
        private float elapsedTime;

        public BossStateType Type => BossStateType.Idle;

        public BossIdleState(BossContext context)
        {
            this.context = context;
        }

        public void Enter()
        {
            elapsedTime = 0f;
            context.Shield.TryRestore();
        }

        public void Tick(float deltaTime)
        {
            context.Boss.FacePlayer(deltaTime);

            // 盾の再生成が完了するまで新しい行動を開始しない。
            if (context.Shield.IsRestoring)
            {
                return;
            }

            elapsedTime += deltaTime;

            if (elapsedTime < context.Boss.IdleDuration)
            {
                return;
            }

            context.Boss.SelectNextAttack();

            IBossAttackStrategy attack =
                context.Boss.SelectedAttack;

            if (attack == null)
            {
                elapsedTime = 0f;
                return;
            }

            BossStateType nextState =
                context.Boss.DistanceToPlayer <= attack.Definition.Range
                    ? BossStateType.Attack
                    : BossStateType.Move;

            context.Boss.StateMachine.ChangeState(nextState);
        }

        public void Exit() { }
    }
}
