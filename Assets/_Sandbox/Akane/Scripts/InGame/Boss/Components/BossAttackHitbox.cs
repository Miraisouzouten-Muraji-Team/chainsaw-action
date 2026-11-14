
using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃中にプレイヤーと接触した場合、攻撃情報を送信する。
    /// 1回の判定開始につき同じプレイヤーには1回だけ通知する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class BossAttackHitbox : MonoBehaviour
    {
        private readonly HashSet<SensorPlayerController> hitPlayers = new();

        private Collider hitCollider;
        private BossAttackDefinition activeDefinition;
        private float activeDamage;
        private bool hitWindowOpen;

        private void Awake()
        {
            hitCollider = GetComponent<Collider>();

            if (!hitCollider.isTrigger)
            {
                Debug.LogError("BossAttackHitbox: Is TriggerをONにしてください。", this);
                enabled = false;
                return;
            }

            hitCollider.enabled = false;
        }

        /// <summary>
        /// 指定した攻撃データとダメージで判定を開始する。
        /// </summary>
        public void OpenHitWindow(BossAttackDefinition definition, float damage)
        {
            if (!isActiveAndEnabled || hitCollider == null || definition == null) { return; }

            CloseHitWindow();

            activeDefinition = definition;
            activeDamage = Mathf.Max(0f, damage);
            hitPlayers.Clear();
            hitWindowOpen = true;
            hitCollider.enabled = true;
        }

        /// <summary>
        /// 判定を終了し、命中済みの情報を破棄する。
        /// </summary>
        public void CloseHitWindow()
        {
            hitWindowOpen = false;
            activeDefinition = null;
            activeDamage = 0f;
            hitPlayers.Clear();

            if (hitCollider != null)
            {
                hitCollider.enabled = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryHitPlayer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryHitPlayer(other);
        }

        /// <summary>
        /// 判定が有効な間、Colliderに入っているプレイヤーへ命中情報を送る。
        /// </summary>
        private void TryHitPlayer(Collider other)
        {
            if (!hitWindowOpen || activeDefinition == null || activeDamage <= 0f || other == null || other.isTrigger) { return; }

            var player = other.GetComponentInParent<SensorPlayerController>();

            if (player == null || hitPlayers.Contains(player)) { return; }

            var receiver = player.GetComponentInChildren<PlayerAttackHitReceiver>(true);

            if (receiver == null || !receiver.isActiveAndEnabled) { return; }

            // 送信前に記録し、同じ攻撃で複数回通知されることを防ぐ。
            hitPlayers.Add(player);

            receiver.ReceiveAttackHit(new EnemyAttackHitData(activeDefinition, activeDamage));
        }

        private void OnDisable()
        {
            CloseHitWindow();
        }
    }
}
