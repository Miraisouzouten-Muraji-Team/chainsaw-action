
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Playerの頭上へ上昇し、落下と衝撃波で攻撃する。
    /// </summary>
    public sealed class ShieldBashAttack : BossAttackStrategyBase
    {
        // 地形設定に問題がある場合、無限に落下状態へ留まらないための保険。
        private const float MAX_FALL_WAIT_SECONDS = 8f;

        public ShieldBashAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override async UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            BossPhysicsMotor motor = context.Boss.PhysicsMotor;
            BossAttackHitboxController hitboxes = context.Boss.AttackHitboxes;

            context.Boss.Telegraph?.Show(Definition.PreparationTime);
            await DelayAsync(Definition.PreparationTime, cancellationToken);
            context.Boss.Telegraph?.Hide();

            // 上昇を開始する。
            motor.BeginAscentAbove(
                context.Player,
                Definition.BashAscentDuration);

            // 最後の物理移動が終わってから次の動作へ進む。
            await UniTask.WaitUntil(
                () => !motor.IsAscending,
                PlayerLoopTiming.FixedUpdate,
                cancellationToken: cancellationToken);

            motor.HoldInAir();

            // 頂点で溜める。
            await DelayAsync(
                Definition.BashAirHoldDuration,
                cancellationToken);

            float damage = GetAttackDamage(context);

            // 落下中の盾Hitboxを有効化する。
            hitboxes.BeginShieldBash(Definition, damage);
            OpenAttackWindow(context);
            motor.BeginFall(Definition.BashFallSpeed);

            float elapsed = 0f;

            while (!motor.IsGrounded)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await UniTask.Yield(
                    PlayerLoopTiming.Update,
                    cancellationToken);

                elapsed += Time.deltaTime;

                if (elapsed >= MAX_FALL_WAIT_SECONDS)
                {
                    Debug.LogWarning(
                        "ShieldBashAttack: 地面へ着地できませんでした。",
                        context.Boss);

                    hitboxes.EndShieldBash();
                    CloseAttackWindow(context);
                    return;
                }
            }

            // 落下中の当たり判定を終了する。
            hitboxes.EndShieldBash();

            // 同じ攻撃での命中履歴を引き継いで衝撃波を発生する。
            hitboxes.HitShockwaveOnce(
                Definition,
                damage,
                Definition.ShockwaveWidth);

            CloseAttackWindow(context);

            // 着地後の硬直。
            await DelayAsync(Definition.RecoveryTime, cancellationToken);
        }

        protected override void OnExit(BossContext context)
        {
            context.Boss.AttackHitboxes?.CloseAll();
            context.Boss.PhysicsMotor?.EndScriptedMotion();
        }
    }
}
