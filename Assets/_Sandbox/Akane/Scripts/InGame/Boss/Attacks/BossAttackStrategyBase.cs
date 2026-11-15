using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 攻撃のキャンセル、完了通知、共通後処理を管理する。
    /// </summary>
    public abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        private CancellationTokenSource attackCancellation;
        private bool attackWindowOpen;
        private bool exited;

        public BossAttackDefinition Definition { get; }
        public bool IsFinished { get; private set; }

        protected BossAttackStrategyBase(BossAttackDefinition definition)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// 攻撃固有の非同期シーケンスを開始する。
        /// </summary>
        public void Enter(BossContext context)
        {
            CancelCurrentAttack();

            attackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.Boss.destroyCancellationToken);
            attackWindowOpen = false;
            exited = false;
            IsFinished = false;

            if (context.Animator != null &&
                !string.IsNullOrEmpty(Definition.AnimatorTrigger))
            {
                context.Animator.SetTrigger(Definition.AnimatorTrigger);
            }

            RunAttackAsync(context, attackCancellation.Token).Forget();
        }

        private async UniTaskVoid RunAttackAsync(
            BossContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                await ExecuteAsync(context, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    IsFinished = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // State終了、死亡、無効化による正常な中断。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context.Boss);
                IsFinished = true;
            }
        }

        /// <summary>
        /// 攻撃固有の処理を順番に実行する。
        /// </summary>
        protected abstract UniTask ExecuteAsync(
            BossContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// TimeScaleに従って指定秒数待機する。
        /// </summary>
        protected static async UniTask DelayAsync(
            float seconds,
            CancellationToken cancellationToken)
        {
            if (seconds <= 0f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 攻撃判定開始を一度だけ通知する。
        /// </summary>
        protected void OpenAttackWindow(BossContext context)
        {
            if (attackWindowOpen) { return; }

            attackWindowOpen = true;
            context.Boss.NotifyAttackWindowOpened(Definition.Type);
        }

        /// <summary>
        /// 攻撃判定終了を一度だけ通知する。
        /// </summary>
        protected void CloseAttackWindow(BossContext context)
        {
            if (!attackWindowOpen) { return; }

            attackWindowOpen = false;
            context.Boss.NotifyAttackWindowClosed(Definition.Type);
        }

        public virtual void Tick(BossContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 実行中処理を止め、攻撃固有の参照を初期状態へ戻す。
        /// </summary>
        public void Exit(BossContext context)
        {
            if (exited) { return; }

            exited = true;
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
