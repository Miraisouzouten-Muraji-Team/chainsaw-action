using System;
using R3;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの状態遷移と攻撃状況を確認する開発用コンポーネント。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossController))]
    public sealed class BossDebugMonitor : MonoBehaviour
    {
        [SerializeField] private BossController boss;

        private IDisposable stateSubscription;
        private bool started;

        private void Start()
        {
            started = true;
            SubscribeEvents();
        }

        private void OnEnable()
        {
            if (started)
            {
                SubscribeEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// R3と各種イベントの通知を購読する。
        /// </summary>
        private void SubscribeEvents()
        {
            if (boss == null)
            {
                boss = GetComponent<BossController>();
            }

            if (boss == null || boss.StateMachine == null) { return; }

            UnsubscribeEvents();

            // R3で状態の変更を監視する。
            stateSubscription = boss.StateMachine.State.Subscribe(state =>
            {
                if (!state.HasValue) { return; }

                var attackName = boss.SelectedAttack == null
                    ? "None"
                    : boss.SelectedAttack.Definition.Type.ToString();

                Debug.Log(
                    $"[Boss] State: {state.Value}, Attack: {attackName}, " +
                    $"Shield: {boss.Shield.IsActive}",
                    boss);
            });

            boss.AttackWindowOpened += OnAttackWindowOpened;
            boss.AttackWindowClosed += OnAttackWindowClosed;
            boss.Shield.ShieldBroken += OnShieldBroken;
            boss.Shield.ShieldRestored += OnShieldRestored;
        }

        /// <summary>
        /// 購読を解除し、重複通知を防止する。
        /// </summary>
        private void UnsubscribeEvents()
        {
            stateSubscription?.Dispose();
            stateSubscription = null;

            if (boss == null) { return; }

            boss.AttackWindowOpened -= OnAttackWindowOpened;
            boss.AttackWindowClosed -= OnAttackWindowClosed;

            if (boss.Shield != null)
            {
                boss.Shield.ShieldBroken -= OnShieldBroken;
                boss.Shield.ShieldRestored -= OnShieldRestored;
            }
        }

        private void OnAttackWindowOpened(BossAttackType attackType)
        {
            Debug.Log($"[Boss] Attack Start: {attackType}", boss);
        }

        private void OnAttackWindowClosed(BossAttackType attackType)
        {
            Debug.Log($"[Boss] Attack End: {attackType}", boss);
        }

        private void OnShieldBroken()
        {
            Debug.Log("[Boss] Shield Broken", boss);
        }

        private void OnShieldRestored()
        {
            Debug.Log("[Boss] Shield Restored", boss);
        }
    }
}
