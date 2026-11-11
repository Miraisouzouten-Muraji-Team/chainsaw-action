using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 攻撃に共通する開始・実行・終了処理を管理する。
    /// </summary>
    public abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        private CancellationTokenSource attackCancellation;
        private bool activeStarted;
        private bool activeEnded;

        public BossAttackDefinition Definition { get; }
        public bool IsFinished { get; private set; }

        protected BossAttackStrategyBase(BossAttackDefinition definition)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// 攻撃を開始する。
        /// </summary>
        public virtual void Enter(BossContext context)
        {
            // 前回の処理が残っていた場合は終了する。
            attackCancellation?.Cancel();
            attackCancellation?.Dispose();

            attackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.Boss.destroyCancellationToken);

            activeStarted = false;
            activeEnded = false;
            IsFinished = false;

            var token = attackCancellation.Token;

            // 攻撃アニメーションを再生する。
            if (context.Animator != null &&
                !string.IsNullOrEmpty(Definition.AnimatorTrigger))
            {
                context.Animator.SetTrigger(Definition.AnimatorTrigger);
            }

            OnEnter(context);

            if (token.IsCancellationRequested) { return; }

            // DOTweenによる攻撃予告を表示する。
            context.Boss.Telegraph?.Show(Definition.PreparationTime);

            // UniTaskで攻撃の時間進行を管理する。
            RunAttackAsync(context, token).Forget();
        }

        /// <summary>
        /// 攻撃の予備動作、判定、硬直を順番に実行する。
        /// </summary>
        private async UniTask RunAttackAsync(BossContext context, CancellationToken token)
        {
            try
            {
                // 予備動作が終了するまで待つ。
                await DelayAsync(Definition.PreparationTime, token);
                token.ThrowIfCancellationRequested();

                context.Boss.Telegraph?.Hide();

                activeStarted = true;
                context.Boss.NotifyAttackWindowOpened(Definition.Type);
                OnActiveStart(context);

                // 攻撃が有効な時間を待つ。
                await DelayAsync(Definition.ActiveTime, token);
                token.ThrowIfCancellationRequested();

                activeEnded = true;
                OnActiveEnd(context);
                context.Boss.NotifyAttackWindowClosed(Definition.Type);

                // 攻撃後の硬直時間を待つ。
                await DelayAsync(Definition.RecoveryTime, token);
                token.ThrowIfCancellationRequested();

                IsFinished = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // State終了による正常なキャンセル。
            }
        }

        /// <summary>
        /// 指定秒数待機する。ゲームのTimeScaleに従う。
        /// </summary>
        private static async UniTask DelayAsync(
            float seconds,
            CancellationToken token)
        {
            if (seconds <= 0f) { return; }

            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                cancellationToken: token);
        }

        /// <summary>
        /// 複雑な攻撃が必要に応じて上書きする更新処理。
        /// 基本攻撃の時間管理はUniTask側で実行する。
        /// </summary>
        public virtual void Tick(BossContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 攻撃終了時に実行中の処理を中断する。
        /// </summary>
        public virtual void Exit(BossContext context)
        {
            attackCancellation?.Cancel();
            attackCancellation?.Dispose();
            attackCancellation = null;

            context.Boss.Telegraph?.Hide();

            // 攻撃中断時も有効判定を必ず終了する。
            if (activeStarted && !activeEnded)
            {
                activeEnded = true;
                OnActiveEnd(context);
                context.Boss.NotifyAttackWindowClosed(Definition.Type);
            }

            OnExit(context);
        }

        /// <summary>
        /// 攻撃固有の開始処理。
        /// </summary>
        protected virtual void OnEnter(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃固有の判定開始処理。
        /// </summary>
        protected virtual void OnActiveStart(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃固有の判定終了処理。
        /// </summary>
        protected virtual void OnActiveEnd(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃固有の終了処理。
        /// </summary>
        protected virtual void OnExit(BossContext context)
        {
        }
    }
}
