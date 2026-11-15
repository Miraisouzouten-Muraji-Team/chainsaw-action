using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾を投げ、地面に沿って戻す。
    /// </summary>
    public sealed class ShieldThrowAttack : BossAttackStrategyBase
    {
        public ShieldThrowAttack(BossAttackDefinition definition)
            : base(definition) { }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossShieldThrowParameters parameters =
                Definition.GetParameters<BossShieldThrowParameters>();

            float preparationTime = parameters.GetPreparationTime(true);
            context.Boss.Telegraph?.Show(preparationTime);
            await DelayAsync(preparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            OpenAttackWindow(context);

            await context.Boss.ShieldMotion.ThrowAsync(
                context.Player,
                Definition,
                parameters,
                GetAttackDamage(context),
                cancellationToken);

            CloseAttackWindow(context);
            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.ShieldMotion?.CancelMotion();
        }
    }
}
