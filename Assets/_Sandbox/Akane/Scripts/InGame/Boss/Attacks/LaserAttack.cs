using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Playerを狙い、0.5秒間隔でレーザーを5発撃つ。
    /// </summary>
    public sealed class LaserAttack : BossAttackStrategyBase
    {
        public LaserAttack(BossAttackDefinition definition)
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

            float damage = context.Boss.CalculateAttackDamage(Definition);
            OpenAttackWindow(context);

            for (int i = 0; i < Definition.LaserShotCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.Boss.LaserController.Fire(
                    Definition,
                    damage,
                    Definition.LaserAimWidth,
                    Definition.LaserSpeed);

                if (i < Definition.LaserShotCount - 1)
                {
                    await DelayAsync(
                        Definition.LaserShotInterval,
                        cancellationToken);
                }
            }

            CloseAttackWindow(context);
            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            if (!IsFinished)
            {
                context.Boss.LaserController.CancelAll();
            }
        }
    }
}
