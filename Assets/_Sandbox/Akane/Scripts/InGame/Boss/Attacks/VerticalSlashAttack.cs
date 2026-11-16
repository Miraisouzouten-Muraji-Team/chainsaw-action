using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 縦切りの溜め・半円・真下切り下ろし・硬直を実行する。
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
            BossVerticalSlashParameters parameters =
                Definition.GetParameters<BossVerticalSlashParameters>();

            BossMeleeVariant variant =
                parameters.GetVariant(HasShieldAtStart);

            BossAttackVisualController visuals =
                context.Boss.AttackVisuals;

            BossAttackHitboxController hitboxes =
                context.Boss.AttackHitboxes;

            hitboxes.PrepareVerticalSlash(Definition);

            // 1. 上半身の上で武器を構え、振り上げる。
            context.Boss.Telegraph?.Show(variant.PreparationTime);

            visuals.PlayVerticalSlashPreparation(
                variant.PreparationTime,
                parameters.RaiseAngle);

            await DelayAsync(
                variant.PreparationTime,
                cancellationToken);

            context.Boss.Telegraph?.Hide();

            // 2. 半円状の振りと真下切り下ろしを実行する。
            OpenAttackWindow(context);

            hitboxes.OpenHitbox(
                Definition.Type,
                Definition,
                GetAttackDamage(context));

            visuals.PlayVerticalSlash(
                variant.ActiveTime,
                parameters.RaiseAngle,
                parameters.FinishAngle,
                variant.AnimationSpeed);

            await DelayAsync(variant.ActiveTime, cancellationToken);

            // 3. すべての攻撃判定を終了する。
            hitboxes.CloseHitbox(Definition.Type);
            CloseAttackWindow(context);

            // 4. 硬直中に武器を通常姿勢へ戻す。
            visuals.PlayVerticalSlashRecovery(
                Definition.RecoveryTime);

            await DelayAsync(
                Definition.RecoveryTime,
                cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes?.CloseHitbox(
                Definition.Type);

            context.Boss.AttackVisuals?.ResetPose();
        }
    }
}
