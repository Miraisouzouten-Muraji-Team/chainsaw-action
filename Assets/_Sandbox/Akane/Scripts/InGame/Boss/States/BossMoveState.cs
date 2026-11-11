using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 次の攻撃に適した距離まで移動する状態。
    /// </summary>
    public sealed class BossMoveState : IBossState
    {
        private const float ARRIVAL_TOLERANCE = 0.05f;

        private readonly BossContext context;
        private float elapsedTime;

        public BossStateType Type => BossStateType.Move;

        public BossMoveState(BossContext context)
        {
            this.context = context;
        }

        public void Enter()
        {
            elapsedTime = 0f;
        }

        public void Tick(float deltaTime)
        {
            var attack = context.Boss.SelectedAttack;

            if (attack == null)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            // 移動中に盾がなくなった場合は攻撃を選び直す。
            if (attack.Definition.RequiresShield && !context.Shield.IsActive)
            {
                context.Boss.ClearSelectedAttack();
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            elapsedTime += deltaTime;
            context.Boss.FacePlayer(deltaTime);

            var targetDistance = attack.Definition.Range *
                attack.Definition.TargetDistanceRatio;

            context.Boss.MoveToTargetDistance(targetDistance, deltaTime);

            var distance = context.Boss.DistanceToPlayer;
            var distanceDifference = Mathf.Abs(distance - targetDistance);

            // 目標距離へ到達した場合は攻撃する。
            if (distanceDifference <= ARRIVAL_TOLERANCE)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Attack);
                return;
            }

            if (elapsedTime < context.Boss.MoveSegmentDuration) { return; }

            // 4秒経過時点で射程内なら攻撃を実行する。
            if (distance <= attack.Definition.Range)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Attack);
                return;
            }

            // 射程外なら移動を継続する。
            elapsedTime = 0f;
        }

        public void Exit()
        {
        }
    }
}
