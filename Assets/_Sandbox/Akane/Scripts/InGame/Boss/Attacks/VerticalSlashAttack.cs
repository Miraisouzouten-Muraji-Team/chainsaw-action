using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 予備動作から振り下ろしまでを連続して実行する。
    /// </summary>
    public sealed class VerticalSlashAttack : BossAttackStrategyBase
    {
        public VerticalSlashAttack(BossAttackDefinition definition)
            : base(definition) { }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossVerticalSlashParameters parameters =
                Definition.GetParameters<BossVerticalSlashParameters>();
            BossMeleeVariant variant =
                parameters.GetVariant(HasShieldAtStart);

            context.Boss.AttackHitboxes.PrepareVerticalSlash(Definition);
            context.Boss.Telegraph?.Show(variant.PreparationTime);
            context.Boss.AttackVisuals.PlayVerticalSlashPreparation(
                variant.PreparationTime, parameters.RaiseAngle);

            await DelayAsync(variant.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            OpenAttackWindow(context);
            context.Boss.AttackHitboxes.OpenHitbox(
                Definition.Type, Definition, GetAttackDamage(context));

            context.Boss.AttackVisuals.PlayVerticalSlash(
                variant.ActiveTime,
                parameters.RaiseAngle,
                parameters.FinishAngle,
                variant.AnimationSpeed);

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
