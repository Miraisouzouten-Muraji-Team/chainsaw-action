using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボス攻撃の開始・完了・中断と非同期処理を管理する。
    /// </summary>
    public abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        private CancellationTokenSource attackCancellation;
        private bool attackWindowOpen;
        private bool exited;
        private int attackGeneration;

        protected bool HasShieldAtStart { get; private set; }
        protected bool UsesAnimationEvents { get; private set; }
        protected bool CompletedSuccessfully { get; private set; }

        public BossAttackDefinition Definition { get; }
        public bool IsFinished { get; private set; }

        protected BossAttackStrategyBase(
            BossAttackDefinition definition)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// 攻撃開始時点の盾状態でダメージを確定する。
        /// </summary>
        protected float GetAttackDamage(BossContext context)
        {
            return context.Boss.CalculateAttackDamage(
                Definition,
                HasShieldAtStart);
        }

        public void Enter(BossContext context)
        {
            CancelCurrentAttack();

            attackGeneration++;
            int generation = attackGeneration;

            var source =
                CancellationTokenSource.CreateLinkedTokenSource(
                    context.Boss.destroyCancellationToken);

            attackCancellation = source;

            attackWindowOpen = false;
            exited = false;

            IsFinished = false;
            CompletedSuccessfully = false;
            UsesAnimationEvents = false;

            HasShieldAtStart = context.Shield.IsActive;

            RunAttackAsync(
                context,
                source,
                generation).Forget();
        }

        /// <summary>
        /// 攻撃の実行とリソースの破棄を行う。
        /// </summary>
        private async UniTaskVoid RunAttackAsync(
            BossContext context,
            CancellationTokenSource source,
            int generation)
        {
            CancellationToken token = source.Token;

            try
            {
                // 攻撃ごとに使用するモーション方式を確定する。
                UsesAnimationEvents =
                    context.Animation?.BeginAttack(
                        Definition,
                        HasShieldAtStart) ?? false;

                await ExecuteAsync(context, token);

                if (!token.IsCancellationRequested &&
                    generation == attackGeneration)
                {
                    CompletedSuccessfully = true;
                    IsFinished = true;
                }
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                // 死亡や状態遷移による正常な中断。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context.Boss);

                if (generation == attackGeneration)
                {
                    // イベント設定不備などが発生した場合も
                    // 攻撃判定を残さない。
                    context.Boss.AttackHitboxes?.CloseAll();

                    CompletedSuccessfully = false;
                    IsFinished = true;
                }
            }
            finally
            {
                if (generation == attackGeneration && !exited)
                {
                    CloseAttackWindow(context);
                }

                if (ReferenceEquals(attackCancellation, source))
                {
                    attackCancellation = null;
                }

                source.Dispose();
            }
        }

        /// <summary>
        /// 攻撃固有の処理。
        /// </summary>
        protected abstract UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// 指定時間経過するまで待機する。
        /// </summary>
        protected static async UniTask DelayAsync(
            float seconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (seconds <= 0f)
                return;

            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Animator方式ではイベントを、
        /// 仮モーション方式では指定時間を待機する。
        /// </summary>
        protected async UniTask WaitForPhaseAsync(
            BossContext context,
            BossAnimationEventType eventType,
            float proceduralDuration,
            CancellationToken cancellationToken)
        {
            if (UsesAnimationEvents)
            {
                await context.Animation.WaitForEventAsync(
                    eventType,
                    cancellationToken);
            }
            else
            {
                await DelayAsync(
                    proceduralDuration,
                    cancellationToken);
            }
        }

        /// <summary>
        /// 攻撃判定の開始を通知する。
        /// </summary>
        protected void OpenAttackWindow(BossContext context)
        {
            if (attackWindowOpen)
                return;

            attackWindowOpen = true;

            context.Boss.NotifyAttackWindowOpened(
                Definition.Type);
        }

        /// <summary>
        /// 攻撃判定の終了を通知する。
        /// </summary>
        protected void CloseAttackWindow(BossContext context)
        {
            if (!attackWindowOpen)
                return;

            attackWindowOpen = false;

            context.Boss.NotifyAttackWindowClosed(
                Definition.Type);
        }

        public virtual void Tick(
            BossContext context,
            float deltaTime)
        {
        }

        public void Exit(BossContext context)
        {
            if (exited)
                return;

            exited = true;
            attackGeneration++;

            CancelCurrentAttack();

            context.Animation?.CancelAttack();

            CloseAttackWindow(context);
            context.Boss.Telegraph?.Hide();

            OnExit(context);
        }

        protected virtual void OnExit(BossContext context)
        {
        }

        /// <summary>
        /// 実行中の攻撃へ中断を通知する。
        /// DisposeはRunAttackAsyncに任せる。
        /// </summary>
        private void CancelCurrentAttack()
        {
            CancellationTokenSource source = attackCancellation;

            attackCancellation = null;

            if (source != null &&
                !source.IsCancellationRequested)
            {
                source.Cancel();
            }
        }
    }
}
