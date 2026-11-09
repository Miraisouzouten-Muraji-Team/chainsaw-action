namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの待機状態。
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

            // 行動の区切りで盾の再生成を確認する。
            context.Shield.TryRestore();
        }

        public void Tick(float deltaTime)
        {
            context.Boss.FacePlayer(deltaTime);

            elapsedTime += deltaTime;

            if (elapsedTime < context.Boss.IdleDuration)
                return;

            // 次の攻撃を抽選する。
            context.Boss.SelectNextAttack();

            var attack = context.Boss.SelectedAttack;

            if (attack == null)
            {
                elapsedTime = 0f;
                return;
            }

            // 射程内なら攻撃、射程外なら移動する。
            var nextState = context.Boss.DistanceToPlayer <= attack.Definition.Range
                ? BossStateType.Attack
                : BossStateType.Move;

            context.Boss.StateMachine.ChangeState(nextState);
        }

        public void Exit()
        {
        }
    }
}
