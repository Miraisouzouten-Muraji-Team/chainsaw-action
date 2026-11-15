using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 溜めた後、チェーンソーを縦方向に振り下ろす。
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
            context.Boss.AttackHitboxes.PrepareVerticalSlash(Definition);
            context.Boss.Telegraph?.Show(Definition.PreparationTime);
            context.Boss.AttackVisuals.PlayVerticalSlashPreparation(
                Definition.PreparationTime);
            await DelayAsync(Definition.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            float damage = context.Boss.CalculateAttackDamage(Definition);
            OpenAttackWindow(context);
            context.Boss.AttackHitboxes.OpenHitbox(
                Definition.Type, Definition, damage);
            context.Boss.AttackVisuals.PlayVerticalSlash(
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
