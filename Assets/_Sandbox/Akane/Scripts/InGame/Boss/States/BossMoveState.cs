using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 選択した攻撃の目標距離まで移動する状態。
    /// </summary>
    public sealed class BossMoveState : IBossState
    {
        private const float ARRIVAL_TOLERANCE = 0.05f;

        private readonly BossContext context;

        public BossStateType Type => BossStateType.Move;

        public BossMoveState(BossContext context)
        {
            this.context = context;
        }

        public void Enter()
        {
        }

        public void Tick(float deltaTime)
        {
            var attack = context.Boss.SelectedAttack;

            if (attack == null)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            // 盾が必要な攻撃を実行できなくなった場合は待機へ戻る。
            if (attack.Definition.RequiresShield && !context.Shield.IsActive)
            {
                context.Boss.ClearSelectedAttack();
                context.Boss.StateMachine.ChangeState(BossStateType.Idle);
                return;
            }

            // プレイヤーの方向へ向きを変更する。
            context.Boss.FacePlayer(deltaTime);

            // 選択した攻撃の射程から移動目標距離を求める。
            var targetDistance = attack.Definition.Range *
                attack.Definition.TargetDistanceRatio;

            // 目標距離以内に到達した場合は攻撃する。
            if (context.Boss.DistanceToPlayer <= targetDistance + ARRIVAL_TOLERANCE)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Attack);
                return;
            }

            // 選択した攻撃を保持したまま移動する。
            context.Boss.MoveToTargetDistance(targetDistance, deltaTime);

            // 移動後に到達判定を行う。
            if (context.Boss.DistanceToPlayer <= targetDistance + ARRIVAL_TOLERANCE)
            {
                context.Boss.StateMachine.ChangeState(BossStateType.Attack);
            }
        }

        public void Exit()
        {
        }
    }
}
