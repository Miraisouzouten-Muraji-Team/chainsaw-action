using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾状態に応じた発射数・速度・照準幅でレーザーを連射する。
    /// </summary>
    public sealed class LaserAttack : BossAttackStrategyBase
    {
        public LaserAttack(BossAttackDefinition definition)
            : base(definition) { }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossLaserParameters parameters =
                Definition.GetParameters<BossLaserParameters>();
            BossLaserVariant variant =
                parameters.GetVariant(HasShieldAtStart);

            context.Boss.Telegraph?.Show(variant.PreparationTime);
            await DelayAsync(variant.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            float damage = GetAttackDamage(context);
            OpenAttackWindow(context);

            for (int i = 0; i < variant.ShotCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                context.Boss.LaserController.Fire(
                    Definition, damage, variant.AimWidth, variant.Speed);

                if (i < variant.ShotCount - 1)
                {
                    await DelayAsync(
                        parameters.ShotInterval, cancellationToken);
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
