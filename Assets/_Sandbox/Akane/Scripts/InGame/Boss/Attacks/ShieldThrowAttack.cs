using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾を投げ、地面を加速させながら引き戻す。
    /// </summary>
    public sealed class ShieldThrowAttack : BossAttackStrategyBase
    {
        public ShieldThrowAttack(BossAttackDefinition definition)
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

            OpenAttackWindow(context);
            await context.Boss.ShieldMotion.ThrowAsync(
                context.Player,
                Definition,
                GetAttackDamage(context),
                cancellationToken);
            CloseAttackWindow(context);

            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.ShieldMotion.CancelMotion();
        }
    }
}
