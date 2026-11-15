using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 1秒溜めた後、前方へ突きを行う。
    /// </summary>
    public sealed class ThrustAttack : BossAttackStrategyBase
    {
        public ThrustAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            context.Boss.Telegraph?.Show(Definition.PreparationTime);
            context.Boss.AttackVisuals.PlayThrustPreparation(Definition.PreparationTime);
            await DelayAsync(Definition.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            context.Boss.FacePlayerImmediately();
            context.Boss.AttackVisuals.AimThrustAt(
                context.Boss.GetPlayerAimPosition());
            float damage = context.Boss.CalculateAttackDamage(Definition);
            OpenAttackWindow(context);
            context.Boss.AttackHitboxes.OpenHitbox(
                Definition.Type, Definition, damage);
            context.Boss.AttackVisuals.PlayThrust(
                Definition.ActiveTime / Definition.AnimationSpeed);

            await DelayAsync(Definition.ActiveTime, cancellationToken);

            context.Boss.AttackHitboxes.CloseHitbox(Definition.Type);
            CloseAttackWindow(context);
            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes.CloseHitbox(Definition.Type);
            context.Boss.AttackVisuals.ResetPose();
        }
    }
}
