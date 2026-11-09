using System;
using System.Collections.Generic;

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
    /// ボスの各行動状態が実装するインターフェース。
    /// </summary>
    public interface IBossState
    {
        BossStateType Type { get; }

        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    /// <summary>
    /// ボスの行動状態と状態遷移を管理する。
    /// </summary>
    public sealed class BossStateMachine
    {
        private readonly Dictionary<BossStateType, IBossState> states = new();
        private IBossState currentState;

        public BossStateType? CurrentType => currentState?.Type;

        /// <summary>
        /// 行動状態を登録する。
        /// </summary>
        public void Register(IBossState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (states.ContainsKey(state.Type))
                throw new InvalidOperationException($"State already registered: {state.Type}");

            states.Add(state.Type, state);
        }

        /// <summary>
        /// 指定した状態へ遷移する。
        /// </summary>
        public void ChangeState(BossStateType nextType)
        {
            if (currentState?.Type == nextType)
                return;

            if (!states.TryGetValue(nextType, out var nextState))
                throw new InvalidOperationException($"State not registered: {nextType}");

            currentState?.Exit();
            currentState = nextState;
            currentState.Enter();
        }

        /// <summary>
        /// 現在の状態を更新する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            currentState?.Tick(deltaTime);
        }
    }
}
