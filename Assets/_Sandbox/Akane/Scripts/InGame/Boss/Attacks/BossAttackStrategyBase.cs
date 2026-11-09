using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃Strategyに共通する処理。
    /// </summary>
    public abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        private float elapsedTime;
        private bool activeStarted;
        private bool activeEnded;

        public BossAttackDefinition Definition { get; }
        public bool IsFinished { get; private set; }

        protected BossAttackStrategyBase(BossAttackDefinition definition)
        {
            Definition = definition;
        }

        /// <summary>
        /// 攻撃開始時の処理。
        /// </summary>
        public virtual void Enter(BossContext context)
        {
            elapsedTime = 0f;
            activeStarted = false;
            activeEnded = false;
            IsFinished = false;

            // 攻撃アニメーションを開始する。
            if (context.Animator != null &&
                !string.IsNullOrEmpty(Definition.AnimatorTrigger))
            {
                context.Animator.SetTrigger(Definition.AnimatorTrigger);
            }

            OnEnter(context);
        }

        /// <summary>
        /// 予備動作、攻撃、硬直の時間を更新する。
        /// </summary>
        public virtual void Tick(BossContext context, float deltaTime)
        {
            if (IsFinished) { return; }

            elapsedTime += deltaTime;

            var activeStartTime = Definition.PreparationTime;
            var activeEndTime = activeStartTime + Definition.ActiveTime;
            var finishTime = activeEndTime + Definition.RecoveryTime;

            // 予備動作が終了し、攻撃が有効になる。
            if (!activeStarted && elapsedTime >= activeStartTime)
            {
                activeStarted = true;

                context.Boss.NotifyAttackWindowOpened(Definition.Type);
                OnActiveStart(context);
            }

            // 攻撃時間が終了し、硬直へ移行する。
            if (activeStarted && !activeEnded && elapsedTime >= activeEndTime)
            {
                activeEnded = true;

                OnActiveEnd(context);
                context.Boss.NotifyAttackWindowClosed(Definition.Type);
            }

            // 硬直終了後に攻撃完了とする。
            if (elapsedTime >= finishTime)
            {
                IsFinished = true;
            }
        }

        /// <summary>
        /// 攻撃終了時、または途中で中断された際の処理。
        /// </summary>
        public virtual void Exit(BossContext context)
        {
            // 死亡などで攻撃が中断された場合も判定を閉じる。
            if (activeStarted && !activeEnded)
            {
                activeEnded = true;

                OnActiveEnd(context);
                context.Boss.NotifyAttackWindowClosed(Definition.Type);
            }

            OnExit(context);
        }

        /// <summary>
        /// 攻撃ごとの開始処理。
        /// </summary>
        protected virtual void OnEnter(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃ごとの有効判定開始処理。
        /// </summary>
        protected virtual void OnActiveStart(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃ごとの有効判定終了処理。
        /// </summary>
        protected virtual void OnActiveEnd(BossContext context)
        {
        }

        /// <summary>
        /// 攻撃ごとの終了処理。
        /// </summary>
        protected virtual void OnExit(BossContext context)
        {
        }
    }
}
