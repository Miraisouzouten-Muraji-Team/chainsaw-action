using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 攻撃のキャンセル・完了・共通後処理を管理する。
    /// </summary>
    public abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        private CancellationTokenSource attackCancellation;
        private bool attackWindowOpen;
        private bool exited;
        private int attackGeneration;

        protected bool HasShieldAtStart { get; private set; }

        public BossAttackDefinition Definition { get; }
        public bool IsFinished { get; private set; }

        protected BossAttackStrategyBase(BossAttackDefinition definition)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// 攻撃開始時の盾状態でダメージを計算する。
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

            attackCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    context.Boss.destroyCancellationToken);

            attackWindowOpen = false;
            exited = false;
            IsFinished = false;
            HasShieldAtStart = context.Shield.IsActive;

            if (context.Animator != null &&
                !string.IsNullOrEmpty(Definition.AnimatorTrigger))
            {
                context.Animator.SetTrigger(Definition.AnimatorTrigger);
            }

            RunAttackAsync(
                context,
                attackCancellation.Token,
                generation).Forget();
        }

        private async UniTaskVoid RunAttackAsync(
            BossContext context,
            CancellationToken cancellationToken,
            int generation)
        {
            try
            {
                await ExecuteAsync(context, cancellationToken);

                if (!cancellationToken.IsCancellationRequested &&
                    generation == attackGeneration)
                {
                    IsFinished = true;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // 死亡・State終了によるキャンセル。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context.Boss);

                if (generation == attackGeneration)
                {
                    IsFinished = true;
                }
            }
            finally
            {
                // 古い非同期処理が次の攻撃判定を閉じないようにする。
                if (generation == attackGeneration && !exited)
                {
                    CloseAttackWindow(context);
                }
            }
        }

        /// <summary>
        /// 攻撃固有の時間進行を実装する。
        /// </summary>
        protected abstract UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken);

        protected static async UniTask DelayAsync(
            float seconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (seconds <= 0f) { return; }

            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                cancellationToken: cancellationToken);
        }

        protected void OpenAttackWindow(BossContext context)
        {
            if (attackWindowOpen) { return; }

            attackWindowOpen = true;
            context.Boss.NotifyAttackWindowOpened(Definition.Type);
        }

        protected void CloseAttackWindow(BossContext context)
        {
            if (!attackWindowOpen) { return; }

            attackWindowOpen = false;
            context.Boss.NotifyAttackWindowClosed(Definition.Type);
        }

        public virtual void Tick(BossContext context, float deltaTime)
        {
        }

        public void Exit(BossContext context)
        {
            if (exited) { return; }

            exited = true;
            attackGeneration++;

            CancelCurrentAttack();
            CloseAttackWindow(context);
            context.Boss.Telegraph?.Hide();
            OnExit(context);
        }

        protected virtual void OnExit(BossContext context)
        {
        }

        private void CancelCurrentAttack()
        {
            attackCancellation?.Cancel();
            attackCancellation?.Dispose();
            attackCancellation = null;
        }
    }
}
