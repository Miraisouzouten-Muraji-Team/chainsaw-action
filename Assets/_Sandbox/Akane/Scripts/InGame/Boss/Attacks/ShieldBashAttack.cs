using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 上昇・溜め・落下・着地衝撃波を順に実行する。
    /// </summary>
    public sealed class ShieldBashAttack : BossAttackStrategyBase
    {
        private const float MAX_ASCENT_WAIT_SECONDS = 5f;
        private const float MAX_FALL_WAIT_SECONDS = 8f;

        public ShieldBashAttack(BossAttackDefinition definition)
            : base(definition) { }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossShieldBashParameters parameters =
                Definition.GetParameters<BossShieldBashParameters>();

            BossPhysicsMotor motor = context.Boss.PhysicsMotor;
            BossAttackHitboxController hitboxes = context.Boss.AttackHitboxes;

            float preparationTime = parameters.GetPreparationTime(true);
            context.Boss.Telegraph?.Show(preparationTime);
            await DelayAsync(preparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            motor.BeginAscentAbove(context.Player, parameters.AscentDuration);

            float ascentElapsed = 0f;
            while (motor.IsAscending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(
                    PlayerLoopTiming.FixedUpdate, cancellationToken);
                ascentElapsed += Time.fixedDeltaTime;

                if (ascentElapsed >= MAX_ASCENT_WAIT_SECONDS)
                {
                    Debug.LogWarning(
                        "ShieldBashAttack: 上昇が完了しませんでした。",
                        context.Boss);
                    motor.EndScriptedMotion();
                    return;
                }
            }

            motor.HoldInAir();
            await DelayAsync(parameters.AirHoldDuration, cancellationToken);

            float damage = GetAttackDamage(context);
            hitboxes.BeginShieldBash(Definition, damage);
            OpenAttackWindow(context);
            motor.BeginFall(parameters.FallSpeed);

            float fallElapsed = 0f;
            while (!motor.IsGrounded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(
                    PlayerLoopTiming.FixedUpdate, cancellationToken);
                fallElapsed += Time.fixedDeltaTime;

                if (fallElapsed >= MAX_FALL_WAIT_SECONDS)
                {
                    Debug.LogWarning(
                        "ShieldBashAttack: 地面へ着地できませんでした。",
                        context.Boss);
                    hitboxes.EndShieldBash();
                    CloseAttackWindow(context);
                    motor.EndScriptedMotion();
                    return;
                }
            }

            hitboxes.EndShieldBash();
            hitboxes.HitShockwaveOnce(
                Definition, damage, parameters.ShockwaveWidth);
            CloseAttackWindow(context);

            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes?.CloseAll();
            context.Boss.PhysicsMotor?.EndScriptedMotion();
        }
    }
}
