using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 攻撃直前のプレイヤー位置へチェーンソーを突き出す。
    /// </summary>
    public sealed class ThrustAttack : BossAttackStrategyBase
    {
        public ThrustAttack(BossAttackDefinition definition)
            : base(definition) { }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossThrustParameters parameters =
                Definition.GetParameters<BossThrustParameters>();
            BossMeleeVariant variant =
                parameters.GetVariant(HasShieldAtStart);

            context.Boss.Telegraph?.Show(variant.PreparationTime);
            context.Boss.AttackVisuals.PlayThrustPreparation(
                variant.PreparationTime);

            await DelayAsync(variant.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();
            context.Boss.FacePlayerImmediately();
            context.Boss.AttackVisuals.AimThrustAt(
                context.Boss.GetPlayerAimPosition());

            OpenAttackWindow(context);
            context.Boss.AttackHitboxes.OpenHitbox(
                Definition.Type, Definition, GetAttackDamage(context));

            context.Boss.AttackVisuals.PlayThrust(
                variant.ActiveTime, variant.AnimationSpeed);

            await DelayAsync(variant.ActiveTime, cancellationToken);
            context.Boss.AttackHitboxes.CloseHitbox(Definition.Type);
            CloseAttackWindow(context);

            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes?.CloseHitbox(Definition.Type);
            context.Boss.AttackVisuals?.ResetPose();
        }
    }
}
