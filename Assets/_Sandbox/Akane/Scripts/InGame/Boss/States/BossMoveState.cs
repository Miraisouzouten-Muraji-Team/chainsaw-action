using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 選択した攻撃の目的距離まで移動する状態。
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

            // 移動中に盾が壊れ、選択した攻撃が使用できなくなった場合。
            if (attack.Definition.RequiresShield && !context.Shield.IsActive)
            {
                context.Boss.ClearSelectedAttack();
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            elapsedTime += deltaTime;
            context.Boss.FacePlayer(deltaTime);

            // 攻撃射程に対する割合から目的距離を求める。
            var targetDistance = attack.Definition.Range * attack.Definition.TargetDistanceRatio;

            context.Boss.MoveTowardPlayer(targetDistance, deltaTime);

            var distance = context.Boss.DistanceToPlayer;

            // 目的距離に到達したら攻撃を実行する。
            if (distance <= targetDistance + ARRIVAL_TOLERANCE)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Attack);
                return;
            }

            // 4秒経過時点で攻撃射程内なら攻撃する。
            if (elapsedTime >= context.Boss.MoveSegmentDuration)
            {
                if (distance <= attack.Definition.Range)
                {
                    context.Boss.StateMachine.ChangeState(BossStateType.Attack);
                    return;
                }

                // 射程外なら同じ攻撃を保持して移動を継続する。
                elapsedTime = 0f;
            }
        }

        public void Exit()
        {
        }
    }
}
