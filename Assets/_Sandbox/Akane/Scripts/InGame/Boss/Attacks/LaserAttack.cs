
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 胸部からPlayerを狙うレーザーを連続発射する。
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
            float preparationTime =
                Definition.GetPreparationTime(HasShieldAtStart);

            context.Boss.Telegraph?.Show(preparationTime);
            await DelayAsync(preparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            int shotCount = Definition.GetLaserShotCount(HasShieldAtStart);
            float speed = Definition.GetLaserSpeed(HasShieldAtStart);
            float aimWidth = Definition.GetLaserAimWidth(HasShieldAtStart);
            float damage = GetAttackDamage(context);

            OpenAttackWindow(context);

            for (int i = 0; i < shotCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Boss.LaserController.Fire(Definition, damage, aimWidth, speed);

                if (i < shotCount - 1)
                {
                    await DelayAsync(Definition.LaserShotInterval, cancellationToken);
                }
            }

            CloseAttackWindow(context);
            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            if (!IsFinished)
            {
                context.Boss.LaserController?.CancelAll();
            }
        }
    }
}
