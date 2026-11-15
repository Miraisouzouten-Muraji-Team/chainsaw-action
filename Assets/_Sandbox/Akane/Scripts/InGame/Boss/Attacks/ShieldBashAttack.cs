using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Playerの頭上へ移動し、落下着地時に横幅8の衝撃波を発生させる。
    /// </summary>
    public sealed class ShieldBashAttack : BossAttackStrategyBase
    {
        public ShieldBashAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            context.Boss.Telegraph?.Show(Definition.PreparationTime);
            await DelayAsync(Definition.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            context.Boss.PhysicsMotor.BeginAscentAbove(
                context.Player,
                Definition.BashAscentDuration);
            await DelayAsync(Definition.BashAscentDuration, cancellationToken);

            context.Boss.PhysicsMotor.HoldInAir();
            await DelayAsync(Definition.BashAirHoldDuration, cancellationToken);

            context.Boss.PhysicsMotor.BeginFall(Definition.BashFallSpeed);
            await UniTask.WaitUntil(
                () => context.Boss.PhysicsMotor.IsGrounded,
                cancellationToken: cancellationToken);

            float damage = context.Boss.CalculateAttackDamage(Definition);
            OpenAttackWindow(context);
            context.Boss.AttackHitboxes.HitShockwaveOnce(
                Definition,
                damage,
                Definition.ShockwaveWidth);
            CloseAttackWindow(context);

            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes.CloseShockwave();
            context.Boss.PhysicsMotor.EndScriptedMotion();
        }
    }
}
