
using System;
using System.Collections.Generic;
using R3;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの行動状態。
    /// </summary>
    public enum BossStateType
    {
        Idle,
        Move,
        Attack,
        Dead
    }

    /// <summary>
    /// ボスの行動状態に共通する処理。
    /// </summary>
    public interface IBossState
    {
        BossStateType Type { get; }
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    /// <summary>
    /// ボスの行動遷移と状態変更通知を管理する。
    /// </summary>
    public sealed class BossStateMachine : IDisposable
    {
        private const int MAX_TRANSITIONS_PER_CALL = 16;

        private readonly Dictionary<BossStateType, IBossState> states = new();
        private readonly ReactiveProperty<BossStateType?> currentStateProperty = new(null);

        private IBossState currentState;
        private BossStateType? pendingState;
        private bool isTransitioning;
        private bool stopRequested;

        public BossStateType? CurrentType => currentState?.Type;
        public ReadOnlyReactiveProperty<BossStateType?> State => currentStateProperty;

        /// <summary>
        /// 行動状態を登録する。
        /// </summary>
        public void Register(IBossState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!states.TryAdd(state.Type, state))
                throw new InvalidOperationException($"State already registered: {state.Type}");
        }

        /// <summary>
        /// 状態遷移を要求する。
        /// 遷移処理中の要求は次に処理する。
        /// </summary>
        public void ChangeState(BossStateType nextType)
        {
            if (!states.ContainsKey(nextType))
                throw new InvalidOperationException($"State not registered: {nextType}");

            // 死亡後は通常行動に復帰させない。
            if (currentState?.Type == BossStateType.Dead &&
                nextType != BossStateType.Dead)
                return;

            // 死亡要求はほかの遷移要求より優先する。
            if (pendingState == BossStateType.Dead &&
                nextType != BossStateType.Dead)
                return;

            pendingState = nextType;

            if (isTransitioning)
                return;

            isTransitioning = true;

            try
            {
                var transitionCount = 0;

                while (pendingState.HasValue && !stopRequested)
                {
                    if (++transitionCount > MAX_TRANSITIONS_PER_CALL)
                    {
                        pendingState = null;
                        throw new InvalidOperationException(
                            "BossStateMachine: 状態遷移が連続しすぎています。");
                    }

                    var targetType = pendingState.Value;
                    pendingState = null;

                    if (currentState?.Type == targetType)
                        continue;

                    var nextState = states[targetType];

                    currentState?.Exit();

                    if (stopRequested)
                        break;

                    // Exit中に死亡要求が発生した場合は死亡を優先する。
                    if (pendingState == BossStateType.Dead &&
                        targetType != BossStateType.Dead)
                        continue;

                    currentState = nextState;
                    currentStateProperty.Value = targetType;
                    currentState.Enter();
                }
            }
            finally
            {
                isTransitioning = false;

                if (stopRequested)
                {
                    stopRequested = false;
                    PerformStop();
                }
            }
        }

        /// <summary>
        /// 現在の行動状態を更新する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            currentState?.Tick(deltaTime);
        }

        /// <summary>
        /// ボスの無効化などで現在の行動を終了する。
        /// </summary>
        public void Stop()
        {
            if (isTransitioning)
            {
                stopRequested = true;
                pendingState = null;
                return;
            }

            PerformStop();
        }

        private void PerformStop()
        {
            pendingState = null;

            var previousState = currentState;
            currentState = null;

            previousState?.Exit();
            currentStateProperty.Value = null;
        }

        /// <summary>
        /// R3の通知元を破棄する。
        /// </summary>
        public void Dispose()
        {
            Stop();
            currentStateProperty.Dispose();
        }
    }
}
