
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// チェーンソーを上から前方へ振り下ろす。
    /// </summary>
    public sealed class VerticalSlashAttack : BossAttackStrategyBase
    {
        public VerticalSlashAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            float preparationTime = Definition.GetPreparationTime(HasShieldAtStart);
            float activeTime = Definition.GetActiveTime(HasShieldAtStart);
            float animationSpeed = Definition.GetAnimationSpeed(HasShieldAtStart);

            context.Boss.AttackHitboxes.PrepareVerticalSlash(Definition);
            context.Boss.Telegraph?.Show(preparationTime);
            context.Boss.AttackVisuals.PlayVerticalSlashPreparation(preparationTime);

            await DelayAsync(preparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            OpenAttackWindow(context);

            context.Boss.AttackHitboxes.OpenHitbox(
                Definition.Type,
                Definition,
                GetAttackDamage(context));

            context.Boss.AttackVisuals.PlayVerticalSlash(
                activeTime / animationSpeed);

            await DelayAsync(activeTime, cancellationToken);

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
